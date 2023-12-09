using Avro;
using Avro.Generic;
using Avro.IO;
using Avro.Reflect;
using Avro.Specific;
using com.sforce.eventbus;
using Database;
using Moq;
using SalesforceGrpc.Database;
using SalesforceGrpc.Extensions;
using SalesforceGrpc.Handlers;
using SalesforceGrpc.Handlers.Account;
using SalesforceGrpc.Handlers.Contact;
using SqlKata.Execution;

namespace SalesforceGRPCTest;
public class UnitTest1 {
    [Fact]
    public void Test1() {
        Assert.Equal(1, 1);
    }

    [Fact]
    public async void TestAccountCreation() {
        //var med = new Mock<IMediator>();
        var db = new Mock<QueryFactory>();

        var dummyEvent = new AccountChangeEvent {
            ChangeEventHeader = new ChangeEventHeader {
                recordIds = new[] { "id 1" },
                changeType = ChangeType.CREATE
            },
            Name = new Switchable_PersonName {
                FirstName = "john",
                LastName = "jasbnkdanskdnakj"
            },
            CreatedDate = 1683583759,
            LastModifiedDate = 1683583759
        };

        var command = new AccountCreateCommand {
            ChangeEvent = dummyEvent,
            Name = "Dummy Name"
        };

        var handler = new AccountCreateHandler.Handler(db.Object);

        await handler.Handle(command, new CancellationToken());
    }

    [Fact]
    public async void TestContactCreation() {
        var db = new Mock<QueryFactory>();

        var dummyEvent = new ContactChangeEvent {
            ChangeEventHeader = new ChangeEventHeader {
                recordIds = new[] { "id 1" },
                changeType = ChangeType.CREATE
            },
            Name = new PersonName {
                FirstName = "john",
                LastName = "jasbnkdanskdnakj"
            },
            CreatedDate = 1683583759,
            LastModifiedDate = 1683583759
        };

        var command = new ContactCreateCommand {
            ChangeEvent = dummyEvent,
            Name = "Dummy Name"
        };

        var handler = new ContactCreateHandler.Handler(db.Object);

        await handler.Handle(command, new CancellationToken());
    }

    /*[Fact(DisplayName = "Test Generic Command Creation")]
    public void MyTestMethod() {
        ISpecificRecord dummyEvent = new AccountChangeEvent {
            ChangeEventHeader = new ChangeEventHeader {
                recordIds = new[] { "id1" },
                changeType = ChangeType.CREATE
            },
            Name = new PersonName {
                FirstName = "john",
                LastName = "jasbnkdanskdnakj"
            },
            CreatedDate = 1683583759,
            LastModifiedDate = 1683583759
        };
        if (dummyEvent.GetType() == typeof(ContactChangeEvent)) {
            Assert.True(true);
        }
        if (dummyEvent.GetType() == typeof(AccountChangeEvent)) {
            Assert.True(false);
        }
    }*/

