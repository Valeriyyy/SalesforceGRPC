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
    date_created timestamptz DEFAULT now() NOT NULL,
    date_updated timestamptz NULL,
    last_synced_at timestamptz NULL, -- When this row was last reconciled against Salesforce
    CONSTRAINT platform_event_channels_pkey PRIMARY KEY (id),
    CONSTRAINT platform_event_channels_sf_id_key UNIQUE (sf_id),
    CONSTRAINT platform_event_channels_full_name_key UNIQUE (full_name)
);

COMMENT ON COLUMN salesforce.platform_event_channels.sf_id IS 'Salesforce ID of the channel, 0YL prefix';
COMMENT ON COLUMN salesforce.platform_event_channels.full_name IS 'Metadata full name including the __chn suffix';
COMMENT ON COLUMN salesforce.platform_event_channels.developer_name IS 'Unique name without the __chn suffix';
COMMENT ON COLUMN salesforce.platform_event_channels.channel_type IS 'data (Change Data Capture) or event (platform events); immutable in Salesforce after create';
COMMENT ON COLUMN salesforce.platform_event_channels.event_type IS 'custom, data, monitoring or standard (API 61.0+); immutable in Salesforce after create';
COMMENT ON COLUMN salesforce.platform_event_channels.last_synced_at IS 'When this row was last reconciled against Salesforce';
