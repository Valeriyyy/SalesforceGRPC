-- salesforce.platform_event_channel_members definition
-- Local mirror of the PlatformEventChannelMember Tooling API object: one event/entity on a channel.
-- DROP TABLE salesforce.platform_event_channel_members;

CREATE TABLE IF NOT EXISTS salesforce.platform_event_channel_members (
    id serial4 NOT NULL,
    channel_id int4 NOT NULL,
    sf_id varchar(18) NOT NULL, -- Salesforce ID of the member, 0v8 prefix
    full_name varchar(255) NOT NULL, -- <channel>_<entity> with double underscores flattened to single
    developer_name varchar(255) NULL,
    selected_entity varchar(255) NOT NULL, -- Entity name, e.g. AccountChangeEvent
    filter_expression text NULL, -- Server-side delivery filter (API 56.0+)
    enriched_fields jsonb NULL, -- Fields always included in the payload (API 51.0+)
    cdc_schema_id int4 NULL, -- Optional link to the sync config for this entity
    date_created timestamptz DEFAULT now() NOT NULL,
    date_updated timestamptz NULL,
    last_synced_at timestamptz NULL,
    CONSTRAINT platform_event_channel_members_pkey PRIMARY KEY (id),
    CONSTRAINT platform_event_channel_members_sf_id_key UNIQUE (sf_id),
    CONSTRAINT pecm_channel_fk FOREIGN KEY (channel_id)
        REFERENCES salesforce.platform_event_channels(id) ON DELETE CASCADE,
    CONSTRAINT pecm_cdc_schema_fk FOREIGN KEY (cdc_schema_id)
        REFERENCES salesforce.cdc_schemas(id) ON DELETE SET NULL
);

CREATE INDEX IF NOT EXISTS platform_event_channel_members_channel_id_idx
    ON salesforce.platform_event_channel_members (channel_id);

COMMENT ON COLUMN salesforce.platform_event_channel_members.sf_id IS 'Salesforce ID of the member, 0v8 prefix';
COMMENT ON COLUMN salesforce.platform_event_channel_members.full_name IS '<channel>_<entity> with double underscores flattened to single';
COMMENT ON COLUMN salesforce.platform_event_channel_members.selected_entity IS 'Entity name, e.g. AccountChangeEvent; immutable in Salesforce after create';
COMMENT ON COLUMN salesforce.platform_event_channel_members.cdc_schema_id IS 'Optional link to the cdc_schemas row that says where this entity lands in the target database';
