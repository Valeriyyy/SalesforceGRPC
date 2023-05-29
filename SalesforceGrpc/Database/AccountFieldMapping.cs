﻿namespace SalesforceGrpc.Database;
public class AccountFieldMapping {
    public static readonly Dictionary<string, string> _accMappings = new() {
        { "Name", "name" },
        { "NameFirstName", "first_name" },
        { "NameLastName", "last_name" },
        { "GUID__c", "guid" },
        { "Status__c", "status" },
        { "Active__c", "is_active" },
        { "LastModifiedDate", "last_modified_date" },
        { "CreatedDate", "created_date" },
        { "RecordTypeId", "record_type_id" },
        { "Type", "type" },
        { "BillingAddressStreet", "billing_street" },
        { "BillingAddressCity", "billing_city" },
        { "BillingAddressState", "billing_state" },
        { "BillingAddressPostalCode", "billing_postal_code" },
        { "BillingAddressCountry", "billing_country" },
        { "BillingAddressLatitude", "billing_latitude" },
        { "BillingAddressLongitude", "billing_longitude" },
        { "ShippingAddressStreet", "shipping_street" },
        { "ShippingAddressCity", "shipping_city" },
        { "ShippingAddressState", "shipping_state" },
        { "ShippingAddressPostalCode", "shipping_postal_code" },
        { "ShippingAddressCountry", "shipping_country" },
        { "ShippingAddressLatitude", "shipping_latitude" },
        { "ShippingAddressLongitude", "shipping_longitude" },
        { "Order_Instructions__c", "order_instructions" },
        { "Phone", "phone" },
        { "Fax", "fax" },
        { "Email__c", "email" },
        { "DOT_Number__c", "dot_number" },
        { "PersonEmail", "person_email" }
    };
}
