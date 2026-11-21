namespace StarPush.ViewModels;

/// <summary>One Entity on the list. <see cref="BindingId"/> and its details are null when it is not bound.</summary>
public sealed record BindingRowView(
    int MemberId,
    string Entity,
    int? BindingId,
    string? TargetTable,
    string? KeyMappingColumn,
    int FieldMappingCount,
    int? FieldCount,
    BindingRowState State,
    string Href,
    bool TwoWay,
    int WritebackFailureCount);
