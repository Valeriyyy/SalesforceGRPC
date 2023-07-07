CREATE SCHEMA IF NOT EXISTS salesforce;


DROP DOMAIN IF EXISTS salesforce.sfid CASCADE;
CREATE DOMAIN salesforce.sfid AS VARCHAR(18) CHECK (VALUE ~* '^[a-z\d]{18}$');


CREATE EXTENSION postgis;
SELECT PostGIS_Full_Version();
CREATE EXTENSION address_standardizer;

DROP TABLE IF EXISTS salesforce.records_types;
CREATE TABLE salesforce.record_types (
    id SERIAL4 PRIMARY KEY,

    sf_id salesforce.sfid NOT NULL,
    name VARCHAR(50) NOT NULL,
    sobjecttype VARCHAR(50) NOT NULL,
    is_active BOOLEAN NOT NULL,
    description VARCHAR(250) NULL
);


DROP TABLE IF EXISTS salesforce.accounts;
CREATE TABLE salesforce.accounts (
	id serial4 NOT NULL,
	sf_id salesforce.sfid NOT NULL,
	record_type_sf_id salesforce.sfid NOT NULL,
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
	active__c varchar(40) NULL,
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
	
	
	CONSTRAINT accounts_pk PRIMARY KEY (id)
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

CREATE TABLE salesforce.schemas (
	id SERIAL PRIMARY KEY,
	entity_name VARCHAR(100) NOT NULL,
	schema_id VARCHAR(100) NOT NULL,
	schema_name VARCHAR(100) NOT NULL,
	avro_schema TEXT NOT NULL
);

--INSERT INTO salesforce.schemas (schema_id, schema_name) VALUES ('S3-K_S0GYegWTRZnKLjeag', 'AccountChangeEvent');
--INSERT INTO salesforce.schemas (schema_id, schema_name) VALUES ('sPvvpP38OBppq-sWal1Iig', 'Some_Custom_Object__ChangeEvent');
--INSERT INTO salesforce.schemas (schema_id, schema_name) VALUES ('yzqdtk6CwUcHsD85ZsJK7A', 'ContactChangeEvent');

CREATE TABLE salesforce.mapped_fields (
	id serial4 PRIMARY KEY,
	schema_id int,
	saleforce_field_name varchar(100) NOT NULL,
	postgres_field_name varchar(100) NOT NULL
);