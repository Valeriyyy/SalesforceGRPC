using System.Text.Json;
using System.Text.RegularExpressions;
using Database.Models;
using DTO;

namespace StarPush.ViewModels;

/// <summary>
/// What went wrong with an Inbound Failure, and what to do about it, in the Binding's own terms: the Salesforce
/// field, the Target Column, the lengths involved — not the database's error code.
/// </summary>
/// <remarks>
/// Built from what was recorded when the event failed (SQLSTATE, the column or constraint the database named, the
/// values being written) and, when the Target Database could be read for the page, its column sizes and mappings.
/// Every explanation ends with the same last step, because fixing the cause never repairs the record that was
/// skipped: StarPush does not edit data, so the user corrects it and then dismisses the failure.
/// </remarks>
/// <param name="Problem">One or two sentences: what was refused, and why.</param>
/// <param name="Steps">What to do, in order. The last step is always correcting the skipped record.</param>
public sealed partial record InboundFailureExplanation(string Problem, IReadOnlyList<string> Steps) {

    /// <param name="bindingTable">The Binding's Target Table, for failures recorded before their table was kept.</param>
    /// <param name="fields">The Entity's fields, to name a compound's part the way Salesforce shows it.</param>
    public static InboundFailureExplanation For(InboundFailure failure, IReadOnlyList<TargetColumnDTO>? columns,
        string? bindingTable = null, IReadOnlyList<BindableFieldDTO>? fields = null) {
        var table = failure.TargetTable ?? bindingTable ?? "the Target Table";
        var names = new FieldNames(columns, fields);
        var values = ReadValues(failure.ValuesSent);
        var sqlState = failure.SqlState ?? SqlStateIn(failure.Message);
        var finalStep = CorrectTheRecord(failure, table);

        // Never reached the database: the event itself could not be read or converted.
        if (failure.TargetTable is null && sqlState is null) {
            return new(
                "StarPush couldn't read or convert this change from Salesforce, so nothing was written for it.",
                [
                    "This is not something your database settings can fix: the event arrived in a shape StarPush " +
                    "did not expect. Check the application log around the time it failed for the full error.",
                    finalStep
                ]);
        }

        var (problem, steps) = sqlState switch {
            "22001" => ValueTooLong(failure, table, values, columns, names),
            "22003" => (
                $"A number in this change is too large for its column{In(failure.TargetColumn)} in {table}, so the " +
                "Target Database refused the whole change.",
                new List<string> { $"Give the column more precision in {table} (for example a wider numeric type), so numbers like this fit." }),
            "22P02" or "22007" or "22008" or "22018" => (
                $"A value in this change couldn't be stored in its column's type{In(failure.TargetColumn)} in {table}, " +
                "so the Target Database refused the whole change.",
                new List<string> {
                    "Check the Field Mappings on this page: the Salesforce field's type may not suit the column. " +
                    "Change the column's type, or map a different field to it."
                }),
            "23502" => MissingValue(failure, table, names),
            "23505" => (
                $"Another row in {table} already has the same value{Under(failure.ConstraintName)}, and the column " +
                "only allows one row per value.",
                new List<string> {
                    $"Find the other row in {table} with that value and decide which one is right; change or remove the duplicate."
                }),
            "23503" => (
                $"This change refers to a row that doesn't exist{Under(failure.ConstraintName)}, so {table} refused it.",
                new List<string> {
                    "Make sure the row it points to exists first (for example by syncing that Entity too), or relax the foreign key."
                }),
            "23514" => (
                $"A value in this change breaks a rule set on {table}{Under(failure.ConstraintName)}.",
                new List<string> { "Relax the check constraint in the database, or correct the value in Salesforce." }),
            "42703" or "42P01" => (
                $"This Binding writes to a {(sqlState == "42P01" ? "table" : "column")} that no longer exists in the " +
                "Target Database, so every change for it will fail the same way.",
                new List<string> {
                    "Open the Field Mappings on this page and map the field to a column that exists, or restore the " +
                    "column in the database."
                }),
            "42501" => (
                $"The database user StarPush connects as isn't allowed to write to {table}.",
                new List<string> { $"Grant that user INSERT, UPDATE and DELETE on {table}." }),
            _ => (
                $"The Target Database refused this change{(sqlState is null ? "" : $" (error {sqlState})")}.",
                new List<string> {
                    "The database's own message is under Details below; it names what it objected to. Fix that in " +
                    "the database or in Salesforce."
                })
        };

        steps.Add(finalStep);
        return new(problem, steps);
    }

