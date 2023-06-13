using Avro.Generic;
using Avro.IO;
using Avro.Reflect;
using Avro;
using Avro.Specific;
using com.sforce.eventbus;
using Google.Protobuf;
using GrpcClient;
using Moq;
using SalesforceGrpc.Handlers;
using SalesforceGrpc.Handlers.Account;
using SalesforceGrpc.Handlers.Contact;
using SqlKata.Execution;
using System.Collections;
using System.Text.Json;
using System.Text;
using SalesforceGrpc.Extensions;

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
}

public class FakeConsumerEvent {
    public FakeProducerEvent Event { get; set; }
}

public class FakeProducerEvent {
    public string Id { get; set; }
    public string SchemaId { get; set; }
    public int[] Payload { get; set; }
}