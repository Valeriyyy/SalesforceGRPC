namespace StarPush.ViewModels;

/// <summary>
/// A Channel's Checkpoint, for the Resume choice and the note beside the Starting Point. Ages are already in
/// words, on the same hour scale the Overview uses.
/// </summary>
public sealed record CheckpointView(DateTime SavedAt, string Age, string ExpiresIn, bool Expired, bool CanResume);
