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
    public static InboundFailureExplanation For(InboundFailure failure, IReadOnlyList<TargetColumnDTO>? columns,
        string? bindingTable = null) {
        var table = failure.TargetTable ?? bindingTable ?? "the Target Table";
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
            "22001" => ValueTooLong(failure, table, values, columns),
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
            "23502" => MissingValue(failure, table, columns),
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
        IReadOnlyDictionary<string, string> values, IReadOnlyList<TargetColumnDTO>? columns) {
        var limitInMessage = LimitIn(failure.Message);

        // The database does not say which column; the values that do not fit do.
        var offenders = values
            .Select(v => (Column: v.Key, Length: v.Value.Length,
                Max: columns?.FirstOrDefault(c => c.ColumnName == v.Key)?.MaxLength ?? limitInMessage))
            .Where(v => v.Max is { } max && v.Length > max)
            .ToList();

        if (offenders.Count > 0) {
            var problem = string.Join(" ", offenders.Select(o =>
                $"Salesforce sent a {o.Length}-character value for {FieldFor(o.Column, columns)}, but {o.Column} " +
                $"allows at most {o.Max} characters."));
            var steps = offenders
                .Select(o => $"Widen the column so values like this fit: ALTER TABLE {table} ALTER COLUMN {o.Column} TYPE varchar({o.Length}); " +
                             "or use a larger size if the field can hold more in Salesforce.")
                .ToList();
            return ($"{problem} The Target Database refused the whole change.", steps);
        }

        var candidates = columns?
            .Where(c => limitInMessage is not null && c.MaxLength == limitInMessage)
            .Select(c => c.ColumnName)
            .ToList() ?? [];
        var limit = limitInMessage is { } l ? $"{l} characters" : "its column's size";
        return (
            $"A text value in this change is longer than its column allows (at most {limit}), so the Target Database " +
            "refused the whole change.",
            [
                candidates.Count > 0
                    ? $"Widen the column it was meant for in {table}. Columns of that size: {string.Join(", ", candidates)}."
                    : $"Find the column of that size in {table} and widen it, so values like this fit."
            ]);
    }

    private static (string, List<string>) MissingValue(InboundFailure failure, string table,
        IReadOnlyList<TargetColumnDTO>? columns) {
        var column = failure.TargetColumn ?? ColumnIn(failure.Message);
        if (column is null) {
            return ($"A column in {table} requires a value, but this change left it empty.",
                ["Allow empty values in that column, give it a default, or map a Salesforce field that is always filled."]);
        }

        return (
            $"{column} in {table} requires a value, but this change left {FieldFor(column, columns)} empty.",
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

    /// <summary>"the Salesforce field Title (column title)", or just the column when no field maps to it.</summary>
    private static string FieldFor(string column, IReadOnlyList<TargetColumnDTO>? columns) =>
        columns?.FirstOrDefault(c => c.ColumnName == column)?.MappedSalesforceFieldName is { } field
            ? $"{field} (column {column})"
            : $"column {column}";

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
