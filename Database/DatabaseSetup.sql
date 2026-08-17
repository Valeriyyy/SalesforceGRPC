-- App Database setup
--
-- GENERATED from Database/Definitions/schemas/. That directory is the source of truth; edit a table there and
-- regenerate this file rather than editing it directly. The two had drifted badly before — this file still
-- declared cdc_schemas.schema_id and schema_name as varchars and keyed mapped_fields on the rotating Avro
-- Schema Id, none of which the code has matched for some time.
--
-- For an existing database, apply Database/Definitions/migrations/ instead of running this.
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


-- salesforce.avro_schemas definition
-- One row per Avro Schema revision Salesforce has issued. Salesforce mints a new schema_id every time an
-- object's shape changes, so several rows share a record_name and only the newest describes current events.
CREATE TABLE IF NOT EXISTS salesforce.avro_schemas (
     id serial4 NOT NULL,
     schema_id varchar NOT NULL, -- The Salesforce Schema Id; rotates on every revision
     record_name varchar NOT NULL, -- The Entity name, e.g. AccountChangeEvent
     schema_json jsonb NOT NULL,
     is_active bool DEFAULT true NOT NULL,
     date_created timestamptz DEFAULT now() NOT NULL,
     date_updated timestamptz NULL,
     CONSTRAINT avro_schemas_pk PRIMARY KEY (id),
     CONSTRAINT avro_schemas_schema_id_key UNIQUE (schema_id)
);

CREATE INDEX IF NOT EXISTS avro_schemas_record_name_idx ON salesforce.avro_schemas (record_name);

COMMENT ON COLUMN salesforce.avro_schemas.schema_id IS 'The Salesforce Schema Id; a new one is issued on every revision';
COMMENT ON COLUMN salesforce.avro_schemas.record_name IS 'The Entity name, e.g. AccountChangeEvent';


-- salesforce.cdc_schemas definition
-- One row per Binding: the decision that one Entity's change events land in one Target Table.
-- The table name predates the term; it holds no schema of any kind.
-- DROP TABLE salesforce.cdc_schemas;

CREATE TABLE IF NOT EXISTS salesforce.cdc_schemas (
      id serial4 NOT NULL,
      avro_schema_id int4 NULL,
      entity_name varchar(100) NOT NULL, -- Entity name, e.g. AccountChangeEvent
      db_schema_full_name varchar NULL, -- Schema-qualified Target Table, e.g. salesforce.account
      binding_state varchar(20) DEFAULT 'Incomplete' NOT NULL, -- Incomplete, Active or Inactive
      soft_delete_enabled bool DEFAULT false NULL,
      soft_delete_column_name varchar NULL,
      CONSTRAINT cdc_schemas_pkey PRIMARY KEY (id),
      CONSTRAINT cdc_schemas_binding_state_check
          CHECK (binding_state IN ('Incomplete', 'Active', 'Inactive')),
      -- One Binding per Entity, so an event has one unambiguous destination.
      CONSTRAINT cdc_schemas_entity_name_key UNIQUE (entity_name),
      -- One Binding per Target Table, so two Entities never fight over the same rows.
      CONSTRAINT cdc_schemas_db_schema_full_name_key UNIQUE (db_schema_full_name),
      CONSTRAINT cdc_schemas_avro_schemas_fk FOREIGN KEY (avro_schema_id) REFERENCES salesforce.avro_schemas(id) ON DELETE CASCADE
);

COMMENT ON TABLE salesforce.cdc_schemas IS 'One row per Binding: which Entity lands in which Target Table';
COMMENT ON COLUMN salesforce.cdc_schemas.entity_name IS 'Entity name, e.g. AccountChangeEvent';
COMMENT ON COLUMN salesforce.cdc_schemas.db_schema_full_name IS 'Schema-qualified Target Table, e.g. salesforce.account';
COMMENT ON COLUMN salesforce.cdc_schemas.binding_state IS 'Incomplete (never applied), Active (worker applies it) or Inactive (switched off, mappings kept)';


-- salesforce.mapped_fields definition
-- One row per Field Mapping: a flattened Salesforce field name paired with a Target Column name.
-- The Key Mapping is stored here too, under the sentinel salesforce_field_name 'MappedSFKey'.

