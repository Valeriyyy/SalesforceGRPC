using DTO;
using System.ComponentModel.DataAnnotations;

namespace Application.Writeback;

/// <summary>
/// Switching a Binding to Two-way was refused. Carries every reason at once, each database prerequisite with
/// the SQL that fixes it, so the user can hand the whole list to a DBA in one go.
/// </summary>
public sealed class WritebackRefusedException : ValidationException {
    public IReadOnlyList<WritebackIssueDTO> Issues { get; }

    public WritebackRefusedException(IReadOnlyList<WritebackIssueDTO> issues)
        : base($"Two-way cannot be switched on: {string.Join(" ", issues.Select(i => i.Message))}") {
        Issues = issues;
    }
}
