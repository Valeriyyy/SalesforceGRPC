using Application.Bindings;
using Application.Services.Interfaces;
using Avro;
using Database.Models;
using Database.Repositories;
using Database.Repositories.Interfaces;
using DTO;
using Microsoft.Extensions.Logging;
using Salesforce.Avro;
using System.ComponentModel.DataAnnotations;

namespace Application.Services;

/// <inheritdoc />
public class BindingService : IBindingService {

    /// <summary>
    /// The sentinel Salesforce field name the Key Mapping is stored under. Every strategy reads it to build
    /// its WHERE clause, so it is a contract with the worker, not an implementation detail of this service.
    /// </summary>
    private const string KeyMappingFieldName = "MappedSFKey";

    /// <summary>Change Data Capture channels. Platform event channels cannot carry a Binding.</summary>
    private const string DataChannelType = "data";

    /// <summary>Target column names offered as the Key Mapping when one of them exists.</summary>
    private static readonly string[] KeyColumnCandidates = ["sf_id", "salesforce_id", "sfid", "salesforceid"];

    private readonly IMetaRepository _meta;
    private readonly IAvroSchemaRepository _avroSchemas;
    private readonly IRepository _targetDb;
    private readonly IPlatformEventChannelRepository _channels;
    private readonly IEntitySchemaProvider _entitySchemas;
    private readonly IBindingChangeSignal _changeSignal;
    private readonly ILogger<BindingService> _logger;

    public BindingService(
        IMetaRepository meta,
        IAvroSchemaRepository avroSchemas,
        IRepository targetDb,
        IPlatformEventChannelRepository channels,
        IEntitySchemaProvider entitySchemas,
        IBindingChangeSignal changeSignal,
        ILogger<BindingService> logger) {
        _meta = meta;
        _avroSchemas = avroSchemas;
        _targetDb = targetDb;
        _channels = channels;
        _entitySchemas = entitySchemas;
        _changeSignal = changeSignal;
        _logger = logger;
    }

    #region Discovery

    public async Task<IReadOnlyList<BindableFieldDTO>> GetBindableFieldsAsync(int memberId,
        CancellationToken cancellationToken = default) {
        var member = await RequireMember(memberId, cancellationToken).ConfigureAwait(false);
        var fields = await ReadEntityFields(member.SelectedEntity, cancellationToken).ConfigureAwait(false);

        // Without a Binding there is no Target Table to map against, so the fields stand alone.
        if (member.CdcSchemaId is not int bindingId) {
            return fields.Select(f => ToDto(f, null, null)).ToList();
        }

        var binding = await _meta.GetSchemaById(bindingId).ConfigureAwait(false);
        var mappings = await ReadMappings(bindingId).ConfigureAwait(false);
        var columns = binding is null
            ? []
            : (await LoadTable(binding.DbSchemaFullName, cancellationToken).ConfigureAwait(false))?.Columns ?? [];

        var mappedByField = mappings
            .Where(m => m.SalesforceFieldName != KeyMappingFieldName)
            .ToDictionary(m => m.SalesforceFieldName, m => m.TargetFieldName, StringComparer.OrdinalIgnoreCase);

        var takenColumns = mappings
            .Select(m => m.TargetFieldName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Suggestions are matched on a normalised name, so BillingAddressCity finds billing_address_city.
        var columnsByNormalisedName = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var column in columns) {
            columnsByNormalisedName.TryAdd(Normalise(column.ColumnName), column.ColumnName);
        }

        return fields.Select(f => {
            var mapped = mappedByField.GetValueOrDefault(f.Name);
            string? suggestion = null;
            if (mapped is null && columnsByNormalisedName.TryGetValue(Normalise(f.Name), out var candidate)
                && !takenColumns.Contains(candidate)) {
                suggestion = candidate;
            }
            return ToDto(f, mapped, suggestion);
        }).ToList();
    }

