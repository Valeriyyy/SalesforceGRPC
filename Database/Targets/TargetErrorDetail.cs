using System.Data.Common;
using Npgsql;

namespace Database.Targets;

/// <summary>What a Target Database driver says about which column or constraint it refused a write over.</summary>
/// <remarks>
/// Only Postgres reports these as fields; the other engines put them in the message, which the user still sees.
/// </remarks>
public static class TargetErrorDetail {
    public static (string? Column, string? Constraint) Of(DbException exception) => exception switch {
        PostgresException postgres => (postgres.ColumnName, postgres.ConstraintName),
        _ => (null, null)
    };
}
