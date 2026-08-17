using System.ComponentModel.DataAnnotations.Schema;

namespace Database.Models;

/// <summary>
/// A row in salesforce.platform_event_channel_members — the local mirror of a Salesforce
/// PlatformEventChannelMember, i.e. one event/entity carried on a channel.
/// </summary>
public class PlatformEventChannelMemberEntity {
    public int Id { get; set; }

    /// <summary>The local platform_event_channels.id this member belongs to.</summary>
    [Column("channel_id")]
    public int ChannelId { get; set; }

    /// <summary>The Salesforce ID of the member (0v8 prefix).</summary>
    [Column("sf_id")]
    public required string SfId { get; set; }

    /// <summary>Channel and entity joined with double underscores flattened to single.</summary>
    [Column("full_name")]
    public required string FullName { get; set; }

    [Column("developer_name")]
    public string? DeveloperName { get; set; }

    /// <summary>The entity name, e.g. "AccountChangeEvent".</summary>
    [Column("selected_entity")]
    public required string SelectedEntity { get; set; }

    [Column("filter_expression")]
    public string? FilterExpression { get; set; }

    /// <summary>The enriched field names as a JSON array, or null when none are configured.</summary>
    [Column("enriched_fields")]
    public string? EnrichedFields { get; set; }

    /// <summary>
    /// The Binding for this member's Entity, or null when the user has not bound it to a Target Table yet.
    /// </summary>
    /// <remarks>
    /// The foreign key is ON DELETE SET NULL, so removing a Channel Member orphans its Binding rather than
    /// destroying it — re-adding the member later restores the configuration instead of costing a rebuild.
    /// </remarks>
    [Column("cdc_schema_id")]
    public int? CdcSchemaId { get; set; }

    [Column("date_created")]
    public DateTime DateCreated { get; set; }

    [Column("date_updated")]
    public DateTime? DateUpdated { get; set; }

    [Column("last_synced_at")]
    public DateTime? LastSyncedAt { get; set; }

    public override string ToString() => $"{Id} {SfId} {FullName}";
}
