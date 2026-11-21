namespace StarPush.Tests;

/// <summary>
/// Tests that create, drop or list tables in the Postgres test database run one class at a time, so one never
/// lists a table another is about to drop.
/// </summary>
[CollectionDefinition(Name)]
public sealed class PostgresTargetDatabase {
    public const string Name = "Postgres target database";
}
