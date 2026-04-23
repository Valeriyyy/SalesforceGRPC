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

-- salesforce.cdc_schemas definition

-- Drop table

DROP TABLE IF EXISTS salesforce.cdc_schemas;
CREATE TABLE salesforce.cdc_schemas (
    id serial4 NOT NULL,
    entity_name varchar(100) NOT NULL, -- The salesforce object the schema belongs to
    schema_id varchar(100) NOT NULL, -- The id of the schema provided from salesforce
    schema_name varchar(100) NOT NULL,
    db_schema_full_name varchar NULL, -- The full database schema name for the target table
    soft_delete bool DEFAULT false NULL, -- Setting to either hard or soft delete records of schema
    soft_delete_column_name varchar NULL, -- Name of the field to set to soft delete, must be boolean type column
    CONSTRAINT cdc_schemas_pkey PRIMARY KEY (id),
    CONSTRAINT cdc_schemas_schema_id_key UNIQUE (schema_id),
    CONSTRAINT cdc_schemas_unique UNIQUE (db_schema_full_name)
);

-- Column comments

COMMENT ON COLUMN salesforce.cdc_schemas.entity_name IS 'The salesforce object the schema belongs to';
COMMENT ON COLUMN salesforce.cdc_schemas.schema_id IS 'The id of the schema provided from salesforce';
COMMENT ON COLUMN salesforce.cdc_schemas.db_schema_full_name IS 'The full database schema name for the target table';
COMMENT ON COLUMN salesforce.cdc_schemas.soft_delete IS 'Setting to either hard or soft delete records of schema';
COMMENT ON COLUMN salesforce.cdc_schemas.soft_delete_column_name IS 'Name of the field to set to soft delete, must be boolean type column';

DROP TABLE IF EXISTS salesforce.mapped_fields;
CREATE TABLE salesforce.mapped_fields (
	id serial4 PRIMARY KEY,
	schema_id VARCHAR(100) NOT NULL, 
	salesforce_field_name varchar(100) NOT NULL,
	target_field_name varchar(100) NOT NULL,
	
	CONSTRAINT schema_id FOREIGN KEY (schema_id) REFERENCES salesforce.cdc_schemas (schema_id)
);