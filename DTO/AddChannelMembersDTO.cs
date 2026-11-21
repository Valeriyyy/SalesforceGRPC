namespace DTO;

/// <summary>
/// Request to add several Entities to a Channel in one submission. All are added or none are.
/// </summary>
public record AddChannelMembersDTO {
    public List<CreateChannelMemberDTO> Members { get; set; } = [];
}
