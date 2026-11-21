using Database.Models;

namespace StarPush.ViewModels;

/// <summary>The Binding a Channel Member's Entity lands through.</summary>
public sealed record MemberBindingView(int Id, string TargetTable, BindingState State);
