namespace Database.Writeback;

/// <summary>A Target Table, named the way its engine names it (schema null where the engine has none).</summary>
public sealed record WritebackTable(string? Schema, string Table) {
    public override string ToString() => Schema is null ? Table : $"{Schema}.{Table}";
}
