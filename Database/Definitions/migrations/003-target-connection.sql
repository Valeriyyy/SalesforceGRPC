-- Migration: Target Connection
--
-- Brings an existing App Database up to the shape in Definitions/schemas. Safe to re-run.
--
-- Adds the one table the Target Connection needs and nothing else. It does not migrate the target database
-- settings out of appsettings.json, for the same reason 002 did not migrate Salesforce credentials: seeding
-- an encrypted store from a plaintext file, unattended at startup, is the exact path this feature exists to
-- close. Enter the Target Connection through the API after applying this, then delete TargetingDatabaseType
-- and ConnectionStrings:targetingDatabase from appsettings.json.

BEGIN;

CREATE TABLE IF NOT EXISTS salesforce.target_connection (
    id serial4 NOT NULL,
    is_singleton bool DEFAULT true NOT NULL,
    engine varchar(20) NOT NULL,
    host varchar(255) NULL,
    port int4 NULL,
    database_name varchar(255) NULL,
    username varchar(255) NULL,
    password_encrypted text NULL,
    file_path text NULL,
    options jsonb DEFAULT '{}'::jsonb NOT NULL,
    connection_state varchar(20) DEFAULT 'Incomplete' NOT NULL,
    last_connected_at timestamptz NULL,
    last_error text NULL,
    last_error_raw text NULL,
    last_error_at timestamptz NULL,
    date_created timestamptz DEFAULT now() NOT NULL,
    date_updated timestamptz NULL,
    CONSTRAINT target_connection_pkey PRIMARY KEY (id),
    CONSTRAINT target_connection_is_singleton_key UNIQUE (is_singleton),
    CONSTRAINT target_connection_is_singleton_check CHECK (is_singleton),
    CONSTRAINT target_connection_engine_check
        CHECK (engine IN ('Postgres', 'SqlServer', 'MySql', 'Sqlite')),
    CONSTRAINT target_connection_state_check
        CHECK (connection_state IN ('Incomplete', 'Connected', 'Failed'))
);

COMMIT;