    public async Task<IReadOnlyList<TargetTableDTO>> GetTargetTablesAsync(string schemaName,
        CancellationToken cancellationToken = default) {
        EnsureDriverSupported();

        var tables = await _targetDb.GetSchemaMetadata(schemaName, cancellationToken).ConfigureAwait(false);
        var bindings = await _meta.GetCachedSchemas(cancellationToken).ConfigureAwait(false);
        var boundTables = bindings
            .Where(b => !string.IsNullOrWhiteSpace(b.DbSchemaFullName))
            .ToDictionary(b => b.DbSchemaFullName, b => b.EntityName, StringComparer.OrdinalIgnoreCase);

        return tables.Select(t => {
            var fullName = $"{t.SchemaName}.{t.TableName}";
            return new TargetTableDTO {
                SchemaName = t.SchemaName,
                TableName = t.TableName,
                FullName = fullName,
                BoundEntityName = boundTables.GetValueOrDefault(fullName)
            };
        }).ToList();
    }

    public async Task<IReadOnlyList<TargetColumnDTO>> GetTargetColumnsAsync(string schemaName, string tableName,
        int? bindingId = null, CancellationToken cancellationToken = default) {
        EnsureDriverSupported();

        var table = await _targetDb.GetTableMetadata(tableName, schemaName, cancellationToken).ConfigureAwait(false);
        if (table is null) {
            throw new KeyNotFoundException($"Target Table '{schemaName}.{tableName}' does not exist in the target database.");
        }

        var mappings = bindingId is int id ? await ReadMappings(id).ConfigureAwait(false) : [];
        var mappedByColumn = mappings
            .GroupBy(m => m.TargetFieldName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().SalesforceFieldName, StringComparer.OrdinalIgnoreCase);

        return table.Columns.Select(c => new TargetColumnDTO {
            ColumnName = c.ColumnName,
            DataType = c.DataType,
            IsNullable = c.IsNullable,
            MaxLength = c.MaxLength,
            IsUnique = c.IsUnique,
            MappedSalesforceFieldName = mappedByColumn.GetValueOrDefault(c.ColumnName)
        }).ToList();
    }

    #endregion

    #region Bindings

    public async Task<IReadOnlyList<BindingDTO>> GetBindingsAsync(CancellationToken cancellationToken = default) {
        var bindings = await _meta.GetCachedSchemas(cancellationToken).ConfigureAwait(false);

        var result = new List<BindingDTO>(bindings.Count);
        foreach (var binding in bindings) {
            result.Add(await ToDto(binding, cancellationToken).ConfigureAwait(false));
        }
        return result;
    }

    public async Task<BindingDTO> GetBindingAsync(int bindingId, CancellationToken cancellationToken = default) {
        var binding = await RequireBinding(bindingId).ConfigureAwait(false);
        return await ToDto(binding, cancellationToken).ConfigureAwait(false);
    }

    public async Task<BindingDTO> CreateBindingAsync(int memberId, CreateBindingDTO dto,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(dto);
        EnsureDriverSupported();

        var member = await RequireMember(memberId, cancellationToken).ConfigureAwait(false);

        var channel = await _channels.GetChannelByIdAsync(member.ChannelId, cancellationToken).ConfigureAwait(false);
        if (channel is null || !string.Equals(channel.ChannelType, DataChannelType, StringComparison.OrdinalIgnoreCase)) {
            throw new ValidationException(
                $"Channel member '{member.FullName}' is not on a Change Data Capture channel. Only Change Data Capture entities can be bound to a table.");
        }

        if (member.CdcSchemaId is not null) {
            throw new ValidationException($"Channel member '{member.FullName}' already has a Binding.");
        }

        var existingForEntity = await _meta.GetSchemaByEntityName(member.SelectedEntity).ConfigureAwait(false);
        if (existingForEntity is not null) {
            throw new ValidationException(
                $"Entity '{member.SelectedEntity}' is already bound to '{existingForEntity.DbSchemaFullName}'. An entity has one destination.");
        }

        var targetTable = BuildFullName(dto.TargetSchema, dto.TargetTable);

        var existingForTable = await _meta.GetSchemaByTargetTable(targetTable).ConfigureAwait(false);
        if (existingForTable is not null) {
            throw new ValidationException(
                $"Target Table '{targetTable}' is already bound to '{existingForTable.EntityName}'. Two entities cannot share a table.");
        }

        var table = await _targetDb.GetTableMetadata(dto.TargetTable, dto.TargetSchema, cancellationToken).ConfigureAwait(false);
        if (table is null) {
            throw new ValidationException(
                $"Target Table '{targetTable}' does not exist. This application never creates tables — create it first, then bind to it.");
        }

        var avroSchemaId = await ResolveAvroSchemaId(member.SelectedEntity, cancellationToken).ConfigureAwait(false);

        var created = await _meta.CreateNewSchemaWithAvroLink(new CDCSchema {
            EntityName = member.SelectedEntity,
            DbSchemaFullName = targetTable,
            BindingState = BindingState.Incomplete
        }, avroSchemaId).ConfigureAwait(false);

        await _channels.SetMemberBindingAsync(memberId, created.Id, cancellationToken).ConfigureAwait(false);
        _changeSignal.Signal();

        _logger.LogInformation("Created Binding {BindingId}: {Entity} -> {Table}", created.Id, created.EntityName, targetTable);

        return await ToDto(created, cancellationToken).ConfigureAwait(false);
    }

