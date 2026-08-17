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
