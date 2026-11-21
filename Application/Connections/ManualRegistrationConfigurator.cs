namespace Application.Connections;

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
