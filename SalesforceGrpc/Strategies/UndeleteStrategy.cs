using Avro;
using Avro.Generic;
using com.sforce.eventbus;
using Database.Models;
using Database.Repositories.Interfaces;

namespace SalesforceGrpc.Strategies;

/// <summary>
/// Restores records Salesforce has undeleted from the recycle bin.
/// </summary>
/// <remarks>
/// Only a Binding with soft delete enabled can act on this. An undelete event carries no field values, so a
/// row that was hard deleted cannot be reconstructed from it — the honest response there is to say so rather
/// than write a half-empty row.
/// </remarks>
public class UndeleteStrategy : IEventStrategy {
    public ChangeType ChangeType => ChangeType.UNDELETE;

    private readonly ILogger<UndeleteStrategy> _logger;
    private readonly IMetaRepository _db;
    private readonly IRepository _dataRepo;

    public UndeleteStrategy(ILogger<UndeleteStrategy> logger, IRepository dataRepo, IMetaRepository db) {
        _logger = logger;
        _dataRepo = dataRepo;
        _db = db;
    }

    public async Task ProcessEvent(GenericRecord record, Schema schema, CDCSchema dbSchema,
        CancellationToken cancellationToken) {
        if (!record.TryGetValue("ChangeEventHeader", out var changeEventHeaderObj) ||
            changeEventHeaderObj is not GenericRecord changeEventHeader) {
            _logger.LogWarning("No ChangeEventHeader found in record");
            return;
        }

        if (!changeEventHeader.TryGetValue("recordIds", out var recordIdsObj) ||
            recordIdsObj is not object[] recordIds || recordIds.Length == 0) {
            _logger.LogWarning("No record IDs found in ChangeEventHeader");
            return;
        }

        var recordIdStrings = recordIds.Select(id => id.ToString() ?? string.Empty).ToList();

        if (!dbSchema.SoftDeleteEnabled || string.IsNullOrWhiteSpace(dbSchema.SoftDeleteColumnName)) {
            _logger.LogWarning(
                "Undelete of {ObjectType} records {RecordIds} was skipped: soft delete is off for this Binding, so the rows were removed and the event carries no values to rebuild them from.",
                dbSchema.EntityName, string.Join(",", recordIdStrings));
            return;
        }

        var fieldMappings = await _db.GetCachedMapping(dbSchema.Id, cancellationToken).ConfigureAwait(false);
        var sfKeyFieldName = fieldMappings.GetValueOrDefault("MappedSFKey");
        if (sfKeyFieldName == null) {
            throw new Exception($"Failed to find salesforce id mapping fieldname for {dbSchema.Id}");
        }

        try {
            var restoredCount = await _dataRepo.UnDelete(dbSchema.DbSchemaFullName, sfKeyFieldName,
                dbSchema.SoftDeleteColumnName, recordIdStrings).ConfigureAwait(false);
            _logger.LogInformation("Restored {RestoredCount} records in {ObjectType}", restoredCount, dbSchema.EntityName);
        } catch (Exception e) {
            _logger.LogCritical(e, "Failed to restore the following {ObjectType} records: {recordIds}",
                dbSchema.EntityName, string.Join(",", recordIdStrings));
        }
    }
}
