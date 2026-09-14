namespace Database.Models;

/// <summary>
/// Which of the supported database engines a Target Connection addresses.
/// </summary>
/// <remarks>
/// Persisted as its name, not its number, so the App Database reads without a lookup table. Renaming a member
/// is therefore a data migration, not a refactor.
/// </remarks>
public enum TargetDatabaseEngine {
    Postgres,
    SqlServer,
    MySql,
    Sqlite
}
