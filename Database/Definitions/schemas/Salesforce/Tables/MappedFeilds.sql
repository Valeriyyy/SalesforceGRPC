CREATE TABLE salesforce.mapped_fields (
      id serial4 NOT NULL,
      salesforce_field_name varchar(100) NOT NULL,
      target_field_name varchar(100) NOT NULL,
      schema_id int4 NULL,
      CONSTRAINT mapped_fields_pkey PRIMARY KEY (id),
      CONSTRAINT unique_mapping UNIQUE (salesforce_field_name, target_field_name, schema_id)
);