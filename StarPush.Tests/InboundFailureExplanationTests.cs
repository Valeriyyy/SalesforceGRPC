using Database.Models;
using DTO;
using StarPush.ViewModels;

namespace StarPush.Tests;

/// <summary>
/// What the Binding page tells the user about an Inbound Failure: what went wrong, in the Binding's own terms, and
/// what to do about it.
/// </summary>
public class InboundFailureExplanationTests {
    private const string Table = "salesforce.contacts";
    private const string LongName = "Xavier-Lehnsherr-Summers-Grey-Munroe-Logan-McCoy-Drake-Wor";

    private static InboundFailure Failure(string message, string? sqlState = null, string? column = null,
        string? constraint = null, string valuesSent = "{}", string? targetTable = Table, string[]? recordIds = null) => new() {
        BindingId = 16, ChannelFullName = "Sales__chn", ReplayId = [1], EntityName = "ContactChangeEvent",
        ChangeType = "UPDATE", RecordIds = recordIds ?? ["003Dp00000T0ulFIAR"], Message = message,
        SqlState = sqlState, TargetColumn = column, ConstraintName = constraint, ValuesSent = valuesSent,
        TargetTable = targetTable
    };

    private static readonly TargetColumnDTO[] Columns = [
        new() { ColumnName = "sf_id", DataType = "character varying", MaxLength = 18, IsUnique = true },
        new() { ColumnName = "last_name", DataType = "character varying", MaxLength = 50, MappedSalesforceFieldName = "NameLastName" },
        new() { ColumnName = "title", DataType = "character varying", MaxLength = 100, MappedSalesforceFieldName = "Title" }
    ];

    private static readonly string ValuesWithLongName =
        $$"""{"sf_id":"003Dp00000T0ulFIAR","last_name":"{{LongName}}","title":"Professor"}""";

    [Fact]
    public void AValueTooLong_NamesTheFieldTheColumnAndBothLengths_AndHowToWidenIt() {
        var explanation = InboundFailureExplanation.For(
            Failure("22001: value too long for type character varying(50)", "22001", valuesSent: ValuesWithLongName), Columns);

        Assert.Contains("NameLastName", explanation.Problem);
        Assert.Contains("last_name", explanation.Problem);
        Assert.Contains("58", explanation.Problem);
        Assert.Contains("50", explanation.Problem);
        Assert.Contains(explanation.Steps, s => s.Contains("ALTER TABLE salesforce.contacts ALTER COLUMN last_name TYPE varchar(58)"));
    }

    [Fact]
    public void AValueTooLong_WithoutTheColumnsToHand_StillFindsTheValueFromTheLimitInTheMessage() {
        var explanation = InboundFailureExplanation.For(
            Failure("22001: value too long for type character varying(50)", "22001", valuesSent: ValuesWithLongName), null);

        Assert.Contains("last_name", explanation.Problem);
        Assert.Contains("58", explanation.Problem);
    }

    [Fact]
    public void AValueTooLong_RecordedBeforeValuesWereKept_SaysWhatItCan_FromTheMessageAlone() {
        var explanation = InboundFailureExplanation.For(
            Failure("22001: value too long for type character varying(50)"), Columns);

        Assert.Contains("50 characters", explanation.Problem);
        Assert.Contains(explanation.Steps, s => s.Contains("last_name"));
    }

    [Fact]
    public void AMissingRequiredValue_NamesTheColumn() {
        var explanation = InboundFailureExplanation.For(
            Failure("23502: null value in column \"last_name\" violates not-null constraint", "23502", column: "last_name"), Columns);

        Assert.Contains("last_name", explanation.Problem);
        Assert.Contains("NameLastName", explanation.Problem);
        Assert.Contains(explanation.Steps, s => s.Contains("DROP NOT NULL"));
    }

    [Fact]
    public void ADuplicate_NamesTheConstraint() {
        var explanation = InboundFailureExplanation.For(
            Failure("23505: duplicate key value violates unique constraint \"contacts_email_key\"", "23505",
                constraint: "contacts_email_key"), Columns);

        Assert.Contains("contacts_email_key", explanation.Problem);
    }

    [Fact]
    public void AColumnThatNoLongerExists_SendsTheUserToTheFieldMappings() {
        var explanation = InboundFailureExplanation.For(
            Failure("42703: column \"nickname\" of relation \"contacts\" does not exist", "42703"), Columns);

        Assert.Contains("no longer", explanation.Problem);
        Assert.Contains(explanation.Steps, s => s.Contains("Field Mapping"));
    }

    [Fact]
    public void AnEventThatNeverReachedTheDatabase_SaysStarPushCouldNotReadIt() {
        var explanation = InboundFailureExplanation.For(
            Failure("The event for ContactChangeEvent carries no readable ChangeEventHeader.", targetTable: null), Columns);

        Assert.Contains("couldn't read", explanation.Problem);
        Assert.Contains(explanation.Steps, s => s.Contains("log"));
    }

    [Fact]
    public void EveryExplanation_EndsByTellingTheUserToCorrectTheRecordThemselves_ThenDismiss() {
        var explanation = InboundFailureExplanation.For(
            Failure("22001: value too long for type character varying(50)", "22001", valuesSent: ValuesWithLongName), Columns);

        Assert.Contains("003Dp00000T0ulFIAR", explanation.Steps[^1]);
        Assert.Contains("Dismiss", explanation.Steps[^1]);
    }

    [Fact]
    public void AFailureRecordedBeforeItsTableWasKept_UsesTheBindingsTable() {
        var explanation = InboundFailureExplanation.For(
            Failure("22001: value too long for type character varying(50)", targetTable: null), Columns, Table);

        Assert.Contains(explanation.Steps, s => s.Contains("salesforce.contacts"));
        Assert.DoesNotContain("the Target Table", explanation.Steps[^1]);
    }
}
