CREATE TABLE salesforce.avro_schemas (
     id serial4 NOT NULL,
     schema_id varchar NOT NULL,
     record_name varchar NOT NULL,
     schema_json jsonb NOT NULL,
     date_created timestamptz DEFAULT now() NOT NULL,
     date_updated timestamptz NULL,
     CONSTRAINT avro_schemas_pk PRIMARY KEY (id)
);