-- Migration: Inbound Failure detail
--
-- Brings an existing App Database up to the shape the code expects. Safe to re-run.
--
-- What an Inbound Failure needs for the user to understand it and fix its cause, not only the driver's message:
-- the Target Table and SQLSTATE of a refused write, the column or constraint the database named, and the values
-- that were being written (as Writeback Failures keep them). All optional: a failure that never reached the
-- database (an event that would not decode) has none of them.

BEGIN;

ALTER TABLE salesforce.inbound_failures
    ADD COLUMN IF NOT EXISTS target_table text NULL,
    ADD COLUMN IF NOT EXISTS sql_state varchar(5) NULL,
    ADD COLUMN IF NOT EXISTS target_column text NULL,
    ADD COLUMN IF NOT EXISTS constraint_name text NULL,
    ADD COLUMN IF NOT EXISTS values_sent jsonb DEFAULT '{}'::jsonb NOT NULL;

COMMIT;
