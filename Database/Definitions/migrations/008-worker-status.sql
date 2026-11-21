-- Migration: Worker status
--
-- Brings an existing App Database up to the shape the code expects. Safe to re-run.
--
-- Two records the Overview's worker card reads (see Inbound Failure and Incident in CONTEXT.md):
--   inbound_failures: change events that could not be applied and were skipped, kept until the user dismisses
--                     them. One per event, so a replayed event that fails again counts an attempt.
--   incidents:        interruptions to syncing, kept for 24 hours. The application prunes older rows itself.

BEGIN;

CREATE TABLE IF NOT EXISTS salesforce.inbound_failures (
    id serial4 NOT NULL,
    binding_id int4 NOT NULL,
    channel_full_name text NOT NULL,
    replay_id bytea NOT NULL,
    entity_name text NOT NULL,
    change_type varchar(10) NULL,
    record_ids text[] DEFAULT '{}' NOT NULL,
    message text NOT NULL,
    first_failed_at timestamptz NOT NULL,
    last_failed_at timestamptz NOT NULL,
    attempts int4 DEFAULT 1 NOT NULL,
    CONSTRAINT inbound_failures_pkey PRIMARY KEY (id),
    CONSTRAINT inbound_failures_event_key UNIQUE (channel_full_name, replay_id),
    CONSTRAINT inbound_failures_binding_fkey FOREIGN KEY (binding_id)
        REFERENCES salesforce.cdc_schemas (id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS inbound_failures_binding_idx ON salesforce.inbound_failures (binding_id);

CREATE TABLE IF NOT EXISTS salesforce.incidents (
    id serial4 NOT NULL,
    direction varchar(10) NULL,
    kind varchar(30) NOT NULL,
    message text NOT NULL,
    started_at timestamptz NOT NULL,
    ended_at timestamptz NULL,
    occurrences int4 DEFAULT 1 NOT NULL,
    CONSTRAINT incidents_pkey PRIMARY KEY (id),
    CONSTRAINT incidents_direction_check CHECK (direction IS NULL OR direction IN ('Inbound', 'Writeback')),
    CONSTRAINT incidents_kind_check
        CHECK (kind IN ('Retrying', 'Blocked', 'CheckpointDiscarded', 'Started', 'Stopped'))
);

CREATE INDEX IF NOT EXISTS incidents_started_at_idx ON salesforce.incidents (started_at);

COMMIT;
