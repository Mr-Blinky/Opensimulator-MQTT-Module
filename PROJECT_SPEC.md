# OpenSim MQTT Integration Module
## Functional and Technical Specification — Project baseline v0.2

**Status:** Requirements baseline for staged development; open technical decisions remain explicitly gated  
**Date:** 30 October 2026  
**Project:** Opensimulator MQTT Module (`Mr-Blinky/Opensimulator-MQTT-Module`)  
**Intended audience:** OpenSimulator module developers, grid operators, scripters, MQTT administrators, contributors

## 1. Executive summary

Build a standalone, open-source C# OpenSimulator region module that provides bidirectional MQTT connectivity to in-world scripts and publishes region telemetry without requiring in-world scripts. The system supports communication between regions on the same simulator, between simulator processes, across independent grids, and between OpenSim and external MQTT clients such as Home Assistant.

**Architectural decision AD-001:** Version 1 must not require changes to YEngine, custom script events, or patches to OpenSimulator core. Integration will use supported region-module and script-to-module interfaces, subject to a compatibility proof of concept. If an interface proves unavailable or insufficient, document the gap rather than silently introducing an engine dependency.

## 2. Goals and success criteria

### 2.1 Goals
- G-01: Publish messages from authorized in-world scripts to MQTT topics.
- G-02: Subscribe authorized in-world scripts to MQTT topics and deliver matching messages asynchronously.
- G-03: Support communication across regions, simulator processes, and independent grids through a reachable broker.
- G-04: Accept external MQTT commands and deliver them to authorized in-world scripts.
- G-05: Publish region population and availability without requiring in-world objects.
- G-06: Provide optional Home Assistant MQTT Discovery and historical population tracking through Home Assistant's recorder.
- G-07: Package as an installable, configurable, documented open-source module.
- G-08: Avoid blocking simulation and scripting threads on broker network operations.

### 2.2 Initial acceptance scenarios
- AC-01: A touch-triggered switch in Grid A changes a light in Grid B via a shared broker.
- AC-02: A separate MQTT client publishes a command and an in-world script responds.
- AC-03: An in-world script publishes a message received by an external client.
- AC-04: A region publishes root-avatar count changes and a periodic authoritative count.
- AC-05: Home Assistant discovers an avatar-count sensor and records its history.
- AC-06: After broker restart, the module reconnects without restarting the simulator.
- AC-07: Unauthorized topics or scripts are rejected and audited without crashing the region.
- AC-08: Reset/deleted scripts do not continue receiving messages from stale subscriptions.

## 3. Scope

### 3.1 In scope for v1
- Outbound MQTT publish and inbound subscribe.
- Per-script subscription registry and event routing through an existing supported event or module callback.
- MQTT broker connection management, optional configurable TLS, authentication, reconnect, health status, and Last Will.
- Shared-region-module deployment where supported; separate connection per simulator process.
- Region telemetry: online/offline and root-avatar counts.
- Optional Home Assistant Discovery for count and availability entities.
- Configuration, structured logging, metrics, tests, examples, and operator documentation.
- Topic namespacing and permission controls.

### 3.2 Deferred / optional
- Custom `osMqtt*` language functions or new `mqtt_message` event.
- Modifications to YEngine or OpenSimulator core.
- Broker clustering or broker hosting within OpenSim.
- Guaranteed exactly-once execution of in-world actions.
- Cross-grid avatar identity federation or avatar-name publication.
- Rich telemetry such as FPS, time dilation, script counts, object counts, and arrival/departure events beyond aggregate counts.
- MQTT v5-only advanced features unless supported by selected library and broker.
- Automatic dashboards beyond discovery and example configurations.

## 4. Architecture

```text
 In-world script <-> Supported script/module API
                           |
                 Subscription/router layer
                           |
               Shared region MQTT module
                /          |           \
        Region registry  Telemetry    Policy/ACL
                           |
                 MQTT connection manager
                           |
                      MQTT broker
                  /         |          \
           Other sim     Other grid    Home Assistant /
            module        module       external clients
```

