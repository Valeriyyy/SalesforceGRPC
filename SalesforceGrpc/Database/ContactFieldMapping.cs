namespace SalesforceGrpc.Database;
public class ContactFieldMapping {
    public static readonly Dictionary<string, string> _contMappings = new() {
        { "Name", "name" },
        { "NameFirstName", "first_name" },
        { "Phone", "phone" },
        { "MobilePhone", "mobile_phone" },
        { "NameLastName", "last_name" },
        { "AccountId", "account_id" },
        { "RecordTypeId", "record_type_id" },
        { "Email", "email" },
        { "Title", "title" },
        { "IsPersonAccount", "is_person_account" },
        { "GUID__c", "guid" },
        { "CreatedDate", "created_date" },
        { "LastModifiedDate", "last_modified_date" }
    };
}
