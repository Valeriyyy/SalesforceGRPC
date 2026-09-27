-- Migration: Binding forced Incomplete
--
-- Brings an existing App Database up to the shape in Definitions/schemas. Safe to re-run.
--
-- Records when the worker set an Active Binding back to Incomplete (its Key Mapping column lost its unique
-- constraint), so the Overview can tell that apart from a Binding that is still being built. Cleared by any
-- later state change. Existing rows take NULL: nothing says they were forced.

BEGIN;

ALTER TABLE salesforce.cdc_schemas
    ADD COLUMN IF NOT EXISTS forced_incomplete_at timestamptz NULL;

COMMENT ON COLUMN salesforce.cdc_schemas.forced_incomplete_at IS
    'When the worker set this Active Binding back to Incomplete; NULL unless that is why it is Incomplete';

COMMIT;
