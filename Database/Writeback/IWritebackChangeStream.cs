namespace Database.Writeback;

/// <summary>
/// A database's changes, one committed transaction at a time, excluding this application's own writes.
/// </summary>
public interface IWritebackChangeStream : IAsyncDisposable {
    IAsyncEnumerable<ChangeTransaction> ReadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Confirms everything up to a position has been sent to Salesforce or recorded as a Writeback Failure, so
    /// the database may let it go and the stream never resends it.
    /// </summary>
    Task ConfirmAsync(WritebackPosition position, CancellationToken cancellationToken = default);
}
