-- Migration: Entity → Target Table Bindings
--
-- Brings an existing App Database up to the shape in Definitions/schemas. Safe to re-run.
--
-- Note the two uniqueness constraints below can fail on an installation whose cdc_schemas rows were created
-- by the old worker path, which invented a Binding per Avro schema revision and guessed the table name. If a
-- constraint fails, deduplicate those rows first — the surviving row should be the one whose
-- db_schema_full_name is the table you actually want written to.

BEGIN;

-- Binding State. Existing rows are assumed to be in use, so they become Active rather than Incomplete;
-- anything without a Target Table cannot be applied and becomes Incomplete.
ALTER TABLE salesforce.cdc_schemas
    ADD COLUMN IF NOT EXISTS binding_state varchar(20) DEFAULT 'Incomplete' NOT NULL;

UPDATE salesforce.cdc_schemas
SET binding_state = CASE
    WHEN db_schema_full_name IS NULL OR db_schema_full_name = '' THEN 'Incomplete'
    ELSE 'Active'
END
WHERE binding_state = 'Incomplete';

ALTER TABLE salesforce.cdc_schemas
    DROP CONSTRAINT IF EXISTS cdc_schemas_binding_state_check;
ALTER TABLE salesforce.cdc_schemas
    ADD CONSTRAINT cdc_schemas_binding_state_check
    CHECK (binding_state IN ('Incomplete', 'Active', 'Inactive'));

-- One Binding per Entity, one Binding per Target Table.
ALTER TABLE salesforce.cdc_schemas
    DROP CONSTRAINT IF EXISTS cdc_schemas_entity_name_key;
ALTER TABLE salesforce.cdc_schemas
    ADD CONSTRAINT cdc_schemas_entity_name_key UNIQUE (entity_name);

ALTER TABLE salesforce.cdc_schemas
    DROP CONSTRAINT IF EXISTS cdc_schemas_db_schema_full_name_key;
ALTER TABLE salesforce.cdc_schemas
    ADD CONSTRAINT cdc_schemas_db_schema_full_name_key UNIQUE (db_schema_full_name);

-- Primary Channel.
ALTER TABLE salesforce.platform_event_channels
    ADD COLUMN IF NOT EXISTS is_primary bool DEFAULT false NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS platform_event_channels_one_primary_idx
    ON salesforce.platform_event_channels (is_primary) WHERE is_primary;

-- Further drift between Definitions/schemas and what the code queries, closed here.
-- AvroSchemaRepository has always read and written is_active, which the definitions never declared.
ALTER TABLE salesforce.avro_schemas
    ADD COLUMN IF NOT EXISTS is_active bool DEFAULT true NOT NULL;

ALTER TABLE salesforce.avro_schemas
    DROP CONSTRAINT IF EXISTS avro_schemas_schema_id_key;
ALTER TABLE salesforce.avro_schemas
    ADD CONSTRAINT avro_schemas_schema_id_key UNIQUE (schema_id);

CREATE INDEX IF NOT EXISTS avro_schemas_record_name_idx ON salesforce.avro_schemas (record_name);

-- Field Mappings had no foreign key to their Binding, so deleting a Binding left them orphaned. Any rows
-- already pointing at a Binding that no longer exists have to go before the constraint can be added.
DELETE FROM salesforce.mapped_fields mf
WHERE mf.schema_id IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM salesforce.cdc_schemas cs WHERE cs.id = mf.schema_id);

ALTER TABLE salesforce.mapped_fields
    DROP CONSTRAINT IF EXISTS mapped_fields_cdc_schema_fk;
ALTER TABLE salesforce.mapped_fields
    ADD CONSTRAINT mapped_fields_cdc_schema_fk FOREIGN KEY (schema_id)
    REFERENCES salesforce.cdc_schemas(id) ON DELETE CASCADE;

CREATE INDEX IF NOT EXISTS mapped_fields_schema_id_idx ON salesforce.mapped_fields (schema_id);

COMMIT;