CREATE TABLE IF NOT EXISTS salesforce.mapped_fields (
      id serial4 NOT NULL,
      salesforce_field_name varchar(100) NOT NULL, -- Flattened, e.g. BillingAddressCity
      target_field_name varchar(100) NOT NULL,
      schema_id int4 NULL, -- The Binding this mapping belongs to
      CONSTRAINT mapped_fields_pkey PRIMARY KEY (id),
      CONSTRAINT unique_mapping UNIQUE (salesforce_field_name, target_field_name, schema_id),
      CONSTRAINT mapped_fields_cdc_schema_fk FOREIGN KEY (schema_id)
          REFERENCES salesforce.cdc_schemas(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS mapped_fields_schema_id_idx ON salesforce.mapped_fields (schema_id);

COMMENT ON COLUMN salesforce.mapped_fields.salesforce_field_name IS 'Flattened Salesforce field name, e.g. BillingAddressCity; MappedSFKey marks the Key Mapping';
COMMENT ON COLUMN salesforce.mapped_fields.schema_id IS 'The cdc_schemas row (Binding) this mapping belongs to';


-- salesforce.platform_event_channels definition
-- Local mirror of the PlatformEventChannel Tooling API object. Salesforce remains the source of
-- truth; rows here are written after a successful Tooling API call and can be rebuilt by resync.
-- DROP TABLE salesforce.platform_event_channels;

CREATE TABLE IF NOT EXISTS salesforce.platform_event_channels (
    id serial4 NOT NULL,
    sf_id varchar(18) NOT NULL, -- Salesforce ID of the channel, 0YL prefix
    full_name varchar(255) NOT NULL, -- Metadata full name including the __chn suffix
    developer_name varchar(255) NOT NULL, -- Unique name without the __chn suffix
    master_label varchar(255) NULL,
    channel_type varchar(20) NOT NULL, -- data (Change Data Capture) or event (platform events)
    event_type varchar(20) NULL, -- custom, data, monitoring or standard (API 61.0+)
    namespace_prefix varchar(15) NULL,
    manageable_state varchar(30) NULL,
    is_primary bool DEFAULT false NOT NULL, -- The single channel the worker subscribes to
    date_created timestamptz DEFAULT now() NOT NULL,
    date_updated timestamptz NULL,
    last_synced_at timestamptz NULL, -- When this row was last reconciled against Salesforce
    CONSTRAINT platform_event_channels_pkey PRIMARY KEY (id),
    CONSTRAINT platform_event_channels_sf_id_key UNIQUE (sf_id),
    CONSTRAINT platform_event_channels_full_name_key UNIQUE (full_name)
);

-- At most one Primary Channel. A partial index rather than a constraint so the many false rows do not
-- collide with each other.
CREATE UNIQUE INDEX IF NOT EXISTS platform_event_channels_one_primary_idx
    ON salesforce.platform_event_channels (is_primary) WHERE is_primary;

COMMENT ON COLUMN salesforce.platform_event_channels.sf_id IS 'Salesforce ID of the channel, 0YL prefix';
COMMENT ON COLUMN salesforce.platform_event_channels.is_primary IS 'The single channel the worker subscribes to; at most one row is true';
COMMENT ON COLUMN salesforce.platform_event_channels.full_name IS 'Metadata full name including the __chn suffix';
COMMENT ON COLUMN salesforce.platform_event_channels.developer_name IS 'Unique name without the __chn suffix';
COMMENT ON COLUMN salesforce.platform_event_channels.channel_type IS 'data (Change Data Capture) or event (platform events); immutable in Salesforce after create';
COMMENT ON COLUMN salesforce.platform_event_channels.event_type IS 'custom, data, monitoring or standard (API 61.0+); immutable in Salesforce after create';
COMMENT ON COLUMN salesforce.platform_event_channels.last_synced_at IS 'When this row was last reconciled against Salesforce';


-- salesforce.platform_event_channel_members definition
-- Local mirror of the PlatformEventChannelMember Tooling API object: one event/entity on a channel.
-- DROP TABLE salesforce.platform_event_channel_members;

CREATE TABLE IF NOT EXISTS salesforce.platform_event_channel_members (
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
