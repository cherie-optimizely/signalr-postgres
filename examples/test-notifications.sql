-- Test script to verify PostgreSQL LISTEN/NOTIFY is working
-- Run this script in a PostgreSQL client while the server is running

-- First, listen on the same channel the SignalR server uses
LISTEN SignalRNotificationChannel;

-- In a separate connection, insert a test message to trigger the notification
-- This should be the same schema and table that the SignalR server creates
-- Replace 'chathub' with the actual hub name if different
INSERT INTO "signalr"."chathub_Messages" ("Payload") 
VALUES (E'\\x74657374206d657373616765'::bytea);  -- 'test message' in hex

-- You should see a notification message appear in the LISTEN session
-- The notification will contain JSON with Id, Payload (base64), and InsertedOn

-- To stop listening:
-- UNLISTEN SignalRNotificationChannel;
