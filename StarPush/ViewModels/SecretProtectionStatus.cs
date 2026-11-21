using DTO;

namespace StarPush.ViewModels;

/// <summary>Whether stored credentials can be written and read. See <see cref="SecretProtectionDTO"/>.</summary>
public enum SecretProtectionStatus {
    Ready,
    NotConfigured,
    Unreadable
}
