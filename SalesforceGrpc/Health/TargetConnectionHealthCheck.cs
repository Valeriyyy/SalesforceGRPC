using Application.Targets;
using Database.Models;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SalesforceGrpc.Health;

/// <summary>
/// Reports the Target Connection's state as service health.
/// </summary>
/// <remarks>
/// Strictly read-only: it reports stored state and never opens a database connection. A health endpoint that
/// proves connections gets polled every few seconds by an orchestrator and becomes its own load problem, which
/// is why <see cref="OrgConnectionHealthCheck"/> does not do it either. The worker's write failures and the
/// re-test endpoint are what turn the state; this only shows it.
/// <para>
/// A connection that has never been set up is Degraded rather than Unhealthy. A fresh install is not broken,
/// and an orchestrator restarting it would not help.
/// </para>
/// </remarks>
public sealed class TargetConnectionHealthCheck : IHealthCheck {
    public const string Name = "target-connection";

    private readonly ITargetConnectionProvider _targets;

    public TargetConnectionHealthCheck(ITargetConnectionProvider targets) {
        _targets = targets;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default) {
        TargetConnection? connection;
        try {
            connection = await _targets.GetAsync(cancellationToken).ConfigureAwait(false);
        } catch (Exception ex) {
            return HealthCheckResult.Unhealthy("The Target Connection could not be read from the App Database.", ex);
        }

        if (connection is null) {
            return HealthCheckResult.Degraded(
                "No Target Connection has been set up. The service is running and its API is available.");
        }

        var data = new Dictionary<string, object> {
            ["state"] = connection.ConnectionState.ToString(),
            ["engine"] = connection.Engine.ToString(),
            ["address"] = connection.Host ?? connection.FilePath ?? "",
            ["lastError"] = connection.LastError ?? ""
        };

        return connection.ConnectionState switch {
            ConnectionState.Connected => HealthCheckResult.Healthy(
                $"Connected to {connection.Engine} at {connection.Host ?? connection.FilePath}.", data),

            ConnectionState.Failed => HealthCheckResult.Unhealthy(
                $"The Target Database is failing: {connection.LastError}", data: data),

            _ => HealthCheckResult.Degraded(
                "The Target Connection has been set up but has never been proved. Correct it and re-test.", data: data)
        };
    }
}
