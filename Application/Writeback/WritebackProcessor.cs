using Application.Targets;
using Database.Models;
using Database.Repositories.Interfaces;
using Database.Writeback;
using Microsoft.Extensions.Logging;
using Salesforce.Clients;
using Salesforce.Writeback;
using System.Data.Common;
using System.Text.Json;

namespace Application.Writeback;

/// <summary>
/// Turns Target Table changes into Salesforce writes: the engine-neutral half of Writeback.
/// </summary>
/// <remarks>
/// <para>
/// A row that already holds a Salesforce record ID is written to that record; a row without one is upserted on
/// the Binding's Writeback Key, so a change delivered twice never creates the record twice (ADR 0007). An update
/// sends only the fields whose value actually changed, so a database edit to one field never overwrites another
/// field someone just edited in Salesforce.
/// </para>
/// <para>
/// Changes to the same row reach Salesforce in commit order: writes go out in waves holding at most one write
/// per row, and a row's next write waits for the wave after. Different rows share a call, up to 200.
/// </para>
/// <para>
/// A record Salesforce refuses becomes a Writeback Failure and is skipped. A failure that may pass (an outage,
/// API limits, a locked row) throws <see cref="WritebackTransientException"/> instead, so the caller confirms
/// nothing and sends the same changes again; every write here is safe to repeat.
/// </para>
/// </remarks>
public sealed class WritebackProcessor {
    private readonly ISalesforceRecordWriter _salesforce;
    private readonly IWritebackFailureRepository _failures;
    private readonly TimeProvider _time;
    private readonly ILogger<WritebackProcessor> _logger;

    public WritebackProcessor(ISalesforceRecordWriter salesforce, IWritebackFailureRepository failures,
        TimeProvider time, ILogger<WritebackProcessor> logger) {
        _salesforce = salesforce;
        _failures = failures;
        _time = time;
        _logger = logger;
    }

    /// <summary>Writes one Target Database transaction's changes to Salesforce.</summary>
    /// <exception cref="WritebackTransientException">Nothing may be confirmed; send the transaction again.</exception>
    public async Task<WritebackSummary> ApplyAsync(ChangeTransaction transaction, WritebackPlan plan, IRepository target,
        CancellationToken cancellationToken) {
        var writes = new List<PendingWrite>();
        var summary = new WritebackSummary();
        var deletes = new Dictionary<WritebackBindingPlan, int>();

        foreach (var change in transaction.Changes) {
            var binding = plan.BindingFor(change.Table);
            if (binding is null) {
                // A table that left the publication after this change was captured.
                continue;
            }

            if (change.Kind is RowChangeKind.Delete) {
                deletes[binding] = deletes.GetValueOrDefault(binding) + 1;
                summary.Skipped++;
                continue;
            }

            var write = await PrepareAsync(binding, change, cancellationToken).ConfigureAwait(false);
            if (write is null) {
                summary.Skipped++;
                continue;
            }
            writes.Add(write);
        }

        // One line per table, not per row: a bulk delete would otherwise log the same sentence hundreds of times.
        foreach (var (binding, count) in deletes) {
            _logger.LogInformation(
                "{Count} row(s) deleted from {Table}: deletes are not written back to Salesforce, so those {Object} records are unchanged",
                count, binding.TableName, binding.SourceObject);
        }

        foreach (var wave in Waves(writes)) {
            await SendAsync(wave, target, summary, cancellationToken).ConfigureAwait(false);
        }
        return summary;
    }

    /// <summary>
    /// Sends a row's current values again, as an insert of that row would be sent. Used by Retry on a Writeback
    /// Failure, after the user fixed the row.
    /// </summary>
    /// <returns>Null when the row reached Salesforce, otherwise the failure as now recorded.</returns>
    /// <exception cref="WritebackTransientException">Salesforce could not be reached; the failure is unchanged.</exception>
    public async Task<string?> RetryAsync(WritebackBindingPlan binding, IReadOnlyDictionary<string, object?> row,
        IRepository target, CancellationToken cancellationToken) {
        var change = new RowChange { Table = binding.Table, Kind = RowChangeKind.Insert, NewValues = row };
        var write = await PrepareAsync(binding, change, cancellationToken).ConfigureAwait(false);
        if (write is null) {
            return "The row could not be sent; see its Writeback Failure.";
        }

        var summary = new WritebackSummary();
        await SendAsync([write], target, summary, cancellationToken).ConfigureAwait(false);
        return summary.Failed > 0 ? summary.LastFailure : null;
    }

