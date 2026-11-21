namespace Database.Writeback;

/// <summary>An opaque position in a database's change stream.</summary>
public readonly record struct WritebackPosition(ulong Value);
