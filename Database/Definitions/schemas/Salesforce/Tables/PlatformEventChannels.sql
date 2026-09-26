-- salesforce.platform_event_channels definition
-- Local mirror of the PlatformEventChannel Tooling API object. Salesforce remains the source of
-- truth; rows here are written after a successful Tooling API call and can be rebuilt by resync.
-- DROP TABLE salesforce.platform_event_channels;

CREATE TABLE IF NOT EXISTS salesforce.platform_event_channels (
    id serial4 NOT NULL,
    sf_id varchar(18) NOT NULL, -- Salesforce ID of the channel, 0YL prefix
    full_name varchar(255) NOT NULL, -- Metadata full name including the __chn suffix
    developer_name varchar(255) NOT NULL, -- Unique name without the __chn suffix
    master_label varchar(255) NULL,
    channel_type varchar(20) NOT NULL, -- data (Change Data Capture) or event (platform events)
    event_type varchar(20) NULL, -- custom, data, monitoring or standard (API 61.0+)
    namespace_prefix varchar(15) NULL,
    manageable_state varchar(30) NULL,
    is_primary bool DEFAULT false NOT NULL, -- The single channel the worker subscribes to
    starting_point varchar(10) DEFAULT 'Latest' NOT NULL, -- Where a channel with no Checkpoint begins: Latest or Earliest
    restart_from varchar(10) NULL, -- One-time start after a discarded Checkpoint; cleared by the next Checkpoint save
    date_created timestamptz DEFAULT now() NOT NULL,
    date_updated timestamptz NULL,
    last_synced_at timestamptz NULL, -- When this row was last reconciled against Salesforce
    CONSTRAINT platform_event_channels_pkey PRIMARY KEY (id),
    CONSTRAINT platform_event_channels_sf_id_key UNIQUE (sf_id),
    CONSTRAINT platform_event_channels_full_name_key UNIQUE (full_name),
    CONSTRAINT platform_event_channels_starting_point_check CHECK (starting_point IN ('Latest', 'Earliest')),
    CONSTRAINT platform_event_channels_restart_from_check CHECK (restart_from IN ('Latest', 'Earliest'))
);

-- At most one Primary Channel. A partial index rather than a constraint so the many false rows do not
-- collide with each other.
CREATE UNIQUE INDEX IF NOT EXISTS platform_event_channels_one_primary_idx
    ON salesforce.platform_event_channels (is_primary) WHERE is_primary;

COMMENT ON COLUMN salesforce.platform_event_channels.sf_id IS 'Salesforce ID of the channel, 0YL prefix';
COMMENT ON COLUMN salesforce.platform_event_channels.is_primary IS 'The single channel the worker subscribes to; at most one row is true';
COMMENT ON COLUMN salesforce.platform_event_channels.full_name IS 'Metadata full name including the __chn suffix';
COMMENT ON COLUMN salesforce.platform_event_channels.developer_name IS 'Unique name without the __chn suffix';
COMMENT ON COLUMN salesforce.platform_event_channels.channel_type IS 'data (Change Data Capture) or event (platform events); immutable in Salesforce after create';
COMMENT ON COLUMN salesforce.platform_event_channels.event_type IS 'custom, data, monitoring or standard (API 61.0+); immutable in Salesforce after create';
COMMENT ON COLUMN salesforce.platform_event_channels.last_synced_at IS 'When this row was last reconciled against Salesforce';
COMMENT ON COLUMN salesforce.platform_event_channels.starting_point IS 'Where the worker begins a channel that has no Checkpoint: Latest (default) or Earliest';
COMMENT ON COLUMN salesforce.platform_event_channels.restart_from IS 'One-time start chosen when the Checkpoint was discarded; used instead of starting_point until the next Checkpoint is saved';
