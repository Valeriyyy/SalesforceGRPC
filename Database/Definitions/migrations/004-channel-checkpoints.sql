-- Migration: Channel Checkpoints
--
-- Brings an existing App Database up to the shape in Definitions/schemas. Safe to re-run.
--
-- Adds the Checkpoint table the worker resumes each Channel from, and the Starting Point a Channel with no
-- Checkpoint begins at. Existing Channels take Latest, which is what the worker has always done.

BEGIN;

ALTER TABLE salesforce.platform_event_channels
    ADD COLUMN IF NOT EXISTS starting_point varchar(10) DEFAULT 'Latest' NOT NULL;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'platform_event_channels_starting_point_check') THEN
        ALTER TABLE salesforce.platform_event_channels
            ADD CONSTRAINT platform_event_channels_starting_point_check
                CHECK (starting_point IN ('Latest', 'Earliest'));
    END IF;
END $$;

CREATE TABLE IF NOT EXISTS salesforce.channel_checkpoints (
    channel_id int4 NOT NULL,
    replay_id bytea NOT NULL,
    saved_at timestamptz DEFAULT now() NOT NULL,
    CONSTRAINT channel_checkpoints_pkey PRIMARY KEY (channel_id),
    CONSTRAINT channel_checkpoints_channel_id_fkey FOREIGN KEY (channel_id)
        REFERENCES salesforce.platform_event_channels (id) ON DELETE CASCADE
);

COMMIT;
