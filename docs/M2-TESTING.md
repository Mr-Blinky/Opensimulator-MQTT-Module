# M2 diagnostic script bridge test

Date: 2026-10-31

Environment: OpenSimulator 0.9.3.1 Nessie Dev, YEngine, MQTT 1 region.

Build: Release succeeded with zero warnings and zero errors after adding the OpenMetaverseTypes assembly reference.

Startup: the module registered its diagnostic script command handler and connected to the MQTT broker. MQTTX received the diagnostic online message.

In-world test: an LSL script called modSendCommand("OpenSimMQTT", "ping", "") and received link_message code 71001, text OpenSimMQTT:pong, with a matching request ID. The module logged that it received the script ping and sent a targeted reply.

Result: PASS for a single-script command/reply round trip.

Not yet verified: isolation between multiple scripts in one object; script-originated MQTT publishing; authorization and rate limiting.

## Same-prim script isolation test — PASS (2026-10-31)

Two YEngine scripts compiled and ran in the same prim. The original script called modSendCommand with the OpenSimMQTT ping command; a second listener-only script handled all link_message events and did not invoke the command.

Observed chat:

```text
[08:34:05] Object: MQTT isolation listener ready.
[08:34:14] Object: Sent MQTT module ping. Request ID: 04315f86-2a90-4328-8740-b49e9796f980
[08:34:14] Object: SUCCESS: Received matching MQTT module pong!
```

No isolation-listener link_message output appeared. PASS: in this two-script, same-prim runtime test, the diagnostic reply reached the requesting script and was not observed by the second script. This does not establish isolation under every lifecycle or race condition; reset/deletion tests remain outstanding.
