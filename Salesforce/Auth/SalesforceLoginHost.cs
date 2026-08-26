namespace Salesforce.Auth;

/// <summary>
/// Which Salesforce login host an org authenticates against, and every URL derived from that choice.
/// </summary>
/// <remarks>
/// A type rather than a bool plus string comparisons, because getting this wrong is one of the most common
/// causes of an opaque <c>invalid_grant</c> and the value has to agree in four places at once: the assertion's
/// <c>aud</c> claim, the token endpoint, the authorize endpoint, and the advice given when Salesforce refuses.
/// Deriving "is this a sandbox" back out of a URL by substring, which is what the alternative amounts to, is
/// how those four drift apart.
/// </remarks>
public sealed record SalesforceLoginHost {
    public static readonly SalesforceLoginHost Production = new("https://login.salesforce.com", false);
    public static readonly SalesforceLoginHost Sandbox = new("https://test.salesforce.com", true);

    private SalesforceLoginHost(string url, bool isSandbox) {
        Url = url;
        IsSandbox = isSandbox;
    }

    public static SalesforceLoginHost For(bool isSandbox) => isSandbox ? Sandbox : Production;

    /// <summary>The host's base URL, which is also the assertion's <c>aud</c> claim.</summary>
    public string Url { get; }

    public bool IsSandbox { get; }

    public string TokenEndpoint => $"{Url}/services/oauth2/token";

    public string AuthorizeEndpoint => $"{Url}/services/oauth2/authorize";

    /// <summary>The other host — what the connection would use if this setting were wrong.</summary>
    public SalesforceLoginHost Opposite => IsSandbox ? Production : Sandbox;

    /// <summary>"sandbox" or "production", for messages the user reads.</summary>
    public string Description => IsSandbox ? "sandbox" : "production";

    public override string ToString() => Url;
}
