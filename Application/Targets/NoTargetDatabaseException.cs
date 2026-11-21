namespace Application.Targets;

/// <summary>
/// Thrown when something needs the Target Database and no Target Connection is configured.
/// </summary>
public sealed class NoTargetDatabaseException : InvalidOperationException {
    public NoTargetDatabaseException()
        : base("No Target Connection has been set up, so there is no database to write to. " +
               "Configure one through the API.") { }
}