### 4.1 Components
- **Region module lifecycle:** attach/detach regions, register hooks, manage cleanup.
- **Connection manager:** one logical broker connection per simulator process by default, reconnect and graceful shutdown.
- **Script adapter:** parse requests, identify caller and region, authorize actions, route replies/events.
- **Subscription registry:** map broker filters to individual script instances and owning regions; consolidate identical broker subscriptions.
- **Message dispatcher:** queue incoming messages and deliver on simulator-safe execution paths.
- **Telemetry publisher:** root-agent counts, state changes, periodic reconciliation, availability.
- **Discovery publisher:** Home Assistant discovery configuration and cleanup.
- **Policy engine:** permissions, topic constraints, payload limits, quotas, and logging.

### 4.2 Module selection
Prefer a shared region module for multiple regions hosted by one simulator process, if its lifecycle and APIs support this cleanly. The module must isolate per-region state and not assume a region is always available. Module interface and API availability must be verified against the actual target OpenSim release.

### 4.3 Dependencies
C#/.NET compatible with the target OpenSimulator build; a pinned, compatible MQTT client library (candidate: MQTTnet). Avoid hard-coding a library major version until dependency compatibility is tested. The module must not require Home Assistant or Node-RED to function.

## 5. Script-facing API contract

**Status: provisional.** The exact supported `modSendCommand`, `modInvoke`, or other module bridge signatures and event-routing capabilities must be verified in the first technical spike. The examples below describe desired behaviour, not existing OSSL calls.

### 5.1 Logical operations
| Operation | Inputs | Result |
|---|---|---|
| `publish` | topic, payload, QoS, retain, correlation ID | queued/accepted or rejected |
| `subscribe` | topic filter, correlation ID | subscription handle or error |
| `unsubscribe` | subscription handle | removed or error |
| `status` | optional correlation ID | connection and capability status |
| `capabilities` | none | supported API version, limits, features |

### 5.2 Command envelope
Use structured JSON rather than delimiter-separated text. Example logical request:

```json
{"version":1,"request_id":"req-42","operation":"publish","topic":"shared/demo/light/set","payload":"ON","qos":0,"retain":false}
```

Example logical received message:

```json
{"version":1,"type":"message","subscription_id":"sub-7","topic":"shared/demo/light/set","payload":"ON","qos":0,"retain":false}
```

Actual serialization and callback fields depend on the validated bridge. The adapter must distinguish module-generated events from ordinary `link_message` traffic, using a reserved discriminator and documented routing conventions. It must not depend on untrusted user-supplied fields for script identity.

### 5.3 Payloads
V1 script-facing payloads are UTF-8 strings; JSON is recommended for structured application messages. MQTT permits binary payloads; binary support may be added later via an explicit encoding. Define maximum payload sizes and reject invalid/oversized requests predictably.

### 5.4 Script identity and lifecycle
- Bind each subscription to the authenticated caller context (region, object, script instance where obtainable).
- Remove or invalidate subscriptions on script reset, script removal, object deletion, region removal, or module shutdown where lifecycle hooks permit.
- Revalidate delivery targets before dispatch; stale events must be dropped.
- Define behaviour for script disabled, detached attachment, region crossing, and object transfer after testing available APIs.
- Never trust an object UUID provided inside a script message as authorization evidence.

### 5.5 Callback feasibility gate
Before implementation proceeds, prove that a module can safely deliver an asynchronous response to the **specific requesting script**, not merely the whole object, without YEngine modifications. If the chosen bridge only supports object-wide `link_message`, document that limitation and establish a reliable script-level discriminator or revise the contract.

## 6. MQTT topic conventions

### 6.1 Reserved module topics
```text
opensim/{grid_id}/{region_id}/availability
opensim/{grid_id}/{region_id}/avatars/count
opensim/{grid_id}/{region_id}/telemetry/heartbeat
opensim/{grid_id}/{region_id}/objects/{object_id}/...
```