    [Fact(DisplayName = "Deserialize event")]
    public void DeserializeEvent() {
        var accSchema = Schema.Parse(File.ReadAllText("C:\\Users\\Valeriy Kutsar\\Documents\\programming\\dotnet\\SalesforceGrpc\\SalesforceGrpc\\avro\\AccountChangeEventGRPCSchema.avsc"));
        var nameSchema = Schema.Parse(File.ReadAllText("C:\\Users\\Valeriy Kutsar\\Documents\\programming\\dotnet\\SalesforceGrpc\\SalesforceGrpc\\avro\\Switchable_PersonNameSchema.avsc"));
        var addressSchema = Schema.Parse(File.ReadAllText("C:\\Users\\Valeriy Kutsar\\Documents\\programming\\dotnet\\SalesforceGrpc\\SalesforceGrpc\\avro\\AddressSchema.avsc"));
        var cehSchema = Schema.Parse(File.ReadAllText("C:\\Users\\Valeriy Kutsar\\Documents\\programming\\dotnet\\SalesforceGrpc\\SalesforceGrpc\\avro\\ChangeEventHeaderSchema.avsc"));
        var customObjectSchema = Schema.Parse(File.ReadAllText("C:\\Users\\Valeriy Kutsar\\Documents\\programming\\dotnet\\SalesforceGrpc\\SalesforceGrpc\\avro\\Some_Custom_Object__ChangeEventGRPCSchema.avsc"));
        var accountChangeEvent = new AccountChangeEvent();
        var changeEventHeader = new ChangeEventHeader {
            entityName = "Account",
            recordIds = new[] { "recordIdListToDecompile" },
            changeType = ChangeType.UPDATE,
            changeOrigin = "Your moms house",
            transactionKey = "Some trans key",
            sequenceNumber = 1,
            commitTimestamp = 23423424,
            commitNumber = 1,
            commitUser = "Some user Id",
            nulledFields = new[] { "nulled fields" },
            diffFields = new[] { "Diff Fields" },
            changedFields = new[] { "Changed Fields" }
        };
        Assert.Equal("Account", changeEventHeader.Get(0));
        accountChangeEvent.Put(0, changeEventHeader);
        accountChangeEvent.Put(1, "Account Name");
        Assert.Equal("Account Name", (string)accountChangeEvent.Get(1));
        Assert.NotNull(accountChangeEvent);
        using var writeStream = new MemoryStream();
        var encoder = new BinaryEncoder(writeStream);
        var writer = new SpecificDatumWriter<AccountChangeEvent>(accSchema);
        writer.Write(accountChangeEvent, encoder);
        encoder.Flush();

        var serializedData = writeStream.ToArray();
        Assert.NotNull(serializedData);

        var cache = new ClassCache();
        cache.LoadClassCache(typeof(Switchable_PersonName), nameSchema);
        cache.LoadClassCache(typeof(Address), addressSchema);
        cache.LoadClassCache(typeof(ChangeEventHeader), cehSchema);
        cache.LoadClassCache(typeof(Some_Custom_Object__ChangeEvent), customObjectSchema);
        using var accStream = new MemoryStream(serializedData);
        var decoder = new BinaryDecoder(accStream);
        var datumReader = new GenericDatumReader<GenericRecord>(accSchema, accSchema);
        var gr = datumReader.Read(null, decoder);

        var grString = gr.ToString();
        Assert.NotNull(grString);
        Assert.Equal("Account Name", (string)gr.GetValue(1));
        var changeEventHeaderValid = gr.GetTypedValue<GenericRecord>("ChangeEventHeader", out var genericChangeEventHeader);
        Assert.True(changeEventHeaderValid);
        Assert.NotNull(genericChangeEventHeader);

        var entityNameFound = genericChangeEventHeader.GetTypedValue<string>("entityName", out var entityName);
        Assert.True(entityNameFound);
        Assert.Equal("Account", entityName);

        var changeTypeFound = genericChangeEventHeader.GetTypedValue<dynamic>("changeType", out var changeType);
        Assert.True(changeTypeFound);
        Assert.NotNull(changeType);
        Assert.Equal(ChangeType.UPDATE.ToString(), changeType.Value!);

        /* var recordIdsFound = genericChangeEventHeader.TryGetValue("recordIds", out var recordIdsObject);
		var recordIds = recordIdsObject as object[];
		Assert.True(recordIdsFound);
		Assert.NotNull(recordIds);
		Assert.Single(recordIds);
		Assert.Equal("recordIdListToDecompile", recordIds.First().ToString()); */
    }

    [Fact(DisplayName = "Process Account Creation as Generic Record")]
    public async Task Should_Process_Account_Creation_As_Generic_Record() {
        var accSchema = Schema.Parse(File.ReadAllText("C:\\Users\\Valeriy Kutsar\\Documents\\programming\\dotnet\\SalesforceGrpc\\SalesforceGrpc\\avro\\AccountChangeEventGRPCSchema.avsc"));
        var cancellationToken = new CancellationToken();
        var mockDb = new Mock<IPGRepository>();
        var mappedFields = new List<MappedField>
        {
            new MappedField { Id = 1, EntityName = "Account", SalesforceFieldName = "Name", PostgresFieldName = "name" },
            new MappedField { Id = 2, EntityName = "Account", SalesforceFieldName = "Id", PostgresFieldName = "sf_id" },
            new MappedField { Id = 3, EntityName = "Account", SalesforceFieldName = "RecordTypeId", PostgresFieldName = "record_type_sf_id" },
            new MappedField { Id = 4, EntityName = "Account", SalesforceFieldName = "Phone", PostgresFieldName = "phone" }
        };
        IEnumerable<MappedField> realMappedFields = mappedFields;
        mockDb
            .Setup(q => q.GetAllMappedFieldsAsync("Account", cancellationToken))
            .ReturnsAsync(realMappedFields);

        var gr = new GenericRecord(accSchema as RecordSchema);
        var ceh = ConstructChangeEventHeader("Account", ChangeType.CREATE);
        gr.Add("ChangeEventHeader", ceh);
        var grName = ConstructPersonName();
        gr.Add("Name", grName);
        gr.Add("RecordTypeId", "Tu Madre");
        gr.Add("Phone", "wing wing herro");

        var command = new CreateCommand {
            ChangeEvent = gr,
            EntityName = "Account"
        };
        var sfRecord = command.ChangeEvent;

        // Test Change Event Header
        /*sfRecord.TryGetValue("ChangeEventHeader", out var changeEventHeader);
        Assert.NotNull(changeEventHeader);
        var cehGR = changeEventHeader as GenericRecord;
        Assert.NotNull(cehGR);
        Assert.Equal("Account", cehGR.GetValue(0));
        Assert.Equal(ChangeType.CREATE, cehGR.GetValue(2));

        // Test Name Object
        sfRecord.TryGetValue("Name", out var resName);
        Assert.NotNull(resName);
        var grResName = resName as GenericRecord;
        Assert.NotNull(grResName);
        var hasFirstName = grResName.TryGetValue("FirstName", out var firstName);
        Assert.True(hasFirstName);
        Assert.NotNull(firstName);
        Assert.Equal("Steven", firstName.ToString());*/

        var handler = new SObjectCreateHandler.Handler(mockDb.Object);
        await handler.Handle(command, cancellationToken);
    }

