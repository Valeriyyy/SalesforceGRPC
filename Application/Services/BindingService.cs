using Application.Bindings;
using Application.Connections;
using Application.Mappers;
using Application.Services.Interfaces;
using Application.Targets;
using Avro;
using Database.Models;
using Database.Repositories;
using Database.Repositories.Interfaces;
using Database.Targets;
using DTO;
using Microsoft.Extensions.Logging;
using Salesforce.Avro;
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
        _time = time;
        _logger = logger;
    }

    #region Discovery

    public async Task<IReadOnlyList<BindableFieldDTO>> GetBindableFieldsAsync(int memberId,
        CancellationToken cancellationToken = default) {
        var member = await RequireMember(memberId, cancellationToken).ConfigureAwait(false);
        var fields = await ReadEntityFields(member.SelectedEntity, cancellationToken).ConfigureAwait(false);

        // Without a Binding there is no Target Table to map against, so the fields stand alone.
        if (member.CdcSchemaId is not { } bindingId) {
            return fields.Select(f => f.ToDto(null, SuggestKeyColumn(f, null))).ToList();
        }

        var binding = await _meta.GetSchemaById(bindingId).ConfigureAwait(false);
        var mappings = await ReadMappings(bindingId).ConfigureAwait(false);
        var columns = binding is null
            ? []
            : (await LoadTable(binding.DbSchemaFullName, cancellationToken).ConfigureAwait(false))?.Columns ?? [];

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

        var tables = await target.GetSchemaMetadata(schemaName, cancellationToken).ConfigureAwait(false);
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

            var check = TypeCompatibilityChecker.CheckSoftDeleteColumn(column, engine);
            if (check.Level is CompatibilityLevel.Error) {
                throw new ValidationException(check.Message);
            }

            columnName = column.ColumnName;
        }

        await _meta.UpdateBinding(bindingId, binding.DbSchemaFullName, dto.Enabled, columnName).ConfigureAwait(false);
        binding.SoftDeleteEnabled = dto.Enabled;
        binding.SoftDeleteColumnName = columnName;
        _changeSignal.Signal();

        return await LoadBindingDto(binding, cancellationToken).ConfigureAwait(false);
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

    #endregion

    #region Primary channel

    public async Task<int?> GetPrimaryChannelIdAsync(CancellationToken cancellationToken = default) {
        var channel = await _channels.GetPrimaryChannelAsync(cancellationToken).ConfigureAwait(false);
        return channel?.Id;
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
                await DemoteIfKeyMappingIsNotUnique(binding, cancellationToken).ConfigureAwait(false)) {
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
    /// Sets an Active Binding back to Incomplete when its Key Mapping column has lost its unique constraint.
    /// </summary>
    /// <remarks>
    /// Delivery is at-least-once (ADR 0005), so a repeated CREATE has to land on the row it already wrote,
    /// and only a unique key lets it. Without one, the repeat fails the write and replays the same batch
    /// forever. The column may have lost its constraint after activation (a DBA dropped it), or the Binding
    /// may date from before the constraint was required. A table that cannot be read is not proof of
    /// anything, so the Binding is kept and the worker's own failure handling takes over.
    /// </remarks>
    private async Task<bool> DemoteIfKeyMappingIsNotUnique(CDCSchema binding, CancellationToken cancellationToken) {
        ColumnMetadata? keyColumn;
        try {
            var keyMapping = (await ReadMappings(binding.Id).ConfigureAwait(false))
                .FirstOrDefault(m => m.SalesforceFieldName == KeyMapping.FieldName);
            if (keyMapping is null) {
                return false;
            }

            var table = await LoadTable(binding.DbSchemaFullName, cancellationToken).ConfigureAwait(false);
            keyColumn = table?.Columns.FirstOrDefault(c =>
                string.Equals(c.ColumnName, keyMapping.TargetFieldName, StringComparison.OrdinalIgnoreCase));
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            _logger.LogWarning(ex, "Could not read the Key Mapping column of Binding {BindingId} ({Table}); keeping it Active",
                binding.Id, binding.DbSchemaFullName);
            return false;
        }

        if (keyColumn is null || keyColumn.IsUnique) {
            return false;
        }

        await _meta.ForceBindingIncomplete(binding.Id, _time.GetUtcNow().UtcDateTime).ConfigureAwait(false);
        binding.BindingState = BindingState.Incomplete;
        _logger.LogWarning(
            "Binding {BindingId} ({Entity} -> {Table}) was set to Incomplete because its Key Mapping column {Column} " +
            "has no unique constraint. Change events can arrive more than once, and a repeated CREATE needs a unique " +
            "key to land on the row it already wrote. Add a unique constraint or primary key to {Column}, then activate it again.",
            binding.Id, binding.EntityName, binding.DbSchemaFullName, keyColumn.ColumnName, keyColumn.ColumnName);
        return true;
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

        return await LoadBindingDto(binding, mappings, cancellationToken).ConfigureAwait(false);
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
        return binding.ToDto(mappings, members.Select(m => m.Id));
    }

    #endregion
}
