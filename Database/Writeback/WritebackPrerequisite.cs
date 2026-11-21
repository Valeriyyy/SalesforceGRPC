namespace Database.Writeback;

/// <summary>Something the user must change before Writeback can run, and the SQL that does it.</summary>
public sealed record WritebackPrerequisite(string Message, string? FixSql = null);
