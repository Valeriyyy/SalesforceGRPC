using Database.Models;

namespace Application.Bindings;

/// <summary>
/// What the worker should subscribe to and what it should do with what arrives.
/// </summary>
/// <remarks>
/// This is the whole of the worker's configuration decision, pulled out of the streaming loop so it can be
/// unit tested. The loop itself stays untested.
/// </remarks>
public sealed record SubscriptionPlan {
    /// <summary>
    /// The Pub/Sub topic for the Primary Channel, or null when no Primary Channel has been selected.
    /// </summary>
    public string? TopicName { get; init; }

    /// <summary>The Primary Channel's full name, for logging. Null when there is no Primary Channel.</summary>
    public string? ChannelFullName { get; init; }

    /// <summary>
    /// The Active Bindings for the Entities this channel carries, keyed by Salesforce Avro Schema Id.
    /// </summary>
    /// <remarks>
    /// Keyed by Schema Id because that is the only identifier an incoming event carries. Incomplete and
    /// Inactive Bindings are absent, so an event for one is skipped rather than written somewhere the user
    /// did not choose.
    /// </remarks>
    public Dictionary<string, CDCSchema> ActiveBindingsBySchemaId { get; init; } = [];

    /// <summary>Entity names the Primary Channel carries, whatever the state of their Bindings.</summary>
    public HashSet<string> ChannelEntityNames { get; init; } = [];

    /// <summary>True when there is a Primary Channel to subscribe to.</summary>
    public bool HasChannel => !string.IsNullOrWhiteSpace(TopicName);

    /// <summary>Builds the plan the worker uses when nothing is configured yet.</summary>
    public static SubscriptionPlan Empty => new();
}