    public async Task<BindingDTO> SetFieldMappingsAsync(int bindingId, SetFieldMappingsDTO dto,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(dto);

        var binding = await RequireBinding(bindingId).ConfigureAwait(false);
        var table = await RequireTable(binding, cancellationToken).ConfigureAwait(false);
        var entityFields = await ReadEntityFieldNames(binding.EntityName, cancellationToken).ConfigureAwait(false);

        var existing = await ReadMappings(bindingId).ConfigureAwait(false);
        var keyMapping = existing.FirstOrDefault(m => m.SalesforceFieldName == KeyMappingFieldName);

        var columns = table.Columns.ToDictionary(c => c.ColumnName, StringComparer.OrdinalIgnoreCase);
        var seenColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var mapping in dto.Mappings) {
            if (!entityFields.Contains(mapping.SalesforceFieldName)) {
                throw new ValidationException(
                    $"'{mapping.SalesforceFieldName}' is not a field of {binding.EntityName}. Compound fields must be named in their flattened form, e.g. BillingAddressCity.");
            }

            if (!columns.ContainsKey(mapping.TargetColumnName)) {
                throw new ValidationException(
                    $"Column '{mapping.TargetColumnName}' does not exist on '{binding.DbSchemaFullName}'.");
            }

            if (!seenColumns.Add(mapping.TargetColumnName)) {
                throw new ValidationException(
                    $"Column '{mapping.TargetColumnName}' is mapped more than once. One Salesforce field per column, or one silently overwrites the other.");
            }

            if (keyMapping is not null &&
                string.Equals(mapping.TargetColumnName, keyMapping.TargetFieldName, StringComparison.OrdinalIgnoreCase)) {
                throw new ValidationException(
                    $"Column '{mapping.TargetColumnName}' holds the Salesforce record ID and cannot also carry a field.");
            }
        }

        var replacement = dto.Mappings
            .Select(m => new MappedField {
                SchemaId = bindingId,
                SalesforceFieldName = m.SalesforceFieldName,
                TargetFieldName = m.TargetColumnName
            })
            .ToList();

        // The Key Mapping is not part of the Field Mapping set the caller submits, so carry it across.
        if (keyMapping is not null) {
            replacement.Add(keyMapping);
        }

        await _meta.ReplaceFieldMappings(bindingId, replacement).ConfigureAwait(false);
        _changeSignal.Signal();

