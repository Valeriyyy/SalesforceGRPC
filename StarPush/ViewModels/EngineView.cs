using DTO;

namespace StarPush.ViewModels;

/// <summary>One engine card and the form it renders. Unavailable engines are listed with their reason.</summary>
public sealed record EngineView(string Engine, bool IsAvailable, string? UnavailableReason, bool SupportsWriteback,
    IReadOnlyList<FieldView> Fields) {
    public static EngineView From(EngineDefinitionDTO engine) => new(
        engine.Engine,
        engine.IsAvailable,
        engine.UnavailableReason,
        engine.SupportsWriteback,
        engine.Fields.Select(FieldView.From).ToList());
}
