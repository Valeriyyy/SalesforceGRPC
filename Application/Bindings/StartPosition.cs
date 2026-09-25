using Database.Models;

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

/// <summary>
/// Where the worker begins reading the Primary Channel.
/// </summary>
/// <remarks>
/// Deliberately does not judge whether a Checkpoint has expired. Salesforce is the authority on whether a
/// position is still valid, and the worker handles its rejection by falling back to Earliest.
/// </remarks>
public sealed record StartPosition(StartFrom From, Checkpoint? Checkpoint = null) {
    public static StartPosition Latest { get; } = new(StartFrom.Latest);

    public static StartPosition Earliest { get; } = new(StartFrom.Earliest);

    public static StartPosition ResumeAfter(Checkpoint checkpoint) =>
        new(StartFrom.Resume, checkpoint ?? throw new ArgumentNullException(nameof(checkpoint)));
}
