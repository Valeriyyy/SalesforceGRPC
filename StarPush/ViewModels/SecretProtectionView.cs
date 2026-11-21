using DTO;

namespace StarPush.ViewModels;

/// <summary>
/// When not Ready, the page replaces the form with the guidance: re-running setup would orphan the External
/// Client App.
/// </summary>
public sealed record SecretProtectionView(SecretProtectionStatus Status, string ProtectingKey, string? Guidance) {
    public static SecretProtectionView From(SecretProtectionDTO protection) => new(
        Enum.TryParse<SecretProtectionStatus>(protection.Status, out var status) ? status : SecretProtectionStatus.Unreadable,
        protection.ProtectingKey,
        protection.Guidance);
}
