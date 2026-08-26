-- salesforce.org_connection definition
-- How this application authenticates to one Salesforce org. Exactly one row, ever.
--
-- The only secret here is signing_private_key: a key this application generated for itself, encrypted with
-- ASP.NET Data Protection. No Salesforce password, client secret or security token is stored at all after
-- setup completes — see docs/adr/0002. The two bootstrap_* columns are the exception, and they are purged
-- the moment the first JWT token succeeds.
CREATE TABLE IF NOT EXISTS salesforce.org_connection (
    id serial4 NOT NULL,

    -- Single-row-ness, enforced rather than assumed. UNIQUE on a column that CHECK pins to true admits
    -- exactly one row; a convention that "there is only one" is a convention the next writer breaks.
    is_singleton bool DEFAULT true NOT NULL,

    consumer_key varchar(255) NOT NULL, -- The External Client App's Consumer Key; the JWT iss claim
    administering_username varchar(255) NOT NULL, -- Approves the Bootstrap and owns setup-level work
    run_as_username varchar(255) NOT NULL, -- The identity the event stream runs under
    is_sandbox bool DEFAULT false NOT NULL, -- Selects test.salesforce.com over login.salesforce.com

    signing_private_key text NOT NULL, -- Data Protection ciphertext; never a plaintext PEM
    signing_certificate text NOT NULL, -- PEM. Public by nature: Salesforce holds the same bytes
    certificate_fingerprint varchar(95) NOT NULL, -- SHA-256, colon-separated hex, for comparing against Setup
    certificate_expires_at timestamptz NOT NULL,

    -- Discovered from the token response, never entered by the user. Null until a token has succeeded once.
    org_url varchar(255) NULL,
    org_id varchar(18) NULL,

    connection_state varchar(20) DEFAULT 'Incomplete' NOT NULL, -- Incomplete, Connected or Failed
    last_connected_at timestamptz NULL,
    last_error text NULL,
    last_error_at timestamptz NULL,

    -- Held only between the Bootstrap and the first successful JWT. Encrypted, and purged after — a metadata
    -- deploy is eventually consistent, so the borrowed session has to outlive the deploy that used it.
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

COMMENT ON TABLE salesforce.org_connection IS 'The single Org Connection: how this application authenticates to one Salesforce org';
COMMENT ON COLUMN salesforce.org_connection.is_singleton IS 'Always true; UNIQUE + CHECK together admit exactly one row';
COMMENT ON COLUMN salesforce.org_connection.consumer_key IS 'The External Client App Consumer Key, used as the JWT iss claim';
COMMENT ON COLUMN salesforce.org_connection.signing_private_key IS 'Data Protection ciphertext of the Signing Keypair private key';
COMMENT ON COLUMN salesforce.org_connection.certificate_fingerprint IS 'SHA-256 of the Signing Certificate, for comparing against what Salesforce shows in Setup';
COMMENT ON COLUMN salesforce.org_connection.org_id IS 'Discovered on the first successful token exchange; feeds the Pub/Sub tenantid header';
COMMENT ON COLUMN salesforce.org_connection.connection_state IS 'Incomplete (never authenticated), Connected, or Failed';
COMMENT ON COLUMN salesforce.org_connection.bootstrap_consumer_secret IS 'Encrypted; purged once the first JWT token succeeds';
