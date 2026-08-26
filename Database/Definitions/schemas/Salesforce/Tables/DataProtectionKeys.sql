-- salesforce.data_protection_keys definition
-- The ASP.NET Data Protection key ring, so the application can decrypt its own stored secrets after a
-- restart without an operator present.
--
-- Read Database/DataProtection/DapperXmlRepository.cs before changing this: the shape is dictated by
-- IXmlRepository, which stores opaque XML fragments and never queries into them.
--
-- These rows are useless on their own. They are themselves encrypted with a certificate resolved from
-- OUTSIDE this database (environment, then file, then KMS), because a key ring sitting beside the
-- ciphertext it protects defends against nothing. See docs/adr/0002.
CREATE TABLE IF NOT EXISTS salesforce.data_protection_keys (
    id serial4 NOT NULL,
    friendly_name varchar(255) NULL, -- Data Protection's own name for the element; not unique by contract
    xml text NOT NULL, -- The opaque key element, encrypted with the protecting certificate
    date_created timestamptz DEFAULT now() NOT NULL,
    CONSTRAINT data_protection_keys_pkey PRIMARY KEY (id)
);

COMMENT ON TABLE salesforce.data_protection_keys IS 'ASP.NET Data Protection key ring; protected by a certificate held outside this database';
COMMENT ON COLUMN salesforce.data_protection_keys.xml IS 'Opaque Data Protection key element. Never parsed by this application';