        return await ReconcileStateAfterEdit(binding, replacement, cancellationToken).ConfigureAwait(false);
    }

    public async Task<BindingDTO> SetKeyMappingAsync(int bindingId, SetKeyMappingDTO dto,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(dto);

        var binding = await RequireBinding(bindingId).ConfigureAwait(false);
        var table = await RequireTable(binding, cancellationToken).ConfigureAwait(false);

        var column = table.Columns.FirstOrDefault(c =>
            string.Equals(c.ColumnName, dto.TargetColumnName, StringComparison.OrdinalIgnoreCase));

        if (column is null) {
            throw new ValidationException(
                $"Column '{dto.TargetColumnName}' does not exist on '{binding.DbSchemaFullName}'.");
        }

        var check = TypeCompatibilityChecker.CheckKeyColumn(column, _targetDb.DatabaseType);
        if (check.Level is CompatibilityLevel.Error) {
            throw new ValidationException(check.Message);
        }

        var replacement = (await ReadMappings(bindingId).ConfigureAwait(false))
            .Where(m => m.SalesforceFieldName != KeyMappingFieldName)
            // A field mapped to this column would fight the WHERE clause, so it gives way.
            .Where(m => !string.Equals(m.TargetFieldName, column.ColumnName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        replacement.Add(new MappedField {
            SchemaId = bindingId,
            SalesforceFieldName = KeyMappingFieldName,
            TargetFieldName = column.ColumnName
        });

        await _meta.ReplaceFieldMappings(bindingId, replacement).ConfigureAwait(false);
        _changeSignal.Signal();

        return await ReconcileStateAfterEdit(binding, replacement, cancellationToken).ConfigureAwait(false);
    }

    public async Task<BindingDTO> SetSoftDeleteAsync(int bindingId, SetSoftDeleteDTO dto,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(dto);

        var binding = await RequireBinding(bindingId).ConfigureAwait(false);

        string? columnName = null;

        if (dto.Enabled) {
            if (string.IsNullOrWhiteSpace(dto.ColumnName)) {
                throw new ValidationException("Soft delete needs the name of the column that carries the flag.");
            }

            var table = await RequireTable(binding, cancellationToken).ConfigureAwait(false);
            var column = table.Columns.FirstOrDefault(c =>
                string.Equals(c.ColumnName, dto.ColumnName, StringComparison.OrdinalIgnoreCase));

            if (column is null) {
                throw new ValidationException(
                    $"Column '{dto.ColumnName}' does not exist on '{binding.DbSchemaFullName}'.");
            }

            var check = TypeCompatibilityChecker.CheckSoftDeleteColumn(column, _targetDb.DatabaseType);
            if (check.Level is CompatibilityLevel.Error) {
                throw new ValidationException(check.Message);
            }

            columnName = column.ColumnName;
        }

        await _meta.UpdateBinding(bindingId, binding.DbSchemaFullName, dto.Enabled, columnName).ConfigureAwait(false);
        binding.SoftDeleteEnabled = dto.Enabled;
        binding.SoftDeleteColumnName = columnName;
        _changeSignal.Signal();

        return await ToDto(binding, cancellationToken).ConfigureAwait(false);
    }

    public async Task<BindingValidationDTO> ValidateBindingAsync(int bindingId,
        CancellationToken cancellationToken = default) {
        var binding = await RequireBinding(bindingId).ConfigureAwait(false);
        var mappings = await ReadMappings(bindingId).ConfigureAwait(false);
        return await Validate(binding, mappings, cancellationToken).ConfigureAwait(false);
    }

    public async Task<BindingDTO> ActivateAsync(int bindingId, CancellationToken cancellationToken = default) {
        var binding = await RequireBinding(bindingId).ConfigureAwait(false);
        var mappings = await ReadMappings(bindingId).ConfigureAwait(false);

        var validation = await Validate(binding, mappings, cancellationToken).ConfigureAwait(false);
        if (!validation.CanActivate) {
            throw new ValidationException(DescribeFailure(binding, validation));
        }

        await _meta.SetBindingState(bindingId, BindingState.Active).ConfigureAwait(false);
        binding.BindingState = BindingState.Active;
        _changeSignal.Signal();

        _logger.LogInformation("Activated Binding {BindingId}: {Entity} -> {Table}",
            bindingId, binding.EntityName, binding.DbSchemaFullName);

        return await ToDto(binding, cancellationToken).ConfigureAwait(false);
    }

    public async Task<BindingDTO> DeactivateAsync(int bindingId, CancellationToken cancellationToken = default) {
        var binding = await RequireBinding(bindingId).ConfigureAwait(false);

        if (binding.BindingState is BindingState.Incomplete) {
            throw new ValidationException(
                $"Binding {bindingId} is Incomplete and was never switched on, so there is nothing to deactivate.");
        }

        // Deliberately no validation: a Binding that has broken since it was activated must still be
        // switchable off, which is exactly when a user most wants to switch it off.
        await _meta.SetBindingState(bindingId, BindingState.Inactive).ConfigureAwait(false);
        binding.BindingState = BindingState.Inactive;
        _changeSignal.Signal();

        return await ToDto(binding, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteBindingAsync(int bindingId, CancellationToken cancellationToken = default) {
        await RequireBinding(bindingId).ConfigureAwait(false);

        var members = await _channels.GetMembersByBindingIdAsync(bindingId, cancellationToken).ConfigureAwait(false) ?? [];
        foreach (var member in members) {
            await _channels.SetMemberBindingAsync(member.Id, null, cancellationToken).ConfigureAwait(false);
        }

        await _meta.DeleteBinding(bindingId).ConfigureAwait(false);
        _changeSignal.Signal();

        _logger.LogInformation("Deleted Binding {BindingId}", bindingId);
    }

    #endregion

    #region Primary channel

    public async Task<int?> GetPrimaryChannelIdAsync(CancellationToken cancellationToken = default) {
        var channel = await _channels.GetPrimaryChannelAsync(cancellationToken).ConfigureAwait(false);
        return channel?.Id;
    }

    public async Task SetPrimaryChannelAsync(int channelId, CancellationToken cancellationToken = default) {
        var channel = await _channels.GetChannelByIdAsync(channelId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Channel {channelId} was not found.");

        if (!string.Equals(channel.ChannelType, DataChannelType, StringComparison.OrdinalIgnoreCase)) {
            throw new ValidationException(
                $"Channel '{channel.FullName}' carries platform events, not Change Data Capture, so it cannot be the Primary Channel.");
        }

        await _channels.SetPrimaryChannelAsync(channelId, cancellationToken).ConfigureAwait(false);
        _changeSignal.Signal();

        _logger.LogInformation("Primary Channel set to {Channel}", channel.FullName);
    }

    public async Task<SubscriptionPlan> GetSubscriptionPlanAsync(CancellationToken cancellationToken = default) {
        var channel = await _channels.GetPrimaryChannelAsync(cancellationToken).ConfigureAwait(false);
        if (channel is null) {
            return SubscriptionPlan.Empty;
        }

        var entityNames = channel.Members
            .Select(m => m.SelectedEntity)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var bindings = await _meta.GetCachedSchemas(cancellationToken).ConfigureAwait(false);

        var active = new Dictionary<string, CDCSchema>(StringComparer.Ordinal);
        foreach (var binding in bindings) {
            // Keyed by Avro Schema Id because that is the only identifier an incoming event carries. A
            // Binding with no linked Avro Schema has never been seen on the wire and cannot be matched.
            if (!binding.IsActive || binding.SchemaId is not string schemaId ||
                !entityNames.Contains(binding.EntityName)) {
                continue;
            }
            active[schemaId] = binding;
        }

        return new SubscriptionPlan {
            TopicName = $"/data/{channel.FullName}",
            ChannelFullName = channel.FullName,
            ActiveBindingsBySchemaId = active,
            ChannelEntityNames = entityNames
        };
    }

    #endregion

    #region Validation

    private async Task<BindingValidationDTO> Validate(CDCSchema binding, List<MappedField> mappings,
        CancellationToken cancellationToken) {
        var result = new BindingValidationDTO { BindingId = binding.Id };

        var table = await LoadTable(binding.DbSchemaFullName, cancellationToken).ConfigureAwait(false);
        if (table is null) {
            result.Blockers.Add(
                $"Target Table '{binding.DbSchemaFullName}' no longer exists in the target database.");
            return result;
        }

        var avro = await _entitySchemas.GetSchemaForEntityAsync(binding.EntityName, cancellationToken).ConfigureAwait(false);
        if (avro is null) {
            result.Blockers.Add(
                $"No Avro Schema is available for {binding.EntityName}, so its fields cannot be checked.");
            return result;
        }

        result.ValidatedAgainstSchemaId = avro.SchemaId;

        var entityFields = ReadFields(avro).ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);
        var columns = table.Columns.ToDictionary(c => c.ColumnName, StringComparer.OrdinalIgnoreCase);

        var keyMapping = mappings.FirstOrDefault(m => m.SalesforceFieldName == KeyMappingFieldName);
        if (keyMapping is null) {
            result.Blockers.Add(
                "No Key Mapping. Choose the column holding the Salesforce record ID — updates and deletes build their WHERE clause from it.");
        } else if (!columns.TryGetValue(keyMapping.TargetFieldName, out var keyColumn)) {
            result.Blockers.Add($"Key Mapping column '{keyMapping.TargetFieldName}' no longer exists on '{binding.DbSchemaFullName}'.");
        } else {
            result.Results.Add(ToDto(TypeCompatibilityChecker.CheckKeyColumn(keyColumn, _targetDb.DatabaseType)));
        }

        var fieldMappings = mappings.Where(m => m.SalesforceFieldName != KeyMappingFieldName).ToList();
        if (fieldMappings.Count == 0) {
            result.Blockers.Add("No Field Mapping. A Binding with nothing mapped would write only record IDs.");
        }

        foreach (var mapping in fieldMappings) {
            if (!columns.TryGetValue(mapping.TargetFieldName, out var column)) {
                result.Blockers.Add(
                    $"Column '{mapping.TargetFieldName}' no longer exists on '{binding.DbSchemaFullName}'.");
                continue;
            }

            if (!entityFields.TryGetValue(mapping.SalesforceFieldName, out var field)) {
                result.Blockers.Add(
                    $"'{mapping.SalesforceFieldName}' is no longer a field of {binding.EntityName} in Avro Schema {avro.SchemaId}.");
                continue;
            }

            result.Results.Add(ToDto(TypeCompatibilityChecker.Check(
                field.Name, field.FieldType, column, _targetDb.DatabaseType)));
        }

        AddUnmappedNotNullWarnings(result, table, mappings, binding);

        if (binding.SoftDeleteEnabled) {
            if (string.IsNullOrWhiteSpace(binding.SoftDeleteColumnName)) {
                result.Blockers.Add("Soft delete is enabled but no column carries the flag.");
            } else if (!columns.TryGetValue(binding.SoftDeleteColumnName, out var softDeleteColumn)) {
                result.Blockers.Add(
                    $"Soft delete column '{binding.SoftDeleteColumnName}' no longer exists on '{binding.DbSchemaFullName}'.");
            } else {
                result.Results.Add(ToDto(TypeCompatibilityChecker.CheckSoftDeleteColumn(softDeleteColumn, _targetDb.DatabaseType)));
            }
        }

        result.CanActivate = result.Blockers.Count == 0
            && result.Results.All(r => r.Level != nameof(CompatibilityLevel.Error));

        return result;
    }

    /// <summary>
    /// Warns about NOT NULL columns nothing writes to, which would fail on the first insert.
    /// </summary>
    /// <remarks>
    /// A column with a default is fine, and the Key Mapping column is written by every strategy, so neither
    /// is reported.
    /// </remarks>
    private static void AddUnmappedNotNullWarnings(BindingValidationDTO result, TableMetadata table,
        List<MappedField> mappings, CDCSchema binding) {
        var written = mappings.Select(m => m.TargetFieldName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(binding.SoftDeleteColumnName)) {
            written.Add(binding.SoftDeleteColumnName);
        }

        foreach (var column in table.Columns) {
            if (column.IsNullable || column.DefaultValue is not null || written.Contains(column.ColumnName)) {
                continue;
            }

            result.Results.Add(new CompatibilityResultDTO {
                SalesforceFieldName = "",
                TargetColumnName = column.ColumnName,
                FieldType = "",
                TargetDataType = column.DataType,
                Level = nameof(CompatibilityLevel.Warning),
                Message = $"Column '{column.ColumnName}' is NOT NULL with no default and nothing is mapped to it, so inserts will fail."
            });
        }
    }

    /// <summary>
    /// After an edit, an Active Binding that no longer validates is switched off rather than left claiming
    /// more than is true.
    /// </summary>
    private async Task<BindingDTO> ReconcileStateAfterEdit(CDCSchema binding, List<MappedField> mappings,
        CancellationToken cancellationToken) {
        if (binding.BindingState is BindingState.Active) {
            var validation = await Validate(binding, mappings, cancellationToken).ConfigureAwait(false);
            if (!validation.CanActivate) {
                await _meta.SetBindingState(binding.Id, BindingState.Inactive).ConfigureAwait(false);
                binding.BindingState = BindingState.Inactive;
                _logger.LogWarning("Binding {BindingId} was deactivated because it no longer validates: {Reason}",
                    binding.Id, DescribeFailure(binding, validation));
            }
        }

        return await ToDto(binding, mappings, cancellationToken).ConfigureAwait(false);
    }

    private static string DescribeFailure(CDCSchema binding, BindingValidationDTO validation) {
        var reasons = validation.Blockers
            .Concat(validation.Results
                .Where(r => r.Level == nameof(CompatibilityLevel.Error))
                .Select(r => r.Message));

        return $"Binding {binding.Id} ({binding.EntityName} -> {binding.DbSchemaFullName}) is not valid: {string.Join(" ", reasons)}";
    }

    #endregion

    #region Helpers

    private void EnsureDriverSupported() {
        if (_targetDb.DatabaseType is DbType.SqlServer or DbType.MySql) {
            throw new ValidationException(
                $"The {_targetDb.DatabaseType} driver is not implemented, so target tables cannot be read and Bindings cannot be configured against it.");
        }
    }

    private async Task<PlatformEventChannelMemberEntity> RequireMember(int memberId, CancellationToken cancellationToken) =>
        await _channels.GetMemberByIdAsync(memberId, cancellationToken).ConfigureAwait(false)
        ?? throw new KeyNotFoundException($"Channel member {memberId} was not found.");

    private async Task<CDCSchema> RequireBinding(int bindingId) =>
        await _meta.GetSchemaById(bindingId).ConfigureAwait(false)
        ?? throw new KeyNotFoundException($"Binding {bindingId} was not found.");

    private async Task<TableMetadata> RequireTable(CDCSchema binding, CancellationToken cancellationToken) =>
        await LoadTable(binding.DbSchemaFullName, cancellationToken).ConfigureAwait(false)
        ?? throw new ValidationException(
            $"Target Table '{binding.DbSchemaFullName}' does not exist in the target database.");

    private Task<TableMetadata?> LoadTable(string fullName, CancellationToken cancellationToken) {
        var (schemaName, tableName) = SplitFullName(fullName);
        return _targetDb.GetTableMetadata(tableName, schemaName, cancellationToken);
    }

    private async Task<List<MappedField>> ReadMappings(int bindingId) =>
        (await _meta.GetEntityMappedFieldsBySchemaId(bindingId).ConfigureAwait(false) ?? []).ToList();

    private async Task<IReadOnlyList<EntityField>> ReadEntityFields(string entityName, CancellationToken cancellationToken) {
        var avro = await _entitySchemas.GetSchemaForEntityAsync(entityName, cancellationToken).ConfigureAwait(false)
            ?? throw new ValidationException(
                $"No Avro Schema is available for {entityName}. Salesforce publishes one once the entity is on a channel.");

        return ReadFields(avro);
    }

    private async Task<HashSet<string>> ReadEntityFieldNames(string entityName, CancellationToken cancellationToken) =>
        (await ReadEntityFields(entityName, cancellationToken).ConfigureAwait(false))
        .Select(f => f.Name)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<EntityField> ReadFields(DbAvroSchema avro) {
        if (Schema.Parse(avro.SchemaJson) is not RecordSchema record) {
            throw new ValidationException($"Avro Schema {avro.SchemaId} is not a record schema and carries no fields.");
        }
        return EntityFieldReader.ReadFields(record);
    }

    /// <summary>
    /// Finds the App Database row for an Entity's current Avro Schema, storing it if it is new.
    /// </summary>
    private async Task<int> ResolveAvroSchemaId(string entityName, CancellationToken cancellationToken) {
        var avro = await _entitySchemas.GetSchemaForEntityAsync(entityName, cancellationToken).ConfigureAwait(false)
            ?? throw new ValidationException(
                $"No Avro Schema is available for {entityName}, so it cannot be bound to a table yet.");

        if (avro.Id > 0) {
            return avro.Id;
        }

        var stored = await _avroSchemas.GetSchemaBySchemaIdAsync(avro.SchemaId, cancellationToken).ConfigureAwait(false);
        return stored?.Id > 0
            ? stored.Id
            : await _avroSchemas.InsertSchemaAsync(avro, cancellationToken).ConfigureAwait(false);
    }

    private static string BuildFullName(string schemaName, string tableName) {
        if (string.IsNullOrWhiteSpace(tableName)) {
            throw new ValidationException("A Binding needs the name of the Target Table it writes to.");
        }
        var schema = string.IsNullOrWhiteSpace(schemaName) ? "public" : schemaName.Trim();
        return $"{schema}.{tableName.Trim()}";
    }

    private static (string SchemaName, string TableName) SplitFullName(string fullName) {
        var separator = fullName.LastIndexOf('.');
        return separator <= 0
            ? ("public", fullName)
            : (fullName[..separator], fullName[(separator + 1)..]);
    }

    /// <summary>
    /// Reduces a name to letters and digits so BillingAddressCity and billing_address_city match.
    /// </summary>
    private static string Normalise(string name) {
        var trimmed = name.EndsWith("__c", StringComparison.OrdinalIgnoreCase) ? name[..^3] : name;
        return new string(trimmed.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }

    #endregion

    #region Mapping to DTOs

    private static BindableFieldDTO ToDto(EntityField field, string? mappedColumn, string? suggestion) => new() {
        Name = field.Name,
        FieldType = field.FieldType.ToString(),
        AvroType = field.AvroType,
        IsNullable = field.IsNullable,
        ParentName = field.ParentName,
        MappedColumnName = mappedColumn,
        SuggestedColumnName = suggestion ?? SuggestKeyColumn(field, mappedColumn)
    };

    /// <summary>
    /// Offers a conventional Key Mapping column against the Salesforce record ID field, so the one field that
    /// makes a Binding work is not left to be discovered.
    /// </summary>
    private static string? SuggestKeyColumn(EntityField field, string? mappedColumn) =>
        mappedColumn is null && field.FieldType is SalesforceFieldType.EntityId && field.Name == "Id"
            ? KeyColumnCandidates[0]
            : null;

    private async Task<BindingDTO> ToDto(CDCSchema binding, CancellationToken cancellationToken) =>
        await ToDto(binding, await ReadMappings(binding.Id).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);

    private async Task<BindingDTO> ToDto(CDCSchema binding, List<MappedField> mappings, CancellationToken cancellationToken) {
        var members = await _channels.GetMembersByBindingIdAsync(binding.Id, cancellationToken).ConfigureAwait(false) ?? [];

        return new BindingDTO {
            Id = binding.Id,
            EntityName = binding.EntityName,
            TargetTable = binding.DbSchemaFullName,
            State = binding.BindingState.ToString(),
            KeyMappingColumn = mappings.FirstOrDefault(m => m.SalesforceFieldName == KeyMappingFieldName)?.TargetFieldName,
            FieldMappingCount = mappings.Count(m => m.SalesforceFieldName != KeyMappingFieldName),
            SoftDeleteEnabled = binding.SoftDeleteEnabled,
            SoftDeleteColumnName = binding.SoftDeleteColumnName,
            AvroSchemaId = binding.SchemaId,
            ChannelMemberIds = members.Select(m => m.Id).ToList()
        };
    }

    private static CompatibilityResultDTO ToDto(FieldCompatibility compatibility) => new() {
        SalesforceFieldName = compatibility.SalesforceFieldName,
        TargetColumnName = compatibility.TargetColumnName,
        FieldType = compatibility.FieldType.ToString(),
        TargetDataType = compatibility.TargetDataType,
        Level = compatibility.Level.ToString(),
        Message = compatibility.Message
    };

    #endregion
}
