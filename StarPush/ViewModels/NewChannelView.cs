namespace StarPush.ViewModels;

/// <summary>Creating a Channel. Making it the Primary Channel is the default only when there is none.</summary>
public sealed record NewChannelView(bool OrgConnectionReady, string? PrimaryChannelFullName, bool MakePrimaryByDefault) {
    public static NewChannelView For(bool orgConnectionReady, string? primaryChannelFullName) =>
        new(orgConnectionReady, primaryChannelFullName, MakePrimaryByDefault: primaryChannelFullName is null);
}
