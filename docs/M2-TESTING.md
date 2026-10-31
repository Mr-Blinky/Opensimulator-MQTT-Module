# M2 diagnostic script bridge test

Date: 2026-10-31

Environment: OpenSimulator 0.9.3.1 Nessie Dev, YEngine, MQTT 1 region.

Build: Release succeeded with zero warnings and zero errors after adding the OpenMetaverseTypes assembly reference.

Startup: the module registered its diagnostic script command handler and connected to the MQTT broker. MQTTX received the diagnostic online message.

In-world test: an LSL script called modSendCommand("OpenSimMQTT", "ping", "") and received link_message code 71001, text OpenSimMQTT:pong, with a matching request ID. The module logged that it received the script ping and sent a targeted reply.

Result: PASS for a single-script command/reply round trip.

Not yet verified: isolation between multiple scripts in one object; script-originated MQTT publishing; authorization and rate limiting.
