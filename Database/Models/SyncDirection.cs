namespace Database.Models;

/// <summary>
/// Whether a Binding only carries Salesforce changes into its Target Table, or also carries the Target Table's
/// changes back to Salesforce.
/// </summary>
public enum SyncDirection {
    /// <summary>Salesforce into the Target Table only. The default, and the only behaviour before Writeback.</summary>
    Inbound = 0,

    /// <summary>Inbound, plus Writeback of rows inserted or updated by anything other than this application.</summary>
    TwoWay = 1
}

/// <summary>
/// Within a Two-way Binding, which way one Field Mapping carries values.
/// </summary>
/// <remarks>
/// What is stored is the user's choice. A direction forced by what either side can accept outranks it and is
/// computed, never stored.
/// </remarks>
public enum FieldDirection {
    TwoWay = 0,
    InboundOnly = 1,
    WritebackOnly = 2
}

public static class FieldDirectionExtensions {
    /// <summary>True when the field's Salesforce value is written into the Target Column.</summary>
    public static bool CarriesInbound(this FieldDirection direction) => direction is not FieldDirection.WritebackOnly;

    /// <summary>True when the Target Column's value is written back to Salesforce.</summary>
    public static bool CarriesWriteback(this FieldDirection direction) => direction is not FieldDirection.InboundOnly;
}
