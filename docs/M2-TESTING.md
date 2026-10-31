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

## M2.6 controlled 10-message publish burst — PASS (2026-10-31)

On MQTT 1, a new YEngine LSL script submitted 10 rapid `modSendCommand("OpenSimMQTT", "publish", ...)` requests to topic `shared/demo/burst` with payloads `M2.6 Test Message 1` through `M2.6 Test Message 10`. The script compiled and ran successfully.

Observed in-world chat at 09:19:21: 10 request IDs were printed; the script reported `Requests sent: 10`, `Successful: 10`, `Rejected: 0`. MQTTX independently received all 10 sequential messages (QoS 0), including message 10 at 2026-10-31 16:19:22.041. The script completed normally.

**PASS:** Baseline rapid-burst publishing and independent subscriber receipt of all 10 messages. **Not yet verified:** queue saturation/overflow behaviour at capacity 32, per-request response ID correlation (the burst script counted replies without matching IDs), and sustained-load responsiveness. No C# changes were required.

## M2.7 controlled 40-message publish burst — PASS (2026-10-31)

With MQTT 1 running and Mosquitto online, the existing LSL burst script was changed to submit 40 requests to `shared/demo/burst`. In-world chat at 09:21:46 listed request IDs for all 40 submissions and reported `Requests sent: 40`, `Successful: 40`, `Rejected: 0`. MQTTX independently received 40 sequential messages, including `M2.6 Test Message 40` (the test script's label was not updated).

**PASS:** All 40 requests returned success and all 40 publications were observed at MQTTX. **Not proven:** The 32-slot queue was ever full; its worker can drain requests during submission. Queue overflow/rejection, request-ID correlation, and sustained-load behaviour remain unverified. No C# module changes.

## M2.8–M2.10 burst diagnostics and request correlation (2026-10-31)

**M2.8 — 100-request unpaced burst: queue saturation observed; full response accounting not established.** The original LSL script submitted 100 publish commands in one `touch_start` event. A text subscriber subsequently recorded 89 messages (numbers 1–64 consecutive, then intermittent gaps), while captured local chat displayed 10 `Publish queue full` rejections and no final script summary. These observations do not establish the fate of every request.

**M2.9 — 100-request unpaced burst with request-ID tracking: INCONCLUSIVE for module reliability.** The revised LSL test tracked request IDs and reported after five seconds. User-observed script totals: 100 sent, 4 successful replies, 60 rejected replies, 0 duplicate replies, 0 unknown replies, 36 missing replies. Independent Mosquitto text subscriber recorded 37 payloads (1–34, 55, 71, 93). Exactly 64 replies were processed by the script, suggesting possible YEngine script-event queue saturation, but the queue's capacity and the location of any lost replies are **not confirmed**. Do not infer that only four MQTT publishes succeeded from the script's callback count.

**M2.10 — paced 100-request test with request-ID tracking: PASS (user-reported live evidence).** The script submitted 10 requests per timer event, with 0.2-second timer interval, for 100 total requests. The user supplied the full LSL summary at 10:15:43: 100 sent, 100 successful replies, 0 rejected, 0 duplicate, 0 unknown, 0 missing. The user independently confirmed exactly 100 messages in the Mosquitto text subscriber log (log file not attached for this run). This verifies complete callback accounting and independent subscriber receipt **for the paced test profile**. It does not prove delivery or callback guarantees for an unpaced 100-request burst, establish YEngine event-queue size, or imply QoS 0 delivery guarantees in general. No C# module changes were made.

**Follow-up:** Determine whether unpaced burst reply losses arise from script event-queue capacity or another dispatch boundary; consider testing a paced sender that still induces module queue pressure while allowing callbacks to drain. Rate limiting at simulator/region, script, and owner scopes is separately specified in `PROJECT_SPEC.md` §22.

### M2.11–M2.14 follow-up burst diagnostics (2026-10-31)

- **M2.11, 64 unpaced publish requests:** 34 success replies, 30 rejection replies, 0 missing, duplicate or unknown replies. Mosquitto subscriber received 34 payloads (1–33 and 62). All 64 requests accounted for.
- **M2.12, 65 unpaced publish requests:** 33 success replies, 32 rejection replies, 0 missing, duplicate or unknown replies. Mosquitto subscriber received 33 payloads (1–32 and 37). All 65 requests accounted for.
- **M2.13, 80 unpaced publish requests:** 21 success replies, 44 rejection replies, 15 missing replies, 0 duplicates or unknowns. Mosquitto subscriber received 36 payloads (1–33, 57, 63, 76), more than the number of script-observed success replies. One reported missing reply was for request 30, whose MQTT payload was independently received. The script processed 65 replies total.
- **M2.14, 100 unpaced `ping` requests:** script compiled successfully and user reported 64 pong replies, 0 duplicates, 0 unknowns, **36 missing**. Ping does not exercise the outbound MQTT publish queue or broker delivery. This reproduces the approximately 64–65 reply ceiling in the script callback path independently of MQTT publishing.

**Interpretation:** The repeatable loss of script-observed callbacks during unpaced bursts, including `ping`, strongly suggests a bounded script event queue or related callback-dispatch bottleneck. The exact mechanism and whether events are dropped by YEngine or elsewhere remain **unverified**. A successful 65-request test does not rule out a 64-slot queue because consumption and enqueueing can overlap. Next investigate a minimal pure-LSL `llMessageLinked` self-burst (without module calls), using a unique event number and counting deliveries, to isolate script event queue behavior. Do not classify the unpaced burst tests as full end-to-end callback PASS.

### M2.15 — Pure LSL linked-message burst (2026-10-31): PASS for reproducing event loss

The user compiled and ran a standalone YEngine LSL script with no MQTT module calls. Within one `touch_start` handler, the script invoked `llMessageLinked(LINK_THIS, 91515, (string)i, NULL_KEY)` 100 times, then counted matching `link_message` events and checked sequence numbers after five seconds. Reported result: **100 messages sent, 64 events received, 0 duplicate, 0 unknown, 36 missing**. This exactly reproduces the 64/36 split seen with the 100-request module ping test (M2.14) and 100-request unpaced MQTT publish test (M2.9).

**Conclusion:** The MQTT module and broker are not necessary to reproduce the missing callback/event symptom. Results strongly support a YEngine script-event queue capacity or event enqueue policy of approximately 64 pending events for this workload. The precise engine implementation, whether the same limit applies to all event types, and whether any other callback-specific limits exist have not been independently verified. Avoid treating a missing script callback as evidence that a publish failed; use paced submissions, bounded in-flight work, and correlation IDs. M2.15 is a PASS for the isolation objective, **not** a PASS for delivery of all 100 events.
