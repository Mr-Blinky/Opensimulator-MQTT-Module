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

## Restricted publish deployment and forbidden-topic test — PASS (2026-10-31)

Release build of commit 66b832e succeeded with zero warnings and zero errors. The updated module was deployed to MQTT 1. At 15:45:34–15:45:35 it initialized, registered its script command handler, connected to the broker, and published the existing online diagnostic. MQTTX received the online diagnostic at 15:45:36.731.

An in-world script attempted a publish to `shared/forbidden/test` with payload `THIS MESSAGE MUST NOT BE PUBLISHED` while MQTTX subscribed to `shared/#`.

```text
[08:49:41] Object: Requested publish to forbidden topic.
[08:49:41] Object: Request ID: 490d645e-c389-4b9c-891f-b3c778d92880
[08:49:41] Object: PASS: Forbidden publish rejected.
[08:49:41] Object: Reason: Topic or payload rejected
```

MQTTX received no message. PASS: this forbidden topic was rejected with a correlated error response; this is not an exhaustive authorization/security test. Next: permitted-topic publish and MQTTX receipt.

## Permitted-topic end-to-end publish — PASS (2026-10-31)

An in-world script published topic `shared/demo/hello` with payload `Hello from OpenSimulator!`. The script reported a matching request ID and received publish-success code 71002, text `OpenSimMQTT:published`.

```text
[08:52:52] Object: Request ID: e3be10c8-a6ed-457e-b2b3-7ed11c199fe6
[08:52:52] Object: PASS: MQTT publish succeeded!
[08:52:52] Object: Response: OpenSimMQTT:published
```

MQTTX independently received `shared/demo/hello`, QoS 0, payload `Hello from OpenSimulator!` at 2026-10-31 15:52:53.946. PASS: LSL to module to broker to subscriber. The script's earlier informational text still said 'forbidden topic', but the observed MQTTX topic verifies the actual permitted destination. This does not yet establish delivery guarantees beyond QoS 0 or production-ready authorization.

## M2.5 broker-disconnected script publishing and recovery — PASS (2026-10-31)

Controlled test on MQTT 1 using the existing deployed module and existing in-world publish script. Mosquitto at `172.16.100.5:1883` was stopped and subsequently restarted. MQTT 2 was untouched; no module changes or simulator restart were required.

- At 16:02:28, OpenSimulator logged `MQTT connection lost; reconnecting.` and began retrying. Repeated connection failures were logged with a configured five-second delay after each failed attempt.
- While disconnected, the script attempted a permitted publish and received a rejection:

```text
[09:04:16] Object: Requested publish to topic.
[09:04:16] Object: Request ID: fb1bcaa6-6e77-4059-9a5f-38e6f4518d62
[09:04:16] Object: FAIL: MQTT publish rejected.
[09:04:16] Object: Reason: Broker disconnected
```

- No additional OpenSimulator log errors were reported for this script request. The script responded while the broker was unavailable. The LSL informational message was clarified from 'forbidden topic' to 'topic'; this was only a script text change.
- After Mosquitto restarted, OpenSimulator logged a successful connection at 16:07:04.414 and reported publishing the diagnostic `online` message at 16:07:04.416. MQTTX did not independently capture that QoS 0 nonretained diagnostic.
- The same script published again after recovery:

```text
[09:09:29] Object: Requested publish to topic.
[09:09:29] Object: Request ID: 1fc946ec-817a-46d3-a1f4-d55477d20d2a
[09:09:29] Object: PASS: MQTT publish succeeded!
[09:09:29] Object: Response: OpenSimMQTT:published
```

- MQTTX independently received topic `shared/demo/hello`, QoS 0, payload `Hello from OpenSimulator!` at 2026-10-31 16:09:31.817.

**PASS:** Broker loss detection, continuing retry attempts, script-level disconnected rejection, automatic reconnection, and end-to-end script publication after recovery. The simulator and script remained responsive in the observed test. **Not established:** bounded queue behaviour under sustained load or races during disconnection, exhaustive request-correlation testing, and production security guarantees. These remain separate tests.
