using SalesforceGrpc.Extensions;

namespace SalesforceGRPCTest;

/// <summary>
/// Converting a decoded Avro value into what the target column expects.
/// </summary>
public class FieldTypeConverterTests {

    /// <summary>2024-01-15 10:30:45 UTC.</summary>
    private const long SampleEpochMilliseconds = 1_705_314_645_000;

    [Fact]
    public void AStringPassesThroughUnchanged() {
        // Every repository writes values as Dapper parameters. Escaping here as well would double up and
        // write O''Brien into the target table.
        Assert.Equal("O'Brien", FieldTypeConverter.ConvertValue("O'Brien", "string", "Data:Text"));
    }

    [Fact]
    public void AStringWithNoDocAnnotationAlsoPassesThroughUnchanged() {
        Assert.Equal("d'Arcy", FieldTypeConverter.ConvertValue("d'Arcy", "string"));
    }

    [Fact]
    public void ADateTimeFieldDecodesToTheInstantSalesforceSent() {
        var value = FieldTypeConverter.ConvertValue(SampleEpochMilliseconds, "long", "Data:DateTime:00N123");

        Assert.Equal(new DateTime(2024, 1, 15, 10, 30, 45, DateTimeKind.Utc), Assert.IsType<DateTime>(value));
    }

    [Fact]
    public void ADateOnlyFieldDecodesToThatSameDateAtMidnight() {
        // Salesforce sends a Date as an epoch datetime whose time component is already zeroed, so DateOnly
        // and DateTime are decoded identically on purpose. Pinned here so the duplication is not "fixed".
        var midnight = new DateTimeOffset(new DateTime(2024, 1, 15, 0, 0, 0, DateTimeKind.Utc)).ToUnixTimeMilliseconds();

        var value = FieldTypeConverter.ConvertValue(midnight, "long", "Data:DateOnly:00N123");

        Assert.Equal(new DateTime(2024, 1, 15, 0, 0, 0, DateTimeKind.Utc), Assert.IsType<DateTime>(value));
    }

    [Fact]
    public void ATimeOnlyFieldDecodesFromMillisecondsSinceMidnight() {
        var value = FieldTypeConverter.ConvertValue(37_845_000L, "long", "Data:TimeOnly:00N123");

        Assert.Equal(new TimeOnly(10, 30, 45), Assert.IsType<TimeOnly>(value));
    }

    [Theory]
    // Both are Avro longs and only the doc annotation separates them; a Date must not become an epoch number.
    [InlineData("Data:DateTime:00N123")]
    [InlineData("Data:DateOnly:00N123")]
    public void ATemporalFieldIsNeverLeftAsANumber(string doc) {
        Assert.IsType<DateTime>(FieldTypeConverter.ConvertValue(SampleEpochMilliseconds, "long", doc));
    }

    [Theory]
    [InlineData("long", 42L)]
    [InlineData("int", 42)]
    [InlineData("double", 42d)]
    [InlineData("boolean", true)]
    public void WithoutADocAnnotationTheAvroTypeDecides(string avroType, object expected) {
        Assert.Equal(expected, FieldTypeConverter.ConvertValue(expected, avroType));
    }

    [Fact]
    public void ANonTemporalDocDoesNotDivertToDateHandling() {
        // Currency and DateTime are both documented under a "Data:" role; only the type segment separates them.
        Assert.Equal(1234.56d, FieldTypeConverter.ConvertValue(1234.56d, "double", "Data:Currency"));
    }

    [Fact]
    public void ANullValueStaysNull() {
        Assert.Null(FieldTypeConverter.ConvertValue(null, "string", "Data:Text"));
    }
}
