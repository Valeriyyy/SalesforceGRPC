using Database.Models;

namespace Database.Repositories.Interfaces;

/// <summary>How many Bindings are in each <see cref="BindingState"/>.</summary>
public sealed record BindingStateCounts {
    public int Active { get; init; }

    /// <summary>Every Incomplete Binding, including those counted in <see cref="ForcedIncomplete"/>.</summary>
    public int Incomplete { get; init; }

    public int Inactive { get; init; }

    /// <summary>Incomplete Bindings the worker set back from Active because their Key Mapping lost its unique constraint.</summary>
    public int ForcedIncomplete { get; init; }
}
