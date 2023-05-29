CREATE TABLE salesforce.schemas (
	id SERIAL PRIMARY KEY,
	schema_id VARCHAR(100) NOT NULL,
	schema_name VARCHAR(100) NOT NULL
);

INSERT INTO salesforce.cdc_schemas (schema_id, schema_name) VALUES ('lhKgvxi31DtlAz18-TdwBQ', 'AccountChangeEvent');
INSERT INTO salesforce.cdc_schemas (schema_id, schema_name) VALUES ('3nHiqTscCiNSzL2YQKsn1Q', 'Vendor_Profile__ChangeEvent');
INSERT INTO salesforce.cdc_schemas (schema_id, schema_name) VALUES ('q0AIBokzXQNGdVcvyc9w7w', 'ContactChangeEvent');
INSERT INTO salesforce.cdc_schemas (schema_id, schema_name) VALUES ('wgsgbc0Gz23dv7xN9pSVOA', 'CaseChangeEvent');

CREATE TABLE salesforce.mapped_fields (
	id serial4 PRIMARY KEY,
	schema_id int,
	saleforce_field_name varchar(100) NOT NULL,
	postgres_field_name varchar(100) NOT NULL
);

