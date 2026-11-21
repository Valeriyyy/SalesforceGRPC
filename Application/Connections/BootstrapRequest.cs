namespace Application.Connections;

/// <summary>What one Bootstrap's authorize request needs, and its callback must present again.</summary>
public sealed record BootstrapRequest(string State, string CodeVerifier);
