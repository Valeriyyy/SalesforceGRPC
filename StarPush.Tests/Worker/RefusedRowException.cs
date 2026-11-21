namespace StarPush.Tests.Worker;

/// <summary>A server that is up but refuses this row — a constraint, a value too long — not an outage.</summary>
internal sealed class RefusedRowException : System.Data.Common.DbException {
    public RefusedRowException() : base("value too long for type character varying(40)") { }

    public override string SqlState => "22001";
}
