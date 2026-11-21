namespace Database.Models;

/// <summary>
/// A value the user set for a Salesforce field that is required on create but that no Field Mapping supplies.
/// </summary>
/// <remarks>
/// Stored as text and converted by the field's Salesforce type when a record is created. Never invented by this
/// application: without one, the field is left for Salesforce automation to fill.
/// </remarks>
public class WritebackDefault {
    public int Id { get; set; }
    public int BindingId { get; set; }

    /// <summary>The Salesforce field's API name, as the REST API knows it (FirstName, not NameFirstName).</summary>
    public required string SalesforceFieldName { get; set; }

    public required string Value { get; set; }
}
