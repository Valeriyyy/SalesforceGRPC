using Database.Models;
using Database.Repositories;
using Salesforce.Avro;

namespace Application.Bindings;

/// <summary>
/// Decides whether a Salesforce Field Type can be written into a Target Column.
/// </summary>
/// <remarks>
/// A pure function of Field Type, column metadata and dialect, so it is exhaustively testable without a
/// database. Errors are deliberately narrow — reserved for mappings that cannot succeed at all — because this
/// matrix has to be right about four SQL dialects, and every gap in it would otherwise become a mapping the
/// user is forbidden to create even though it would have worked.
/// </remarks>
public static class TypeCompatibilityChecker {

    /// <summary>A Salesforce record ID is always 18 characters in its case-safe form.</summary>
    private const int SalesforceIdLength = 18;

    /// <summary>
    /// The standard Salesforce maximum length per Field Type, used to warn about truncation.
    /// </summary>
    /// <remarks>
    /// These are Salesforce's defaults, not the org's actual per-field lengths — a custom text field may be
    /// shorter. Reading real lengths needs the REST describe, which the spec leaves for later; until then a
    /// warning here is conservative in the right direction.
    /// </remarks>
    private static readonly Dictionary<SalesforceFieldType, int> StandardMaxLengths = new() {
        [SalesforceFieldType.Text] = 255,
        [SalesforceFieldType.Email] = 80,
        [SalesforceFieldType.Url] = 255,
        [SalesforceFieldType.Phone] = 40,
        [SalesforceFieldType.EntityId] = SalesforceIdLength,
        [SalesforceFieldType.ExternalId] = 255,
        [SalesforceFieldType.DynamicEnum] = 255,
        [SalesforceFieldType.StaticEnum] = 255,
        [SalesforceFieldType.MultiEnum] = 4099,
        [SalesforceFieldType.StringPlusClob] = 131072
    };

    /// <summary>The shape of a target column, once the dialect's type name has been classified.</summary>
    private enum ColumnFamily {
        Unknown,
        Text,
        Integer,
        Decimal,
        Boolean,
        Date,
        Time,
        Timestamp
    }

    public static FieldCompatibility Check(string salesforceFieldName, SalesforceFieldType fieldType,
        ColumnMetadata column, DbType dbType) {
        ArgumentNullException.ThrowIfNull(column);

        var family = Classify(column.DataType, dbType);
        var (level, message) = Evaluate(fieldType, family, column, dbType);

        return new FieldCompatibility {
            SalesforceFieldName = salesforceFieldName,
            TargetColumnName = column.ColumnName,
            FieldType = fieldType,
            TargetDataType = column.DataType,
            Level = level,
            Message = message
        };
    }

    /// <summary>
    /// Checks the column nominated as the Key Mapping. Stricter than an ordinary text mapping: a truncated
    /// record ID does not lose a detail, it makes every UPDATE and DELETE match the wrong row or none.
    /// </summary>
    public static FieldCompatibility CheckKeyColumn(ColumnMetadata column, DbType dbType) {
        ArgumentNullException.ThrowIfNull(column);

        var family = Classify(column.DataType, dbType);

        var (level, message) = family switch {
            ColumnFamily.Text when column.MaxLength is int len && len < SalesforceIdLength =>
                (CompatibilityLevel.Error,
                    $"Key Mapping column '{column.ColumnName}' holds {len} characters but a Salesforce record ID is {SalesforceIdLength}. A truncated ID would match the wrong rows."),
            ColumnFamily.Text when !column.IsUnique =>
                (CompatibilityLevel.Warning,
                    $"Key Mapping column '{column.ColumnName}' has no unique constraint, so one Salesforce record could update more rows than intended."),
            ColumnFamily.Text =>
                (CompatibilityLevel.Compatible, $"Key Mapping column '{column.ColumnName}' can hold a Salesforce record ID."),
            _ =>
                (CompatibilityLevel.Error,
                    $"Key Mapping column '{column.ColumnName}' is '{column.DataType}'. A Salesforce record ID is an {SalesforceIdLength}-character string and needs a text column.")
        };

        return new FieldCompatibility {
            SalesforceFieldName = "MappedSFKey",
            TargetColumnName = column.ColumnName,
            FieldType = SalesforceFieldType.EntityId,
            TargetDataType = column.DataType,
            Level = level,
            Message = message
        };
    }