`grid_id` is a configured stable namespace; `region_id` is a stable region UUID, not a display name. IDs must be validated to avoid topic wildcard and separator injection. The module reserves `opensim/...` for managed telemetry unless explicit policy permits custom writes.

### 6.2 Shared topics
Scripts may be permitted to use application namespaces such as `shared/demo/light/set`, subject to broker ACLs and module policy. Cross-grid sharing is opt-in. Wildcard subscriptions (`+`, `#`) are restricted by policy; wildcards in publish topic names are invalid.

### 6.3 Message semantics
- Use `/set` for commands and `/state` for observed state when practical.
- Use retained messages for current state and discovery metadata, not one-shot actions.
- Retained commands are disabled by default to avoid unintended replay after restart.
- MQTT QoS is transport delivery assurance, **not** exactly-once execution of script side effects.
- Consumers should be idempotent where practical; optional application-level message IDs support deduplication.
- Message ordering is only assumed within the guarantees of the chosen client, connection, and subscription topology.

## 7. Connectivity and reliability

### 7.1 Broker connectivity
- Configurable host, port, client ID, username/password or equivalent authentication.
- TLS is explicitly configurable (`UseTLS = true|false`). Initial local testing uses `UseTLS = false` and broker port 1883; no certificates are required for that test profile. TLS-capable code and configuration are required for v1, with certificate validation enabled when TLS is used and no insecure validation bypass by default. TLS is recommended for non-local deployments.
- Reconnect with bounded exponential backoff and jitter.
- MQTT Last Will publishes an offline state; successful connection publishes online state.
- Graceful shutdown publishes offline before disconnecting when possible.
- Keep network operations asynchronous; no broker I/O on region simulation thread.

### 7.2 Queues and backpressure
- Bounded outbound and inbound queues with configurable limits.
- Clear policy for overflow: drop QoS 0 telemetry with counters; reject or report failed script requests when necessary.
- No unbounded per-script subscription or message accumulation.
- Publish acceptance means accepted into the module queue, not necessarily acknowledged by the broker; expose later delivery failures through status/diagnostics where feasible.

### 7.3 Connection loss
- Scripts may receive connection-state notifications if supported by the bridge.
- Desired subscriptions are restored after reconnect.
- Non-retained messages during an outage may be lost unless an explicit bounded retry policy is configured.
- No promise of delivery during simulator downtime.

## 8. Region telemetry

### 8.1 Population measurement
Count root agents in the region; exclude child agents. Publish on authoritative count change, with periodic reconciliation to recover from missed lifecycle events. A configurable debounce avoids floods during rapid movement. On startup, publish a fresh authoritative count; do not publish an assumed zero before the region is ready.

Example state payload:

```json
{"schema":1,"region_id":"00000000-0000-0000-0000-000000000001","region_name":"Welcome Island","count":14,"timestamp":"2026-10-30T16:00:00Z"}
```

### 8.2 Availability and freshness
Publish region availability separately from simulator availability. A retained `online` state alone can become stale if a module loses broker connectivity unexpectedly; use a Last Will for process-level offline status and a timestamped heartbeat or availability model for each region. Define explicit behaviour when one region unloads but others remain hosted by the process.

### 8.3 Privacy
Aggregate avatar counts are enabled by default when telemetry is enabled. Names, UUIDs, arrival/departure identity logs, and per-avatar tracking are out of scope for v1. Telemetry must be configurable per region and capable of being disabled entirely.

### 8.4 Historical data
The module publishes current observations; it does **not** operate its own time-series database. Home Assistant recorder or another MQTT consumer stores history. Document retention and sampling tradeoffs. State changes plus a periodic heartbeat should be sufficient for basic graphs; Home Assistant's treatment of unchanged values and availability must be tested.

## 9. Home Assistant integration

### 9.1 Discovery
Optional MQTT Discovery publishes retained configuration for each enabled region. At minimum expose:
- Avatar count sensor with `state_class: measurement`.
- Region availability (as entity availability and/or binary sensor).
- Device metadata with stable identifiers tied to grid and region.

