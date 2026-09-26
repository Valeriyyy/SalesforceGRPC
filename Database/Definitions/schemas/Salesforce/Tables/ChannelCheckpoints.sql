-- salesforce.channel_checkpoints definition
-- One Checkpoint per mirrored Channel: the position in its event stream up to which every event has been
-- applied or deliberately skipped. The worker resumes from the event after it. See ADR 0005.
-- DROP TABLE salesforce.channel_checkpoints;

CREATE TABLE IF NOT EXISTS salesforce.channel_checkpoints (
    channel_id int4 NOT NULL, -- The Channel this position belongs to
    replay_id bytea NOT NULL, -- Opaque Salesforce replay ID, stored exactly as received
    saved_at timestamptz DEFAULT now() NOT NULL, -- Salesforce keeps events for 72 hours after this
    CONSTRAINT channel_checkpoints_pkey PRIMARY KEY (channel_id),
    CONSTRAINT channel_checkpoints_channel_id_fkey FOREIGN KEY (channel_id)
        REFERENCES salesforce.platform_event_channels (id) ON DELETE CASCADE
);

COMMENT ON COLUMN salesforce.channel_checkpoints.replay_id IS 'Opaque Salesforce replay ID, stored exactly as received; never parsed or compared';
COMMENT ON COLUMN salesforce.channel_checkpoints.saved_at IS 'When the position was saved; Salesforce keeps events for 72 hours, so an older one cannot be resumed from';
