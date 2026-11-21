namespace StarPush.Tests.Worker;

/// <summary>A driver failure that never reached a server — the Target Database is down, not the data bad.</summary>
internal sealed class UnreachableDatabaseException : System.Data.Common.DbException {
    public UnreachableDatabaseException() : base("connection refused") { }
}
