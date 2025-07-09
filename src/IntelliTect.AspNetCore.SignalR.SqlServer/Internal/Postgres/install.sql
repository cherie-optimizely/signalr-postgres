-- Database needs to be created beforehand manually
-- CREATE DATABASE "SignalRCore";

-- Create schema
CREATE SCHEMA IF NOT EXISTS "SignalR";

-- Create tables
CREATE TABLE IF NOT EXISTS "SignalR"."Messages" (
    "PayloadId" SERIAL PRIMARY KEY, -- Auto-incrementing integer for payload ID
    "Payload" BYTEA NOT NULL, -- Binary data for the payload
    "InsertedOn" TIMESTAMP NOT NULL DEFAULT NOW()
);

-- The following are not in use
CREATE TABLE IF NOT EXISTS "SignalR"."Messages_Id" (
    "PayloadId" INT PRIMARY KEY
);

CREATE TABLE IF NOT EXISTS "SignalR"."Schema" (
    "SchemaVersion" INT PRIMARY KEY
);
