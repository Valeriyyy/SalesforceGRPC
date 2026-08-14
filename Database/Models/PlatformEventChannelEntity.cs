using System.ComponentModel.DataAnnotations.Schema;

namespace Database.Models;

/// <summary>
/// A row in salesforce.platform_event_channels — the local mirror of a Salesforce PlatformEventChannel.
/// Salesforce is the source of truth; this exists so the app can list and link channels without a
/// round-trip, and so channel members can be tied to sync configuration.
/// </summary>
public class PlatformEventChannelEntity {
    public int Id { get; set; }

    /// <summary>The Salesforce ID of the channel (0YL prefix).</summary>
    [Column("sf_id")]
    public required string SfId { get; set; }

    /// <summary>The metadata full name including the __chn suffix, e.g. "SalesEvents__chn".</summary>
    [Column("full_name")]
    public required string FullName { get; set; }

    [Column("developer_name")]
    public required string DeveloperName { get; set; }

    [Column("master_label")]
    public string? MasterLabel { get; set; }

    /// <summary>"data" for Change Data Capture, "event" for platform events.</summary>
    [Column("channel_type")]
    public required string ChannelType { get; set; }

    [Column("event_type")]
    public string? EventType { get; set; }

    [Column("namespace_prefix")]
    public string? NamespacePrefix { get; set; }

    [Column("manageable_state")]
    public string? ManageableState { get; set; }

    [Column("date_created")]
    public DateTime DateCreated { get; set; }

    [Column("date_updated")]
    public DateTime? DateUpdated { get; set; }

    [Column("last_synced_at")]
    public DateTime? LastSyncedAt { get; set; }

    /// <summary>Populated by the repository when a channel is loaded with its members.</summary>
    public List<PlatformEventChannelMemberEntity> Members { get; set; } = [];

    public override string ToString() => $"{Id} {SfId} {FullName}";
}
