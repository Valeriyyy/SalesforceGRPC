namespace Database.Models;

/// <summary>Which direction of syncing something belongs to: Salesforce into the Target Database, or back.</summary>
public enum RunDirection {
    Inbound,
    Writeback
}
