CREATE SCHEMA IF NOT EXISTS salesforce;


DROP DOMAIN IF EXISTS salesforce.sfid CASCADE;
CREATE DOMAIN salesforce.sfid AS VARCHAR(18) CHECK (VALUE ~* '^[a-z\d]{18}$');


CREATE EXTENSION postgis;
SELECT PostGIS_Full_Version();
CREATE EXTENSION address_standardizer;

DROP TABLE IF EXISTS salesforce.record_types;
CREATE TABLE salesforce.record_types (
    id SERIAL4 NOT NULL,
    sf_id salesforce.sfid NOT NULL UNIQUE,
    name VARCHAR(50) NOT NULL,
    sobjecttype VARCHAR(50) NOT NULL,
    is_active BOOLEAN NOT NULL,
    description VARCHAR(250) NULL,
    
    CONSTRAINT record_types_pk PRIMARY KEY (id)
);


DROP TABLE IF EXISTS salesforce.accounts;
CREATE TABLE salesforce.accounts (
	id serial4 NOT NULL,
	sf_id salesforce.sfid NOT NULL,
	record_type_sf_id salesforce.sfid UNIQUE,
	guid uuid NULL,
	"name" varchar(70) NULL,
	first_name varchar(30) NULL,
	last_name varchar(40) NULL,
	phone varchar(60) NULL,
	mobile_phone varchar(60) NULL,
	person_email varchar(100) NULL,
	some_email varchar(100) NULL,
	person_title varchar(80) NULL,
	"type" varchar(30) NULL,
	account_number varchar(40) NULL,
	active varchar(40) NULL,
	some_date date NULL,
	some_date_time timestamptz NULL,
	latitude numeric(15,12) NULL,
	longitude numeric(15,12) NULL,
	some_picklist varchar(60) NULL,
	some_number numeric(10, 0) NULL,
	some_percent numeric(3,2) NULL,
	some_phone varchar(60) NULL,
	some_rich_text TEXT NULL,
	some_text varchar(250) NULL,
	some_time time NULL,
	some_url varchar(255) NULL,
	some_check_box bool DEFAULT FALSE,
	
	created_date timestamptz,
	last_modified_date timestamptz,
	is_deleted bool DEFAULT FALSE,
	deleted_date timestamptz,
	
	CONSTRAINT accounts_pk PRIMARY KEY (id),
	CONSTRAINT record_type_sf_id FOREIGN KEY (record_type_sf_id) REFERENCES salesforce.record_types (sf_id)
);

CREATE UNIQUE INDEX accounts_un_sf_id ON salesforce.accounts (sf_id);
CREATE INDEX accounts_idx_record_type_id ON salesforce.accounts (record_type_id);


DROP TABLE IF EXISTS salesforce.contacts;
CREATE TABLE salesforce.contacts (
	id serial4 NOT NULL,
	sf_id salesforce.sfid NOT NULL,
	record_type_sf_id salesforce.sfid NOT NULL,
	account_sf_id salesforce.sfid NULL,
	guid uuid NULL,
	"name" varchar(100) NULL,
	first_name varchar(50) NULL,
	last_name varchar(50) NULL,
	title varchar(100) NULL,
	phone varchar(60) NULL,
	mobile_phone varchar(60) NULL,
	email varchar(100) NULL,
	contact_level varchar(30) NULL,
	
	CONSTRAINT contacts_pk PRIMARY KEY (id)
);

CREATE UNIQUE INDEX contacts_un_sf_id ON salesforce.contacts (sf_id);
CREATE INDEX contacts_idx_record_type_id ON salesforce.contacts (record_type_id);


DROP TABLE IF EXISTS salesforce.addresses;
CREATE TABLE salesforce.addresses (
	id serial4 NOT NULL,
	street varchar(50) NULL,
	street_2 varchar(10) NULL,
	city varchar(50) NULL,
	postal_code varchar(15) NULL,
	state varchar(50) NULL,
	country varchar(50) NULL,
	latitude float8 NULL,
	longitude float8 NULL,
	CONSTRAINT addresses_pk PRIMARY KEY (id)
);

DROP TABLE IF EXISTS salesforce.cdc_schemas;
CREATE TABLE salesforce.cdc_schemas (
	id SERIAL PRIMARY KEY,
	entity_name VARCHAR(100) NOT NULL,
	schema_id VARCHAR(100) NOT NULL UNIQUE,
	schema_name VARCHAR(100) NOT NULL,
	avro_schema TEXT
);

COMMENT ON COLUMN salesforce.cdc_schemas.SCHEMA_ID IS 'The id of the schema provided from salesforce';
COMMENT ON COLUMN salesforce.cdc_schemas.entity_name IS 'The salesforce object the schema belongs to';

