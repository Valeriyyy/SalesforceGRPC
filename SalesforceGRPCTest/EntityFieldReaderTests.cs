using Avro;
using Salesforce.Avro;

namespace SalesforceGRPCTest;

/// <summary>
/// Reading the bindable field list out of a real Salesforce Avro schema.
/// </summary>
/// <remarks>
/// The fixture is the committed AccountChangeEvent.avsc, linked into the test output by the project file.
/// Using the real schema rather than a hand-written one is deliberate: the compound shapes Salesforce emits
/// (a two-way union for Address, a three-way union for Name) are exactly what a synthetic fixture would get
/// wrong, and getting them wrong produces mappings that never match at run time.
/// </remarks>
public class EntityFieldReaderTests {

    private static RecordSchema AccountSchema() =>
        (RecordSchema)Schema.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "avro", "AccountChangeEvent.avsc")));

    private static IReadOnlyList<EntityField> AccountFields() => EntityFieldReader.ReadFields(AccountSchema());

    [Fact]
    public void ReadFields_ExcludesTheChangeEventHeader() {
        // The header is envelope, not data. Its record ID feeds the Key Mapping by a different route.
        Assert.DoesNotContain(AccountFields(), f => f.Name == "ChangeEventHeader");
    }

    [Theory]
    [InlineData("BillingAddressStreet")]
    [InlineData("BillingAddressCity")]
    [InlineData("BillingAddressPostalCode")]
    [InlineData("BillingAddressCountryCode")]
    [InlineData("BillingAddressLatitude")]
    [InlineData("ShippingAddressCity")]
    [InlineData("Some_Geolocation__cLatitude")]
    [InlineData("Some_Geolocation__cLongitude")]
    public void ReadFields_FlattensCompoundFieldsByConcatenatingParentAndChild(string expected) {
        Assert.Contains(AccountFields(), f => f.Name == expected);
    }

    [Theory]
    [InlineData("NameSalutation")]
    [InlineData("NameFirstName")]
    [InlineData("NameLastName")]
    public void ReadFields_FlattensTheThreeWayNameUnion(string expected) {
        Assert.Contains(AccountFields(), f => f.Name == expected);
    }

    [Fact]
    public void ReadFields_KeepsNameItselfBecauseABusinessAccountSendsItAsAString() {
        // Account.Name is ["null", "string", Switchable_PersonName]: a business account sends a plain string,
        // a person account sends the record. Both forms must be mappable.
        var name = Assert.Single(AccountFields(), f => f.Name == "Name");
        Assert.Null(name.ParentName);
    }

    [Fact]
    public void ReadFields_NeverEmitsACompoundFieldUnderItsOwnName() {
        // A compound has no single value to write. Emitting "BillingAddress" as bindable would let a user
        // create a mapping that silently never matches.
        var names = AccountFields().Select(f => f.Name).ToList();
        Assert.DoesNotContain("BillingAddress", names);
        Assert.DoesNotContain("ShippingAddress", names);
        Assert.DoesNotContain("Some_Geolocation__c", names);
    }

    [Theory]
    [InlineData("Some_Date_Time__c", SalesforceFieldType.DateTime)]
    [InlineData("Some_Date__c", SalesforceFieldType.DateOnly)]
    [InlineData("Some_Time__c", SalesforceFieldType.TimeOnly)]
    [InlineData("AnnualRevenue", SalesforceFieldType.Currency)]
    [InlineData("Some_Percent__c", SalesforceFieldType.Percent)]
    [InlineData("NumberOfEmployees", SalesforceFieldType.Integer)]
    [InlineData("OwnerId", SalesforceFieldType.EntityId)]
    [InlineData("Some_Email__c", SalesforceFieldType.Email)]
    [InlineData("Some_URL__c", SalesforceFieldType.Url)]
    [InlineData("PersonDoNotCall", SalesforceFieldType.Boolean)]
    public void ReadFields_CarriesTheFieldTypeFromTheDocAnnotation(string field, SalesforceFieldType expected) {
        Assert.Equal(expected, Assert.Single(AccountFields(), f => f.Name == field).FieldType);
    }

    [Theory]
    // Salesforce writes no doc on the children of a compound record, so their type comes from the Avro type.
    // That is safe here and only here: no compound carries a temporal or currency child, so the collapse of
    // Date/DateTime/Time onto long cannot bite.
    [InlineData("BillingAddressCity", SalesforceFieldType.Text)]
    [InlineData("BillingAddressLatitude", SalesforceFieldType.Double)]
    [InlineData("NameFirstName", SalesforceFieldType.Text)]
    public void ReadFields_InfersCompoundChildTypesFromTheAvroType(string field, SalesforceFieldType expected) {
        Assert.Equal(expected, Assert.Single(AccountFields(), f => f.Name == field).FieldType);
    }

    [Fact]
    public void ReadFields_RecordsTheParentAndChildOfAFlattenedField() {
        var city = Assert.Single(AccountFields(), f => f.Name == "BillingAddressCity");
        Assert.Equal("BillingAddress", city.ParentName);
        Assert.Equal("City", city.ChildName);
    }

    [Fact]
    public void ReadFields_LeavesParentAndChildNullForATopLevelField() {
        var phone = Assert.Single(AccountFields(), f => f.Name == "Phone");
        Assert.Null(phone.ParentName);
        Assert.Null(phone.ChildName);
    }

    [Fact]
    public void ReadFields_ReturnsEveryNameOnlyOnce() {
        // Two compounds both carry Latitude and City; without the parent prefix they would collide.
        var names = AccountFields().Select(f => f.Name).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public void FlattenedName_MatchesWhatTheUpdateStrategyBuildsFromTheChangedFieldsBitmap() {
        // UpdateStrategy composes the same key as parent + child when decoding a nested bitmap entry. If these
        // two ever disagree, every compound mapping silently stops matching, so pin them together.
        Assert.Equal("NameFirstName", EntityFieldReader.FlattenedName("Name", "FirstName"));
        Assert.Equal("BillingAddressCity", EntityFieldReader.FlattenedName("BillingAddress", "City"));
    }
}
