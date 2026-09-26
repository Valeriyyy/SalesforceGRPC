namespace Application.Bindings;

/// <summary>
/// How a Key Mapping is stored: as a Field Mapping under a sentinel Salesforce field name.
/// </summary>
/// <remarks>
/// Every strategy reads the sentinel to build its WHERE clause, so it is a contract with the worker, not an
/// implementation detail of whoever writes it.
/// </remarks>
public static class KeyMapping {
    /// <summary>The sentinel Salesforce field name the Key Mapping is stored under.</summary>
    public const string FieldName = "MappedSFKey";
}