    /// <summary>
    /// Checks the column nominated to carry the soft delete flag.
    /// </summary>
    public static FieldCompatibility CheckSoftDeleteColumn(ColumnMetadata column, DbType dbType) {
        ArgumentNullException.ThrowIfNull(column);

        var family = Classify(column.DataType, dbType);

        var (level, message) = family switch {
            ColumnFamily.Boolean =>
                (CompatibilityLevel.Compatible, $"Soft delete column '{column.ColumnName}' is boolean."),
            ColumnFamily.Integer =>
                (CompatibilityLevel.Warning,
                    $"Soft delete column '{column.ColumnName}' is '{column.DataType}'; the flag will be stored as 0 and 1."),
            _ =>
                (CompatibilityLevel.Error,
                    $"Soft delete column '{column.ColumnName}' is '{column.DataType}' and cannot hold a boolean flag.")
        };

        return new FieldCompatibility {
            SalesforceFieldName = "SoftDelete",
            TargetColumnName = column.ColumnName,
            FieldType = SalesforceFieldType.Boolean,
            TargetDataType = column.DataType,
            Level = level,
            Message = message
        };
    }

    private static (CompatibilityLevel, string) Evaluate(SalesforceFieldType fieldType, ColumnFamily family,
        ColumnMetadata column, DbType dbType) {
        var col = column.ColumnName;
        var type = column.DataType;

        if (fieldType.IsCompound()) {
            return (CompatibilityLevel.Error,
                $"{fieldType} is a compound field and arrives as a nested record. Map its flattened parts instead of the compound itself.");
        }

        if (fieldType is SalesforceFieldType.Unknown) {
            return (CompatibilityLevel.Warning,
                $"The Salesforce type of this field is not recognised, so its fit with '{type}' on column '{col}' cannot be checked.");
        }

        if (family is ColumnFamily.Unknown) {
            return (CompatibilityLevel.Warning,
                $"Target type '{type}' on column '{col}' is not one this application recognises, so the fit with {fieldType} cannot be checked.");
        }

        if (fieldType.IsTemporal()) {
            return EvaluateTemporal(fieldType, family, col, type, dbType);
        }

        if (fieldType.IsNumeric()) {
            return EvaluateNumeric(fieldType, family, col, type);
        }

        if (fieldType.IsBoolean()) {
            return family switch {
                ColumnFamily.Boolean => (CompatibilityLevel.Compatible, $"{fieldType} fits '{type}'."),
                ColumnFamily.Integer => (CompatibilityLevel.Warning,
                    $"{fieldType} into '{type}' on column '{col}' will be stored as 0 and 1."),
                ColumnFamily.Text => (CompatibilityLevel.Warning,
                    $"{fieldType} into text column '{col}' ('{type}') will be stored as the words true and false."),
                _ => (CompatibilityLevel.Error, $"{fieldType} cannot be written into '{type}' on column '{col}'.")
            };
        }

        // Everything else is text-shaped.
        return family switch {
            ColumnFamily.Text => EvaluateTextLength(fieldType, column),
            _ => (CompatibilityLevel.Error,
                $"{fieldType} is text and cannot be written into '{type}' on column '{col}'.")
        };
    }

    private static (CompatibilityLevel, string) EvaluateTemporal(SalesforceFieldType fieldType,
        ColumnFamily family, string col, string type, DbType dbType) {
        // SQLite has no temporal type; an epoch in an INTEGER column is the idiom there, so blocking it would
        // make the dialect unusable.
        if (dbType is DbType.SqlLite && family is ColumnFamily.Integer) {
            return (CompatibilityLevel.Warning,
                $"{fieldType} into '{type}' on column '{col}' will be stored as a number, which is how SQLite holds dates.");
        }

        return (fieldType, family) switch {
            (SalesforceFieldType.DateTime, ColumnFamily.Timestamp) => Ok(fieldType, type),
            (SalesforceFieldType.DateTime, ColumnFamily.Date) => (CompatibilityLevel.Warning,
                $"{fieldType} into date column '{col}' ('{type}') will drop the time component."),

            (SalesforceFieldType.DateOnly, ColumnFamily.Date) => Ok(fieldType, type),
            (SalesforceFieldType.DateOnly, ColumnFamily.Timestamp) => Ok(fieldType, type),

            (SalesforceFieldType.TimeOnly, ColumnFamily.Time) => Ok(fieldType, type),

            (_, ColumnFamily.Text) => (CompatibilityLevel.Compatible,
                $"{fieldType} into text column '{col}' ('{type}') will be stored in its string form."),

            _ => (CompatibilityLevel.Error,
                $"{fieldType} cannot be written into '{type}' on column '{col}'. It needs a matching temporal or text column.")
        };
    }