Discovery configuration must reference the correct state and availability topics, include stable unique IDs, and provide a valid `value_template` for JSON payloads. Remove obsolete discovery entries when regions are permanently removed or discovery is disabled, subject to documented operator controls.

### 9.2 Example discovery payload
```json
{
  "name":"Welcome Island Avatars",
  "unique_id":"grid_a_region_001_avatar_count",
  "state_topic":"opensim/grid-a/region-001/avatars/count",
  "value_template":"{{ value_json.count }}",
  "state_class":"measurement",
  "icon":"mdi:account-group",
  "availability_topic":"opensim/grid-a/region-001/availability",
  "payload_available":"online",
  "payload_not_available":"offline",
  "device":{"identifiers":["opensim_grid_a_region_001"],"name":"Welcome Island","manufacturer":"OpenSimulator"}
}
```

Example identifiers are illustrative; production uses actual stable UUID-derived values. Discovery topic conventions and exact payload support must be checked against the target Home Assistant release.

## 10. Security model

### 10.1 Trust boundaries
A grid operator controls module configuration. In-world script authors and external MQTT publishers may be untrusted. The MQTT broker is a separate security boundary. A successful broker connection must not automatically grant arbitrary scripts access to all topics.

### 10.2 Controls
- Default-deny script MQTT access until explicitly enabled by grid policy.
- Configurable permission model: estate/region owner, object owner, explicit allowlist, or trusted groups, subject to reliable API support.
- Topic prefix allowlists for publish and subscribe, independent of broker ACLs.
- Per-script rate limits, concurrent subscription caps, and payload limits.
- Broker credentials stored outside committed source; redact credentials and sensitive payloads from logs.
- TLS and broker ACLs strongly recommended for inter-grid deployments; plaintext MQTT is permitted for explicitly configured local development/testing only and must not be silently upgraded or assumed secure.
- External messages are data, not commands to execute arbitrary server code.
- Reject malformed topic filters and unexpected schema versions.
- Log security denials with bounded, privacy-aware detail.

### 10.3 Operational caution
If Home Assistant controls real-world devices, grid operators must explicitly opt into the relevant topics and consider physical safety, authentication, replay protection, and authorization. The module does not treat virtual-world actions as intrinsically trusted.

## 11. Configuration contract (illustrative)

```ini
[OpenSimMQTT]
Enabled = false
BrokerHost = localhost
BrokerPort = 1883
UseTLS = false
ClientIdPrefix = opensim
GridId = my-grid
Username =
PasswordEnvironmentVariable = OPENSIM_MQTT_PASSWORD
EnableScriptBridge = false
EnableTelemetry = true
EnableAvatarCount = true
EnableHomeAssistantDiscovery = false
AvatarCountReconcileSeconds = 60
HeartbeatSeconds = 60
MaxPayloadBytes = 8192
MaxSubscriptionsPerScript = 16
MaxPublishPerScriptPerMinute = 120
AllowedPublishPrefixes =
AllowedSubscribePrefixes =
```

These keys are a proposed configuration contract, not known OpenSim defaults. The final format must follow actual module configuration loading and support per-region overrides where feasible. Secure defaults are essential; enabling the module must not automatically enable unrestricted script access.

## 12. Operational behaviour

### 12.1 Startup
Read and validate configuration; register regions; establish broker connection asynchronously; install subscriptions; publish region availability and fresh telemetry; publish discovery when enabled. Failure to reach the broker must not prevent regions from loading unless an operator explicitly configures strict startup behaviour.

### 12.2 Shutdown and unload
Stop accepting new requests; invalidate subscriptions; publish offline state where possible; cancel tasks; dispose MQTT client; release region references and handlers. Region unload must not disconnect a shared broker client while other regions remain active.

### 12.3 Observability
Structured logs with configurable verbosity; counters for publishes, receives, rejected requests, queue depth, reconnects, delivery failures, and stale subscription cleanup. Never log passwords. Include module version, broker connectivity, and active-region count in diagnostics.

