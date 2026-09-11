-- salesforce.target_connection definition
-- How this application reaches the Target Database. Exactly one row, ever.
--
-- The details are decomposed rather than stored as a connection string, so the user can review and edit
-- everything that is not a secret and so a form can be rendered per engine. The only secret is
-- password_encrypted, held as Data Protection ciphertext under the same protecting certificate as the Org
-- Connection — see docs/adr/0002. The assembled connection string is never stored and never logged.
--
-- Changing engine, host, database_name or file_path points the application at a different database and
-- destroys every Binding; see docs/adr/0004 for why that is not merely a deactivation.
CREATE TABLE IF NOT EXISTS salesforce.target_connection (
    id serial4 NOT NULL,

    -- Single-row-ness, enforced rather than assumed, the same way org_connection does it.
    is_singleton bool DEFAULT true NOT NULL,

    engine varchar(20) NOT NULL, -- Postgres, SqlServer, MySql or Sqlite. Text, so psql can read it

    -- Address, for the server-style engines. All nullable because Sqlite needs none of them.
    host varchar(255) NULL,
    port int4 NULL,
    database_name varchar(255) NULL,
    username varchar(255) NULL,
    password_encrypted text NULL, -- Data Protection ciphertext; never plaintext

    file_path text NULL, -- Sqlite's address. Its own column: a file path is not a database name

    -- Engine-specific extras (Postgres SslMode, and whatever a future engine needs). jsonb so a new option is
    -- never a migration. Keys are validated against the engine's field definitions in code, and mapped onto
    -- real driver keys by the driver's own connection-string builder.
    options jsonb DEFAULT '{}'::jsonb NOT NULL,

    connection_state varchar(20) DEFAULT 'Incomplete' NOT NULL, -- Incomplete, Connected or Failed
    last_connected_at timestamptz NULL,
    last_error text NULL,
    last_error_raw text NULL, -- The driver's message, untouched. A summary cannot be un-summarised later
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

COMMENT ON TABLE salesforce.target_connection IS 'The single Target Connection: how this application reaches the Target Database';
COMMENT ON COLUMN salesforce.target_connection.is_singleton IS 'Always true; UNIQUE + CHECK together admit exactly one row';
COMMENT ON COLUMN salesforce.target_connection.engine IS 'Target Database Engine, stored by name. Part of the connection identity';
COMMENT ON COLUMN salesforce.target_connection.password_encrypted IS 'Data Protection ciphertext of the database password. The only secret here';
COMMENT ON COLUMN salesforce.target_connection.file_path IS 'Sqlite database file. Part of the connection identity';
COMMENT ON COLUMN salesforce.target_connection.options IS 'Engine-specific settings, validated in code against the engine field definitions';
COMMENT ON COLUMN salesforce.target_connection.connection_state IS 'Incomplete (never proved), Connected, or Failed (used to work)';