    #region Preparing writes

    private enum Operation {
        UpdateById,
        Upsert
    }

    private sealed class PendingWrite {
        public required WritebackBindingPlan Binding { get; init; }
        public required Operation Operation { get; init; }
        public required string RowKey { get; init; }
        public string? SalesforceId { get; init; }
        public object? KeyValue { get; init; }
        public string? KeyText { get; init; }
        public required Dictionary<string, object?> Fields { get; init; }
    }

    /// <summary>
    /// Decides what one change becomes in Salesforce, or null when it becomes nothing (no Writeback field
    /// changed) or could not be prepared (recorded as a Writeback Failure).
    /// </summary>
    private async Task<PendingWrite?> PrepareAsync(WritebackBindingPlan binding, RowChange change,
        CancellationToken cancellationToken) {
        var values = change.NewValues;
        var salesforceId = Text(values.GetValueOrDefault(binding.KeyMappingColumn)) is { Length: > 0 } id ? id : null;
        var keyValue = values.GetValueOrDefault(binding.WritebackKey.Column);
        var keyText = Text(keyValue);
        var rowKey = WritebackFailure.RowKeyFor(keyText, salesforceId);

        try {
            if (change.Kind is RowChangeKind.Update) {
                var changed = binding.Fields.Where(f => HasChanged(change, f.Column)).ToList();
                if (changed.Count == 0) {
                    // Only columns Writeback does not carry changed: the Key Mapping, an Inbound-only column,
                    // or an unmapped one.
                    return null;
                }

                if (salesforceId is not null) {
                    return new PendingWrite {
                        Binding = binding, Operation = Operation.UpdateById, RowKey = rowKey,
                        SalesforceId = salesforceId, KeyValue = keyValue, KeyText = keyText,
                        Fields = WithWritebackKey(binding, keyValue, Convert(changed, values, includeNulls: true))
                    };
                }
            } else if (salesforceId is not null) {
                // Inserted already holding a Salesforce ID — copied or restored — so it updates that record with
                // the fields the row has values for. Empty columns are left out: a null for each would wipe those
                // fields, and Salesforce refuses some fields on some records even as null (Person Account fields
                // such as FirstName on a business Account).
                return new PendingWrite {
                    Binding = binding, Operation = Operation.UpdateById, RowKey = rowKey,
                    SalesforceId = salesforceId, KeyValue = keyValue, KeyText = keyText,
                    Fields = WithWritebackKey(binding, keyValue, Convert(binding.Fields, values, includeNulls: false))
                };
            }

            // No Salesforce ID yet: the record may not exist, so the whole row is sent, matched on the
            // Writeback Key so that sending it twice still makes one record.
            if (keyText is null) {
                await RecordAsync(binding, rowKey, keyText, salesforceId, WritebackOperation.Create,
                    "MISSING_WRITEBACK_KEY",
                    $"The row has no value in its Writeback Key column '{binding.WritebackKey.Column}', so it cannot be matched to a Salesforce record.",
                    new Dictionary<string, object?>(), cancellationToken).ConfigureAwait(false);
                return null;
            }

            var fields = Convert(binding.Fields, values, includeNulls: false);
            foreach (var fallback in binding.Defaults) {
                fields.TryAdd(fallback.SalesforceField, fallback.Value);
            }

            return new PendingWrite {
                Binding = binding, Operation = Operation.Upsert, RowKey = rowKey,
                KeyValue = keyValue, KeyText = keyText,
                Fields = WithWritebackKey(binding, keyValue, fields)
            };
        } catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException) {
            await RecordAsync(binding, rowKey, keyText, salesforceId,
                salesforceId is null ? WritebackOperation.Create : WritebackOperation.Update, "CONVERSION_FAILED",
                $"A value in the row could not be converted for Salesforce: {ex.Message}",
                new Dictionary<string, object?>(), cancellationToken).ConfigureAwait(false);
            return null;
        }
    }

    private static Dictionary<string, object?> Convert(IEnumerable<WritebackField> fields,
        IReadOnlyDictionary<string, object?> values, bool includeNulls) {
        var converted = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var field in fields) {
            if (!values.TryGetValue(field.Column, out var value) || (value is null && !includeNulls)) {
                continue;
            }
            converted[field.SalesforceField] = WritebackValueConverter.ToSalesforce(value, field.SalesforceType);
        }
        return converted;
    }

    /// <summary>
    /// Adds the Writeback Key's value under its External ID field, so every record ends up carrying it —
    /// including records that came from Salesforce and never had one.
    /// </summary>
    private static Dictionary<string, object?> WithWritebackKey(WritebackBindingPlan binding, object? keyValue,
        Dictionary<string, object?> fields) {
        if (keyValue is not null) {
            fields[binding.WritebackKey.SalesforceField] =
                WritebackValueConverter.ToSalesforce(keyValue, binding.WritebackKey.SalesforceType);
        }
        return fields;
    }

    private static bool HasChanged(RowChange change, string column) {
        if (!change.NewValues.TryGetValue(column, out var after) || change.UnchangedColumns.Contains(column)) {
            return false;
        }
        // Without the old row, nothing can be ruled out.
        if (change.OldValues is null || !change.OldValues.TryGetValue(column, out var before)) {
            return true;
        }
        return !SameValue(before, after);
    }

    private static bool SameValue(object? a, object? b) => (a, b) switch {
        (null, null) => true,
        (null, _) or (_, null) => false,
        (byte[] x, byte[] y) => x.AsSpan().SequenceEqual(y),
        _ => a.Equals(b)
    };

    private static string? Text(object? value) => value switch {
        null or DBNull => null,
        string s => s,
        IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString()
    };

    /// <summary>
    /// Splits writes into waves in which no row appears twice, keeping each row's writes in commit order.
    /// </summary>
    private static IEnumerable<List<PendingWrite>> Waves(List<PendingWrite> writes) {
        var wave = new List<PendingWrite>();
        var rows = new HashSet<(int, string)>();
        foreach (var write in writes) {
            if (!rows.Add((write.Binding.BindingId, write.RowKey))) {
                yield return wave;
                wave = [];
                rows.Clear();
                rows.Add((write.Binding.BindingId, write.RowKey));
            }
            wave.Add(write);
        }
        if (wave.Count > 0) {
            yield return wave;
        }
    }

    #endregion

    #region Sending

    private async Task SendAsync(List<PendingWrite> wave, IRepository target, WritebackSummary summary,
        CancellationToken cancellationToken) {
        var calls = wave
            .GroupBy(w => (w.Binding.BindingId, w.Operation))
            .SelectMany(g => g.Chunk(ISalesforceRecordWriter.MaxRecordsPerCall));

        foreach (var chunk in calls) {
            var first = chunk[0];
            var records = chunk.Select(w => (IReadOnlyDictionary<string, object?>)RecordFor(w)).ToList();

            IReadOnlyList<RecordSaveResult> results;
            try {
                results = first.Operation is Operation.UpdateById
                    ? await _salesforce.UpdateAsync(first.Binding.SourceObject, records, cancellationToken).ConfigureAwait(false)
                    : await _salesforce.UpsertAsync(first.Binding.SourceObject, first.Binding.WritebackKey.SalesforceField,
                        records, cancellationToken).ConfigureAwait(false);
            } catch (SalesforceTransientException ex) {
                throw new WritebackTransientException(ex.Message, ex);
            } catch (SalesforceRequestRefusedException ex) {
                // The whole call was refused, so no record in it was written; each row is reported, because
                // each row is what the user will look for.
                foreach (var write in chunk) {
                    await RecordAsync(write, string.Join(",", ex.StatusCodes), ex.Message, cancellationToken).ConfigureAwait(false);
                    summary.Failed++;
                    summary.LastFailure = ex.Message;
                }
                continue;
            }

            // A record Salesforce asked to be sent again is retried with the whole transaction, after the rest
            // of this call has been dealt with; every write in it is safe to repeat.
            var transient = new List<string>();

            for (var i = 0; i < chunk.Length; i++) {
                var write = chunk[i];
                var result = results[i];

                if (result.Success) {
                    await SucceededAsync(write, result, target, cancellationToken).ConfigureAwait(false);
                    summary.Sent++;
                } else if (result.Errors.Count > 0 && result.Errors.All(e => e.IsTransient)) {
                    transient.AddRange(result.Errors.Select(e => e.StatusCode));
                } else {
                    var codes = string.Join(",", result.Errors.Select(e => e.StatusCode).Distinct());
                    var message = string.Join(" ", result.Errors.Select(e =>
                        e.Fields.Count > 0 ? $"{e.Message} ({string.Join(", ", e.Fields)})" : e.Message));
                    await RecordAsync(write, codes, message, cancellationToken).ConfigureAwait(false);
                    summary.Failed++;
                    summary.LastFailure = message;
                }
            }

            if (transient.Count > 0) {
                throw new WritebackTransientException(
                    $"Salesforce asked for {transient.Count} record(s) to be sent again ({string.Join(", ", transient.Distinct())}).");
            }
        }
    }

    private static Dictionary<string, object?> RecordFor(PendingWrite write) {
        if (write.Operation is not Operation.UpdateById) {
            return write.Fields;
        }
        var record = new Dictionary<string, object?>(StringComparer.Ordinal) { ["Id"] = write.SalesforceId };
        foreach (var (field, value) in write.Fields) {
            record[field] = value;
        }
        return record;
    }

    private async Task SucceededAsync(PendingWrite write, RecordSaveResult result, IRepository target,
        CancellationToken cancellationToken) {
        // A row with no Salesforce ID has just been matched or created: store the record's ID in its Key
        // Mapping column, through the tagged repository so this write is not read back as a change.
        if (write.SalesforceId is null && result.Id is { } newId && write.KeyValue is not null) {
            try {
                await target.UpdateByKey(write.Binding.TableName, write.Binding.WritebackKey.Column, write.KeyValue,
                    new Dictionary<string, object?> { [write.Binding.KeyMappingColumn] = newId },
                    cancellationToken).ConfigureAwait(false);
            } catch (DbException ex) {
                // Sending the row again matches the same record on its Writeback Key, then stores the ID.
                throw new WritebackTransientException(
                    $"{write.Binding.SourceObject} {newId} was written, but its ID could not be stored in {write.Binding.TableName}: {ex.Message}",
                    new TargetDatabaseWriteException(write.Binding.TableName, ex));
            }
        }

        await _failures.ClearAsync(write.Binding.BindingId, write.RowKey, cancellationToken).ConfigureAwait(false);
    }

    private Task RecordAsync(PendingWrite write, string statusCodes, string message, CancellationToken cancellationToken) =>
        RecordAsync(write.Binding, write.RowKey, write.KeyText, write.SalesforceId,
            write.Operation is Operation.UpdateById ? WritebackOperation.Update : WritebackOperation.Create,
            statusCodes, message, write.Fields, cancellationToken);

    private async Task RecordAsync(WritebackBindingPlan binding, string rowKey, string? keyText, string? salesforceId,
        WritebackOperation operation, string statusCodes, string message, Dictionary<string, object?> fields,
        CancellationToken cancellationToken) {
        _logger.LogWarning(
            "Salesforce refused a Writeback of row {RowKey} of {Table} to {Object} ({StatusCodes}): {Message}. Recorded as a Writeback Failure and skipped.",
            rowKey, binding.TableName, binding.SourceObject, statusCodes, message);

        var now = _time.GetUtcNow().UtcDateTime;
        await _failures.RecordAsync(new WritebackFailure {
            BindingId = binding.BindingId,
            RowKey = rowKey,
            WritebackKeyValue = keyText,
            SalesforceId = salesforceId,
            Operation = operation,
            StatusCodes = statusCodes,
            Message = message,
            ValuesSent = JsonSerializer.Serialize(fields),
            FirstFailedAt = now,
            LastFailedAt = now
        }, cancellationToken).ConfigureAwait(false);
    }

    #endregion
}
