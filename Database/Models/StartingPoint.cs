namespace Database.Models;

/// <summary>
/// Where the worker begins reading a Channel that has no Checkpoint.
/// </summary>
/// <remarks>
/// Plays no part once a Checkpoint exists. An expired Checkpoint falls back to <see cref="Earliest"/>
/// whatever this says, because after an outage the aim is to lose as little as possible.
/// </remarks>
public enum StartingPoint {
    /// <summary>Only changes from now on. The default.</summary>
    Latest = 0,

    /// <summary>Everything Salesforce still keeps, up to 72 hours back.</summary>
    Earliest = 1
}
