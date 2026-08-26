namespace Application.Connections;

/// <summary>
/// What Self-Configuration did, or why it did not run.
/// </summary>
public sealed record SelfConfigurationResult {
    /// <summary>True when the org was configured. False is not necessarily an error — see <see cref="ManualSteps"/>.</summary>
    public required bool Configured { get; init; }

    /// <summary>What happened, in one line, for the setup screen and the log.</summary>
    public required string Summary { get; init; }

    /// <summary>
    /// The steps the user must perform in Setup themselves, when Self-Configuration could not.
    /// </summary>
    /// <remarks>
    /// Empty when everything was configured. Populated rather than left to documentation because a user who
    /// reaches this point is mid-setup and needs the list in front of them.
    /// </remarks>
    public IReadOnlyList<string> ManualSteps { get; init; } = [];
}

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

/// <summary>
/// The Manual Registration fallback: configures nothing and says exactly what to do in Setup instead.
/// </summary>
/// <remarks>
/// This is a real path, not a placeholder for one. Some orgs will not permit the deploy, and this is what
/// they get; it is also the recovery path when a deploy half-succeeds.
/// </remarks>
public sealed class ManualRegistrationConfigurator : IOrgSelfConfigurator {
    public Task<SelfConfigurationResult> ConfigureAsync(string accessToken, string instanceUrl,
        string certificatePem, CancellationToken cancellationToken = default) {
        return Task.FromResult(new SelfConfigurationResult {
            Configured = false,
            Summary = "Salesforce was not configured automatically. Complete the remaining steps in Setup, " +
                      "then verify the connection.",
            ManualSteps = [
                "On the External Client App, upload this application's Signing Certificate (download it from " +
                "the connection endpoint) into the OAuth settings' Digital Signatures field.",
                "Set the app's Permitted Users policy to admin-approved / pre-authorized.",
                "Create a permission set granting API Enabled and access to the External Client App, and " +
                "assign it to both the Run-as User and the Administering User. Both identities need " +
                "pre-authorization: the application signs assertions for each.",
                "Enable 'Allow client secret readback' on the External Client App settings, if you want the " +
                "Consumer Key to be readable through the API later.",
                "Return here and verify the connection. Salesforce propagates these changes with a delay, so " +
                "give it a minute before deciding something is wrong."
            ]
        });
    }
}