    private static (CompatibilityLevel, string) EvaluateNumeric(SalesforceFieldType fieldType,
        ColumnFamily family, string col, string type) {
        var hasFraction = fieldType is not SalesforceFieldType.Integer;

        return family switch {
            ColumnFamily.Decimal => Ok(fieldType, type),
            ColumnFamily.Integer when !hasFraction => Ok(fieldType, type),
            ColumnFamily.Integer => (CompatibilityLevel.Warning,
                $"{fieldType} into integer column '{col}' ('{type}') will drop everything after the decimal point."),
            ColumnFamily.Text => (CompatibilityLevel.Warning,
                $"{fieldType} into text column '{col}' ('{type}') stores the number as text, which prevents arithmetic and ordering."),
            _ => (CompatibilityLevel.Error,
                $"{fieldType} is numeric and cannot be written into '{type}' on column '{col}'.")
        };
    }

    private static (CompatibilityLevel, string) EvaluateTextLength(SalesforceFieldType fieldType, ColumnMetadata column) {
        if (column.MaxLength is not int maxLength) {
            return Ok(fieldType, column.DataType);
        }

        if (!StandardMaxLengths.TryGetValue(fieldType, out var salesforceMax) || maxLength >= salesforceMax) {
            return Ok(fieldType, column.DataType);
        }

        return (CompatibilityLevel.Warning,
            $"{fieldType} can be up to {salesforceMax} characters but column '{column.ColumnName}' holds {maxLength}, so values may be truncated.");
    }

    private static (CompatibilityLevel, string) Ok(SalesforceFieldType fieldType, string dataType) =>
        (CompatibilityLevel.Compatible, $"{fieldType} fits '{dataType}'.");

    /// <summary>
    /// Maps a dialect's type name onto a shape. The families overlap heavily across dialects, so one shared
    /// table does most of the work and each dialect only overrides where it genuinely differs.
    /// </summary>
    private static ColumnFamily Classify(string dataType, DbType dbType) {
        var name = dataType.Trim().ToLowerInvariant();

        // Strip any declared size — SQLite and some drivers report "varchar(50)" rather than "varchar".
        var parenIndex = name.IndexOf('(');
        if (parenIndex > 0) {
            name = name[..parenIndex].Trim();
        }

        // SQL Server's bit is the only dialect-specific boolean; every other name below is shared.
        if (dbType is DbType.SqlServer && name is "bit") {
            return ColumnFamily.Boolean;
        }

        return name switch {
            "character varying" or "varchar" or "character" or "char" or "text" or "nvarchar" or "nchar" or
                "ntext" or "longtext" or "mediumtext" or "tinytext" or "citext" or "clob" => ColumnFamily.Text,

            "integer" or "int" or "int4" or "int8" or "int2" or "bigint" or "smallint" or "mediumint" or
                "tinyint" or "serial" or "bigserial" => ColumnFamily.Integer,

            "numeric" or "decimal" or "double precision" or "double" or "real" or "float" or "money" or
                "smallmoney" or "float8" or "float4" => ColumnFamily.Decimal,

            "boolean" or "bool" => ColumnFamily.Boolean,

            "date" => ColumnFamily.Date,

            "time" or "time without time zone" or "time with time zone" or "timetz" => ColumnFamily.Time,

            "timestamp" or "timestamp without time zone" or "timestamp with time zone" or "timestamptz" or
                "datetime" or "datetime2" or "smalldatetime" or "datetimeoffset" => ColumnFamily.Timestamp,

            _ => ColumnFamily.Unknown
        };
    }
}
