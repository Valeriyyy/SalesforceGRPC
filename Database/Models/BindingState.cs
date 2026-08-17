namespace Database.Models;

/// <summary>
/// Whether a Binding is being built, switched on, or switched off.
/// </summary>
/// <remarks>
/// Only <see cref="Active"/> Bindings are applied by the worker. <see cref="Inactive"/> is the user's off
/// switch and preserves every Field Mapping, so switching a Binding back on costs one call rather than a
/// rebuild — which is the whole reason this is three states and not a boolean.
/// </remarks>
public enum BindingState {
    /// <summary>Still being built. Never applied, and cannot be until it validates.</summary>
    Incomplete = 0,

    /// <summary>Validated and switched on. The worker applies this Entity's events.</summary>
    Active = 1,

    /// <summary>Fully configured but switched off. Field Mappings and Key Mapping are kept intact.</summary>
    Inactive = 2
}
