namespace DTO;

/// <summary>
/// The connection details a user supplies, taken from the External Client App they created by hand.
/// </summary>
/// <remarks>
/// The Consumer Secret is the only secret here, it is needed only for the Bootstrap, and it is purged once
/// the first JWT token succeeds. No Salesforce password or security token is ever asked for: the application
/// authenticates with a keypair it generates for itself.
/// </remarks>
public record SaveOrgConnectionDTO {
    /// <summary>The External Client App's Consumer Key.</summary>
    public string ConsumerKey { get; set; } = "";

    /// <summary>The External Client App's Consumer Secret. Used for the Bootstrap, then purged.</summary>
    public string ConsumerSecret { get; set; } = "";

    /// <summary>The administrator who will approve the Bootstrap in their browser.</summary>
    public string AdministeringUsername { get; set; } = "";

    /// <summary>
    /// The user the event stream runs as. Their object permissions bound what the worker can see, so a
    /// Binding can still fail at runtime on an object this user cannot read.
    /// </summary>
    public string RunAsUsername { get; set; } = "";

    /// <summary>True for a sandbox, which selects test.salesforce.com over login.salesforce.com.</summary>
    public bool IsSandbox { get; set; }
}

/// <summary>
/// Confirms a Disconnect, which is the only destructive action in the application.
/// </summary>
/// <remarks>
/// The confirmation names counts rather than being a bare boolean, so a caller that has not looked at what it
/// is destroying cannot satisfy it by accident. The user is losing hand-built mapping work.
/// </remarks>
public record ConfirmDisconnectDTO {
    /// <summary>Must match the Binding count reported by the Disconnect preview.</summary>
    public int ExpectedBindings { get; set; }

    /// <summary>Must match the Field Mapping count reported by the Disconnect preview.</summary>
    public int ExpectedFieldMappings { get; set; }
}
