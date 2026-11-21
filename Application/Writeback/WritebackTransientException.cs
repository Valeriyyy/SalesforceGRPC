namespace Application.Writeback;

/// <summary>
/// Writeback could not finish for a reason that may pass. Nothing is confirmed, and the same changes are sent
/// again; every Writeback is safe to repeat.
/// </summary>
public sealed class WritebackTransientException : Exception {
    public WritebackTransientException(string message, Exception? inner = null) : base(message, inner) { }
}