-- I don't care about keeping these schemas secret. What are people going to do? Hack me?
INSERT INTO salesforce.cdc_schemas (entity_name, schema_id, schema_name) VALUES ('Account', 'S3-K_S0GYegWTRZnKLjeag', 'AccountChangeEvent');
INSERT INTO salesforce.cdc_schemas (entity_name, schema_id, schema_name) VALUES ('Some_Custom_Object__c','sPvvpP38OBppq-sWal1Iig', 'Some_Custom_Object__ChangeEvent');
INSERT INTO salesforce.cdc_schemas (entity_name, schema_id, schema_name) VALUES ('Contact', 'yzqdtk6CwUcHsD85ZsJK7A', 'ContactChangeEvent');

DROP TABLE IF EXISTS salesforce.mapped_fields;
CREATE TABLE salesforce.mapped_fields (
	id serial4 PRIMARY KEY,
	schema_id VARCHAR(100) NOT NULL, 
	salesforce_field_name varchar(100) NOT NULL,
	postgres_field_name varchar(100) NOT NULL,
	
	CONSTRAINT schema_id FOREIGN KEY (schema_id) REFERENCES salesforce.cdc_schemas (schema_id)
);

-- inserting account mapping fields
INSERT INTO salesforce.MAPPED_FIELDS (SCHEMA_ID, salesforce_field_name, POSTGRES_FIELD_NAME) values('4egfTTaxua4Y39nYlBXfNg', 'Name', 'name');
INSERT INTO salesforce.MAPPED_FIELDS (SCHEMA_ID, salesforce_field_name, POSTGRES_FIELD_NAME) values('4egfTTaxua4Y39nYlBXfNg', 'Id', 'sf_id');
INSERT INTO salesforce.MAPPED_FIELDS (SCHEMA_ID, salesforce_field_name, POSTGRES_FIELD_NAME) values('4egfTTaxua4Y39nYlBXfNg', 'RecordTypeId', 'record_type_sf_id');
INSERT INTO salesforce.MAPPED_FIELDS (SCHEMA_ID, salesforce_field_name, POSTGRES_FIELD_NAME) values('4egfTTaxua4Y39nYlBXfNg', 'Phone', 'phone');
INSERT INTO salesforce.MAPPED_FIELDS (SCHEMA_ID, salesforce_field_name, POSTGRES_FIELD_NAME) values('4egfTTaxua4Y39nYlBXfNg', 'Some_Email__c', 'some_email');
INSERT INTO salesforce.MAPPED_FIELDS (SCHEMA_ID, salesforce_field_name, POSTGRES_FIELD_NAME) values('4egfTTaxua4Y39nYlBXfNg', 'Active__c', 'active');

INSERT INTO salesforce.MAPPED_FIELDS (SCHEMA_ID, salesforce_field_name, POSTGRES_FIELD_NAME) values('4egfTTaxua4Y39nYlBXfNg', 'BillingAddress.Street', 'billing_street');
INSERT INTO salesforce.MAPPED_FIELDS (SCHEMA_ID, salesforce_field_name, POSTGRES_FIELD_NAME) values('4egfTTaxua4Y39nYlBXfNg', 'BillingAddress.City', 'billing_city');
INSERT INTO salesforce.MAPPED_FIELDS (SCHEMA_ID, salesforce_field_name, POSTGRES_FIELD_NAME) values('4egfTTaxua4Y39nYlBXfNg', 'BillingAddress.State', 'billing_state');
INSERT INTO salesforce.MAPPED_FIELDS (SCHEMA_ID, salesforce_field_name, POSTGRES_FIELD_NAME) values('4egfTTaxua4Y39nYlBXfNg', 'BillingAddress.PostalCode', 'billing_postal_code');
INSERT INTO salesforce.MAPPED_FIELDS (SCHEMA_ID, salesforce_field_name, POSTGRES_FIELD_NAME) values('4egfTTaxua4Y39nYlBXfNg', 'BillingAddress.Country', 'billing_country');
INSERT INTO salesforce.MAPPED_FIELDS (SCHEMA_ID, salesforce_field_name, POSTGRES_FIELD_NAME) values('4egfTTaxua4Y39nYlBXfNg', 'BillingAddress.Latitude', 'billing_latitude');
INSERT INTO salesforce.MAPPED_FIELDS (SCHEMA_ID, salesforce_field_name, POSTGRES_FIELD_NAME) values('4egfTTaxua4Y39nYlBXfNg', 'BillingAddress.Longitude', 'billing_longitude');

