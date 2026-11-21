namespace Application.Writeback;

/// <summary>
/// A Target Column and the Salesforce field it is written to, with that field's describe type, which decides
/// how the column's value is sent.
/// </summary>
public sealed record WritebackField(string Column, string SalesforceField, string SalesforceType);
