using Avro;
using Avro.Generic;
using Avro.IO;
using Avro.Reflect;
using Avro.Specific;
using com.sforce.eventbus;
using Database;
using GrpcClient;
using Moq;
using Newtonsoft.Json;
using SalesforceGrpc.Database;
using SalesforceGrpc.Extensions;
using SalesforceGrpc.Handlers;
using SqlKata.Execution;
using System;
using System.Collections;
using System.Linq;
using static System.Console;

namespace SalesforceGRPCTest;
public class UnitTest1 {
    private static string GetSchemaFilePath(string entityName) {
        // /Users/valeriykutsar/Documents/programming/dotnet/SalesforceGRPC/SalesforceGrpc/avro/AccountChangeEventGRPCSchema.avsc
        return $"/Users/valeriykutsar/Documents/programming/dotnet/SalesforceGRPC/SalesforceGrpc/avro/{entityName}ChangeEventGRPCSchema.avsc";
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
        var accSchema = Schema.Parse(File.ReadAllText("/Users/valeriykutsar/Documents/programming/dotnet/SalesforceGRPC/SalesforceGrpc/avro/AccountChangeEventGRPCSchema.avsc"));
        var cancellationToken = new CancellationToken();
        var mockDb = new Mock<IMetaRepository>();
        var mappedFields = new List<MappedField>
        {
            new MappedField { Id = 1, SalesforceFieldName = "Name", PostgresFieldName = "name" },
            new MappedField { Id = 2, SalesforceFieldName = "Id", PostgresFieldName = "sf_id" },
            new MappedField { Id = 3, SalesforceFieldName = "RecordTypeId", PostgresFieldName = "record_type_sf_id" },
            new MappedField { Id = 4, SalesforceFieldName = "Phone", PostgresFieldName = "phone" }
        };
        IEnumerable<MappedField> realMappedFields = mappedFields;
        mockDb
            .Setup(q => q.GetAllMappedFieldsAsync(1, cancellationToken))
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

    [Fact]
    public void DeserializeAvroEvent() {
        var accSchema = Schema.Parse(File.ReadAllText(GetSchemaFilePath("Account")));
        Assert.NotNull(accSchema);
        var updateJson = File.ReadAllText("/Users/valeriykutsar/Documents/programming/dotnet/SalesforceGRPC/SalesforceGrpc/AccountUpdateEvents.json");
        Assert.NotNull(updateJson);
        var cdcEvent = JsonConvert.DeserializeObject<FakeConsumerEvent>(updateJson);
        Assert.NotNull(cdcEvent);
        Assert.NotEmpty(cdcEvent.Events);
        Assert.Equal("i8wgJwwM-AVcDbFkbRl5Nw", cdcEvent.Events[0].@event.SchemaId);
        
        
    }

    [Fact(DisplayName = "Deserialize AccountUpdateEvents payload to GenericRecord (schema-agnostic)")]
    public void DeserializeAccountUpdateEventsPayloadAsGenericRecord() {
        // Load the schema
        var accSchema = Schema.Parse(File.ReadAllText(GetSchemaFilePath("Account")));
        
        // Load the JSON event
        var updateJson = File.ReadAllText("/Users/valeriykutsar/Documents/programming/dotnet/SalesforceGRPC/SalesforceGrpc/json/AccountEvents/AccountAddressUpdateEvent.json");
        var cdcEvent = JsonConvert.DeserializeObject<FakeConsumerEvent>(updateJson);
        
        Assert.NotNull(cdcEvent);
        Assert.NotEmpty(cdcEvent.Events);
        
        var firstEvent = cdcEvent.Events[0];
        Assert.NotNull(firstEvent.@event.Payload);
        
        // Decode the base64 payload
        byte[] decodedPayload = Convert.FromBase64String(firstEvent.@event.Payload);
        Assert.NotNull(decodedPayload);
        Assert.NotEmpty(decodedPayload);
        
        // Deserialize as GenericRecord (schema-agnostic, handles schema evolution automatically)
        var reader = new GenericDatumReader<GenericRecord>(accSchema, accSchema);
        using var payloadStream = new MemoryStream(decodedPayload);
        payloadStream.Seek(0, SeekOrigin.Begin);
        var decoder = new BinaryDecoder(payloadStream);
        var genericRecord = reader.Read(new GenericRecord(accSchema as RecordSchema), decoder);
        
        // Verify the deserialized object
        Assert.NotNull(genericRecord);
        
        // Access nested ChangeEventHeader using TryGetValue
        bool hasCEH = genericRecord.TryGetValue("ChangeEventHeader", out var cehObj);
        Assert.True(hasCEH);
        var changeEventHeader = cehObj as GenericRecord;
        Assert.NotNull(changeEventHeader);
        
        bool hasEntity = changeEventHeader.TryGetValue("entityName", out var entity);
        Assert.True(hasEntity);
        Assert.Equal("Account", entity);
        
        WriteLine("=== Deserialized as GenericRecord (Schema-Agnostic) ===");
        WriteLine($"Entity: {entity}");
        
        if (changeEventHeader.TryGetValue("recordIds", out var recordIdsObj)) {
            var recordIds = recordIdsObj as dynamic[];
            if (recordIds != null) {
                WriteLine($"Record IDs: {string.Join(", ", recordIds)}");
            }
        }
        
        if (changeEventHeader.TryGetValue("changeType", out var changeType)) {
            WriteLine($"Change Type: {changeType}");
        }
        
        if (changeEventHeader.TryGetValue("changeOrigin", out var changeOrigin)) {
            WriteLine($"Change Origin: {changeOrigin}");
        }
        
        if (changeEventHeader.TryGetValue("transactionKey", out var transactionKey)) {
            WriteLine($"Transaction Key: {transactionKey}");
        }
        
        if (changeEventHeader.TryGetValue("sequenceNumber", out var sequenceNumber)) {
            WriteLine($"Sequence Number: {sequenceNumber}");
        }
        
        if (changeEventHeader.TryGetValue("commitTimestamp", out var commitTimestamp)) {
            WriteLine($"Commit Timestamp: {commitTimestamp}");
        }
        
        if (changeEventHeader.TryGetValue("commitNumber", out var commitNumber)) {
            WriteLine($"Commit Number: {commitNumber}");
        }
        
        if (changeEventHeader.TryGetValue("commitUser", out var commitUser)) {
            WriteLine($"Commit User: {commitUser}");
        }
        
        if (changeEventHeader.TryGetValue("changedFields", out var changedFieldsObj)) {
            var changedFields = changedFieldsObj as dynamic[];
            if (changedFields != null && changedFields.Length > 0) {
                WriteLine($"\n=== Decoding Changed Fields Bitmap ===");
                WriteLine($"Number of changed field bitmaps: {changedFields.Length}");
                
                // Decode each bitmap entry
                foreach (var changedFieldHex in changedFields) {
                    WriteLine($"\nBitmap entry: {changedFieldHex}");
                    
                    // Check if this is a nested field change (contains a dash)
                    var changedFieldStr = changedFieldHex.ToString() ?? "";
                    if (string.IsNullOrEmpty(changedFieldStr)) continue;
                    
                    var changedFieldsSplitArray = changedFieldStr.Split('-');
                    
                    if (changedFieldsSplitArray.Length > 1) {
                        // Nested field changed
                        var avroFieldNumber = int.Parse(changedFieldsSplitArray[0]);
                        var hexBitMap = changedFieldsSplitArray[1];
                        WriteLine($"  Nested field change at AVRO field position: {avroFieldNumber}");
                        WriteLine($"  Hex bitmap: {hexBitMap}");
                        
                        // Decode the bitmap for the nested field
                        var decodedNestedFields = DecodeChangedFieldsBitmap(hexBitMap, accSchema, avroFieldNumber);
                        WriteLine($"  Changed nested fields: {string.Join(", ", decodedNestedFields)}");
                        
                        // Retrieve and test the changed nested field values
                        RecordSchema? recSchema = accSchema as RecordSchema;
                        if (recSchema != null && avroFieldNumber < recSchema.Fields.Count) {
                            var nestedFieldName = recSchema.Fields[avroFieldNumber].Name;
                            object nestedFieldValue;
                            if (genericRecord.TryGetValue(nestedFieldName, out nestedFieldValue)) {
                                if (nestedFieldValue is GenericRecord nestedRecord) {
                                    WriteLine($"  Nested field '{nestedFieldName}' values:");
                                    foreach (var decodedField in decodedNestedFields) {
                                        object fieldValue;
                                        if (nestedRecord.TryGetValue(decodedField, out fieldValue)) {
                                            WriteLine($"    {decodedField}: {fieldValue}");
                                            Assert.NotNull(fieldValue);
                                        }
                                    }
                                }
                            }
                        }
                    } else {
                        // Top-level field changed
                        var hexBitMap = changedFieldStr;
                        WriteLine($"  Top-level field change");
                        WriteLine($"  Hex bitmap: {hexBitMap}");
                        
                        // Decode the bitmap for top-level fields
                        var decodedFields = DecodeChangedFieldsBitmap(hexBitMap, accSchema, -1);
                        WriteLine($"  Changed fields: {string.Join(", ", decodedFields)}");
                        
                        // Retrieve and test the actual field values
                        WriteLine($"  Field values:");
                        var changedFieldsWithValues = new Dictionary<string, object>();
                        foreach (var fieldName in decodedFields) {
                            object fieldValue;
                            if (genericRecord.TryGetValue(fieldName, out fieldValue)) {
                                changedFieldsWithValues[fieldName] = fieldValue;
                                WriteLine($"    {fieldName}: {fieldValue}");
                                // Assert that changed fields have values
                                Assert.NotNull(fieldValue);
                            }
                        }
                        // Assert that we found at least one changed field with a value
                        Assert.NotEmpty(changedFieldsWithValues);
                    }
                }
            }
        }
        
        // Dynamically iterate through all top-level fields
        WriteLine("\n=== All Top-Level Fields ===");
        foreach (var field in genericRecord.Schema.Fields) {
            if (genericRecord.TryGetValue(field.Name, out var value) && value != null && !(value is GenericRecord)) {
                WriteLine($"{field.Name}: {value}");
            }
        }
    }

    /// <summary>
    /// Decodes a hexadecimal bitmap string to determine which fields changed
    /// </summary>
    /// <param name="hexBitmap">The hex string to decode (e.g., "0x100000000000800000")</param>
    /// <param name="schema">The Avro schema</param>
    /// <param name="nestedFieldIndex">The index of the nested field if this is a nested change, -1 for top-level</param>
    /// <returns>List of field names that changed</returns>
    private List<string> DecodeChangedFieldsBitmap(string hexBitmap, Schema schema, int nestedFieldIndex) {
        var fieldNames = new List<string>();
        
        // Extract the hex value (remove "0x" prefix if present)
        var hexValue = hexBitmap.Contains("x") 
            ? hexBitmap.Substring(hexBitmap.LastIndexOf('x') + 1) 
            : hexBitmap;
        
        try {
            // Convert hex string to bytes and reverse them
            var bytes = Convert.FromHexString(hexValue);
            Array.Reverse(bytes);
            var bits = new BitArray(bytes);
            
            // Get the appropriate schema based on whether this is nested or top-level
            Schema targetSchema = schema;
            if (nestedFieldIndex >= 0 && schema is RecordSchema recordSchema) {
                // For nested fields, get the schema of the field at the specified index
                if (nestedFieldIndex < recordSchema.Fields.Count) {
                    var field = recordSchema.Fields[nestedFieldIndex];
                    // Handle union types (fields may be [null, SomeType])
                    if (field.Schema is UnionSchema unionSchema) {
                        foreach (var s in unionSchema.Schemas) {
                            if (s is RecordSchema) {
                                targetSchema = s;
                                break;
                            }
                        }
                    } else {
                        targetSchema = field.Schema;
                    }
                }
            }
            
            // Extract field names from the target schema
            if (targetSchema is RecordSchema recSchema) {
                for (int i = 0; i < bits.Length && i < recSchema.Fields.Count; i++) {
                    if (bits[i]) {
                        fieldNames.Add(recSchema.Fields[i].Name);
                    }
                }
            }
        } catch (Exception ex) {
            WriteLine($"Error decoding bitmap: {ex.Message}");
        }
        
        return fieldNames;
    }
}

public class FakeConsumerEvent {
    public List<FakeEvent> Events { get; set; }
    public string LatestReplayId { get; set; }
    public string RpcId { get; set; }
    public int PendingNumRequested { get; set; }
}

public class FakeEvent {
    public FakeProducerEvent @event { get; set; }
    public string ReplayId { get; set; }
}

public class FakeProducerEvent {
    public string Id { get; set; }
    public string SchemaId { get; set; }
    public string Payload { get; set; }
}