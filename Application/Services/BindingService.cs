using Application.Bindings;
using Application.Connections;
using Application.Mappers;
using Application.Services.Interfaces;
using Application.Targets;
using Application.Writeback;
using Avro;
using Database.Models;
using Database.Repositories;
using Database.Repositories.Interfaces;
using Database.Targets;
using Database.Writeback;
using DTO;
using Microsoft.Extensions.Logging;
using Salesforce.Avro;
using Salesforce.Auth;
using Salesforce.Clients;
using Salesforce.Writeback;
using System.Data.Common;
using System.ComponentModel.DataAnnotations;

namespace Application.Services;

/// <inheritdoc />
public class BindingService : IBindingService {

    /// <summary>Change Data Capture channels. Platform event channels cannot carry a Binding.</summary>
    private const string DataChannelType = "data";

    /// <summary>Target column names offered as the Key Mapping when one of them exists.</summary>
    private static readonly string[] KeyColumnCandidates = ["sf_id", "salesforce_id", "sfid", "salesforceid"];

    private readonly IMetaRepository _meta;
    private readonly IAvroSchemaRepository _avroSchemas;
    private readonly ITargetConnectionProvider _target;
    private readonly ITargetEngineCatalog _engines;
    private readonly IPlatformEventChannelRepository _channels;
    private readonly IEntitySchemaProvider _entitySchemas;
    private readonly IConfigurationChangeSignal _changeSignal;
    private readonly IOrgConnectionProvider _connections;
    private readonly ICheckpointRepository _checkpoints;
    private readonly ISObjectDescriber _describer;
    private readonly IWritebackFailureRepository _failures;
    private readonly WritebackProcessor _writeback;
    private readonly TimeProvider _time;
    private readonly ILogger<BindingService> _logger;

    public BindingService(
        IMetaRepository meta,
        IAvroSchemaRepository avroSchemas,
        ITargetConnectionProvider target,
        ITargetEngineCatalog engines,
        IPlatformEventChannelRepository channels,
        IEntitySchemaProvider entitySchemas,
        IConfigurationChangeSignal changeSignal,
        IOrgConnectionProvider connections,
        ICheckpointRepository checkpoints,
        ISObjectDescriber describer,
        IWritebackFailureRepository failures,
        WritebackProcessor writeback,
        TimeProvider time,
        ILogger<BindingService> logger) {
        _meta = meta;
        _avroSchemas = avroSchemas;
        _target = target;
        _engines = engines;
        _channels = channels;
        _entitySchemas = entitySchemas;
        _changeSignal = changeSignal;
        _connections = connections;
        _checkpoints = checkpoints;
        _describer = describer;
        _failures = failures;
        _writeback = writeback;
        _time = time;
        _logger = logger;
    }

    #region Discovery

