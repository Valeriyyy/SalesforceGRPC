using Avro;
using Avro.IO;
using Avro.Reflect;
using Avro.Specific;
//using com.sforce.eventbus;
//using Models.PostgresModels;
using Newtonsoft.Json;
using Npgsql;
using SalesforceGrpc.Database;
using SalesforceGrpc.Extensions;
//using SalesforceGrpc.Models;
using SqlKata.Compilers;
using SqlKata.Execution;
using System;
using System.Collections;
using System.Dynamic;
using System.Net.Http.Headers;
using System.Numerics;
using System.Text;
using static System.Console;

namespace SalesforceGrpc;

public class SalesforceAvroDeserializer {
    /*private readonly ILogger<SalesforceAvroDeserializer> _logger;
    private readonly IConfiguration _config;
    private readonly QueryFactory _db;

    public SalesforceAvroDeserializer(
        ILogger<SalesforceAvroDeserializer> logger,
        IConfiguration config,
        QueryFactory db) {
        _logger = logger;
        _config = config;
        _db = db;
    }

    public async Task KataSelect() {
        var changedFields = new List<string> { "Name", "Guid__c" };
        var connection = new NpgsqlConnection(_config.GetConnectionString("postgresLocal"));
        var compiler = new PostgresCompiler();

        var db = new QueryFactory(connection, compiler) {
            Logger = compiled => {
                _logger.LogInformation("Query {query}", compiled.ToString());
            }
        };

        var query = db.Query("salesforce.account");
        foreach (var field in changedFields) {
            AccountFieldMapping._accMappings.TryGetValue(field, out var value);
            if (value is not null) {
                query.Select(value);
            }
        }
    }

    public async Task KataInsert() {
        var changedFields = new Dictionary<string, object> {
            { "Name", "Chingu" },
            { "IsActive",  false }
        };
        var connection = new NpgsqlConnection(_config.GetConnectionString("postgresLocal"));
        var compiler = new PostgresCompiler();
        var db = new QueryFactory(connection, compiler);

    }

    public async Task KataUpdate() {
        var connection = new NpgsqlConnection(_config.GetConnectionString("postgresLocal"));
        var compiler = new PostgresCompiler();
        var db = new QueryFactory(connection, compiler);
        db.Logger = compiled => {
            _logger.LogInformation("Query {query}", compiled.ToString());
        };
        var fieldMapper = new Dictionary<string, string> {
            { "Status__c", "status" },
            { "Active__c", "is_active" }
        };
        var changedFields = new List<string> { "Status__c", "Active__c" };
        var recordIds = new List<string> { "001DT000013KhO1YAK", "001DT000013KgiBY7S" };
        var dAcc = new Dictionary<string, object>();
        dAcc.Add("Status__c", "Some Status");
        dAcc.Add("Active__c", false);
        var eo = new ExpandoObject();
        var eoColl = (ICollection<KeyValuePair<string, object>>)eo;
        foreach (var kvp in dAcc) {
            var pgName = fieldMapper[kvp.Key];
            var newKvp = new KeyValuePair<string, object>(pgName, kvp.Value);
            eoColl.Add(newKvp);
        }

        var q = await db.Query("salesforce.account").WhereIn("sf_id", recordIds).UpdateAsync(eo);
        WriteLine("Number of rows updated " + q);
    }

    public async Task DeserializeVPEConcrete(byte[] payload) {
        var schemaName = "Vendor_Profile__ChangeEventGRPCSchema";
        var vpeSchema = Avro.Schema.Parse(await File.ReadAllTextAsync($"./avro/{schemaName}.avsc"));
        var vpeReader = new ReflectReader<Vendor_Profile__ChangeEvent>(vpeSchema, vpeSchema);
        using var vpeStream = new MemoryStream(payload);
        vpeStream.Seek(0, SeekOrigin.Begin);
        var vpeDecoder = new BinaryDecoder(vpeStream);
        var vendorProfileEvent = vpeReader.Read(vpeDecoder);
        var eventType = vendorProfileEvent.ChangeEventHeader.changeType;
        WriteLine(vendorProfileEvent.ChangeEventHeader.entityName + " " + vendorProfileEvent.Name + " has been " + vendorProfileEvent.ChangeEventHeader.changeType.ToString());
        //var con = new NpgsqlConnection(_config.GetConnectionString("postgresLocal"));
        if (eventType is ChangeType.CREATE) {
            foreach (var sfid in vendorProfileEvent.ChangeEventHeader.recordIds) {
                _logger.LogInformation("Vendor Profile was created: {sfid} {vpName}", sfid, vendorProfileEvent.Name);
            }
        } else if (eventType is ChangeType.UPDATE) {
            var changedFields = GetFieldNames(vendorProfileEvent.ChangeEventHeader.changedFields[0][(vendorProfileEvent.ChangeEventHeader.changedFields[0].LastIndexOf('x') + 1)..], vendorProfileEvent);
            changedFields.Add("Status__c");
            foreach (var sfId in vendorProfileEvent.ChangeEventHeader.recordIds) {
                //var vp = new VendorProfile();
                WriteLine(sfId);
                foreach (var field in changedFields) {
                    if (field == "LastModifiedDate" && vendorProfileEvent?.LastModifiedDate is not null) {
                        TimeZoneInfo pacific = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
                        DateTimeOffset dateTimeOffset = DateTimeOffset.FromUnixTimeMilliseconds((long)vendorProfileEvent.LastModifiedDate);
                        DateTime dateTime = dateTimeOffset.DateTime.ToLocalTime();
                        var lastModifiedDateStringLocal = TimeZoneInfo.ConvertTime(dateTime, pacific).ToString("MM/dd/yyyy hh:mm:ss tt");
                        WriteLine("LastModifiedDate " + lastModifiedDateStringLocal);
                    } else {
                        _logger.LogInformation("{0} {1}", field, vendorProfileEvent?.GetType()?.GetProperty(field)?.GetValue(vendorProfileEvent));
                    }
                    //Console.WriteLine(field + " " + vendorProfileEvent.GetType().GetProperty(field).GetValue(vendorProfileEvent));
                }
            }
        } else if (eventType is ChangeType.DELETE) {
            WriteLine("Vendor Profile has been deleted");
            foreach (var sfid in vendorProfileEvent.ChangeEventHeader.recordIds) {
                WriteLine(sfid + " was deleted");
            }
        }
    }

    public async Task DeserializeAccountConcrete(byte[] payload) {
        var accSchema = Schema.Parse(File.ReadAllText("./avro/AccountGRPCSchema.avsc"));
        var nameSchema = Schema.Parse(File.ReadAllText("./avro/SwitchablePersonNameSchema.avsc"));
        var addressSchema = Schema.Parse(File.ReadAllText("./avro/AddressSchema.avsc"));
        var cache = new ClassCache();
        cache.LoadClassCache(typeof(Switchable_PersonName), nameSchema);
        cache.LoadClassCache(typeof(Address), addressSchema);
        var reader = new ReflectReader<AccountChangeEvent>(accSchema, accSchema, cache);
        using var accStream = new MemoryStream(payload);
        accStream.Seek(0, SeekOrigin.Begin);
        var accDecoder = new BinaryDecoder(accStream);
        var accEvent = reader.Read(accDecoder);
        WriteLine("Event Type: " + accEvent.ChangeEventHeader.changeType.ToString());
        var eventType = accEvent.ChangeEventHeader.changeType;
        _db.Logger = compiled => {
            _logger.LogInformation("Query {query}", compiled.ToString());
        };
        var tasks = new List<Task>();
        if (eventType is ChangeType.CREATE) {
            var eo = new ExpandoObject();
            var eoColl = (ICollection<KeyValuePair<string, object>>)eo;
            var accEventFieldNamesDict = accEvent.GetFieldNamesDict();
            for (int i = 1; i < accEventFieldNamesDict.Count; i++) {
                var fieldName = accEventFieldNamesDict[i];
                var isValidName = AccountFieldMapping._accMappings.TryGetValue(fieldName, out var pgName);
                if (isValidName) {
                    var fieldValue = accEvent.Get(i);
                    var kvp = new KeyValuePair<string, object>();
                    if (fieldName == "LastModifiedDate" || fieldName == "CreatedDate") {
                        var dateTime = ConvertEpochToDateTime((long)accEvent.Get(i));
                        //var newKvp = new KeyValuePair<string, object>("last_modified_date", dateTime);
                        //eoColl.Add(newKvp);
                        kvp = new KeyValuePair<string, object>(fieldName, dateTime);
                    } else {
                        //eoColl.Add(new KeyValuePair<string, object>(pgName, accEvent.Get(i)));
                        kvp = new KeyValuePair<string, object>(pgName, accEvent.Get(i));
                    }
                    eoColl.Add(kvp);
                }
            }
            eoColl.Add(new KeyValuePair<string, object>("sf_id", accEvent.ChangeEventHeader.recordIds[0]));
            eoColl.Add(new KeyValuePair<string, object>("guid", Guid.NewGuid()));
            //eoColl.Add(new KeyValuePair<string, object> ("created_date", DateTime.Now));
            tasks.Add(_db.Query("salesforce.account").InsertAsync(eoColl));
        } else if (eventType is ChangeType.UPDATE) {
            var eo = new ExpandoObject();
            var eoColl = (ICollection<KeyValuePair<string, object>>)eo;
            var accEventFieldNamesDict = accEvent.GetFieldNamesDict();
            WriteLine(accEvent.ChangeEventHeader.changedFields.Count);
            foreach (var changedFieldHex in accEvent.ChangeEventHeader.changedFields) {
                _logger.LogInformation("Changed Field Hex: {hex}", changedFieldHex);
                var changedFieldsSplitArray = changedFieldHex.Split('-');
                if (changedFieldsSplitArray.Length > 1) {
                    var avroFieldNumber = int.Parse(changedFieldsSplitArray[0]);
                    var hexBitMap = changedFieldsSplitArray[1];
                    _logger.LogInformation("Nested field changed at position: {p} with value: {v}", avroFieldNumber, hexBitMap);
                    var nestedObj = (ISpecificRecord)accEvent.Get(avroFieldNumber);
                    var accFieldName = accEventFieldNamesDict[avroFieldNumber];
                    var reversedBitArray = GetReveresedBitArray(hexBitMap[(hexBitMap.LastIndexOf('x') + 1)..]);
                    var nestedNamesDict = nestedObj.GetFieldNamesDict();
                    for (int i = 0; i < reversedBitArray.Length; i++) {
                        if (reversedBitArray[i]) {
                            WriteLine("found true at " + i);
                            var fieldName = nestedNamesDict[i];
                            var fieldValue = nestedObj.Get(i);
                            var validField = AccountFieldMapping._accMappings.TryGetValue(accFieldName + fieldName, out var pgName);
                            WriteLine(accFieldName + " " + fieldName + " " + fieldValue);
                            if (validField) {
                                var newKvp = new KeyValuePair<string, object>(pgName, fieldValue);
                                eoColl.Add(newKvp);
                            }
                        }
                    }
                } else {
                    var hexBitMap = changedFieldsSplitArray.First();
                    var reversedBitArray = GetReveresedBitArray(hexBitMap[(hexBitMap.LastIndexOf('x') + 1)..]);
                    _logger.LogInformation("Main Object Changed with hex value: {v}", hexBitMap);
                    for (int i = 0; i < reversedBitArray.Length; i++) {
                        if (reversedBitArray[i]) {
                            WriteLine("found true at " + i);
                            var fieldName = accEventFieldNamesDict[i];
                            var fieldValue = accEvent.Get(i);
                            WriteLine(fieldName + " " + fieldValue);
                            var validField = AccountFieldMapping._accMappings.TryGetValue(fieldName, out var pgName);
                            if (validField && pgName is not null) {
                                if (fieldName == "LastModifiedDate") {
                                    TimeZoneInfo pacific = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
                                    DateTimeOffset dateTimeOffset = DateTimeOffset.FromUnixTimeMilliseconds((long)accEvent.LastModifiedDate);
                                    DateTime dateTime = dateTimeOffset.DateTime.ToLocalTime();
                                    var lastModifiedDateStringLocal = TimeZoneInfo.ConvertTime(dateTime, pacific).ToString("MM/dd/yyyy hh:mm:ss tt");
                                    var newKvp = new KeyValuePair<string, object>("last_modified_date", dateTime);
                                    eoColl.Add(newKvp);
                                } else {
                                    var newKvp = new KeyValuePair<string, object>(pgName, fieldValue);
                                    eoColl.Add(newKvp);
                                }
                            } else {
                                _logger.LogWarning("Field {f} is not a valid field and will not be updated in the db", fieldName);
                            }
                        }
                    }
                }
            }
            tasks.Add(_db.Query("salesforce.account").WhereIn("sf_id", accEvent.ChangeEventHeader.recordIds).UpdateAsync(eo));
        } else if (eventType is ChangeType.DELETE) {
            WriteLine("Account has been deleted");
            tasks.Add(_db.Query("salesforce.account").WhereIn("sf_id", accEvent.ChangeEventHeader.recordIds).UpdateAsync(new { is_deleted = true, deleted_date = DateTime.Now }));
        } else if (eventType is ChangeType.UNDELETE) {
            tasks.Add(_db.Query("salesforce.account").WhereIn("sf_id", accEvent.ChangeEventHeader.recordIds).UpdateAsync(new { is_deleted = false, deleted_date = (DateTime?)null }));
        }
        try {
            await Task.WhenAll(tasks);
        } catch (Exception ex) {
            _logger.LogError("Account Processing Exception: {message}", ex.Message);
            _logger.LogError("With stack Trace: {trace}", ex.StackTrace);
            foreach (var task in tasks) {
                if (task.Exception is not null) {
                    _logger.LogError(ex.StackTrace);
                }
            }
        }
    }

    public async Task DeserializeContactConcrete(byte[] payload) {
        var contactSchema = Avro.Schema.Parse(await File.ReadAllTextAsync("./avro/ContactChangeEventGRPCSchema.avsc"));
        var nameSchema = Avro.Schema.Parse(File.ReadAllText("./avro/PersonNameSchema.avsc"));
        var addressSchema = Avro.Schema.Parse(File.ReadAllText("./avro/AddressSchema.avsc"));
        var cache = new ClassCache();
        cache.LoadClassCache(typeof(PersonName), nameSchema);
        cache.LoadClassCache(typeof(Address), addressSchema);
        var reader = new ReflectReader<ContactChangeEvent>(contactSchema, contactSchema, cache);
        using var contStream = new MemoryStream(payload);
        contStream.Seek(0, SeekOrigin.Begin);
        var contDecoder = new BinaryDecoder(contStream);
        var contEvent = reader.Read(contDecoder);
        WriteLine("Event Type: " + contEvent.ChangeEventHeader.changeType.ToString());
        var eventType = contEvent.ChangeEventHeader.changeType;
        if (eventType is ChangeType.UPDATE) {
            var changedFields = GetFieldNames(contEvent.ChangeEventHeader.changedFields[0][(contEvent.ChangeEventHeader.changedFields[0].LastIndexOf('x') + 1)..], contEvent);
            if (contEvent.Name is not null) {
                var fullName = contEvent.Name;
                WriteLine(contEvent.Name.FirstName + " " + contEvent.Name.LastName);

            }
            foreach (var sfId in contEvent.ChangeEventHeader.recordIds) {
                WriteLine(sfId);
                foreach (var field in changedFields) {
                    if (field == "LastModifiedDate" && contEvent.LastModifiedDate is not null) {
                        TimeZoneInfo pacific = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
                        DateTimeOffset dateTimeOffset = DateTimeOffset.FromUnixTimeMilliseconds((long)contEvent.LastModifiedDate);
                        DateTime dateTime = dateTimeOffset.DateTime.ToLocalTime();
                        var lastModifiedDateStringLocal = TimeZoneInfo.ConvertTime(dateTime, pacific).ToString("MM/dd/yyyy hh:mm:ss tt");
                        WriteLine("LastModifiedDate " + lastModifiedDateStringLocal);
                    } else {
                        WriteLine(field + " " + contEvent.GetType().GetProperty(field).GetValue(contEvent));
                    }
                }
            }
        } else if (eventType is ChangeType.DELETE) {
            WriteLine("contact has been deleted");
            foreach (var sfid in contEvent.ChangeEventHeader.recordIds) {
                WriteLine(sfid + " was deleted");
            }
        }
    }

    private static List<string> GetFieldNames(string byteString, string schemaName) {
        var bytes = Convert.FromHexString(byteString);
        Array.Reverse(bytes);
        var bits = new BitArray(bytes);
        var schema = JsonConvert.DeserializeObject<dynamic>(
           File.ReadAllText($"./avro/{schemaName}.avsc"));
        var fieldNames = new List<string>();
        for (int i = 0; i < bits.Length; i++) {
            if (bits[i]) {
                fieldNames.Add((string)schema.fields[i].name);
            }
        }

        return fieldNames;
    }

    private static List<string> GetFieldNames(string byteString, ISpecificRecord avroEvent) {
        var bytes = Convert.FromHexString(byteString);
        Array.Reverse(bytes);
        var bits = new BitArray(bytes);
        var fieldNames = new List<string>();
        WriteLine("Bits Length " + bits.Length);
        for (int i = 0; i < bits.Length; i++) {
            WriteLine(bits[i]);
            if (bits[i]) {
                WriteLine("found true at " + i);
                //fieldNames.Add(avroEvent.Get(i));
                WriteLine(avroEvent.Get(i));
            }
        }

        return fieldNames;
    }

    private static List<string> GetFieldNamesReflection(string byteString, ISpecificRecord avroEvent) {
        var bits = GetReveresedBitArray(byteString);
        var fields = avroEvent
            .GetType()
            .GetProperties()
            .Where(f => f.Name != "Schema")
            .Select((obj, index) => new { Key = index, Value = obj.Name })
            .ToDictionary(p => p.Key, p => p.Value);
        var fieldNames = new List<string>();
        for (int i = 0; i < bits.Length; i++) {
            if (bits[i]) {
                fieldNames.Add(fields[i]);
            }
        }

        return fieldNames;
    }

    private static BitArray GetReveresedBitArray(string byteString) {
        var bytes = Convert.FromHexString(byteString);
        Array.Reverse(bytes);
        return new BitArray(bytes);
    }

    private static DateTime ConvertEpochToDateTime(long dateTimeNumber) {
        DateTimeOffset dateTimeOffset = DateTimeOffset.FromUnixTimeMilliseconds(dateTimeNumber);
        return dateTimeOffset.DateTime;
    }

    *//*public async Task<SFAccount> SetAccountGUID(SFAccount acc) {
        var uri = new Uri("https://rcgauto--valeriybox.sandbox.my.salesforce.com");
        var sfClient = new HttpClient {
            BaseAddress = uri
        };
        sfClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Worker.token);
        var dict = new Dictionary<string, Guid?>
        {
            { "GUID__c", acc.Guid }
        };
        var patchPayload = new StringContent(JsonConvert.SerializeObject(dict), Encoding.UTF8, "application/json");
        WriteLine($"updating account in salesforce {acc.SfId} with guid {acc.Guid}");
        try {
            var res = await sfClient.PatchAsync($"/services/data/v56.0/sobjects/Account/{acc.SfId}", patchPayload);
            WriteLine(res.StatusCode.ToString());
        } catch (Exception ex) {
            WriteLine("sfclient exception: " + ex.Message);
        }

        return acc;
    }*//*

    private static (string? name, string? firstName, string? lastName) GetAccountName(object accountName) {
        if (accountName is Switchable_PersonName && accountName is not null) {
            var personName = accountName as Switchable_PersonName;
            return (personName?.FirstName + " " + personName?.LastName, personName?.FirstName, personName?.LastName);
        } else {
            WriteLine(accountName?.ToString());
            return (accountName?.ToString(), null, null);
        }
    }*/
}
