-- Migration: Self-Configuration outcome
--
-- Brings an existing App Database up to the shape in Definitions/schemas. Safe to re-run.
--
-- Keeps what the last Bootstrap's Self-Configuration did, and the steps it left to do by hand, so the Org
-- Connection page can show them rather than only the log. Each Bootstrap overwrites them; Disconnect removes
-- them with the row. Existing rows take NULL: no outcome was recorded for them.

BEGIN;

ALTER TABLE salesforce.org_connection
    ADD COLUMN IF NOT EXISTS self_configuration_at timestamptz NULL,
    ADD COLUMN IF NOT EXISTS self_configuration_configured bool NULL,
    ADD COLUMN IF NOT EXISTS self_configuration_summary text NULL,
    ADD COLUMN IF NOT EXISTS self_configuration_manual_steps jsonb NULL;

COMMENT ON COLUMN salesforce.org_connection.self_configuration_at IS
    'When the last Bootstrap ran Self-Configuration; NULL until one has completed';
COMMENT ON COLUMN salesforce.org_connection.self_configuration_configured IS
    'Whether that Self-Configuration configured the org; false leaves Manual Registration steps';
COMMENT ON COLUMN salesforce.org_connection.self_configuration_summary IS
    'What that Self-Configuration did, in one line';
COMMENT ON COLUMN salesforce.org_connection.self_configuration_manual_steps IS
    'JSON array of the steps Self-Configuration left for the user to do in Setup';

COMMIT;
