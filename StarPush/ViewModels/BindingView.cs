namespace StarPush.ViewModels;

/// <summary>One Binding's editor. <see cref="Binding"/> is null when there is no such Binding.</summary>
public sealed record BindingView(IReadOnlyList<SetupStepView> Waiting, BindingEditorView? Binding) {
    public static BindingView WaitingOn(IReadOnlyList<SetupStepView> missing) => new(missing, null);

    public static BindingView NotFound() => new([], null);
}
