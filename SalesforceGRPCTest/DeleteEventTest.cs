using Avro;
using Avro.Generic;
using com.sforce.eventbus;
using Database.Models;
using Database.Repositories.Interfaces;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SalesforceGrpc.Strategies;

namespace SalesforceGRPCTest;

public class DeleteEventTest {
    private const string ContactSchemaPath = "/Users/valeriykutsar/Documents/programming/dotnet/SalesforceGRPC/SalesforceGrpc/avro/ContactChangeEvent.avsc";
    private const string ChangeEventHeaderPath = "/Users/valeriykutsar/Documents/programming/dotnet/SalesforceGRPC/SalesforceGrpc/avro/ChangeEventHeaderSchema.avsc";
    
    [Fact]
    public async Task TestDeleteEvent() {
        // Arrange
        var mockMetaRepo = Substitute.For<IMetaRepository>();
        var mockLogger = Substitute.For<ILogger<DeleteStrategy>>();
        var mockDataRepo = Substitute.For<IDataRepository>();
        var dbSchema = new CDCSchema {
            Id = 1,
            SchemaId = "SomeSchemaId",
            SchemaName = "ContactSchema",
            EntityName = "Contact",
            DbSchemaFullName = "salesforce.contacts"
        };
        
        mockMetaRepo.GetCachedMapping(dbSchema.Id, CancellationToken.None).Returns(new Dictionary<string, string> {
            { "MappedSFKey", "sf_id" }
        });
        var deleteStrategy = new DeleteStrategy(mockLogger, mockDataRepo, mockMetaRepo);
        
        var changeEventHeaderSchema = (RecordSchema)Schema.Parse(await File.ReadAllTextAsync(ChangeEventHeaderPath, TestContext.Current.CancellationToken));
        var changeEventHeader = new GenericRecord(changeEventHeaderSchema);
        changeEventHeader.Add("changeType", ChangeType.CREATE);
        changeEventHeader.Add("entityName", "Contact");
        changeEventHeader.Add("recordIds", new object[] { "001" });
        
        var contactSchema = (RecordSchema)Schema.Parse(await File.ReadAllTextAsync(ContactSchemaPath, TestContext.Current.CancellationToken));
        var record = new GenericRecord(contactSchema);
        record.Add("ChangeEventHeader", changeEventHeader);
        
        // Act
        await deleteStrategy.ProcessEvent(record, contactSchema, dbSchema, CancellationToken.None);
        
        // Assert does not throw for now
    }
}