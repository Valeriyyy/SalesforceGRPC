namespace Database.Models;

public static class FieldDirectionExtensions {
    /// <summary>True when the field's Salesforce value is written into the Target Column.</summary>
    public static bool CarriesInbound(this FieldDirection direction) => direction is not FieldDirection.WritebackOnly;

    /// <summary>True when the Target Column's value is written back to Salesforce.</summary>
    public static bool CarriesWriteback(this FieldDirection direction) => direction is not FieldDirection.InboundOnly;
}
