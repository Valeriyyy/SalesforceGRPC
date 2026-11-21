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