## 13. Performance and latency

The intended experience is near-real-time for switches, lighting, and interactive objects. **Sub-100 ms end-to-end delivery is an aspirational benchmark, not a guaranteed service level.** Measure separately: script request to module, module to broker, broker to receiving module, module to script dispatch, and script-to-visible-state. Include simulator load and cross-grid WAN conditions. Avoid claims of deterministic timing.

Initial performance goals to validate: no measurable simulation stall from network I/O; bounded memory under broker outage; stable operation with many subscribed objects; predictable queue overflow behaviour. Numerical throughput and latency targets will be set after baseline tests.

## 14. Compatibility and packaging

- Target a clearly named OpenSimulator release and runtime after compatibility verification; 0.9.3.x/.NET 8/YEngine is a candidate, not an assumed confirmed target.
- Confirm supported region-module interfaces, script/module messaging hooks, and event dispatch paths using source and a running test grid.
- Ship compiled binaries for supported versions, source, dependency notices, sample config, script examples, and installation guide.
- Document DLL placement and configuration path based on the verified build; do not assume a universal layout.
- Avoid global changes to scripting engine configuration where possible; document any required module permission settings.
- Use semantic versioning for module releases and explicit versioning for script command and telemetry schemas.

## 15. Testing and acceptance

### 15.1 Unit tests
Configuration parsing; topic validation; ACL matching; command serialization; subscription registry; deduplication; lifecycle cleanup; queue limits; discovery payload generation.

### 15.2 Integration tests
Local broker publish/subscribe; broker unavailable at startup; disconnect/reconnect; plaintext local connection with `UseTLS=false`; TLS enabled connection and certificate validation failure in a separate secured test profile; wrong credentials; wildcard subscription rules; retained state; QoS 0/1 duplicate delivery; malformed payload; broker ACL denial.

### 15.3 OpenSim tests
Two regions in one process; two simulator processes; two separate grids; script reset; object deletion; attachment/detachment; region crossing; region unload/reload; multiple scripts in one object; simulator shutdown; high script load; inbound messages while destination script is unavailable.

### 15.4 Home Assistant tests
Discovery on first connect; entity stable after restart; correct avatar count; availability after graceful and unexpected shutdown; history graphs across idle periods; disabled discovery cleanup; renamed region without duplicate entity.

### 15.5 Definition of done for v1
All AC-01 through AC-08 pass on the supported OpenSim version; no engine patches; security defaults tested; module installs from documented steps; sample switch/light and Home Assistant monitoring examples included; known limitations documented.

## 16. Open questions and decisions required

| ID | Question | Proposed direction |
|---|---|---|
| D-01 | Exact target OpenSim version/runtime? | Confirm from test grid before coding |
| D-02 | Which script/module bridge is actually supported? | Spike `modSendCommand`/`modInvoke` and alternatives |
| D-03 | Can callbacks reach one specific script? | Mandatory feasibility gate |
| D-04 | What lifecycle events are available for script reset and attachment? | Test, then define cleanup semantics |
| D-05 | MQTT protocol baseline? | Prefer MQTT 3.1.1 compatibility; optional v5 |
| D-06 | Broker topology for independent grids? | Shared broker initially; federation deferred |
| D-07 | Topic permission ownership model? | Operator-defined allowlists, default deny |
| D-08 | Retained message policy? | State yes; commands no by default |
| D-09 | Should region telemetry default on? | Opt-in module, opt-in telemetry in production |
| D-10 | Repository name and license? | Repository confirmed: `Mr-Blinky/Opensimulator-MQTT-Module`; license still to choose |
| D-11 | Do we support multiple brokers per process? | One initially, extensible design |
| D-12 | How are per-region overrides represented? | Decide after configuration API validation |
| D-13 | What is the canonical script event envelope? | Design after callback proof |
| D-14 | How is a shared module's region-specific availability handled on crash? | Heartbeat/TTL consumer logic plus process LWT |

