namespace StarPush.ViewModels;

/// <summary>
/// What every page hands its Svelte component: the shell's data and the page's own view class. See ADR 0006.
/// </summary>
public sealed record PageView<TPage>(ShellView Shell, TPage Page);
