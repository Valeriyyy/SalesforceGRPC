using Salesforce.Avro;

namespace SalesforceGRPCTest;

/// <summary>
/// The Avro schema Salesforce issues carries the semantic Field Type in each field's doc annotation,
/// formatted "&lt;role&gt;:&lt;type&gt;[:&lt;field id&gt;]". The wire types cannot substitute for it — Date,
/// DateTime and Time all arrive as long, and Currency, Percent and Number all as double — so this parse is the
/// only thing standing between a user and a mapping that silently loses data.
/// </summary>
/// <remarks>
/// Every doc string below is copied verbatim from SalesforceGrpc/avro/AccountChangeEvent.avsc.
/// </remarks>
public class SalesforceFieldDocTests {

    [Theory]
    // The three temporal types that all decode to a bare Avro long.
    [InlineData("Data:DateTime:00NDp000009Rr9D", SalesforceFieldType.DateTime)]
    [InlineData("Data:DateOnly:00NDp000009Rr9I", SalesforceFieldType.DateOnly)]
    [InlineData("Data:TimeOnly:00NDp000009RrA1", SalesforceFieldType.TimeOnly)]
    // The numeric types that all decode to a bare Avro double.
    [InlineData("Data:Currency", SalesforceFieldType.Currency)]
    [InlineData("Data:Percent:00NDp000009Rr9X", SalesforceFieldType.Percent)]
    [InlineData("Data:Double:00NDp000009Rr9S", SalesforceFieldType.Double)]
    [InlineData("Data:Integer", SalesforceFieldType.Integer)]
    // The string-shaped types.
    [InlineData("Data:Text", SalesforceFieldType.Text)]
    [InlineData("Data:StringPlusClob", SalesforceFieldType.StringPlusClob)]
    [InlineData("Data:Email:00NDp000009Rr99", SalesforceFieldType.Email)]
    [InlineData("Data:Url:00NDp000009RrA6", SalesforceFieldType.Url)]
    [InlineData("Data:Phone", SalesforceFieldType.Phone)]
    [InlineData("Data:DynamicEnum:00NDp000009Rr9h", SalesforceFieldType.DynamicEnum)]
    [InlineData("Data:MultiEnum:00NDp000009Rr9m", SalesforceFieldType.MultiEnum)]
    [InlineData("Data:StaticEnum", SalesforceFieldType.StaticEnum)]
    [InlineData("Data:Boolean", SalesforceFieldType.Boolean)]
    // Compound types.
    [InlineData("Data:Address", SalesforceFieldType.Address)]
    [InlineData("Data:Location:00NDp000009Rr9N", SalesforceFieldType.Location)]
    [InlineData("Data:ComplexValueType", SalesforceFieldType.ComplexValueType)]
    // Roles other than Data carry a type in the same position.
    [InlineData("ForeignKey:EntityId", SalesforceFieldType.EntityId)]
    [InlineData("CreatedDate:DateTime", SalesforceFieldType.DateTime)]
    [InlineData("Data:ExternalId", SalesforceFieldType.ExternalId)]
    public void Parse_ReadsTheFieldType(string doc, SalesforceFieldType expected) {
        Assert.Equal(expected, SalesforceFieldDoc.Parse(doc).FieldType);
    }

    [Fact]
    public void Parse_MapsSwitchablePersonNameToPersonName() {
        // Account.Name is documented as Data:Switchable_PersonName because a person account carries a
        // structured name where a business account carries a plain string.
        Assert.Equal(SalesforceFieldType.PersonName, SalesforceFieldDoc.Parse("Data:Switchable_PersonName").FieldType);
    }

    [Theory]
    [InlineData("Data:DateTime:00NDp000009Rr9D", "Data", "00NDp000009Rr9D")]
    [InlineData("ForeignKey:EntityId", "ForeignKey", null)]
    [InlineData("CreatedDate:DateTime", "CreatedDate", null)]
    public void Parse_SeparatesRoleAndFieldId(string doc, string expectedRole, string? expectedFieldId) {
        var parsed = SalesforceFieldDoc.Parse(doc);
        Assert.Equal(expectedRole, parsed.Role);
        Assert.Equal(expectedFieldId, parsed.FieldId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_TreatsAMissingDocAsUnknown(string? doc) {
        Assert.Equal(SalesforceFieldType.Unknown, SalesforceFieldDoc.Parse(doc).FieldType);
    }

    [Fact]
    public void Parse_TreatsAnUnrecognisedTypeAsUnknownAndKeepsTheRawText() {
        // Salesforce adds field types over time; an unknown one must not throw, and the raw text is kept so
        // the reason a field cannot be validated is visible to the user.
        var parsed = SalesforceFieldDoc.Parse("Data:SomeTypeInventedLater:00N123");
        Assert.Equal(SalesforceFieldType.Unknown, parsed.FieldType);
        Assert.Equal("SomeTypeInventedLater", parsed.RawType);
    }

    [Theory]
    [InlineData(SalesforceFieldType.DateTime)]
    [InlineData(SalesforceFieldType.DateOnly)]
    [InlineData(SalesforceFieldType.TimeOnly)]
    public void IsTemporal_IsTrueForTheThreeTypesThatShareTheAvroLong(SalesforceFieldType type) {
        Assert.True(type.IsTemporal());
    }

    [Theory]
    [InlineData(SalesforceFieldType.Integer)]
    [InlineData(SalesforceFieldType.Double)]
    [InlineData(SalesforceFieldType.Currency)]
    [InlineData(SalesforceFieldType.Percent)]
    public void IsNumeric_IsTrueForTheNumericTypes(SalesforceFieldType type) {
        Assert.True(type.IsNumeric());
        Assert.False(type.IsTemporal());
    }

    [Theory]
    [InlineData(SalesforceFieldType.Address)]
    [InlineData(SalesforceFieldType.Location)]
    [InlineData(SalesforceFieldType.PersonName)]
    [InlineData(SalesforceFieldType.ComplexValueType)]
    public void IsCompound_IsTrueForTheRecordShapedTypes(SalesforceFieldType type) {
        // A compound type has no single value to write, so mapping one as a whole is always an error.
        Assert.True(type.IsCompound());
    }
}