## 17. Suggested repository structure

```text
/
  README.md
  LICENSE
  docs/
    SPECIFICATION.md
    architecture.md
    security.md
    installation.md
    mqtt-topics.md
    script-api.md
    home-assistant.md
    compatibility.md
  src/
    OpenSim.MQTT/
  tests/
    OpenSim.MQTT.UnitTests/
    OpenSim.MQTT.IntegrationTests/
  examples/
    scripts/
      switch.lsl
      light.lsl
    home-assistant/
    configuration/
  .github/
    workflows/
```

## 18. Future enhancements

- First-class `osMqttPublish` and `osMqttSubscribe` if supported by upstream agreement.
- Richer region metrics and operational dashboards.
- Per-avatar presence events with explicit privacy controls.
- Broker federation and optional multiple broker connections.
- Advanced request/reply correlation, command acknowledgements, and application-level idempotency.
- Binary payloads and schema registry.
- Optional tooling for topic inspection and permission diagnostics.

## 19. Design principles and constraints

1. **Standalone first:** no OpenSimulator core or YEngine patches in v1.
2. **Opt-in and least privilege:** in-world scripts do not gain broad MQTT authority by default.
3. **Non-blocking:** MQTT I/O never blocks simulation processing.
4. **Explicit lifecycle:** subscriptions must not outlive valid destinations.
5. **Honest delivery semantics:** transport QoS does not guarantee script execution.
6. **Grid-neutral:** no assumptions that communicating regions share a grid.
7. **Telemetry without scripts:** region monitoring is independently useful.
8. **Documented contracts:** topic and message schemas are stable and versioned.
9. **Test before promising:** verify script callback mechanisms and performance in a running simulator.

---

**Next review:** Resolve D-01–D-04 as the first engineering gates, then close the remaining open decisions through tracked GitHub issues. See `DEVELOPMENT_PLAN.md` for the proposed staged implementation and milestone acceptance criteria.


## 20. Baseline clarifications (30 October 2026)

- **BC-01 — Repository:** Official repository is `Mr-Blinky/Opensimulator-MQTT-Module`.
- **BC-02 — TLS:** `UseTLS` must accept `true` or `false`; initial test profile uses `false` on port 1883. Production guidance recommends TLS for remote/non-local broker access. The module must not assume port 8883 or require certificates when TLS is disabled.
- **BC-03 — Security:** Disabling TLS does not disable topic ACLs, script authorization, rate limiting, or safe secret handling. Do not expose plaintext MQTT to untrusted networks.
- **BC-04 — Delivery sequencing:** Each milestone must end with an independently testable working build; all core-engine/YEngine modifications remain prohibited.
- **BC-05 — Contract status:** Script bridge signatures, callback semantics, target OpenSim release, and module DLL layout are unresolved until tested. Code examples in this document remain conceptual.
- **BC-06 — Change control:** This document is the canonical requirements baseline; implementation plans may refine *how* requirements are achieved but must not silently drop or expand v1 scope.

## 21. Requirements traceability and delivery gates

| Requirements | Primary delivery milestone | Proof |
|---|---|---|
| AD-001, G-07, compatibility | M0–M1 | Verified APIs and independently loadable module |
| G-01, G-08 | M2–M3 | Authorized script publish; nonblocking broker I/O |
| G-02, G-03, G-04 | M2–M4 | Targeted callbacks and cross-grid switch/light |
| G-05 | M5 | Root-agent count and availability telemetry |
| G-06 | M6 | HA discovery and historical graph acceptance |
| AC-06, AC-07, AC-08 | M3–M7 | Reconnect, policy denial, lifecycle cleanup tests |
| TLS configuration and security | M1, M3, M7 | Plaintext local test and optional verified TLS test |
| Packaging and v1 definition of done | M7 | Repeatable install and AC-01–AC-08 evidence |

For phase-level tasks, acceptance criteria, dependencies, and release gates, see `DEVELOPMENT_PLAN.md`.