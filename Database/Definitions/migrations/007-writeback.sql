-- Migration: Writeback
--
-- Brings an existing App Database up to the shape the code expects. Safe to re-run.
--
-- Lets a Binding be Two-way: its Target Table's changes are written back to Salesforce (see
-- docs/adr/0007-writeback-breaks-the-loop-in-the-target-database.md). Existing Bindings take Inbound and keep
-- behaving exactly as before; existing Field Mappings take Two-way, which only matters once their Binding is
-- switched to Two-way. The Writeback position is not stored here: the replication slot in the Target Database
-- holds it.

BEGIN;

ALTER TABLE salesforce.cdc_schemas
    ADD COLUMN IF NOT EXISTS sync_direction varchar(10) DEFAULT 'Inbound' NOT NULL,
    ADD COLUMN IF NOT EXISTS writeback_key_column text NULL,
    ADD COLUMN IF NOT EXISTS writeback_key_sf_field text NULL;

DO $$ BEGIN
    ALTER TABLE salesforce.cdc_schemas
        ADD CONSTRAINT cdc_schemas_sync_direction_check CHECK (sync_direction IN ('Inbound', 'TwoWay'));
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;

-- The user's choice only. A direction forced by what either side can accept is computed on every validation
-- and never stored, so a change in Salesforce or in the Target Table takes effect without a migration.
ALTER TABLE salesforce.mapped_fields
    ADD COLUMN IF NOT EXISTS field_direction varchar(15) DEFAULT 'TwoWay' NOT NULL;

DO $$ BEGIN
    ALTER TABLE salesforce.mapped_fields
        ADD CONSTRAINT mapped_fields_field_direction_check
            CHECK (field_direction IN ('TwoWay', 'InboundOnly', 'WritebackOnly'));
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;

CREATE TABLE IF NOT EXISTS salesforce.writeback_defaults (
    id serial4 NOT NULL,
    binding_id int4 NOT NULL,
    salesforce_field_name text NOT NULL,
    value text NOT NULL,
    CONSTRAINT writeback_defaults_pkey PRIMARY KEY (id),
    CONSTRAINT writeback_defaults_binding_field_key UNIQUE (binding_id, salesforce_field_name),
    CONSTRAINT writeback_defaults_binding_fkey FOREIGN KEY (binding_id)
        REFERENCES salesforce.cdc_schemas (id) ON DELETE CASCADE
);

-- One open failure per row of a Binding; a later attempt for the same row overwrites it. The row is named by
-- whichever of its keys it had: the Writeback Key value, the Salesforce record ID, or both.
CREATE TABLE IF NOT EXISTS salesforce.writeback_failures (
    id serial4 NOT NULL,
    binding_id int4 NOT NULL,
    row_key text NOT NULL,
    writeback_key_value text NULL,
    salesforce_id text NULL,
    operation varchar(10) NOT NULL,
    status_codes text NOT NULL,
    message text NOT NULL,
    values_sent jsonb DEFAULT '{}'::jsonb NOT NULL,
    first_failed_at timestamptz NOT NULL,
    last_failed_at timestamptz NOT NULL,
    attempts int4 DEFAULT 1 NOT NULL,
    CONSTRAINT writeback_failures_pkey PRIMARY KEY (id),
    CONSTRAINT writeback_failures_binding_row_key UNIQUE (binding_id, row_key),
    CONSTRAINT writeback_failures_binding_fkey FOREIGN KEY (binding_id)
        REFERENCES salesforce.cdc_schemas (id) ON DELETE CASCADE,
    CONSTRAINT writeback_failures_operation_check CHECK (operation IN ('Create', 'Update'))
);

-- The Replication Credential: read-only plus REPLICATION, used only to stream changes, so the credential that
-- writes rows never needs replication rights. writeback_armed_at says this application has set the Target
-- Database up for Writeback, and therefore that its own writes must carry its tag.
ALTER TABLE salesforce.target_connection
    ADD COLUMN IF NOT EXISTS replication_username varchar(255) NULL,
    ADD COLUMN IF NOT EXISTS replication_password_encrypted text NULL,
    ADD COLUMN IF NOT EXISTS replication_state varchar(20) DEFAULT 'Incomplete' NOT NULL,
    ADD COLUMN IF NOT EXISTS replication_last_proved_at timestamptz NULL,
    ADD COLUMN IF NOT EXISTS replication_last_error text NULL,
    ADD COLUMN IF NOT EXISTS replication_last_error_at timestamptz NULL,
    ADD COLUMN IF NOT EXISTS writeback_armed_at timestamptz NULL;

DO $$ BEGIN
    ALTER TABLE salesforce.target_connection
        ADD CONSTRAINT target_connection_replication_state_check
            CHECK (replication_state IN ('Incomplete', 'Connected', 'Failed'));
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;

COMMIT;
