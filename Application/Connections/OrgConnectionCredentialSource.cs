using Application.Bindings;
using Database.Repositories.Interfaces;
using Microsoft.Extensions.Logging;
using Salesforce.Auth;

namespace Application.Connections;

/// <summary>
/// Thrown when a token exchange succeeds against an org that is not the one this application is connected to.
/// </summary>
/// <remarks>
/// Carries both org ids because the first question the user will ask is "which one is which", and the answer
/// determines whether they meant to do this.
/// </remarks>
public sealed class OrgMismatchException : InvalidOperationException {
    public OrgMismatchException(string storedOrgId, string discoveredOrgId)
        : base($"These credentials authenticate against org {discoveredOrgId}, but this application is " +
               $"connected to org {storedOrgId}. Nothing has been changed. To move to a different org, use " +
               "Disconnect — it will destroy the Bindings, Field Mappings and cached schemas belonging to " +
               "the current org, which is why it is a deliberate action rather than something a credential " +
               "edit does silently.") {
        StoredOrgId = storedOrgId;
        DiscoveredOrgId = discoveredOrgId;
    }

    public string StoredOrgId { get; }
    public string DiscoveredOrgId { get; }
}

/// <summary>
/// Implements the Salesforce project's credential seam over the stored Org Connection.
/// </summary>
/// <remarks>
/// This is where the Salesforce project's <see cref="ISalesforceCredentialSource"/> meets the App Database,
/// and where the connection's state machine actually turns: Incomplete or Failed becomes Connected on a
/// successful token, anything becomes Failed on a rejected one.
/// </remarks>
public sealed class OrgConnectionCredentialSource : ISalesforceCredentialSource {
    private readonly IOrgConnectionProvider _provider;
    private readonly IOrgConnectionRepository _repository;
    private readonly IConfigurationChangeSignal _changeSignal;
    private readonly ILogger<OrgConnectionCredentialSource> _logger;
    private readonly TimeProvider _time;

    public OrgConnectionCredentialSource(IOrgConnectionProvider provider, IOrgConnectionRepository repository,
        IConfigurationChangeSignal changeSignal, ILogger<OrgConnectionCredentialSource> logger, TimeProvider time) {
        _provider = provider;
        _repository = repository;
        _changeSignal = changeSignal;
        _logger = logger;
        _time = time;
    }

    public Task<SalesforceCredentials?> GetCredentialsAsync(CancellationToken cancellationToken = default) =>
        _provider.GetCredentialsAsync(cancellationToken);

    public async Task<string?> GetOrgUrlAsync(CancellationToken cancellationToken = default) {
        var connection = await _provider.GetAsync(cancellationToken).ConfigureAwait(false);
        return connection?.OrgUrl;
    }

    /// <inheritdoc />
    public async Task RecordTokenSuccessAsync(string orgUrl, string? orgId, CancellationToken cancellationToken = default) {
        var connection = await _provider.GetAsync(cancellationToken).ConfigureAwait(false);
        if (connection is null) {
            // Nothing to record against. Not an error: a token cannot have been requested without a
            // connection, so this only happens if one was deleted mid-flight.
            return;
        }

        if (string.IsNullOrWhiteSpace(orgId)) {
            // The org id feeds the Pub/Sub tenantid header, so a connection without one cannot stream. Left
            // as a failure rather than a partial success, because "Connected but the worker will not start"
            // is the least explicable state the application could be in.
            await RecordTokenFailureAsync(
                "The token exchange succeeded but carried no org id, which the Pub/Sub API requires as its " +
                "tenantid. Verify the connection again; if this persists the org id must be read from " +
                "/services/oauth2/userinfo instead.", cancellationToken).ConfigureAwait(false);
            return;
        }

        // The guard that makes Disconnect the only route between orgs rather than merely the intended one.
        // Without it, pointing the Run-as User at a different org's user connects cleanly and every Binding
        // starts writing that org's records into tables mapped for the old one — corruption with no error.
        if (!string.IsNullOrWhiteSpace(connection.OrgId) &&
            !string.Equals(connection.OrgId, orgId, StringComparison.OrdinalIgnoreCase)) {
            _logger.LogError(
                "Refusing a token for org {DiscoveredOrgId}; this application is connected to org {StoredOrgId}",
                orgId, connection.OrgId);
            throw new OrgMismatchException(connection.OrgId, orgId);
        }

        var firstConnection = connection.ConnectionState != Database.Models.ConnectionState.Connected;

        await _repository.RecordSuccessAsync(orgUrl, orgId, _time.GetUtcNow().UtcDateTime, cancellationToken)
            .ConfigureAwait(false);
        _provider.Invalidate();

        if (firstConnection) {
            _logger.LogInformation("Org Connection is now Connected to org {OrgId} at {OrgUrl}", orgId, orgUrl);
            // The worker idles while there is no usable connection, so it has to be told rather than
            // discovering this whenever it next happens to re-plan.
            _changeSignal.Signal();
        }
    }

    public async Task RecordTokenFailureAsync(string error, CancellationToken cancellationToken = default) {
        var connection = await _provider.GetAsync(cancellationToken).ConfigureAwait(false);
        if (connection is null) {
            return;
        }

        _logger.LogError("Salesforce rejected the token request: {Error}", error);

        await _repository.RecordFailureAsync(error, _time.GetUtcNow().UtcDateTime, cancellationToken)
            .ConfigureAwait(false);
        _provider.Invalidate();
    }
}
