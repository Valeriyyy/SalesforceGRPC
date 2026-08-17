using Application.Bindings;
using Database.Models;
using Database.Repositories;
using Salesforce.Avro;

namespace SalesforceGRPCTest;

/// <summary>
/// The Type Compatibility matrix — a pure function of Field Type, target column and dialect, so it needs no
/// database and can be driven exhaustively.
/// </summary>
/// <remarks>
/// Error blocks activation, Warning does not. The split is deliberate: a hard block needs a matrix that is
/// right about four SQL dialects, and every gap in it becomes a mapping the user cannot create even though it
/// would have worked. Errors are therefore reserved for mappings that cannot succeed at all.
/// </remarks>
public class TypeCompatibilityCheckerTests {

    private static ColumnMetadata Column(string dataType, bool nullable = true, int? maxLength = null) =>
        new() { ColumnName = "target_col", DataType = dataType, IsNullable = nullable, MaxLength = maxLength };

    private static CompatibilityLevel Level(SalesforceFieldType type, string dataType,
        DbType db = DbType.Postgres, int? maxLength = null) =>
        TypeCompatibilityChecker.Check("SomeField", type, Column(dataType, maxLength: maxLength), db).Level;

    #region Temporal

    [Theory]
    [InlineData(SalesforceFieldType.DateTime, "timestamp without time zone")]
    [InlineData(SalesforceFieldType.DateTime, "timestamp with time zone")]
    [InlineData(SalesforceFieldType.DateOnly, "date")]
    [InlineData(SalesforceFieldType.DateOnly, "timestamp without time zone")]
    [InlineData(SalesforceFieldType.TimeOnly, "time without time zone")]
    public void Temporal_IntoAMatchingTemporalColumn_IsCompatible(SalesforceFieldType type, string dataType) {
        Assert.Equal(CompatibilityLevel.Compatible, Level(type, dataType));
    }

