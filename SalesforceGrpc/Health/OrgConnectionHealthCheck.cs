using Application.Connections;
using Database.Models;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SalesforceGrpc.Health;

/// <summary>
/// Reports the Org Connection's state as service health.
/// </summary>
/// <remarks>
/// The worker no longer shuts the host down when the stream fails, which is the right call — credentials are
/// managed through this host's API, so a service that kills itself on a credential failure cannot be repaired
/// through the UI that repairs credentials. The cost of that is a broken connection becoming a process that
/// runs forever, logs, and does nothing: the quietest possible failure, and now the default one. This is what
/// makes it visible to whatever is watching.
/// <para>
/// A connection that has never been set up is Degraded rather than Unhealthy. A fresh install is not broken,
/// and an orchestrator restarting it would not help.
/// </para>
/// </remarks>
public sealed class OrgConnectionHealthCheck : IHealthCheck {
    public const string Name = "salesforce-org-connection";

    private readonly IOrgConnectionProvider _connections;

    public OrgConnectionHealthCheck(IOrgConnectionProvider connections) {
        _connections = connections;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default) {
        OrgConnection? connection;
        try {
            connection = await _connections.GetAsync(cancellationToken).ConfigureAwait(false);
        } catch (Exception ex) {
            return HealthCheckResult.Unhealthy("The Org Connection could not be read from the App Database.", ex);
        }

        if (connection is null) {
            return HealthCheckResult.Degraded(
                "No Salesforce Org Connection has been set up. The service is running and its API is available.");
        }

        var data = new Dictionary<string, object> {
            ["state"] = connection.ConnectionState.ToString(),
            ["orgId"] = connection.OrgId ?? "(not discovered)",
            ["certificateExpiresAt"] = connection.CertificateExpiresAt,
            ["certificateFingerprint"] = connection.CertificateFingerprint
        };

        return connection.ConnectionState switch {
            ConnectionState.Connected => HealthCheckResult.Healthy(
                $"Connected to org {connection.OrgId}.", data),

            ConnectionState.Failed => HealthCheckResult.Unhealthy(
                $"The Salesforce connection is failing: {connection.LastError}", data: data),

            _ => HealthCheckResult.Degraded(
                "The Org Connection has been set up but has never authenticated. Complete the Bootstrap or " +
                "verify the connection.", data: data)
        };
    }
}
