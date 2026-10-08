using DTO;

namespace SalesforceGrpc.ViewModels;

/// <summary>
/// Where the Target Connection is. NotStarted shows the setup form; every other phase shows the summary.
/// </summary>
public enum TargetConnectionPhase {
    NotStarted,
    Connected,

    /// <summary>Stored, never proved: what was typed has not worked yet.</summary>
    Incomplete,

    /// <summary>Worked once and has since stopped.</summary>
    Failed
}

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

/// <summary>Every stored detail, shown back for review and edit. The password never is; only whether one is stored.</summary>
/// <param name="Engine">The API's engine name ("Postgres", "SqlServer", ...), sent back unchanged on save.</param>
public sealed record TargetConnectionDetailsView(
    string Engine,
    string? Host,
    int? Port,
    string? DatabaseName,
    string? Username,
    string? FilePath,
    IReadOnlyDictionary<string, string> Options,
    bool HasPassword,
    DateTime? LastConnectedAt,
    TargetDatabaseErrorView? LastError) {

    public static TargetConnectionDetailsView From(TargetConnectionDTO connection) => new(
        connection.Engine,
        connection.Host,
        connection.Port,
        connection.DatabaseName,
        connection.Username,
        connection.FilePath,
        connection.Options,
        connection.HasPassword,
        connection.LastConnectedAt,
        TargetDatabaseErrorView.From(connection.LastError));
}

/// <summary>A Target Database failure: the summary, and the driver's own words.</summary>
public sealed record TargetDatabaseErrorView(string Message, string RawResponse, DateTime? OccurredAt) {
    public static TargetDatabaseErrorView? From(TargetConnectionFailureDTO? failure) => failure is null
        ? null
        : new(failure.Message, failure.RawResponse, failure.OccurredAt);
}

/// <summary>One engine card and the form it renders. Unavailable engines are listed with their reason.</summary>
public sealed record EngineView(string Engine, bool IsAvailable, string? UnavailableReason, IReadOnlyList<FieldView> Fields) {
    public static EngineView From(EngineDefinitionDTO engine) => new(
        engine.Engine,
        engine.IsAvailable,
        engine.UnavailableReason,
        engine.Fields.Select(FieldView.From).ToList());
}

public enum FieldKindView {
    String,
    Int,
    Bool,
    Secret,
    Choice
}

/// <summary>One detail an engine asks for. Typed fields and options alike; the name says which.</summary>
public sealed record FieldView(string Name, string Label, FieldKindView Kind, bool Required, string? Default,
    IReadOnlyList<string>? Choices) {

    public static FieldView From(FieldDefinitionDTO field) => new(
        field.Name,
        field.Label,
        Enum.Parse<FieldKindView>(field.Kind),
        field.Required,
        field.Default,
        field.Choices);
}
