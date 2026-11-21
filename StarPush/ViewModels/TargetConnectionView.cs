using DTO;

namespace StarPush.ViewModels;

/// <summary>
/// The Target Connection page. The server decides the phase and whether the identity is locked; the component
/// only renders, and holds the client-side mode (viewing, editing, repointing) on top.
/// </summary>
/// <param name="Bindings">What a repoint would destroy, echoed back as its confirmation.</param>
/// <param name="IdentityLocked">
/// True once any Binding exists: engine, host, database name and file path can then only change through a
/// repoint. See docs/adr/0004 and its amendment.
/// </param>
/// <param name="OrgConnectionOk">For the informational note only. The Target Connection can be set up first.</param>
public sealed record TargetConnectionView(
    TargetConnectionPhase Phase,
    TargetConnectionDetailsView? Connection,
    IReadOnlyList<EngineView> Engines,
    int Bindings,
    int FieldMappings,
    bool IdentityLocked,
    bool OrgConnectionOk,
    SecretProtectionView SecretProtection) {

    public static TargetConnectionView For(TargetConnectionDTO connection, IReadOnlyList<EngineDefinitionDTO> engines,
        RepointPreviewDTO counts, bool orgConnectionOk) => new(
        PhaseOf(connection),
        connection.Exists ? TargetConnectionDetailsView.From(connection) : null,
        engines.Select(EngineView.From).ToList(),
        counts.Bindings,
        counts.FieldMappings,
        connection.Exists && counts.Bindings > 0,
        orgConnectionOk,
        SecretProtectionView.From(connection.SecretProtection));

    private static TargetConnectionPhase PhaseOf(TargetConnectionDTO connection) => connection switch {
        { Exists: false } => TargetConnectionPhase.NotStarted,
        { ConnectionState: "Connected" } => TargetConnectionPhase.Connected,
        { ConnectionState: "Failed" } => TargetConnectionPhase.Failed,
        _ => TargetConnectionPhase.Incomplete
    };
}
