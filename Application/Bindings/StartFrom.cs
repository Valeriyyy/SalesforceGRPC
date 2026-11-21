namespace Application.Bindings;

/// <summary>Where a subscription begins: after a Checkpoint, or at one end of the stream.</summary>
public enum StartFrom {
    /// <summary>Right after the Channel's Checkpoint.</summary>
    Resume,

    /// <summary>The oldest event Salesforce still keeps, up to 72 hours back.</summary>
    Earliest,

    /// <summary>Only events published from now on.</summary>
    Latest
}
