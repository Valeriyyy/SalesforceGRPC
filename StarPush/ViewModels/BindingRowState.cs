namespace StarPush.ViewModels;

/// <summary>
/// Where a Binding stands, as the Bindings pages show it. <see cref="NeedsAttention"/> is an Incomplete Binding
/// that was Active and was forced back — by the worker or by an edit — set apart from one never finished.
/// </summary>
public enum BindingRowState {
    Unbound,
    Incomplete,
    NeedsAttention,
    Active,
    Inactive
}