    private static GenericRecord ConstructPersonName() {
        var nameSchema = Schema.Parse(File.ReadAllText("C:\\Users\\Valeriy Kutsar\\Documents\\programming\\dotnet\\SalesforceGrpc\\SalesforceGrpc\\avro\\Switchable_PersonNameSchema.avsc"));
        var grName = new GenericRecord(nameSchema as RecordSchema);
        grName.Add("Salutation", "Mr");
        grName.Add("FirstName", "Steven");
        grName.Add("LastName", "Spielberg");
        grName.Add("MiddleName", "Jason");
        grName.Add("InformalName", "Faget");
        grName.Add("Suffix", "Retard");

        return grName;
    }

    private static GenericRecord ConstructChangeEventHeader(string entity, ChangeType changeType) {
        var cehSchema = Schema.Parse(File.ReadAllText("C:\\Users\\Valeriy Kutsar\\Documents\\programming\\dotnet\\SalesforceGrpc\\SalesforceGrpc\\avro\\ChangeEventHeaderSchema.avsc"));
        var ceh = new GenericRecord(cehSchema as RecordSchema);
        ceh.Add("entityName", entity);
        ceh.Add("recordIds", new List<string> { "recordIdsthatAreEncodedSomehowIForgotHow" });
        ceh.Add("changeType", changeType);
        ceh.Add("changeOrigin", "API");
        ceh.Add("transactionKey", "Don\'t know what this is");
        ceh.Add("sequenceNumber", "Don\'t know what this is either");
        ceh.Add("commitTimestamp", 1690429429453);
        ceh.Add("commitNumber", 1690429429453);
        ceh.Add("commitUser", "YourMomma");
        ceh.Add("nulledFields", new List<string> { "SomeStringForNulledFields" });
        ceh.Add("diffFields", new List<string> { "SomeStringForDiffFields" });
        ceh.Add("changedFields", new List<string> { "SomeStringForChangedFields" });

        return ceh;
    }

    private static GenericRecord ConstructAddress() {
        var addressSchema = Schema.Parse(File.ReadAllText("C:\\Users\\Valeriy Kutsar\\Documents\\programming\\dotnet\\SalesforceGrpc\\SalesforceGrpc\\avro\\AddressSchema.avsc"));
        var addy = new GenericRecord(addressSchema as RecordSchema);
        addy.Add("Street", "3331 Kimberly Rd");
        addy.Add("City", "Cameron Park");
        addy.Add("State", "California");
        addy.Add("PostalCode", "95682");
        addy.Add("Country", "United States");
        addy.Add("StateCode", "CA");
        addy.Add("CountryCode", "USA");
        addy.Add("Latitude", 38.668770);
        addy.Add("Longitude", -120.992410);
        addy.Add("Xyz", "idk what this is");
        addy.Add("GeocodeAccuracy", "Address");

        return addy;
    }
}

public class FakeConsumerEvent {
    public FakeProducerEvent Event { get; set; }
}

public class FakeProducerEvent {
    public string Id { get; set; }
    public string SchemaId { get; set; }
    public int[] Payload { get; set; }
}