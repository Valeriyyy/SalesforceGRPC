using Database.Models;
using DTO;

namespace Application.Mappers;

/// <summary>Turns Target Database table and column metadata into their DTOs.</summary>
public static class TargetMetadataMapper {
    public static TargetTableDTO ToDto(this TableMetadata table, string fullName, string? boundEntityName) => new() {
        SchemaName = table.SchemaName,
        TableName = table.TableName,
        FullName = fullName,
        BoundEntityName = boundEntityName
    };

    public static TargetColumnDTO ToDto(this ColumnMetadata column, string? mappedSalesforceFieldName) => new() {
        ColumnName = column.ColumnName,
        DataType = column.DataType,
        IsNullable = column.IsNullable,
        MaxLength = column.MaxLength,
        IsUnique = column.IsUnique,
        IsPrimaryKey = column.IsPrimaryKey,
        MappedSalesforceFieldName = mappedSalesforceFieldName
    };
}
