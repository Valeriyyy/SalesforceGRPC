using Avro;
using Avro.Generic;
using com.sforce.eventbus;
using Database.Models;
using Database.Repositories;
using Database.Repositories.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SalesforceGrpc.Strategies;

namespace SalesforceGRPCTest;

public class UpdateEventTest {
    private const string ContactSchemaPath = "/Users/valeriykutsar/Documents/programming/dotnet/SalesforceGRPC/SalesforceGrpc/avro/ContactChangeEvent.avsc";
    private const string ChangeEventHeaderPath = "/Users/valeriykutsar/Documents/programming/dotnet/SalesforceGRPC/SalesforceGrpc/avro/ChangeEventHeaderSchema.avsc";
    private const string PersonNamePath = "/Users/valeriykutsar/Documents/programming/dotnet/SalesforceGRPC/SalesforceGrpc/avro/PersonNameSchema.avsc";
    
    
    [Fact]
    public async Task TestContactUpdatedEvent() {
        // Arrange
        // var mockMetaRepo = new Mock<IMetaRepository>();
        // var mockStrategy = new Mock<IEventStrategy>();
        // var processor = new EventProcessor(mockMetaRepo.Object, new List<IEventStrategy> { mockStrategy.Object });
        
        var mockMetaRepo = Substitute.For<IMetaRepository>();
        var mockDataRepo = Substitute.For<IDataRepository>();
        var mockLogger = Substitute.For<ILogger<UpdateStrategy>>();
        var inMemorySettings = new Dictionary<string, string> { { "Salesforce:ClientId", "testClientId" }, { "Salesforce:ClientSecret", "testClientSecret" } };
        var dbSchema = new CDCSchema { Id = 1, SchemaId = "SomeSchemaId", SchemaName = "ContactSchema", EntityName = "Contact", DbSchemaFullName = "salesforce.contacts" };
        mockMetaRepo.GetCachedSchemas(CancellationToken.None).Returns(new List<CDCSchema> { dbSchema });
        mockMetaRepo.GetCachedMapping(dbSchema.Id, CancellationToken.None).Returns(new Dictionary<string, string> {
            { "NameFirstName", "first_name" },
            { "NameLastName", "last_name" },
            // { "Name", "name" },
            { "Phone", "phone"}
        });
        var updateStrategy = new UpdateStrategy(mockLogger, mockMetaRepo, mockDataRepo);
        
        var changeEventHeaderSchema = (RecordSchema)Schema.Parse(await File.ReadAllTextAsync(ChangeEventHeaderPath, TestContext.Current.CancellationToken));
        var changeEventHeader = new GenericRecord(changeEventHeaderSchema);
        changeEventHeader.Add("changeType", ChangeType.UPDATE);
        changeEventHeader.Add("entityName", "Contact");
        changeEventHeader.Add("recordIds", new object[] { "001" });
        
        var personNameSchema = (RecordSchema)Schema.Parse(await File.ReadAllTextAsync(PersonNamePath, TestContext.Current.CancellationToken));
        var personName = new GenericRecord(personNameSchema);
        personName.Add("FirstName", "Updated");
        personName.Add("LastName", "User");
        
        var contactSchema = (RecordSchema)Schema.Parse(await File.ReadAllTextAsync(ContactSchemaPath, TestContext.Current.CancellationToken));
        var record = new GenericRecord(contactSchema);
        record.Add("ChangeEventHeader", changeEventHeader);
        record.Add("Name", personName);
        record.Add("Phone", "0987654321");
        Assert.NotNull(record);
        
        await updateStrategy.ProcessEvent(record, contactSchema, dbSchema, CancellationToken.None);
    }
}