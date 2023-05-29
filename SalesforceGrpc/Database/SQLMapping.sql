CREATE TABLE salesforce.schemas (
	id SERIAL PRIMARY KEY,
	schema_id VARCHAR(100) NOT NULL,
	schema_name VARCHAR(100) NOT NULL
);

INSERT INTO salesforce.cdc_schemas (schema_id, schema_name) VALUES ('AccountSchemaId', 'AccountChangeEvent');
INSERT INTO salesforce.cdc_schemas (schema_id, schema_name) VALUES ('ContactSchemaId', 'ContactChangeEvent');
INSERT INTO salesforce.cdc_schemas (schema_id, schema_name) VALUES ('CaseSchemaId', 'CaseChangeEvent');

CREATE TABLE salesforce.mapped_fields (
	id serial4 PRIMARY KEY,
	schema_id int,
	saleforce_field_name varchar(100) NOT NULL,
	postgres_field_name varchar(100) NOT NULL
);

