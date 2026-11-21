namespace Application.Writeback;

/// <summary>A Writeback Default, converted to the value sent when a record is created.</summary>
public sealed record WritebackDefaultValue(string SalesforceField, object? Value);
