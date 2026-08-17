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
