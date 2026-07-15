-- salesforce.cdc_schemas definition

-- Drop table

-- DROP TABLE salesforce.cdc_schemas;

CREATE TABLE IF NOT EXISTS salesforce.cdc_schemas (
      id serial4 NOT NULL,
      avro_schema_id int4 NULL,
      entity_name varchar(100) NOT NULL,
      db_schema_full_name varchar NULL,
      soft_delete_enabled bool DEFAULT false NULL,
      soft_delete_column_name varchar NULL,
      CONSTRAINT cdc_schemas_pkey PRIMARY KEY (id),
      CONSTRAINT cdc_schemas_avro_schemas_fk FOREIGN KEY (avro_schema_id) REFERENCES salesforce.avro_schemas(id) ON DELETE CASCADE
);