    public async Task<IReadOnlyList<BindableFieldDTO>> GetBindableFieldsAsync(int memberId,
        CancellationToken cancellationToken = default) {
        var member = await RequireMember(memberId, cancellationToken).ConfigureAwait(false);

        var binding = member.CdcSchemaId is { } bindingId
            ? await _meta.GetSchemaById(bindingId).ConfigureAwait(false)
            : null;

        // Without a Binding there is no Target Table to map against, so the fields stand alone.
        if (binding is null) {
            var fields = await ReadEntityFields(member.SelectedEntity, cancellationToken).ConfigureAwait(false);
            return fields.Select(f => f.ToDto(null, SuggestKeyColumn(f, null))).ToList();
        }

        return await ReadBindableFields(binding, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<BindableFieldDTO>> GetBindableFieldsForBindingAsync(int bindingId,
        CancellationToken cancellationToken = default) {
        var binding = await RequireBinding(bindingId).ConfigureAwait(false);
        return await ReadBindableFields(binding, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>A Binding's Entity fields, each with its mapped Target Column or a suggested one.</summary>
    private async Task<IReadOnlyList<BindableFieldDTO>> ReadBindableFields(CDCSchema binding,
        CancellationToken cancellationToken) {
        var fields = await ReadEntityFields(binding.EntityName, cancellationToken).ConfigureAwait(false);
        var mappings = await ReadMappings(binding.Id).ConfigureAwait(false);
        var columns = (await LoadTable(binding.DbSchemaFullName, cancellationToken).ConfigureAwait(false))?.Columns ?? [];

        var mappedByField = mappings
            .Where(m => m.SalesforceFieldName != KeyMapping.FieldName)
            .ToDictionary(m => m.SalesforceFieldName, m => m.TargetFieldName, StringComparer.OrdinalIgnoreCase);

        var takenColumns = mappings
            .Select(m => m.TargetFieldName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Suggestions are matched on a normalized name, so BillingAddressCity finds billing_address_city.
        var columnsByNormalisedName = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var column in columns) {
            columnsByNormalisedName.TryAdd(Normalize(column.ColumnName), column.ColumnName);
        }

        return fields.Select(f => {
            var mapped = mappedByField.GetValueOrDefault(f.Name);
            string? suggestion = null;
            if (mapped is null && columnsByNormalisedName.TryGetValue(Normalize(f.Name), out var candidate)
                && !takenColumns.Contains(candidate)) {
                suggestion = candidate;
            }
            return f.ToDto(mapped, suggestion ?? SuggestKeyColumn(f, mapped));
        }).ToList();
    }

    public async Task<IReadOnlyList<TargetTableDTO>> GetTargetTablesAsync(string? schemaName,
        CancellationToken cancellationToken = default) {
        var target = await EnsureEngineSupported(cancellationToken).ConfigureAwait(false);

        // With no schema named, every schema is read: a table can be bound wherever it lives, not only in the
        // engine's default schema.
        var schemas = schemaName is null
            ? await target.GetSchemaNames(cancellationToken).ConfigureAwait(false)
            : [schemaName];

        var tables = new List<TableMetadata>();
        foreach (var schema in schemas) {
            tables.AddRange(await target.GetSchemaMetadata(schema, cancellationToken).ConfigureAwait(false));
        }

        var bindings = await _meta.GetCachedSchemas(cancellationToken).ConfigureAwait(false);
        var boundTables = bindings
            .Where(b => !string.IsNullOrWhiteSpace(b.DbSchemaFullName))
            .ToDictionary(b => b.DbSchemaFullName, b => b.EntityName, StringComparer.OrdinalIgnoreCase);

        return tables.Select(t => {
            // Built the same way a Binding's own full name is built, so a schema-less table's identity
            // agrees with what CreateBindingAsync stored for it — an inline "{schema}.{table}" here would
            // always insert a dot, even for an engine with no schema, and never match.
            var fullName = BuildFullName(t.SchemaName, t.TableName);
            return t.ToDto(fullName, boundTables.GetValueOrDefault(fullName));
        }).ToList();
    }

    public async Task<IReadOnlyList<TargetColumnDTO>> GetTargetColumnsAsync(string? schemaName, string tableName,
        int? bindingId = null, CancellationToken cancellationToken = default) {
        var target = await EnsureEngineSupported(cancellationToken).ConfigureAwait(false);

        var table = await target.GetTableMetadata(tableName, schemaName, cancellationToken).ConfigureAwait(false);
        if (table is null) {
            throw new KeyNotFoundException($"Target Table '{BuildFullName(schemaName, tableName)}' does not exist in the target database.");
        }

        var mappings = bindingId is int id ? await ReadMappings(id).ConfigureAwait(false) : [];
        var mappedByColumn = mappings
            .GroupBy(m => m.TargetFieldName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().SalesforceFieldName, StringComparer.OrdinalIgnoreCase);

        return table.Columns.Select(c => c.ToDto(mappedByColumn.GetValueOrDefault(c.ColumnName))).ToList();
    }

    public async Task<IReadOnlyList<TargetColumnDTO>> GetBindingColumnsAsync(int bindingId,
        CancellationToken cancellationToken = default) {
        var binding = await RequireBinding(bindingId).ConfigureAwait(false);
        var (schemaName, tableName) = SplitFullName(binding.DbSchemaFullName);
        return await GetTargetColumnsAsync(schemaName, tableName, bindingId, cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Bindings

    public async Task<IReadOnlyList<BindingDTO>> GetBindingsAsync(CancellationToken cancellationToken = default) {
        var bindings = await _meta.GetCachedSchemas(cancellationToken).ConfigureAwait(false);

        var result = new List<BindingDTO>(bindings.Count);
        foreach (var binding in bindings) {
            result.Add(await LoadBindingDto(binding, cancellationToken).ConfigureAwait(false));
        }
        return result;
    }

    public async Task<BindingDTO> GetBindingAsync(int bindingId, CancellationToken cancellationToken = default) {
        var binding = await RequireBinding(bindingId).ConfigureAwait(false);
        return await LoadBindingDto(binding, cancellationToken).ConfigureAwait(false);
    }

    public async Task<BindingDTO> CreateBindingAsync(int memberId, CreateBindingDTO dto,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(dto);
        var target = await EnsureEngineSupported(cancellationToken).ConfigureAwait(false);

        var member = await RequireMember(memberId, cancellationToken).ConfigureAwait(false);

        var channel = await _channels.GetChannelByIdAsync(member.ChannelId, cancellationToken).ConfigureAwait(false);
        if (channel is null || !string.Equals(channel.ChannelType, DataChannelType, StringComparison.OrdinalIgnoreCase)) {
            throw new ValidationException(
                $"Channel member '{member.FullName}' is not on a Change Data Capture channel. Only Change Data Capture entities can be bound to a table.");
        }

        if (member.CdcSchemaId is not null) {
            throw new ValidationException($"Channel member '{member.FullName}' already has a Binding.");
        }

        // An Entity has one destination. When it was bound through another Channel's member, this member
        // shares that Binding rather than being left unable to bind or edit it.
        var existingForEntity = await _meta.GetSchemaByEntityName(member.SelectedEntity).ConfigureAwait(false);
        if (existingForEntity is not null) {
            await _channels.SetMemberBindingAsync(memberId, existingForEntity.Id, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Linked channel member {Member} to the existing Binding {BindingId} for {Entity}",
                member.FullName, existingForEntity.Id, member.SelectedEntity);
            return await LoadBindingDto(existingForEntity, cancellationToken).ConfigureAwait(false);
        }

        var targetTable = BuildFullName(dto.TargetSchema, dto.TargetTable);

        var existingForTable = await _meta.GetSchemaByTargetTable(targetTable).ConfigureAwait(false);
        if (existingForTable is not null) {
            throw new ValidationException(
                $"Target Table '{targetTable}' is already bound to '{existingForTable.EntityName}'. Two entities cannot share a table.");
        }

        var table = await target.GetTableMetadata(dto.TargetTable, dto.TargetSchema, cancellationToken).ConfigureAwait(false);
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

        return await LoadBindingDto(created, cancellationToken).ConfigureAwait(false);
    }

    public async Task<BindingDTO> SetFieldMappingsAsync(int bindingId, SetFieldMappingsDTO dto,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(dto);

        var binding = await RequireBinding(bindingId).ConfigureAwait(false);
        var table = await RequireTable(binding, cancellationToken).ConfigureAwait(false);
        var entityFields = await ReadEntityFieldNames(binding.EntityName, cancellationToken).ConfigureAwait(false);

        var existing = await ReadMappings(bindingId).ConfigureAwait(false);
        var keyMapping = existing.FirstOrDefault(m => m.SalesforceFieldName == KeyMapping.FieldName);

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

        // Each field keeps its Field Direction. A new mapping in a Two-way Binding onto a column the database
        // generates starts Writeback only, so the inbound flow never writes into it.
        var directions = existing.ToDictionary(m => m.SalesforceFieldName, m => m.FieldDirection, StringComparer.OrdinalIgnoreCase);
        var replacement = dto.Mappings
            .Select(m => new MappedField {
                SchemaId = bindingId,
                SalesforceFieldName = m.SalesforceFieldName,
                TargetFieldName = m.TargetColumnName,
                FieldDirection = directions.TryGetValue(m.SalesforceFieldName, out var kept) ? kept
                    : binding.IsTwoWay && columns[m.TargetColumnName].IsGenerated ? FieldDirection.WritebackOnly
                    : FieldDirection.TwoWay
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
        var engine = await Engine(cancellationToken).ConfigureAwait(false);

        var column = table.Columns.FirstOrDefault(c =>
            string.Equals(c.ColumnName, dto.TargetColumnName, StringComparison.OrdinalIgnoreCase));

        if (column is null) {
            throw new ValidationException(
                $"Column '{dto.TargetColumnName}' does not exist on '{binding.DbSchemaFullName}'.");
        }

        var check = TypeCompatibilityChecker.CheckKeyColumn(column, engine);
        if (check.Level is CompatibilityLevel.Error) {
            throw new ValidationException(check.Message);
        }

        var replacement = (await ReadMappings(bindingId).ConfigureAwait(false))
            .Where(m => m.SalesforceFieldName != KeyMapping.FieldName)
            // A field mapped to this column would fight the WHERE clause, so it gives way.
            .Where(m => !string.Equals(m.TargetFieldName, column.ColumnName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        replacement.Add(new MappedField {
            SchemaId = bindingId,
            SalesforceFieldName = KeyMapping.FieldName,
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
            var engine = await Engine(cancellationToken).ConfigureAwait(false);
            var column = table.Columns.FirstOrDefault(c =>
                string.Equals(c.ColumnName, dto.ColumnName, StringComparison.OrdinalIgnoreCase));

            if (column is null) {
                throw new ValidationException(
                    $"Column '{dto.ColumnName}' does not exist on '{binding.DbSchemaFullName}'.");
            }

            if (!CanCarrySoftDeleteFlag(column, engine)) {
                throw new ValidationException(TypeCompatibilityChecker.CheckSoftDeleteColumn(column, engine).Message);
            }

            columnName = column.ColumnName;
        }

        await _meta.UpdateBinding(bindingId, binding.DbSchemaFullName, dto.Enabled, columnName).ConfigureAwait(false);
        binding.SoftDeleteEnabled = dto.Enabled;
        binding.SoftDeleteColumnName = columnName;
        _changeSignal.Signal();

        var mappings = await ReadMappings(bindingId).ConfigureAwait(false);
        return await ReconcileStateAfterEdit(binding, mappings, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> GetSoftDeleteColumnsAsync(int bindingId,
        CancellationToken cancellationToken = default) {
        var binding = await RequireBinding(bindingId).ConfigureAwait(false);
        var table = await RequireTable(binding, cancellationToken).ConfigureAwait(false);
        var engine = await Engine(cancellationToken).ConfigureAwait(false);

        return table.Columns
            .Where(c => CanCarrySoftDeleteFlag(c, engine))
            .Select(c => c.ColumnName)
            .ToList();
    }

    public async Task<BindingValidationDTO> ValidateBindingAsync(int bindingId,
        CancellationToken cancellationToken = default) {
        var binding = await RequireBinding(bindingId).ConfigureAwait(false);
        var mappings = await ReadMappings(bindingId).ConfigureAwait(false);
        return await Validate(binding, mappings, cancellationToken).ConfigureAwait(false);
    }

    public async Task<BindingValidationDTO> ValidateProposedFieldMappingsAsync(int bindingId, SetFieldMappingsDTO proposed,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(proposed);

        var binding = await RequireBinding(bindingId).ConfigureAwait(false);
        var keyMapping = (await ReadMappings(bindingId).ConfigureAwait(false))
            .FirstOrDefault(m => m.SalesforceFieldName == KeyMapping.FieldName);

        var mappings = proposed.Mappings
            .Select(m => new MappedField {
                SchemaId = bindingId,
                SalesforceFieldName = m.SalesforceFieldName,
                TargetFieldName = m.TargetColumnName
            })
            .ToList();
        if (keyMapping is not null) {
            mappings.Add(keyMapping);
        }

        var result = await Validate(binding, mappings, cancellationToken).ConfigureAwait(false);

        // Validation of a stored set never meets these, because saving refuses them; a draft can hold them.
        var seenColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in proposed.Mappings) {
            if (!seenColumns.Add(mapping.TargetColumnName)) {
                result.Blockers.Add(
                    $"Column '{mapping.TargetColumnName}' is mapped more than once. One Salesforce field per column, or one silently overwrites the other.");
            } else if (keyMapping is not null &&
                       string.Equals(mapping.TargetColumnName, keyMapping.TargetFieldName, StringComparison.OrdinalIgnoreCase)) {
                result.Blockers.Add(
                    $"Column '{mapping.TargetColumnName}' holds the Salesforce record ID and cannot also carry a field.");
            }
        }
        result.CanActivate &= result.Blockers.Count == 0;

        return result;
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

        return await LoadBindingDto(binding, cancellationToken).ConfigureAwait(false);
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

        return await LoadBindingDto(binding, cancellationToken).ConfigureAwait(false);
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

    /// <summary>
    /// Whether a column can carry the soft delete flag. The one rule behind both what the editor offers and
    /// what <see cref="SetSoftDeleteAsync"/> accepts.
    /// </summary>
    private static bool CanCarrySoftDeleteFlag(ColumnMetadata column, TargetDatabaseEngine engine) =>
        TypeCompatibilityChecker.CheckSoftDeleteColumn(column, engine).Level is not CompatibilityLevel.Error;

    #endregion

    #region Primary channel

    public async Task<int?> GetPrimaryChannelIdAsync(CancellationToken cancellationToken = default) {
        var channel = await _channels.GetPrimaryChannelAsync(cancellationToken).ConfigureAwait(false);
        return channel?.Id;
    }

    public async Task ClearPrimaryChannelAsync(CancellationToken cancellationToken = default) {
        await _channels.ClearPrimaryChannelAsync(cancellationToken).ConfigureAwait(false);
        _changeSignal.Signal();
        _logger.LogInformation("Primary Channel cleared; nothing streams until another is chosen");
    }

    public async Task SetPrimaryChannelAsync(SetPrimaryChannelDTO dto, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(dto);

        var channel = await RequireChannel(dto.ChannelId, cancellationToken).ConfigureAwait(false);

        if (!string.Equals(channel.ChannelType, DataChannelType, StringComparison.OrdinalIgnoreCase)) {
            throw new ValidationException(
                $"Channel '{channel.FullName}' carries platform events, not Change Data Capture, so it cannot be the Primary Channel.");
        }

        var start = ParseStart(dto.Start);
        var checkpoint = await _checkpoints.GetAsync(channel.Id, cancellationToken).ConfigureAwait(false);

        // Without a Checkpoint there is nothing to choose between: the Starting Point applies.
        if (checkpoint is not null) {
            switch (start) {
                case StartFrom.Resume when checkpoint.IsExpired(_time.GetUtcNow()):
                    throw new ValidationException(
                        $"Channel '{channel.FullName}' cannot Resume: its Checkpoint was saved {AgeHours(checkpoint):F0} hours ago, " +
                        $"and Salesforce keeps change events for only {Checkpoint.Retention.TotalHours:F0} hours. Choose Earliest or Latest.");
                case StartFrom.Earliest:
                    await _checkpoints.DiscardAsync(channel.Id, StartingPoint.Earliest, cancellationToken).ConfigureAwait(false);
                    break;
                case StartFrom.Latest:
                    await _checkpoints.DiscardAsync(channel.Id, StartingPoint.Latest, cancellationToken).ConfigureAwait(false);
                    break;
            }
        }

        await _channels.SetPrimaryChannelAsync(channel.Id, cancellationToken).ConfigureAwait(false);
        _changeSignal.Signal();

        _logger.LogInformation("Primary Channel set to {Channel}, starting from {Start}", channel.FullName,
            checkpoint is null ? $"its Starting Point ({channel.StartingPoint})" : start.ToString());
    }

    public async Task<ChannelStartDTO> GetChannelStartAsync(int channelId, CancellationToken cancellationToken = default) {
        var channel = await RequireChannel(channelId, cancellationToken).ConfigureAwait(false);
        var checkpoint = await _checkpoints.GetAsync(channel.Id, cancellationToken).ConfigureAwait(false);

        return channel.ToStartDto(checkpoint,
            canResume: checkpoint is not null && !checkpoint.IsExpired(_time.GetUtcNow()));
    }

    public async Task<ChannelStartDTO> SetStartingPointAsync(int channelId, SetStartingPointDTO dto,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(dto);

        var channel = await RequireChannel(channelId, cancellationToken).ConfigureAwait(false);

        if (!Enum.TryParse<StartingPoint>(dto.StartingPoint, ignoreCase: true, out var startingPoint)
            || !Enum.IsDefined(startingPoint)) {
            throw new ValidationException(
                $"'{dto.StartingPoint}' is not a Starting Point. Choose {nameof(StartingPoint.Latest)} or {nameof(StartingPoint.Earliest)}.");
        }

        // No signal: once the worker is streaming this channel it has a Checkpoint, which the Starting Point
        // never overrides, so a re-plan would change nothing.
        await _channels.SetStartingPointAsync(channel.Id, startingPoint, cancellationToken).ConfigureAwait(false);

        return await GetChannelStartAsync(channel.Id, cancellationToken).ConfigureAwait(false);
    }

    private static StartFrom ParseStart(string? start) {
        if (string.IsNullOrWhiteSpace(start)) {
            return StartFrom.Resume;
        }

        if (Enum.TryParse<StartFrom>(start, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)) {
            return parsed;
        }

        throw new ValidationException(
            $"'{start}' is not a start. Choose {nameof(StartFrom.Resume)}, {nameof(StartFrom.Earliest)} or {nameof(StartFrom.Latest)}.");
    }

    private double AgeHours(Checkpoint checkpoint) =>
        (_time.GetUtcNow() - new DateTimeOffset(DateTime.SpecifyKind(checkpoint.SavedAt, DateTimeKind.Utc))).TotalHours;

    public async Task<SubscriptionPlan> GetSubscriptionPlanAsync(CancellationToken cancellationToken = default) {
        // Whether the connection is usable, not merely present: a connection that has never authenticated
        // has no org id, and the Pub/Sub API requires one as its tenant.
        var connection = await _connections.GetAsync(cancellationToken).ConfigureAwait(false);
        var hasConnection = connection?.IsUsable == true;

        var target = await _target.GetAsync(cancellationToken).ConfigureAwait(false);
        var targetState = target?.ConnectionState;

        var channel = await _channels.GetPrimaryChannelAsync(cancellationToken).ConfigureAwait(false);
        if (channel is null) {
            return SubscriptionPlan.Empty with { HasConnection = hasConnection, TargetConnectionState = targetState };
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

            if (targetState is ConnectionState.Connected &&
                await DemoteIfInboundColumnsAreUnwritable(binding, cancellationToken).ConfigureAwait(false)) {
                continue;
            }
            active[schemaId] = binding;
        }

        // A one-time restart position, left when a Checkpoint was discarded, outranks the Starting Point.
        var checkpoint = await _checkpoints.GetAsync(channel.Id, cancellationToken).ConfigureAwait(false);
        var start = checkpoint is not null
            ? StartPosition.ResumeAfter(checkpoint)
            : (channel.RestartFrom ?? channel.StartingPoint) is StartingPoint.Earliest ? StartPosition.Earliest : StartPosition.Latest;

        return new SubscriptionPlan {
            HasConnection = hasConnection,
            TargetConnectionState = targetState,
            TopicName = $"/data/{channel.FullName}",
            ChannelFullName = channel.FullName,
            ChannelId = channel.Id,
            StartPosition = start,
            ActiveBindingsBySchemaId = active,
            ChannelEntityNames = entityNames
        };
    }

    /// <summary>
    /// Sets an Active Binding back to Incomplete when the columns the inbound flow writes can no longer take it:
    /// its Key Mapping column has lost its unique constraint, or a field is mapped onto a column the database
    /// generates.
    /// </summary>
    /// <remarks>
    /// Delivery is at-least-once (ADR 0005), so a repeated CREATE has to land on the row it already wrote,
    /// and only a unique key lets it. Without one, the repeat fails the write and replays the same batch
    /// forever. A generated column refuses every write the same way. The column may have changed after
    /// activation (a DBA dropped the constraint, or made the column generated), or the Binding may date from
    /// before the rule existed. A table that cannot be read is not proof of anything, so the Binding is kept and
    /// the worker's own failure handling takes over.
    /// </remarks>
    private async Task<bool> DemoteIfInboundColumnsAreUnwritable(CDCSchema binding, CancellationToken cancellationToken) {
        List<MappedField> mappings;
        TableMetadata? table;
        try {
            mappings = await ReadMappings(binding.Id).ConfigureAwait(false);
            table = await LoadTable(binding.DbSchemaFullName, cancellationToken).ConfigureAwait(false);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            _logger.LogWarning(ex, "Could not read the Target Table of Binding {BindingId} ({Table}); keeping it Active",
                binding.Id, binding.DbSchemaFullName);
            return false;
        }

        if (table is null) {
            return false;
        }

        var columns = table.Columns.ToDictionary(c => c.ColumnName, StringComparer.OrdinalIgnoreCase);
        var keyMapping = mappings.FirstOrDefault(m => m.SalesforceFieldName == KeyMapping.FieldName);

        if (keyMapping is not null && columns.TryGetValue(keyMapping.TargetFieldName, out var keyColumn) && !keyColumn.IsUnique) {
            await ForceIncomplete(binding).ConfigureAwait(false);
            _logger.LogWarning(
                "Binding {BindingId} ({Entity} -> {Table}) was set to Incomplete because its Key Mapping column {Column} " +
                "has no unique constraint. Change events can arrive more than once, and a repeated CREATE needs a unique " +
                "key to land on the row it already wrote. Add a unique constraint or primary key to {Column}, then activate it again.",
                binding.Id, binding.EntityName, binding.DbSchemaFullName, keyColumn.ColumnName, keyColumn.ColumnName);
            return true;
        }

        var generated = mappings
            .Where(m => m.SalesforceFieldName != KeyMapping.FieldName)
            .Where(m => !binding.IsTwoWay || m.FieldDirection.CarriesInbound())
            .FirstOrDefault(m => columns.TryGetValue(m.TargetFieldName, out var column) && column.IsGenerated);
        if (generated is not null) {
            await ForceIncomplete(binding).ConfigureAwait(false);
            _logger.LogWarning(
                "Binding {BindingId} ({Entity} -> {Table}) was set to Incomplete because {Field} is mapped onto {Column}, " +
                "which the database generates and refuses to be written. Map the field to another column, then activate it again.",
                binding.Id, binding.EntityName, binding.DbSchemaFullName, generated.SalesforceFieldName, generated.TargetFieldName);
            return true;
        }

        return false;
    }

    #endregion

    #region Writeback

    public async Task<WritebackSetupDTO> GetWritebackSetupAsync(int bindingId, CancellationToken cancellationToken = default) {
        var binding = await RequireBinding(bindingId).ConfigureAwait(false);
        var mappings = await ReadMappings(bindingId).ConfigureAwait(false);
        var defaults = await _meta.GetWritebackDefaults(bindingId).ConfigureAwait(false);

        var assessment = await AssessWriteback(binding, mappings, WritebackSettings.Stored(binding, mappings, defaults),
            includeEnvironment: true, cancellationToken).ConfigureAwait(false);
        var failures = await _failures.ListAsync(bindingId, cancellationToken).ConfigureAwait(false);

        var issues = assessment.Issues.ToList();
        if (assessment.CheckUnavailable is { } unavailable) {
            issues.Add(new WritebackIssueDTO { Message = $"Writeback could not be checked just now: {unavailable}" });
        }

        return new WritebackSetupDTO {
            BindingId = binding.Id,
            SyncDirection = binding.SyncDirection.ToString(),
            CanBeTwoWay = assessment.UnavailableReason is null,
            UnavailableReason = assessment.UnavailableReason,
            WritebackKeyColumn = binding.WritebackKeyColumn,
            WritebackKeySalesforceField = binding.WritebackKeySalesforceField,
            KeyColumnCandidates = assessment.KeyColumnCandidates,
            ExternalIdFields = assessment.ExternalIdFields,
            Fields = assessment.Fields.Select(f => new WritebackFieldDTO {
                SalesforceFieldName = f.Mapping.SalesforceFieldName,
                TargetColumnName = f.Mapping.TargetFieldName,
                RestFieldName = f.SalesforceField?.Name,
                Direction = f.Effective.ToString(),
                ChosenDirection = f.Chosen.ToString(),
                Forced = f.Forced,
                Reason = f.Reason
            }).ToList(),
            RequiredFields = assessment.RequiredFields,
            Issues = issues,
            Warnings = assessment.Warnings,
            FailureCount = failures.Count
        };
    }

    public async Task<BindingDTO> SetSyncDirectionAsync(int bindingId, SetSyncDirectionDTO dto,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(dto);

        if (!Enum.TryParse<SyncDirection>(dto.SyncDirection, ignoreCase: true, out var direction) || !Enum.IsDefined(direction)) {
            throw new ValidationException(
                $"'{dto.SyncDirection}' is not a Sync Direction. Choose {nameof(SyncDirection.Inbound)} or {nameof(SyncDirection.TwoWay)}.");
        }

        var binding = await RequireBinding(bindingId).ConfigureAwait(false);
        var mappings = await ReadMappings(bindingId).ConfigureAwait(false);

        if (direction is SyncDirection.Inbound) {
            // Field Directions, the Writeback Key and Writeback Defaults are all kept, so switching to Two-way
            // again restores the same setup.
            var keyColumn = dto.WritebackKeyColumn ?? binding.WritebackKeyColumn;
            var keyField = dto.WritebackKeySalesforceField ?? binding.WritebackKeySalesforceField;
            await _meta.SetWriteback(bindingId, SyncDirection.Inbound, keyColumn, keyField).ConfigureAwait(false);
            binding.SyncDirection = SyncDirection.Inbound;
            binding.WritebackKeyColumn = keyColumn;
            binding.WritebackKeySalesforceField = keyField;
            _changeSignal.Signal();

            _logger.LogInformation("Binding {BindingId} ({Entity}) is Inbound; Writeback stops for it", bindingId, binding.EntityName);
            return await ReconcileStateAfterEdit(binding, mappings, cancellationToken).ConfigureAwait(false);
        }

        var chosen = mappings
            .Where(m => m.SalesforceFieldName != KeyMapping.FieldName)
            .ToDictionary(m => m.SalesforceFieldName, m => m.FieldDirection, StringComparer.OrdinalIgnoreCase);
        foreach (var fieldDirection in dto.FieldDirections) {
            if (!chosen.ContainsKey(fieldDirection.SalesforceFieldName)) {
                throw new ValidationException($"'{fieldDirection.SalesforceFieldName}' has no Field Mapping on Binding {bindingId}.");
            }
            if (!Enum.TryParse<FieldDirection>(fieldDirection.Direction, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed)) {
                throw new ValidationException(
                    $"'{fieldDirection.Direction}' is not a Field Direction. Choose TwoWay, InboundOnly or WritebackOnly.");
            }
            chosen[fieldDirection.SalesforceFieldName] = parsed;
        }

        var settings = new WritebackSettings(SyncDirection.TwoWay, dto.WritebackKeyColumn, dto.WritebackKeySalesforceField,
            chosen, dto.WritebackDefaults.ToDictionary(d => d.SalesforceFieldName, d => d.Value, StringComparer.OrdinalIgnoreCase));

        var assessment = await AssessWriteback(binding, mappings, settings, includeEnvironment: true, cancellationToken)
            .ConfigureAwait(false);
        if (assessment.CheckUnavailable is { } unavailable) {
            throw new ValidationException($"Two-way cannot be checked just now, so it was not switched on: {unavailable}");
        }
        if (assessment.Issues.Count > 0) {
            throw new WritebackRefusedException(assessment.Issues);
        }

        // The user's choice is what is stored, so it comes back once whatever forced a field changes. The one
        // exception is a column the database generates: stored as Writeback only, so the inbound flow — which
        // knows nothing of column metadata — leaves it out. Every validation recomputes the forced directions.
        var generated = assessment.Fields
            .Where(f => f.Forced && f.Effective is FieldDirection.WritebackOnly)
            .Select(f => f.Mapping.SalesforceFieldName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var replacement = mappings.Select(m => new MappedField {
            SchemaId = m.SchemaId,
            SalesforceFieldName = m.SalesforceFieldName,
            TargetFieldName = m.TargetFieldName,
            FieldDirection = generated.Contains(m.SalesforceFieldName)
                ? FieldDirection.WritebackOnly
                : chosen.GetValueOrDefault(m.SalesforceFieldName, m.FieldDirection)
        }).ToList();

        await _meta.ReplaceFieldMappings(bindingId, replacement).ConfigureAwait(false);
        await _meta.ReplaceWritebackDefaults(bindingId, dto.WritebackDefaults.Select(d => new WritebackDefault {
            BindingId = bindingId, SalesforceFieldName = d.SalesforceFieldName, Value = d.Value
        })).ConfigureAwait(false);
        await _meta.SetWriteback(bindingId, SyncDirection.TwoWay, settings.KeyColumn, settings.KeyField).ConfigureAwait(false);

        binding.SyncDirection = SyncDirection.TwoWay;
        binding.WritebackKeyColumn = settings.KeyColumn;
        binding.WritebackKeySalesforceField = settings.KeyField;
        _changeSignal.Signal();

        _logger.LogInformation("Binding {BindingId} ({Entity} -> {Table}) is Two-way, keyed on {Column} -> {Field}",
            bindingId, binding.EntityName, binding.DbSchemaFullName, settings.KeyColumn, settings.KeyField);

        return await ReconcileStateAfterEdit(binding, replacement, cancellationToken).ConfigureAwait(false);
    }

    public async Task<WritebackPlan> GetWritebackPlanAsync(CancellationToken cancellationToken = default) {
        var connection = await _target.GetAsync(cancellationToken).ConfigureAwait(false);
        if (connection is null) {
            return WritebackPlan.Empty;
        }

        var capability = _engines.For(connection.Engine).Writeback;
        var endpoints = capability is null ? null : await _target.GetWritebackEndpointsAsync(cancellationToken).ConfigureAwait(false);

        var twoWay = (await _meta.GetCachedSchemas(cancellationToken).ConfigureAwait(false))
            .Where(b => b.IsActive && b.IsTwoWay)
            .ToList();

        // Problems of the Target Connection, not of any one Binding: they block Writeback as a whole until
        // fixed in one place, and demote nothing.
        string? blocked = capability is null
            ? $"Writeback is not yet supported for {connection.Engine}."
            : connection.ConnectionState is not ConnectionState.Connected
                ? "The Target Connection is not Connected."
                : endpoints is null || connection.ReplicationState is not ConnectionState.Connected
                    ? "The Replication Credential is missing or has not been proved. Enter it on the Target Connection page."
                    : null;

        var plans = new List<WritebackBindingPlan>();
        foreach (var binding in twoWay) {
            var mappings = await ReadMappings(binding.Id).ConfigureAwait(false);
            var defaults = await _meta.GetWritebackDefaults(binding.Id).ConfigureAwait(false);

            WritebackAssessment assessment;
            try {
                assessment = await AssessWriteback(binding, mappings, WritebackSettings.Stored(binding, mappings, defaults),
                    includeEnvironment: blocked is null, cancellationToken).ConfigureAwait(false);
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                _logger.LogWarning(ex, "Could not check Two-way Binding {BindingId} ({Table}); leaving it out of Writeback this time",
                    binding.Id, binding.DbSchemaFullName);
                continue;
            }

            if (assessment.CheckUnavailable is { } unavailable) {
                // A Salesforce or database that cannot be reached is not proof the Binding is broken.
                _logger.LogWarning("Could not check Two-way Binding {BindingId} ({Table}): {Reason}. It is kept, and left out of Writeback until it can be.",
                    binding.Id, binding.DbSchemaFullName, unavailable);
                continue;
            }

            if (assessment.Issues.Count > 0) {
                await ForceIncomplete(binding).ConfigureAwait(false);
                _logger.LogWarning(
                    "Binding {BindingId} ({Entity} -> {Table}) was set to Incomplete because it no longer meets Writeback's requirements: {Reasons}",
                    binding.Id, binding.EntityName, binding.DbSchemaFullName, string.Join(" ", assessment.Issues.Select(i => i.Message)));
                continue;
            }

            plans.Add(assessment.Plan!);
        }

        return new WritebackPlan {
            Bindings = plans,
            Capability = capability,
            Endpoints = endpoints,
            BlockedReason = blocked
        };
    }

    public async Task<IReadOnlyList<WritebackFailureDTO>> GetWritebackFailuresAsync(int? bindingId,
        CancellationToken cancellationToken = default) =>
        (await _failures.ListAsync(bindingId, cancellationToken).ConfigureAwait(false))
        .Select(f => f.ToDto())
        .ToList();

    public async Task<WritebackRetryDTO> RetryWritebackFailureAsync(int failureId, CancellationToken cancellationToken = default) {
        var failure = await RequireFailure(failureId, cancellationToken).ConfigureAwait(false);
        var binding = await RequireBinding(failure.BindingId).ConfigureAwait(false);
        if (!binding.IsTwoWay) {
            throw new ValidationException(
                $"Binding {binding.Id} is no longer Two-way, so nothing is written back for it. Dismiss the failure instead.");
        }

        var mappings = await ReadMappings(binding.Id).ConfigureAwait(false);
        var defaults = await _meta.GetWritebackDefaults(binding.Id).ConfigureAwait(false);
        var assessment = await AssessWriteback(binding, mappings, WritebackSettings.Stored(binding, mappings, defaults),
            includeEnvironment: false, cancellationToken).ConfigureAwait(false);
        if (assessment.CheckUnavailable is { } unavailable) {
            return new WritebackRetryDTO { Outcome = "Failed", Message = unavailable };
        }
        if (assessment.Plan is not { } plan) {
            throw new ValidationException(
                $"Binding {binding.Id} no longer meets Writeback's requirements: {string.Join(" ", assessment.Issues.Select(i => i.Message))}");
        }

        // The row as it is now: the point of Retry is that the user has fixed it.
        var target = await _target.GetRepositoryAsync(cancellationToken).ConfigureAwait(false);
        var row = failure.WritebackKeyValue is { } keyValue
            ? await target.GetRowByKey(plan.TableName, plan.WritebackKey.Column, keyValue, cancellationToken).ConfigureAwait(false)
            : await target.GetRowByKey(plan.TableName, plan.KeyMappingColumn, failure.SalesforceId!, cancellationToken).ConfigureAwait(false);

        if (row is null) {
            await _failures.DeleteAsync(failureId, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Writeback Failure {FailureId} discarded on Retry: its row is no longer in {Table}", failureId, plan.TableName);
            return new WritebackRetryDTO { Outcome = "Discarded", Message = "The row no longer exists, so there is nothing to send." };
        }

        string? refused;
        try {
            refused = await _writeback.RetryAsync(plan, row, target, cancellationToken).ConfigureAwait(false);
        } catch (WritebackTransientException ex) {
            return new WritebackRetryDTO { Outcome = "Failed", Message = $"Salesforce could not be reached, so nothing changed: {ex.Message}" };
        }

        if (refused is not null) {
            return new WritebackRetryDTO { Outcome = "Failed", Message = refused };
        }

        // The row may be known by a different key now (it gained a Writeback Key value), so the failure is
        // removed by its own id rather than trusting the processor's clear to find it.
        await _failures.DeleteAsync(failureId, cancellationToken).ConfigureAwait(false);
        return new WritebackRetryDTO { Outcome = "Succeeded" };
    }

    public async Task DismissWritebackFailureAsync(int failureId, CancellationToken cancellationToken = default) {
        await RequireFailure(failureId, cancellationToken).ConfigureAwait(false);
        await _failures.DeleteAsync(failureId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<WritebackFailure> RequireFailure(int failureId, CancellationToken cancellationToken) =>
        await _failures.GetAsync(failureId, cancellationToken).ConfigureAwait(false)
        ?? throw new KeyNotFoundException($"Writeback Failure {failureId} was not found.");

    /// <summary>
    /// A Binding's Writeback settings: stored, or proposed by an edit.
    /// </summary>
    private sealed record WritebackSettings(SyncDirection Direction, string? KeyColumn, string? KeyField,
        IReadOnlyDictionary<string, FieldDirection> Chosen, IReadOnlyDictionary<string, string> Defaults) {

        public static WritebackSettings Stored(CDCSchema binding, IEnumerable<MappedField> mappings,
            IEnumerable<WritebackDefault> defaults) => new(
            binding.SyncDirection, binding.WritebackKeyColumn, binding.WritebackKeySalesforceField,
            mappings.Where(m => m.SalesforceFieldName != KeyMapping.FieldName)
                .ToDictionary(m => m.SalesforceFieldName, m => m.FieldDirection, StringComparer.OrdinalIgnoreCase),
            defaults.ToDictionary(d => d.SalesforceFieldName, d => d.Value, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>One Field Mapping, judged for Writeback.</summary>
    private sealed record FieldAssessment(MappedField Mapping, ColumnMetadata Column, SObjectField? SalesforceField,
        FieldDirection Chosen, FieldDirection Effective, bool Forced, string? Reason);

    /// <summary>Everything decided about one Binding's Writeback.</summary>
    private sealed class WritebackAssessment {
        public List<WritebackIssueDTO> Issues { get; } = [];
        public List<string> Warnings { get; } = [];
        public List<FieldAssessment> Fields { get; } = [];
        public List<WritebackRequiredFieldDTO> RequiredFields { get; } = [];
        public List<string> KeyColumnCandidates { get; set; } = [];
        public List<string> ExternalIdFields { get; set; } = [];

        /// <summary>Why Two-way cannot be chosen at all; null when it can.</summary>
        public string? UnavailableReason { get; set; }

        /// <summary>Why nothing could be decided right now (Salesforce or the database unreachable); null when it could.</summary>
        public string? CheckUnavailable { get; set; }

        /// <summary>What the worker runs, set only when there are no issues.</summary>
        public WritebackBindingPlan? Plan { get; set; }

        public void Issue(string message, string? sql = null) => Issues.Add(new WritebackIssueDTO { Message = message, Sql = sql });
    }

    /// <summary>
    /// Judges whether a Binding can be Two-way with these settings: the Target Database Engine and its
    /// prerequisites, the Writeback Key, every field's effective direction, and the required Salesforce fields.
    /// </summary>
    /// <param name="includeEnvironment">
    /// False to judge only the Binding itself, leaving out the engine, the Replication Credential and the
    /// database prerequisites — used when those already block Writeback as a whole.
    /// </param>
    private async Task<WritebackAssessment> AssessWriteback(CDCSchema binding, List<MappedField> mappings,
        WritebackSettings settings, bool includeEnvironment, CancellationToken cancellationToken) {
        var assessment = new WritebackAssessment();
        var connection = await _target.GetAsync(cancellationToken).ConfigureAwait(false) ?? throw new NoTargetDatabaseException();
        var engine = connection.Engine;
        var writebackTable = WritebackTableOf(binding.DbSchemaFullName, engine);

        if (includeEnvironment) {
            await AssessEnvironment(assessment, connection, writebackTable, cancellationToken).ConfigureAwait(false);
            if (assessment.CheckUnavailable is not null) {
                return assessment;
            }
        }

        var table = await LoadTable(binding.DbSchemaFullName, cancellationToken).ConfigureAwait(false);
        if (table is null) {
            assessment.Issue($"Target Table '{binding.DbSchemaFullName}' no longer exists in the target database.");
            return assessment;
        }

        var avro = await _entitySchemas.GetSchemaForEntityAsync(binding.EntityName, cancellationToken).ConfigureAwait(false);
        if (avro is null) {
            assessment.Issue($"No Avro Schema is available for {binding.EntityName}, so its fields cannot be checked.");
            return assessment;
        }

        var sourceObject = SourceObjectOf(binding.EntityName);
        SObjectDescribe? describe;
        try {
            describe = await _describer.DescribeAsync(sourceObject, cancellationToken).ConfigureAwait(false);
        } catch (Exception ex) when (ex is SalesforceTransientException or SalesforceUnauthorizedException
                                         or NoOrgConnectionException or HttpRequestException
                                         or SalesforceRequestRefusedException) {
            assessment.CheckUnavailable = $"Salesforce could not describe {sourceObject}: {ex.Message}";
            return assessment;
        }
        if (describe is null) {
            assessment.Issue($"Salesforce reports no object named {sourceObject} to the Run-as User, so nothing can be written to it.");
            return assessment;
        }

        var entityFields = ReadFields(avro).ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);
        var columns = table.Columns.ToDictionary(c => c.ColumnName, StringComparer.OrdinalIgnoreCase);
        var keyMapping = mappings.FirstOrDefault(m => m.SalesforceFieldName == KeyMapping.FieldName);

        assessment.KeyColumnCandidates = table.Columns
            .Where(c => c.IsUnique && !string.Equals(c.ColumnName, keyMapping?.TargetFieldName, StringComparison.OrdinalIgnoreCase))
            .Select(c => c.ColumnName)
            .ToList();
        assessment.ExternalIdFields = describe.Fields.Where(f => f.ExternalId && f.Unique).Select(f => f.Name).ToList();

        if (keyMapping is null) {
            assessment.Issue("No Key Mapping. Writeback stores each new record's Salesforce ID in it.");
        }

        AssessFields(assessment, mappings, settings, entityFields, columns, describe, engine);
        AssessWritebackKey(assessment, settings, keyMapping, columns, describe);
        var defaults = AssessDefaults(assessment, settings, describe);
        AssessRequiredFields(assessment, settings, describe, defaults);

        if (assessment.Issues.Count == 0 && keyMapping is not null) {
            var keyField = describe.Field(settings.KeyField!)!;
            assessment.Plan = new WritebackBindingPlan {
                BindingId = binding.Id,
                EntityName = binding.EntityName,
                SourceObject = sourceObject,
                Table = writebackTable,
                TableName = binding.DbSchemaFullName,
                KeyMappingColumn = keyMapping.TargetFieldName,
                WritebackKey = new WritebackField(columns[settings.KeyColumn!].ColumnName, keyField.Name, keyField.Type),
                Fields = assessment.Fields
                    .Where(f => f.Effective.CarriesWriteback() && f.SalesforceField is not null)
                    .Select(f => new WritebackField(f.Column.ColumnName, f.SalesforceField!.Name, f.SalesforceField.Type))
                    .ToList(),
                Defaults = defaults.Select(d => new WritebackDefaultValue(d.Key, d.Value)).ToList()
            };
        }

        return assessment;
    }

    /// <summary>The engine, the Replication Credential, the server and the database prerequisites.</summary>
    private async Task AssessEnvironment(WritebackAssessment assessment, TargetConnection connection,
        WritebackTable table, CancellationToken cancellationToken) {
        var capability = _engines.For(connection.Engine).Writeback;
        if (capability is null) {
            assessment.UnavailableReason = $"Writeback is not yet supported for {connection.Engine}.";
            assessment.Issue(assessment.UnavailableReason);
            return;
        }

        var endpoints = await _target.GetWritebackEndpointsAsync(cancellationToken).ConfigureAwait(false);
        if (endpoints is null || connection.ReplicationState is not ConnectionState.Connected) {
            assessment.Issue(
                "Two-way needs a Replication Credential, proved to work: a login that may read the database's changes, so the login that writes rows never needs that right. Enter it on the Target Connection page.");
            return;
        }

        try {
            if (await capability.GetUnavailableReasonAsync(endpoints.ConnectionString, cancellationToken).ConfigureAwait(false) is { } reason) {
                assessment.UnavailableReason = reason;
                assessment.Issue(reason);
                return;
            }

            foreach (var prerequisite in await capability.CheckPrerequisitesAsync(endpoints, [table], cancellationToken).ConfigureAwait(false)) {
                assessment.Issue(prerequisite.Message, prerequisite.FixSql);
            }
        } catch (DbException ex) {
            assessment.CheckUnavailable = $"The Target Database could not be checked: {ex.Message}";
        }
    }

    private static void AssessFields(WritebackAssessment assessment, List<MappedField> mappings, WritebackSettings settings,
        Dictionary<string, EntityField> entityFields, Dictionary<string, ColumnMetadata> columns, SObjectDescribe describe,
        TargetDatabaseEngine engine) {
        foreach (var mapping in mappings.Where(m => m.SalesforceFieldName != KeyMapping.FieldName)) {
            if (!columns.TryGetValue(mapping.TargetFieldName, out var column) ||
                !entityFields.TryGetValue(mapping.SalesforceFieldName, out var entityField)) {
                // Validation reports a missing column or field; there is nothing to direct.
                continue;
            }

            var chosen = settings.Chosen.GetValueOrDefault(mapping.SalesforceFieldName, mapping.FieldDirection);
            var salesforceField = RestFieldOf(entityField, describe);
            var salesforceWritable = salesforceField?.IsWritable == true;

            if (!salesforceWritable && column.IsGenerated) {
                assessment.Issue(
                    $"'{mapping.SalesforceFieldName}' cannot be mapped to '{column.ColumnName}' in a Two-way Binding: Salesforce does not let the field be written and the database generates the column, so neither side can take the other's value.");
                continue;
            }

            (FieldDirection Direction, string Reason)? forced = null;
            if (salesforceField is null) {
                forced = (FieldDirection.InboundOnly,
                    $"Salesforce does not report {mapping.SalesforceFieldName} on {describe.Name} to the Run-as User, so it cannot be written.");
            } else if (!salesforceWritable) {
                forced = (FieldDirection.InboundOnly, $"Salesforce does not let {salesforceField.Name} be written; it sets the value itself.");
            } else if (column.IsGenerated) {
                forced = (FieldDirection.WritebackOnly,
                    $"The database generates column '{column.ColumnName}' itself and refuses any value written into it.");
            } else {
                var fit = TypeCompatibilityChecker.CheckWriteback(mapping.SalesforceFieldName, entityField.FieldType,
                    salesforceField, column, engine);
                if (fit.Level is CompatibilityLevel.Error) {
                    forced = (FieldDirection.InboundOnly, fit.Message);
                    assessment.Warnings.Add($"{mapping.SalesforceFieldName} is Inbound only: {fit.Message}");
                } else if (fit.Level is CompatibilityLevel.Warning && chosen.CarriesWriteback()) {
                    assessment.Warnings.Add(fit.Message);
                }
            }

            assessment.Fields.Add(new FieldAssessment(mapping, column, salesforceField, chosen,
                forced?.Direction ?? chosen, forced is not null, forced?.Reason));
        }
    }

    private static void AssessWritebackKey(WritebackAssessment assessment, WritebackSettings settings, MappedField? keyMapping,
        Dictionary<string, ColumnMetadata> columns, SObjectDescribe describe) {
        if (string.IsNullOrWhiteSpace(settings.KeyColumn)) {
            assessment.Issue("Choose a Writeback Key column: one that identifies each row, so a row inserted in the database reaches Salesforce exactly once.");
        } else if (!columns.TryGetValue(settings.KeyColumn, out var keyColumn)) {
            assessment.Issue($"Writeback Key column '{settings.KeyColumn}' does not exist on the Target Table.");
        } else if (keyMapping is not null && string.Equals(keyColumn.ColumnName, keyMapping.TargetFieldName, StringComparison.OrdinalIgnoreCase)) {
            assessment.Issue($"Writeback Key column '{keyColumn.ColumnName}' is the Key Mapping column, which holds the Salesforce record ID. Choose a column of the table's own, such as its primary key.");
        } else {
            if (!keyColumn.IsUnique) {
                assessment.Issue($"Writeback Key column '{keyColumn.ColumnName}' needs a unique constraint or primary key, or two rows could claim the same Salesforce record.");
            }
            var receivesInbound = assessment.Fields.FirstOrDefault(f =>
                string.Equals(f.Column.ColumnName, keyColumn.ColumnName, StringComparison.OrdinalIgnoreCase) && f.Effective.CarriesInbound());
            if (receivesInbound is not null) {
                assessment.Issue($"Writeback Key column '{keyColumn.ColumnName}' also receives {receivesInbound.Mapping.SalesforceFieldName} inbound. The key belongs to the database; make that mapping Writeback only, or map it elsewhere.");
            }
        }

        if (string.IsNullOrWhiteSpace(settings.KeyField)) {
            assessment.Issue("Choose the Salesforce External ID field that stores the Writeback Key.");
            return;
        }

        var keyField = describe.Field(settings.KeyField);
        if (keyField is null) {
            assessment.Issue($"Salesforce reports no field {settings.KeyField} on {describe.Name} to the Run-as User.");
            return;
        }
        if (!keyField.ExternalId) {
            assessment.Issue($"{keyField.Name} is not an External ID field, so Salesforce cannot match records on it. Mark it External ID and Unique in Setup, or choose another field.");
        } else if (!keyField.Unique) {
            assessment.Issue($"{keyField.Name} is an External ID but not marked Unique, so a match on it could find two records. Mark it Unique in Setup, or choose another field.");
        }

        var mapped = assessment.Fields.FirstOrDefault(f =>
            string.Equals(f.Mapping.SalesforceFieldName, keyField.Name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(f.SalesforceField?.Name, keyField.Name, StringComparison.OrdinalIgnoreCase));
        if (mapped is not null) {
            assessment.Issue($"{keyField.Name} stores the Writeback Key, so it cannot also be the subject of a Field Mapping (it is mapped to '{mapped.Column.ColumnName}'). Remove that Field Mapping.");
        }
    }

    /// <summary>Checks each Writeback Default and converts it to the value sent.</summary>
    private static Dictionary<string, object?> AssessDefaults(WritebackAssessment assessment, WritebackSettings settings,
        SObjectDescribe describe) {
        var converted = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (fieldName, text) in settings.Defaults) {
            var field = describe.Field(fieldName);
            if (field is null || !field.Createable) {
                assessment.Issue($"Writeback Default for {fieldName}: Salesforce does not let the field be set when a {describe.Name} is created.");
                continue;
            }
            try {
                converted[field.Name] = WritebackValueConverter.ParseDefault(text, field.Type);
            } catch (FormatException ex) {
                assessment.Issue($"Writeback Default for {field.Name}: {ex.Message}");
            }
        }
        return converted;
    }

    private static void AssessRequiredFields(WritebackAssessment assessment, WritebackSettings settings,
        SObjectDescribe describe, Dictionary<string, object?> defaults) {
        var supplied = assessment.Fields
            .Where(f => f.Effective.CarriesWriteback() && f.SalesforceField is not null)
            .Select(f => f.SalesforceField!.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var field in describe.Fields.Where(f => f.IsRequiredOnCreate)) {
            if (string.Equals(field.Name, settings.KeyField, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var bySupply = supplied.Contains(field.Name);
            var defaultValue = settings.Defaults.FirstOrDefault(d =>
                string.Equals(d.Key, field.Name, StringComparison.OrdinalIgnoreCase)).Value;

            assessment.RequiredFields.Add(new WritebackRequiredFieldDTO {
                SalesforceFieldName = field.Name,
                Label = field.Label,
                Type = field.Type,
                SuppliedByMapping = bySupply,
                DefaultValue = defaultValue,
                AllowedValues = field.PicklistValues.Where(v => v.Active).Select(v => v.Value).ToList()
            });

            if (!bySupply && !defaults.ContainsKey(field.Name)) {
                assessment.Warnings.Add(
                    $"{field.Name} is required to create a {describe.Name} and nothing supplies it. Set a Writeback Default, or every row inserted in the database fails to reach Salesforce unless automation there fills it.");
            }
        }
    }

    /// <summary>
    /// The Salesforce field a flattened Entity field is written to. A compound child is named by the REST API
    /// differently from its flattened form: NameFirstName is FirstName, BillingAddressStreet is BillingStreet.
    /// </summary>
    private static SObjectField? RestFieldOf(EntityField field, SObjectDescribe describe) {
        if (field.ParentName is not { } parent || field.ChildName is not { } child) {
            return describe.Field(field.Name);
        }

        var parts = describe.Fields
            .Where(f => string.Equals(f.CompoundFieldName, parent, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var prefix = parent.EndsWith("Address", StringComparison.Ordinal) ? parent[..^"Address".Length] : parent;

        return parts.FirstOrDefault(f => string.Equals(f.Name, child, StringComparison.OrdinalIgnoreCase))
               ?? parts.FirstOrDefault(f => string.Equals(f.Name, prefix + child, StringComparison.OrdinalIgnoreCase))
               ?? parts.FirstOrDefault(f => f.Name.EndsWith(child, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The Salesforce object an Entity reports on: AccountChangeEvent is Account, Invoice__ChangeEvent is Invoice__c.
    /// </summary>
    private static string SourceObjectOf(string entityName) =>
        entityName.EndsWith("__ChangeEvent", StringComparison.Ordinal)
            ? entityName[..^"__ChangeEvent".Length] + "__c"
            : entityName.EndsWith("ChangeEvent", StringComparison.Ordinal)
                ? entityName[..^"ChangeEvent".Length]
                : entityName;

    /// <summary>The Target Table as the engine's change stream names it, resolving the engine's default schema.</summary>
    private static WritebackTable WritebackTableOf(string fullName, TargetDatabaseEngine engine) {
        var (schemaName, tableName) = SplitFullName(fullName);
        return new WritebackTable(schemaName ?? (engine is TargetDatabaseEngine.Postgres ? "public" : null), tableName);
    }

    #endregion

    #region Validation

    private async Task<BindingValidationDTO> Validate(CDCSchema binding, List<MappedField> mappings,
        CancellationToken cancellationToken) {
        var result = new BindingValidationDTO { BindingId = binding.Id };
        var engine = await Engine(cancellationToken).ConfigureAwait(false);

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

        var keyMapping = mappings.FirstOrDefault(m => m.SalesforceFieldName == KeyMapping.FieldName);
        if (keyMapping is null) {
            result.Blockers.Add(
                "No Key Mapping. Choose the column holding the Salesforce record ID — updates and deletes build their WHERE clause from it.");
        } else if (!columns.TryGetValue(keyMapping.TargetFieldName, out var keyColumn)) {
            result.Blockers.Add($"Key Mapping column '{keyMapping.TargetFieldName}' no longer exists on '{binding.DbSchemaFullName}'.");
        } else {
            result.Results.Add(TypeCompatibilityChecker.CheckKeyColumn(keyColumn, engine).ToDto());
        }

        var fieldMappings = mappings.Where(m => m.SalesforceFieldName != KeyMapping.FieldName).ToList();
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

            // A Writeback-only field is never written inbound, so whether Salesforce's value would fit the
            // column does not matter; the Writeback assessment below judges the other direction.
            if (binding.IsTwoWay && mapping.FieldDirection is FieldDirection.WritebackOnly) {
                continue;
            }

            result.Results.Add(TypeCompatibilityChecker.Check(field.Name, field.FieldType, column, engine).ToDto());
        }

        AddUnmappedNotNullWarnings(result, table, mappings, binding);

        if (binding.SoftDeleteEnabled) {
            if (string.IsNullOrWhiteSpace(binding.SoftDeleteColumnName)) {
                result.Blockers.Add("Soft delete is enabled but no column carries the flag.");
            } else if (!columns.TryGetValue(binding.SoftDeleteColumnName, out var softDeleteColumn)) {
                result.Blockers.Add(
                    $"Soft delete column '{binding.SoftDeleteColumnName}' no longer exists on '{binding.DbSchemaFullName}'.");
            } else {
                result.Results.Add(TypeCompatibilityChecker.CheckSoftDeleteColumn(softDeleteColumn, engine).ToDto());
            }
        }

        if (binding.IsTwoWay) {
            var defaults = await _meta.GetWritebackDefaults(binding.Id).ConfigureAwait(false);
            var writeback = await AssessWriteback(binding, mappings, WritebackSettings.Stored(binding, mappings, defaults),
                includeEnvironment: true, cancellationToken).ConfigureAwait(false);

            if (writeback.CheckUnavailable is { } unavailable) {
                // Not proof of anything: the Binding is judged again when the worker next plans.
                result.Results.Add(WritebackNote($"Writeback could not be checked just now: {unavailable}"));
            } else {
                result.Blockers.AddRange(writeback.Issues.Select(i => i.Sql is null ? i.Message : $"{i.Message} Fix: {i.Sql}"));
                result.Results.AddRange(writeback.Warnings.Select(WritebackNote));
            }
        }

        result.CanActivate = result.Blockers.Count == 0
            && result.Results.All(r => r.Level != nameof(CompatibilityLevel.Error));

        return result;
    }

    private static CompatibilityResultDTO WritebackNote(string message) => new() {
        Level = nameof(CompatibilityLevel.Warning),
        Message = message
    };

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
    /// After an edit, an Active Binding that no longer validates is forced back to Incomplete rather than left
    /// claiming more than is true.
    /// </summary>
    /// <remarks>
    /// Forced Incomplete, never Inactive: Inactive records only the user's own choice to switch a Binding off,
    /// while a Binding broken by an edit — or by its Target Table changing under it — must show as needing
    /// attention. An Inactive or Incomplete Binding is left alone; it is not syncing either way.
    /// </remarks>
    private async Task<BindingDTO> ReconcileStateAfterEdit(CDCSchema binding, List<MappedField> mappings,
        CancellationToken cancellationToken) {
        if (binding.BindingState is BindingState.Active) {
            var validation = await Validate(binding, mappings, cancellationToken).ConfigureAwait(false);
            if (!validation.CanActivate) {
                await ForceIncomplete(binding).ConfigureAwait(false);
                _logger.LogWarning("Binding {BindingId} was set to Incomplete because it no longer validates: {Reason}",
                    binding.Id, DescribeFailure(binding, validation));
            }
        }

        return await LoadBindingDto(binding, mappings, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Takes an Active Binding out of the worker's plan and marks it as needing attention.</summary>
    private async Task ForceIncomplete(CDCSchema binding) {
        var at = _time.GetUtcNow().UtcDateTime;
        await _meta.ForceBindingIncomplete(binding.Id, at).ConfigureAwait(false);
        binding.BindingState = BindingState.Incomplete;
        binding.ForcedIncompleteAt = at;
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

    /// <summary>The stored Target Database Engine, without building a repository or decrypting anything.</summary>
    private async Task<TargetDatabaseEngine> Engine(CancellationToken cancellationToken) =>
        (await _target.GetAsync(cancellationToken).ConfigureAwait(false) ?? throw new NoTargetDatabaseException()).Engine;

    /// <summary>
    /// The Target Database repository, refusing an engine whose profile says it cannot be used yet. The
    /// profile is the one source of truth for that; nothing here lists engines by name.
    /// </summary>
    private async Task<IRepository> EnsureEngineSupported(CancellationToken cancellationToken) {
        var profile = _engines.For(await Engine(cancellationToken).ConfigureAwait(false));
        if (!profile.IsAvailable) {
            throw new ValidationException(
                $"{profile.UnavailableReason} Target tables cannot be read and Bindings cannot be configured against {profile.Engine}.");
        }
        return await _target.GetRepositoryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<PlatformEventChannelMemberEntity> RequireMember(int memberId, CancellationToken cancellationToken) =>
        await _channels.GetMemberByIdAsync(memberId, cancellationToken).ConfigureAwait(false)
        ?? throw new KeyNotFoundException($"Channel member {memberId} was not found.");

    private async Task<PlatformEventChannelEntity> RequireChannel(int channelId, CancellationToken cancellationToken) =>
        await _channels.GetChannelByIdAsync(channelId, cancellationToken).ConfigureAwait(false)
        ?? throw new KeyNotFoundException($"Channel {channelId} was not found.");

    private async Task<CDCSchema> RequireBinding(int bindingId) =>
        await _meta.GetSchemaById(bindingId).ConfigureAwait(false)
        ?? throw new KeyNotFoundException($"Binding {bindingId} was not found.");

    private async Task<TableMetadata> RequireTable(CDCSchema binding, CancellationToken cancellationToken) =>
        await LoadTable(binding.DbSchemaFullName, cancellationToken).ConfigureAwait(false)
        ?? throw new ValidationException(
            $"Target Table '{binding.DbSchemaFullName}' does not exist in the target database.");

    private async Task<TableMetadata?> LoadTable(string fullName, CancellationToken cancellationToken) {
        var (schemaName, tableName) = SplitFullName(fullName);
        var target = await _target.GetRepositoryAsync(cancellationToken).ConfigureAwait(false);
        return await target.GetTableMetadata(tableName, schemaName, cancellationToken).ConfigureAwait(false);
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

    private static string BuildFullName(string? schemaName, string tableName) {
        if (string.IsNullOrWhiteSpace(tableName)) {
            throw new ValidationException("A Binding needs the name of the Target Table it writes to.");
        }

        // databases like sqlite do not have schemas, so a schema name is optional. The table name is required.
        return string.IsNullOrWhiteSpace(schemaName) ? tableName.Trim() : $"{schemaName.Trim()}.{tableName.Trim()}";
    }

    private static (string? SchemaName, string TableName) SplitFullName(string fullName) {
        var separator = fullName.LastIndexOf('.');
        return separator <= 0
            ? (null, fullName)
            : (fullName[..separator], fullName[(separator + 1)..]);
    }

    /// <summary>
    /// Reduces a name to letters and digits so BillingAddressCity and billing_address_city match.
    /// </summary>
    private static string Normalize(string name) {
        var trimmed = name.EndsWith("__c", StringComparison.OrdinalIgnoreCase) ? name[..^3] : name;
        return new string(trimmed.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }

    #endregion

    #region Building DTOs

    /// <summary>
    /// Offers a conventional Key Mapping column against the Salesforce record ID field, so the one field that
    /// makes a Binding work is not left to be discovered.
    /// </summary>
    private static string? SuggestKeyColumn(EntityField field, string? mappedColumn) =>
        mappedColumn is null && field.FieldType is SalesforceFieldType.EntityId && field.Name == "Id"
            ? KeyColumnCandidates[0]
            : null;

    private async Task<BindingDTO> LoadBindingDto(CDCSchema binding, CancellationToken cancellationToken) =>
        await LoadBindingDto(binding, await ReadMappings(binding.Id).ConfigureAwait(false), cancellationToken)
            .ConfigureAwait(false);

    /// <summary>Reads the Channel Members pointing at a Binding, which its DTO lists alongside the mappings.</summary>
    private async Task<BindingDTO> LoadBindingDto(CDCSchema binding, List<MappedField> mappings,
        CancellationToken cancellationToken) {
        var members = await _channels.GetMembersByBindingIdAsync(binding.Id, cancellationToken).ConfigureAwait(false) ?? [];
        return binding.ToDto(mappings, members.Select(m => m.Id), CountFields(binding));
    }

    /// <summary>
    /// The flattened fields in the Avro Schema the Binding was last linked to. Read from the App Database, so
    /// listing Bindings never reaches Salesforce; null when the Binding has no usable schema linked.
    /// </summary>
    private static int? CountFields(CDCSchema binding) {
        if (binding.AvroSchema is not { SchemaJson: { Length: > 0 } } avro) {
            return null;
        }
        try {
            return ReadFields(avro).Count;
        } catch (Exception ex) when (ex is AvroException or ValidationException) {
            return null;
        }
    }

    #endregion
}
