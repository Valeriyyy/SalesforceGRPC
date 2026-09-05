-- Migration: Salesforce Org Connection
--
-- Brings an existing App Database up to the shape in Definitions/schemas. Safe to re-run.
--
-- Adds the two tables the Org Connection feature needs and nothing else. It does not migrate credentials out
-- of appsettings.json, deliberately: reading them here would need this migration to know the Data Protection
-- key ring, and seeding a connection from a file is the plaintext-secrets path the feature exists to close.
-- Set the connection up through the API after applying this, then delete the SalesforceConfig secrets from
-- appsettings.json and rotate them in Salesforce.

BEGIN;

CREATE TABLE IF NOT EXISTS salesforce.data_protection_keys (
    id serial4 NOT NULL,
    friendly_name varchar(255) NULL,
    xml text NOT NULL,
    date_created timestamptz DEFAULT now() NOT NULL,
    CONSTRAINT data_protection_keys_pkey PRIMARY KEY (id)
);

CREATE TABLE IF NOT EXISTS salesforce.org_connection (
    id serial4 NOT NULL,
    is_singleton bool DEFAULT true NOT NULL,
    consumer_key varchar(255) NOT NULL,
    administering_username varchar(255) NOT NULL,
    run_as_username varchar(255) NOT NULL,
    is_sandbox bool DEFAULT false NOT NULL,
    signing_private_key text NOT NULL,
    signing_certificate text NOT NULL,
    certificate_fingerprint varchar(95) NOT NULL,
    certificate_expires_at timestamptz NOT NULL,
    org_url varchar(255) NULL,
    org_id varchar(18) NULL,
    connection_state varchar(20) DEFAULT 'Incomplete' NOT NULL,
    last_connected_at timestamptz NULL,
    last_error text NULL,
    last_error_raw text NULL,
    last_error_at timestamptz NULL,
    bootstrap_consumer_secret text NULL,
    bootstrap_refresh_token text NULL,
    date_created timestamptz DEFAULT now() NOT NULL,
    date_updated timestamptz NULL,
    CONSTRAINT org_connection_pkey PRIMARY KEY (id),
    CONSTRAINT org_connection_is_singleton_key UNIQUE (is_singleton),
    CONSTRAINT org_connection_is_singleton_check CHECK (is_singleton),
    CONSTRAINT org_connection_state_check
        CHECK (connection_state IN ('Incomplete', 'Connected', 'Failed'))
);

COMMIT;
