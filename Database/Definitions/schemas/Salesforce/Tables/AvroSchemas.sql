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