    private static (string, List<string>) ValueTooLong(InboundFailure failure, string table,
        IReadOnlyDictionary<string, string> values, IReadOnlyList<TargetColumnDTO>? columns, FieldNames names) {
        var limitInMessage = LimitIn(failure.Message);

        // The database does not say which column; the values that do not fit do.
        var offenders = values
            .Select(v => (Column: v.Key, Value: v.Value,
                Max: columns?.FirstOrDefault(c => c.ColumnName == v.Key)?.MaxLength ?? limitInMessage))
            .Where(v => v.Max is { } max && v.Value.Length > max)
            .ToList();

        if (offenders.Count > 0) {
            var problem = string.Join(" ", offenders.Select(o =>
                $"Salesforce sent {Quote(o.Value)} ({o.Value.Length} characters) for {names.Describe(o.Column)}, " +
                $"but the column only holds {o.Max}."));
            var steps = offenders
                .Select(o => $"Widen {o.Column} so values like this fit — for example ALTER TABLE {table} ALTER COLUMN " +
                             $"{o.Column} TYPE varchar({o.Value.Length}); — or use the field's full length in Salesforce " +
                             "so no future value can overflow it.")
                .ToList();
            return ($"{problem} The Target Database refused the whole change, so none of it was written.", steps);
        }

        // Recorded before failures kept the values written: the message gives the limit, not the column.
        var limit = limitInMessage is { } l ? $"{l} characters" : "its column's size";
        var candidates = columns?
            .Where(c => limitInMessage is not null && c.MaxLength == limitInMessage && c.MappedSalesforceFieldName is not null)
            .Select(c => names.Describe(c.ColumnName))
            .ToList() ?? [];
        var record = failure.RecordIds.Length == 1 ? $"record {failure.RecordIds[0]}" : "the record";
        return (
            $"A text value in this change was longer than its column allows (at most {limit}), so the Target Database " +
            "refused the whole change. This failure was recorded before StarPush kept the values it tried to write, " +
            "so it can't tell which field it was.",
            [
                candidates.Count > 0
                    ? $"Open {record} in Salesforce and check which of these is longer than {limit}: " +
                      $"{string.Join("; ", candidates)}. Then widen that column in {table}."
                    : $"Open {record} in Salesforce, find the value longer than {limit}, and widen its column in {table}."
            ]);
    }

    private static (string, List<string>) MissingValue(InboundFailure failure, string table, FieldNames names) {
        var column = failure.TargetColumn ?? ColumnIn(failure.Message);
        if (column is null) {
            return ($"A column in {table} requires a value, but this change left it empty.",
                ["Allow empty values in that column, give it a default, or map a Salesforce field that is always filled."]);
        }

        return (
            $"{column} in {table} requires a value, but this change left {names.Describe(column)} empty.",
            [
                $"Allow empty values (ALTER TABLE {table} ALTER COLUMN {column} DROP NOT NULL), give the column a " +
                "default, or map a Salesforce field that is always filled."
            ]);
    }

    private static string CorrectTheRecord(InboundFailure failure, string table) {
        var records = failure.RecordIds.Length switch {
            0 => "the records it was for",
            1 => $"record {failure.RecordIds[0]}",
            _ => $"records {string.Join(", ", failure.RecordIds)}"
        };
        return $"Fixing the cause only helps later changes. This change was skipped and won't be retried, so correct " +
               $"{records} in {table} yourself, then Dismiss this failure.";
    }

    /// <summary>The start of a value, quoted, so a long one stays readable.</summary>
    private static string Quote(string value) => value.Length <= 30 ? $"\"{value}\"" : $"\"{value[..30]}…\"";

    /// <summary>
    /// Names a Target Column by the Salesforce field mapped to it, the way Salesforce shows that field: a compound's
    /// part as "LastName (part of Name)" rather than the flattened "NameLastName" it is stored under.
    /// </summary>
    private sealed class FieldNames(IReadOnlyList<TargetColumnDTO>? columns, IReadOnlyList<BindableFieldDTO>? fields) {
        public string Describe(string column) => Field(column) is { } field ? $"{field}, stored in {column}" : $"column {column}";

        private string? Field(string column) {
            if (fields?.FirstOrDefault(f => f.MappedColumnName == column) is { } field) {
                return Readable(field.Name, field.ParentName);
            }
            return columns?.FirstOrDefault(c => c.ColumnName == column)?.MappedSalesforceFieldName is { } mapped
                ? Readable(mapped, fields?.FirstOrDefault(f => f.Name == mapped)?.ParentName)
                : null;
        }

        private static string Readable(string name, string? parent) =>
            parent is not null && name.StartsWith(parent, StringComparison.Ordinal) && name.Length > parent.Length
                ? $"{name[parent.Length..]} (part of {parent})"
                : name;
    }

    private static string In(string? column) => column is null ? "" : $" ({column})";
    private static string Under(string? constraint) => constraint is null ? "" : $" (constraint {constraint})";

    /// <summary>The column values as text, ignoring nulls; empty when none were recorded.</summary>
    private static Dictionary<string, string> ReadValues(string json) {
        try {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.EnumerateObject()
                .Where(p => p.Value.ValueKind is not JsonValueKind.Null)
                .ToDictionary(p => p.Name,
                    p => p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString()! : p.Value.GetRawText());
        } catch (JsonException) {
            return [];
        }
    }

    /// <summary>Npgsql prefixes its messages with the SQLSTATE: "22001: value too long ...".</summary>
    private static string? SqlStateIn(string message) =>
        SqlStatePrefix().Match(message) is { Success: true } m ? m.Groups[1].Value : null;

    private static int? LimitIn(string message) =>
        LengthLimit().Match(message) is { Success: true } m ? int.Parse(m.Groups[1].Value) : null;

    private static string? ColumnIn(string message) =>
        QuotedColumn().Match(message) is { Success: true } m ? m.Groups[1].Value : null;

    [GeneratedRegex(@"^([0-9A-Z]{5}): ")]
    private static partial Regex SqlStatePrefix();

    [GeneratedRegex(@"\((\d+)\)")]
    private static partial Regex LengthLimit();

    [GeneratedRegex("column \"([^\"]+)\"")]
    private static partial Regex QuotedColumn();
}