    [Fact]
    public void DateTime_IntoADateColumn_WarnsThatTheTimeIsLost() {
        var result = TypeCompatibilityChecker.Check("CreatedDate", SalesforceFieldType.DateTime,
            Column("date"), DbType.Postgres);

        Assert.Equal(CompatibilityLevel.Warning, result.Level);
        Assert.Contains("time", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(SalesforceFieldType.DateTime, "integer")]
    [InlineData(SalesforceFieldType.DateTime, "boolean")]
    [InlineData(SalesforceFieldType.DateOnly, "numeric")]
    [InlineData(SalesforceFieldType.TimeOnly, "date")]
    [InlineData(SalesforceFieldType.TimeOnly, "timestamp without time zone")]
    public void Temporal_IntoANonTemporalColumn_IsAnError(SalesforceFieldType type, string dataType) {
        Assert.Equal(CompatibilityLevel.Error, Level(type, dataType));
    }

    [Fact]
    public void Temporal_IntoASqliteIntegerColumn_IsOnlyAWarning() {
        // SQLite has no temporal type at all; storing an epoch in an INTEGER column is the idiom, so blocking
        // it would make the dialect unusable.
        Assert.Equal(CompatibilityLevel.Warning, Level(SalesforceFieldType.DateTime, "INTEGER", DbType.SqlLite));
    }

    #endregion

    #region Numeric

    [Theory]
    [InlineData(SalesforceFieldType.Integer, "integer")]
    [InlineData(SalesforceFieldType.Integer, "bigint")]
    [InlineData(SalesforceFieldType.Integer, "numeric")]
    [InlineData(SalesforceFieldType.Currency, "numeric")]
    [InlineData(SalesforceFieldType.Percent, "double precision")]
    [InlineData(SalesforceFieldType.Double, "real")]
    public void Numeric_IntoANumericColumnOfSufficientKind_IsCompatible(SalesforceFieldType type, string dataType) {
        Assert.Equal(CompatibilityLevel.Compatible, Level(type, dataType));
    }

    [Fact]
    public void Currency_IntoAnIntegerColumn_WarnsThatTheFractionIsLost() {
        var result = TypeCompatibilityChecker.Check("AnnualRevenue", SalesforceFieldType.Currency,
            Column("integer"), DbType.Postgres);

        Assert.Equal(CompatibilityLevel.Warning, result.Level);
        Assert.Contains("integer", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Numeric_IntoATextColumn_IsAWarningNotAnError() {
        // It works — the value round-trips as text — but it is rarely what the user meant.
        Assert.Equal(CompatibilityLevel.Warning, Level(SalesforceFieldType.Double, "text"));
    }

    [Theory]
    [InlineData("boolean")]
    [InlineData("date")]
    [InlineData("timestamp without time zone")]
    public void Numeric_IntoANonNumericNonTextColumn_IsAnError(string dataType) {
        Assert.Equal(CompatibilityLevel.Error, Level(SalesforceFieldType.Double, dataType));
    }

    #endregion

    #region Text

    [Theory]
    [InlineData(SalesforceFieldType.Text, "text")]
    [InlineData(SalesforceFieldType.Email, "character varying")]
    [InlineData(SalesforceFieldType.EntityId, "character varying")]
    [InlineData(SalesforceFieldType.DynamicEnum, "text")]
    [InlineData(SalesforceFieldType.MultiEnum, "text")]
    public void Text_IntoAnUnboundedTextColumn_IsCompatible(SalesforceFieldType type, string dataType) {
        Assert.Equal(CompatibilityLevel.Compatible, Level(type, dataType));
    }

    [Fact]
    public void Text_IntoATextColumnLongEnoughForSalesforcesMaximum_IsCompatible() {
        Assert.Equal(CompatibilityLevel.Compatible,
            Level(SalesforceFieldType.Email, "character varying", maxLength: 80));
    }

    [Fact]
    public void Text_IntoATextColumnShorterThanSalesforcesMaximum_WarnsAboutTruncation() {
        var result = TypeCompatibilityChecker.Check("Some_Email__c", SalesforceFieldType.Email,
            Column("character varying", maxLength: 20), DbType.Postgres);

        Assert.Equal(CompatibilityLevel.Warning, result.Level);
        Assert.Contains("truncat", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("integer")]
    [InlineData("boolean")]
    [InlineData("date")]
    public void Text_IntoANonTextColumn_IsAnError(string dataType) {
        Assert.Equal(CompatibilityLevel.Error, Level(SalesforceFieldType.Text, dataType));
    }

    #endregion

    #region Boolean

    [Fact]
    public void Boolean_IntoABooleanColumn_IsCompatible() {
        Assert.Equal(CompatibilityLevel.Compatible, Level(SalesforceFieldType.Boolean, "boolean"));
    }

    [Fact]
    public void Boolean_IntoAnIntegerColumn_IsAWarning() {
        // Stored as 0/1. Correct on every dialect, and the only option on SQLite and SQL Server's tinyint.
        Assert.Equal(CompatibilityLevel.Warning, Level(SalesforceFieldType.Boolean, "integer"));
    }

    [Fact]
    public void Boolean_IntoADateColumn_IsAnError() {
        Assert.Equal(CompatibilityLevel.Error, Level(SalesforceFieldType.Boolean, "date"));
    }

    #endregion

    #region Compound and unknown

    [Theory]
    [InlineData(SalesforceFieldType.Address)]
    [InlineData(SalesforceFieldType.Location)]
    [InlineData(SalesforceFieldType.PersonName)]
    public void Compound_MappedAsAWhole_IsAlwaysAnError(SalesforceFieldType type) {
        // Even into a text column: a compound arrives as a nested record with no single value to write.
        var result = TypeCompatibilityChecker.Check("BillingAddress", type, Column("text"), DbType.Postgres);

        Assert.Equal(CompatibilityLevel.Error, result.Level);
        Assert.Contains("compound", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnknownFieldType_IsAWarningSoANewSalesforceTypeDoesNotBlockTheUser() {
        var result = TypeCompatibilityChecker.Check("Whatever", SalesforceFieldType.Unknown,
            Column("text"), DbType.Postgres);

        Assert.Equal(CompatibilityLevel.Warning, result.Level);
    }

    [Fact]
    public void UnrecognisedTargetDataType_IsAWarningNotAnError() {
        // A dialect-specific type this matrix has not seen must not become a mapping the user cannot create.
        Assert.Equal(CompatibilityLevel.Warning, Level(SalesforceFieldType.Text, "some_custom_domain_type"));
    }

    #endregion

    #region Dialects

    [Theory]
    [InlineData(DbType.SqlServer, "nvarchar")]
    [InlineData(DbType.SqlServer, "varchar")]
    [InlineData(DbType.MySql, "longtext")]
    [InlineData(DbType.SqlLite, "TEXT")]
    public void TextColumnsAreRecognisedAcrossDialects(DbType db, string dataType) {
        Assert.Equal(CompatibilityLevel.Compatible, Level(SalesforceFieldType.Text, dataType, db));
    }

    [Theory]
    [InlineData(DbType.SqlServer, "datetime2")]
    [InlineData(DbType.SqlServer, "datetimeoffset")]
    [InlineData(DbType.MySql, "datetime")]
    public void TimestampColumnsAreRecognisedAcrossDialects(DbType db, string dataType) {
        Assert.Equal(CompatibilityLevel.Compatible, Level(SalesforceFieldType.DateTime, dataType, db));
    }

    [Fact]
    public void SqlServerBitIsABooleanColumn() {
        Assert.Equal(CompatibilityLevel.Compatible, Level(SalesforceFieldType.Boolean, "bit", DbType.SqlServer));
    }

    #endregion

    #region Reporting

    [Fact]
    public void TheResultNamesBothTypesSoTheUserKnowsWhatToChange() {
        var result = TypeCompatibilityChecker.Check("Some_Date_Time__c", SalesforceFieldType.DateTime,
            Column("integer"), DbType.Postgres);

        Assert.Equal("Some_Date_Time__c", result.SalesforceFieldName);
        Assert.Equal("target_col", result.TargetColumnName);
        Assert.Contains("DateTime", result.Message, StringComparison.Ordinal);
        Assert.Contains("integer", result.Message, StringComparison.Ordinal);
    }

    #endregion

    #region Key Mapping column

    [Fact]
    public void CheckKeyColumn_OnATextColumnWithAUniqueConstraint_IsCompatible() {
        var column = Column("character varying", nullable: false, maxLength: 18);
        column.ColumnConstraints.Add(new ColumnConstraint { ConstraintType = "UNIQUE" });

        Assert.Equal(CompatibilityLevel.Compatible, TypeCompatibilityChecker.CheckKeyColumn(column, DbType.Postgres).Level);
    }

    [Fact]
    public void CheckKeyColumn_WithoutAUniqueConstraint_WarnsThatUpdatesCouldTouchExtraRows() {
        var result = TypeCompatibilityChecker.CheckKeyColumn(Column("text"), DbType.Postgres);

        Assert.Equal(CompatibilityLevel.Warning, result.Level);
        Assert.Contains("unique", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CheckKeyColumn_OnANonTextColumn_IsAnError() {
        // A Salesforce record ID is an 18-character string; nothing else can hold it.
        Assert.Equal(CompatibilityLevel.Error, TypeCompatibilityChecker.CheckKeyColumn(Column("integer"), DbType.Postgres).Level);
    }

    [Fact]
    public void CheckKeyColumn_TooShortForASalesforceId_IsAnError() {
        // Truncating a record ID does not warn, it corrupts: every WHERE clause would then match the wrong row.
        var result = TypeCompatibilityChecker.CheckKeyColumn(Column("character varying", maxLength: 10), DbType.Postgres);

        Assert.Equal(CompatibilityLevel.Error, result.Level);
        Assert.Contains("18", result.Message, StringComparison.Ordinal);
    }

    #endregion

    #region Soft delete column

    [Fact]
    public void CheckSoftDeleteColumn_OnABooleanColumn_IsCompatible() {
        Assert.Equal(CompatibilityLevel.Compatible,
            TypeCompatibilityChecker.CheckSoftDeleteColumn(Column("boolean"), DbType.Postgres).Level);
    }

    [Fact]
    public void CheckSoftDeleteColumn_OnAnIntegerColumn_IsAWarning() {
        Assert.Equal(CompatibilityLevel.Warning,
            TypeCompatibilityChecker.CheckSoftDeleteColumn(Column("integer"), DbType.Postgres).Level);
    }

    [Fact]
    public void CheckSoftDeleteColumn_OnATextColumn_IsAnError() {
        Assert.Equal(CompatibilityLevel.Error,
            TypeCompatibilityChecker.CheckSoftDeleteColumn(Column("text"), DbType.Postgres).Level);
    }

    [Fact]
    public void CheckSoftDeleteColumn_ThatIsNotNullable_IsStillCompatible() {
        // NOT NULL DEFAULT false is the ideal shape for this column, not a problem with it.
        Assert.Equal(CompatibilityLevel.Compatible,
            TypeCompatibilityChecker.CheckSoftDeleteColumn(Column("boolean", nullable: false), DbType.Postgres).Level);
    }

    #endregion
}
