namespace Database.Models;

/// <summary>Whether a failed Writeback was creating a Salesforce record or updating one.</summary>
public enum WritebackOperation {
    Create,
    Update
}
