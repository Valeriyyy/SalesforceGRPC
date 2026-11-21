namespace DTO;

/// <summary>One Entity added to or removed from a Channel by a Resync.</summary>
public record ResyncMemberChangeDTO {
    public string Channel { get; set; } = "";
    public string SelectedEntity { get; set; } = "";

    /// <summary>
    /// The Target Table of the Binding this removal set Inactive, when it was removed from the Primary Channel and
    /// its Binding was Active; otherwise null.
    /// </summary>
    public string? BindingSetInactive { get; set; }
}
