namespace Application.Connections;

/// <summary>
/// Installs everything this application needs onto the External Client App the user created, using the
/// borrowed Bootstrap session.
/// </summary>
/// <remarks>
/// The seam is here and the implementation is not, deliberately. Self-Configuration rests on an assumption
/// that could not be confirmed from Salesforce's own documentation — that an OAuth access token is accepted
/// as the Metadata API <c>SessionHeader</c> — and that has to be proved against a real org before the deploy
/// is worth writing. Until then <see cref="ManualRegistrationConfigurator"/> stands in and tells the user
/// what to do by hand, which is the documented fallback for orgs that would refuse the deploy anyway.
/// </remarks>
public interface IOrgSelfConfigurator {
    Task<SelfConfigurationResult> ConfigureAsync(string accessToken, string instanceUrl,
        string certificatePem, CancellationToken cancellationToken = default);
}
