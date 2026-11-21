using DTO;

namespace StarPush.ViewModels;

/// <summary>
/// The parts of the editor read from the Target Database and the Entity's Avro Schema.
/// </summary>
/// <param name="SoftDeleteColumns">Exactly the columns the soft delete setting would accept.</param>
/// <param name="SuggestedKeyColumn">
/// A uniquely-constrained column conventionally named for the Salesforce record ID, offered — never applied —
/// while there is no Key Mapping.
/// </param>
/// <param name="Prefill">
/// The Field Mappings a Binding with none starts its draft from: every name match, so the first visit is a
/// review. Empty once any Field Mapping is saved.
/// </param>
/// <param name="Validation">The stored Binding's validation, as it stands now.</param>
public sealed record BindingTargetView(
    IReadOnlyList<BindableFieldDTO> Fields,
    IReadOnlyList<TargetColumnDTO> Columns,
    IReadOnlyList<string> SoftDeleteColumns,
    string? SuggestedKeyColumn,
    IReadOnlyList<FieldMappingDTO> Prefill,
    BindingValidationDTO Validation) {

    /// <summary>Normalised names conventionally given to the column holding the Salesforce record ID.</summary>
    private static readonly string[] KeyColumnNames = ["sfid", "salesforceid"];

    /// <summary>The Salesforce record ID belongs in the Key Mapping, never in a Field Mapping.</summary>
    private const string RecordIdField = "Id";

    public static BindingTargetView For(BindingDTO binding, IReadOnlyList<BindableFieldDTO> fields,
        IReadOnlyList<TargetColumnDTO> columns, IReadOnlyList<string> softDeleteColumns,
        BindingValidationDTO validation) {
        var suggestedKey = binding.KeyMappingColumn is null
            ? columns.FirstOrDefault(c => c.IsUnique && KeyColumnNames.Contains(Normalize(c.ColumnName)))?.ColumnName
            : null;

        return new BindingTargetView(fields, columns, softDeleteColumns, suggestedKey,
            binding.FieldMappingCount == 0 ? NameMatches(fields, columns, binding.KeyMappingColumn ?? suggestedKey) : [],
            validation);
    }

    private static List<FieldMappingDTO> NameMatches(IReadOnlyList<BindableFieldDTO> fields,
        IReadOnlyList<TargetColumnDTO> columns, string? keyColumn) {
        var existing = columns.Select(c => c.ColumnName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (keyColumn is not null) {
            taken.Add(keyColumn);
        }

        var prefill = new List<FieldMappingDTO>();
        foreach (var field in fields) {
            if (field.Name == RecordIdField || field.SuggestedColumnName is not { } column
                || !existing.Contains(column) || !taken.Add(column)) {
                continue;
            }
            prefill.Add(new FieldMappingDTO { SalesforceFieldName = field.Name, TargetColumnName = column });
        }
        return prefill;
    }

    private static string Normalize(string name) =>
        new(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
