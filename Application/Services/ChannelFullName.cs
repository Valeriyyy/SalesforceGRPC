namespace Application.Services;

/// <summary>
/// The shape of a Channel's Full Name: its developer name, namespaced when it has a namespace, and the
/// <see cref="Suffix"/> Salesforce requires.
/// </summary>
public static class ChannelFullName {
    public const string Suffix = "__chn";

    /// <summary>Builds the Full Name Salesforce gives a Channel, for when a response leaves it out.</summary>
    public static string For(string developerName, string? namespacePrefix) =>
        string.IsNullOrWhiteSpace(namespacePrefix)
            ? $"{developerName}{Suffix}"
            : $"{namespacePrefix}__{developerName}{Suffix}";
}
