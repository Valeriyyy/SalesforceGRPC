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

-- Local mirror of the PlatformEventChannel Tooling API object. Salesforce remains the source of
-- truth; rows here are written after a successful Tooling API call and can be rebuilt by resync.
DROP TABLE IF EXISTS salesforce.platform_event_channel_members;
DROP TABLE IF EXISTS salesforce.platform_event_channels;
CREATE TABLE salesforce.platform_event_channels (
    id serial4 NOT NULL,
    sf_id varchar(18) NOT NULL, -- Salesforce ID of the channel, 0YL prefix
    full_name varchar(255) NOT NULL, -- Metadata full name including the __chn suffix
    developer_name varchar(255) NOT NULL, -- Unique name without the __chn suffix
    master_label varchar(255) NULL,
    channel_type varchar(20) NOT NULL, -- data (Change Data Capture) or event (platform events)
    event_type varchar(20) NULL, -- custom, data, monitoring or standard (API 61.0+)
    namespace_prefix varchar(15) NULL,
    manageable_state varchar(30) NULL,
    date_created timestamptz DEFAULT now() NOT NULL,
    date_updated timestamptz NULL,
    last_synced_at timestamptz NULL, -- When this row was last reconciled against Salesforce
    CONSTRAINT platform_event_channels_pkey PRIMARY KEY (id),
    CONSTRAINT platform_event_channels_sf_id_key UNIQUE (sf_id),
    CONSTRAINT platform_event_channels_full_name_key UNIQUE (full_name)
);

COMMENT ON COLUMN salesforce.platform_event_channels.sf_id IS 'Salesforce ID of the channel, 0YL prefix';
COMMENT ON COLUMN salesforce.platform_event_channels.full_name IS 'Metadata full name including the __chn suffix';
COMMENT ON COLUMN salesforce.platform_event_channels.developer_name IS 'Unique name without the __chn suffix';
COMMENT ON COLUMN salesforce.platform_event_channels.channel_type IS 'data (Change Data Capture) or event (platform events); immutable in Salesforce after create';
COMMENT ON COLUMN salesforce.platform_event_channels.event_type IS 'custom, data, monitoring or standard (API 61.0+); immutable in Salesforce after create';
COMMENT ON COLUMN salesforce.platform_event_channels.last_synced_at IS 'When this row was last reconciled against Salesforce';

-- Local mirror of the PlatformEventChannelMember Tooling API object: one event/entity on a channel.
CREATE TABLE salesforce.platform_event_channel_members (
    id serial4 NOT NULL,
    channel_id int4 NOT NULL,
    sf_id varchar(18) NOT NULL, -- Salesforce ID of the member, 0v8 prefix
    full_name varchar(255) NOT NULL, -- <channel>_<entity> with double underscores flattened to single
    developer_name varchar(255) NULL,
    selected_entity varchar(255) NOT NULL, -- Entity name, e.g. AccountChangeEvent
    filter_expression text NULL, -- Server-side delivery filter (API 56.0+)
    enriched_fields jsonb NULL, -- Fields always included in the payload (API 51.0+)
    cdc_schema_id int4 NULL, -- Optional link to the sync config for this entity
    date_created timestamptz DEFAULT now() NOT NULL,
    date_updated timestamptz NULL,
    last_synced_at timestamptz NULL,
    CONSTRAINT platform_event_channel_members_pkey PRIMARY KEY (id),
    CONSTRAINT platform_event_channel_members_sf_id_key UNIQUE (sf_id),
    CONSTRAINT pecm_channel_fk FOREIGN KEY (channel_id)
        REFERENCES salesforce.platform_event_channels(id) ON DELETE CASCADE,
    CONSTRAINT pecm_cdc_schema_fk FOREIGN KEY (cdc_schema_id)
        REFERENCES salesforce.cdc_schemas(id) ON DELETE SET NULL
);

CREATE INDEX IF NOT EXISTS platform_event_channel_members_channel_id_idx
    ON salesforce.platform_event_channel_members (channel_id);

COMMENT ON COLUMN salesforce.platform_event_channel_members.sf_id IS 'Salesforce ID of the member, 0v8 prefix';
COMMENT ON COLUMN salesforce.platform_event_channel_members.full_name IS '<channel>_<entity> with double underscores flattened to single';
COMMENT ON COLUMN salesforce.platform_event_channel_members.selected_entity IS 'Entity name, e.g. AccountChangeEvent; immutable in Salesforce after create';
COMMENT ON COLUMN salesforce.platform_event_channel_members.cdc_schema_id IS 'Optional link to the cdc_schemas row that says where this entity lands in the target database';