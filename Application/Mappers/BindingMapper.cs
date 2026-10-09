using Application.Bindings;
using Database.Models;
using DTO;
using Salesforce.Avro;

namespace Application.Mappers;

/// <summary>
/// Turns Bindings, bindable Entity fields and Type Compatibility results into their DTOs.
/// </summary>
/// <remarks>
/// Pure: every value a DTO carries is handed in. Loading and deciding what to show stays in the service.
/// </remarks>
public static class BindingMapper {
    public static BindingDTO ToDto(this CDCSchema binding, IReadOnlyCollection<MappedField> mappings,
        IEnumerable<int> channelMemberIds) => new() {
        Id = binding.Id,
        EntityName = binding.EntityName,
        TargetTable = binding.DbSchemaFullName,
        State = binding.BindingState.ToString(),
        NeedsAttention = binding.BindingState is BindingState.Incomplete && binding.ForcedIncompleteAt is not null,
        KeyMappingColumn = mappings.FirstOrDefault(m => m.SalesforceFieldName == KeyMapping.FieldName)?.TargetFieldName,
        FieldMappingCount = mappings.Count(m => m.SalesforceFieldName != KeyMapping.FieldName),
        SoftDeleteEnabled = binding.SoftDeleteEnabled,
        SoftDeleteColumnName = binding.SoftDeleteColumnName,
        AvroSchemaId = binding.SchemaId,
        ChannelMemberIds = channelMemberIds.ToList()
    };

    public static BindableFieldDTO ToDto(this EntityField field, string? mappedColumn, string? suggestedColumn) => new() {
        Name = field.Name,
        FieldType = field.FieldType.ToString(),
        AvroType = field.AvroType,
        IsNullable = field.IsNullable,
        ParentName = field.ParentName,
        MappedColumnName = mappedColumn,
        SuggestedColumnName = suggestedColumn
    };

    public static CompatibilityResultDTO ToDto(this FieldCompatibility compatibility) => new() {
        SalesforceFieldName = compatibility.SalesforceFieldName,
        TargetColumnName = compatibility.TargetColumnName,
        FieldType = compatibility.FieldType.ToString(),
        TargetDataType = compatibility.TargetDataType,
        Level = compatibility.Level.ToString(),
        Message = compatibility.Message
    };
}
