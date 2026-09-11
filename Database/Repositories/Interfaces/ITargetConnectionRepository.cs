using Database.Models;

namespace Database.Repositories.Interfaces;

/// <summary>
/// Reads and writes the single Target Connection row.
/// </summary>
/// <remarks>
/// Nothing here caches. The connection is read through <c>ITargetConnectionProvider</c>, which invalidates
/// explicitly on save — the schema and mapping caches elsewhere in this codebase use a one-hour sliding
/// expiration with no invalidation hook, and a credential must not inherit that.
/// </remarks>
public interface ITargetConnectionRepository {
    /// <summary>The Target Connection, or null on a fresh install.</summary>
    Task<TargetConnection?> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates the Target Connection, or replaces the user-supplied half of the existing one.
    /// </summary>
    /// <remarks>
    /// Resets the connection to Incomplete and clears the error columns. State is the outcome of a proof, not
    /// something a caller supplies, so the service records it separately after this returns.
    /// </remarks>
    Task<TargetConnection> UpsertAsync(TargetConnection connection, CancellationToken cancellationToken = default);

    /// <summary>Records a successful proof, moving the connection to Connected.</summary>
    Task RecordSuccessAsync(DateTime at, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a failed proof, moving the connection to Failed and keeping the driver's text alongside the
    /// translated summary.
    /// </summary>
    Task RecordFailureAsync(string error, string? rawResponse, DateTime at, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a failed proof on a connection that has never worked, keeping it Incomplete with the error.
    /// </summary>
    /// <remarks>
    /// Never yet proved is not the same as broken. Incomplete with an error means "what you typed did not
    /// work"; Failed means "what used to work no longer does". The user's remedy differs.
    /// </remarks>
    Task RecordIncompleteAsync(string error, string? rawResponse, DateTime at, CancellationToken cancellationToken = default);

    /// <summary>How many Bindings and Field Mappings a repoint would destroy.</summary>
    Task<BindingCounts> CountBindingsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Destroys every Binding and replaces the Target Connection with one already proved, in one transaction.
    /// </summary>
    /// <remarks>
    /// One transaction because a half-repoint leaves the application describing Bindings against a database
    /// it is no longer pointed at — and the new row is written as Connected in the same statement, because
    /// the caller proved it before calling and a separate success write could fail in between. Field
    /// Mappings go through the cascade; Channel Members keep their rows and lose their Binding reference,
    /// because the Mirror records what exists in Salesforce and a repoint says nothing about that. See
    /// docs/adr/0004.
    /// </remarks>
    Task<(TargetConnection Connection, BindingCounts Destroyed)> RepointAsync(TargetConnection connection,
        DateTime provedAt, CancellationToken cancellationToken = default);
}

/// <summary>What a repoint destroys, so the user can be told before they lose it.</summary>
public sealed record BindingCounts {
    public int Bindings { get; init; }
    public int FieldMappings { get; init; }
}
