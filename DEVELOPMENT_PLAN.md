# Opensimulator MQTT Module — Structured Development Plan

**Plan version:** 0.1 · **Date:** 30 October 2026 · **Repository:** `Mr-Blinky/Opensimulator-MQTT-Module`  
**Requirements source:** [`PROJECT_SPEC.md`](PROJECT_SPEC.md), baseline v0.2.  
**Status:** Proposed delivery plan; no code generation or repository modifications authorized by this plan.

## 1. Project outcome and guiding constraints

Deliver an independently installable C# OpenSimulator region module for MQTT-based inter-region, inter-grid and external communications, with script-free region telemetry and optional Home Assistant discovery. Preserve all G-01–G-08, AC-01–AC-08 and AD-001 requirements from the specification. No YEngine, script engine, or OpenSimulator core modifications. Every milestone produces a working, testable artifact and documented evidence.

**TLS decision:** `UseTLS` is a Boolean configuration setting. Initial local development uses `UseTLS=false`, port `1883`, and no certificates. Implement and validate optional `UseTLS=true` later before v1 release; recommend TLS for non-local connections. Never disable authentication/authorization just because TLS is off.

## 2. Recommended repository structure

```text
Opensimulator-MQTT-Module/
├── README.md
├── PROJECT_SPEC.md                # canonical functional/technical requirements
├── DEVELOPMENT_PLAN.md            # phases, gates, acceptance criteria
├── LICENSE                        # decision pending
├── CHANGELOG.md
├── CONTRIBUTING.md
├── SECURITY.md
├── .editorconfig
├── .gitignore
├── .github/
│   ├── ISSUE_TEMPLATE/
│   ├── PULL_REQUEST_TEMPLATE.md
│   └── workflows/
│       ├── build.yml
│       └── test.yml
├── docs/
│   ├── architecture.md
│   ├── compatibility.md
│   ├── installation.md
│   ├── configuration.md
│   ├── script-api.md
│   ├── mqtt-topics.md
│   ├── security-model.md
│   ├── operations.md
│   ├── home-assistant.md
│   ├── testing.md
│   ├── decisions/                   # ADR-0001, ADR-0002, ...
│   └── evidence/                    # reproducible test reports, no secrets
├── src/
│   ├── OpenSim.MQTT.Module/         # region-module adapter, lifecycle
│   ├── OpenSim.MQTT.Core/           # broker-agnostic routing, policy, queues
│   └── OpenSim.MQTT.Mqtt/           # MQTTnet adapter, reconnect, transport
├── tests/
│   ├── OpenSim.MQTT.UnitTests/
│   ├── OpenSim.MQTT.IntegrationTests/
│   ├── OpenSim.MQTT.OpenSimTests/   # manual/automated simulator harness
│   └── fixtures/
├── examples/
│   ├── config/                      # local-plaintext and secure templates
│   ├── scripts/                     # publish, subscribe, switch, light
│   ├── home-assistant/
│   └── mqtt-clients/
├── tools/                            # optional test and packaging scripts
└── artifacts/                        # ignored local build output
```

The split between Core, Mqtt, and Module is recommended, not mandatory; validate compatibility and deployment complexity before finalizing project files. Keep the OpenSim-dependent assembly thin so unit tests can run without a simulator.

## 3. Software architecture and boundaries

```text
Scripts --[verified mod API]--> ScriptAdapter --> Authorization --> RequestRouter
   ^                                                        |
   |                                                        v
   +<--[verified targeted callback]-- DispatchQueue <-- SubscriptionRegistry
                                                            |
OpenSim region hooks --> RegionRegistry --> TelemetryPublisher|
                                                            v
                                                Bounded outbound queue
                                                            |
                                               MQTT ConnectionManager
                                                            |
                                                        Broker
                                                            |
                        other simulator modules / external clients / HA
```

**Ownership:** One broker connection per simulator process where a shared region module is feasible; per-region state remains isolated. The module must never block region threads on MQTT network I/O.

| Component | Responsibility | Key interfaces |
|---|---|---|
| Region module adapter | OpenSim registration, region add/remove, startup/shutdown, safe hooks | Verified OpenSim public interfaces |
| Script adapter | Authenticate caller, parse JSON commands, return correlated results | Verified mod interface; targeted dispatch gate |
| Policy engine | Default deny, prefix ACLs, rate limits, payload caps | Pure C# policy contracts |
| Subscription registry | Per-script subscriptions, consolidated broker filters, lifecycle cleanup | Script identity + broker subscription adapter |
| Message dispatcher | Bounded queues, delivery validation, safe event scheduling | OpenSim event API only after proof |
| Connection manager | MQTT 3.1.1 baseline, QoS 0/1, reconnect, LWT, optional TLS | Pinned compatible MQTTnet version |
| Region telemetry | Root avatar counts, reconciliation, region availability/heartbeat | Region hooks and authoritative scene reads |
| HA discovery | Retained discovery config, stable IDs, cleanup | MQTT publisher |
| Diagnostics | Structured logs, counters, status, redaction | Host logging abstraction |

**Critical uncertainty:** The script callback may be object-wide rather than script-targeted. Do not promise targeted script delivery until demonstrated. A documented routing discriminator is acceptable only if it prevents cross-script leakage and meets the requirements; otherwise block M3 and revisit the design.

## 4. Technical dependencies and compatibility

| Dependency | Requirement / decision |
|---|---|
| OpenSimulator | Exact version and public APIs **TBD at M0**; 0.9.3.x / .NET 8 / YEngine candidate only |
| .NET SDK/runtime | Match verified OpenSim build; pin in `global.json` after M0 |
| MQTTnet | Candidate MQTT client; pin compatible version and transitive dependencies after M1 |
| MQTT broker | Mosquitto recommended for local development; MQTT 3.1.1 compatible |
| Unit test framework | xUnit or NUnit, select in M1; mocking optional |
| CI | GitHub Actions for build/unit tests; integration broker service where supported |
| Home Assistant | Optional, needed only for M6 integration validation |
| Node-RED / external MQTT clients | Optional interoperability tools, not runtime requirements |
| Certificates | **Not required** for local `UseTLS=false` testing; required for TLS verification |

Maintain a compatibility matrix with exact OpenSim commit/release, runtime, OS, MQTTnet version, broker version, and observed module/script APIs. Do not invent verified compatibility.

## 5. Development phases and GitHub milestones

Milestone numbering is sequential. A stage is accepted only when its demo works from a clean checkout/build and its tests and limitations are recorded.

### M0 — Requirements freeze and API feasibility spike
**Goal:** Prove the extension strategy before committing to it.

**Work:** Confirm target OpenSim build and runtime; inspect/test shared region module interfaces; verify `modSendCommand`, `modInvoke` or supported alternatives; demonstrate script-originated request with trusted caller identity; demonstrate asynchronous callback to the intended script; inspect lifecycle hooks for reset, deletion, detachment and region unload; choose license and record ADRs.

**Deliverable:** Minimal loadable diagnostic module or isolated proof-of-concept (no production MQTT required), compatibility report, script/callback feasibility evidence, selected bridge contract.

**Gate:** No engine changes; module loads/unloads; inbound request and asynchronous response demonstrably reach intended destination; known limitations documented. If targeted callback cannot be proved, stop and redesign before M3.

### M1 — Buildable module skeleton and MQTT connectivity
**Goal:** Reliable local MQTT connection without in-world script access.

**Work:** Establish solution, build/test CI, config loader/validator, logging, region lifecycle; connect to local broker with `UseTLS=false`, port 1883; publish module online/diagnostic message; graceful shutdown; basic reconnect; pinned dependencies.

**Deliverable:** Installable module DLLs, sample plaintext config, reproducible broker test and logs.

**Gate:** Clean build; loads into verified simulator; broker receives online message; broker outage does not stall simulator; reconnect works; shutdown releases resources.

### M2 — Script command bridge and outbound publishing
**Goal:** An authorized script publishes a message to a broker.

**Work:** JSON envelope and correlation IDs; capability/status commands; trusted caller identification; default-deny policy, topic validation, payload/rate limits; bounded outbound queue; publish QoS 0/1 and retain flag; error/acceptance response semantics.

**Deliverable:** In-world publish demo + external MQTT subscriber; unit tests for validation/policy/serialization.

**Gate:** AC-03 passes; unauthorized scripts/topics rejected; publish request does not block simulation; queue-accepted vs broker-delivered semantics documented.

### M3 — Inbound subscription and script delivery
**Goal:** A broker message safely reaches the subscribed in-world script.

**Work:** Subscription registry; subscribe/unsubscribe commands; consolidate filters; incoming bounded dispatch; correlated callbacks; lifecycle cleanup; restore subscriptions after reconnect; wildcard policy; multi-script isolation.

**Deliverable:** External MQTT publish triggers a scripted response; reset/deletion/unload tests.

**Gate:** AC-02 and AC-08 pass; messages cannot reach unauthorized scripts; broker reconnect restores desired subscriptions; no stale callback after reset/deletion; callback feasibility decision from M0 remains valid.

### M4 — Cross-region, cross-process and cross-grid demo
**Goal:** Demonstrate real bidirectional simulator communications.

**Work:** Switch/light example; explicit shared namespace; test two regions in one process, two processes, two separate grids; retained state vs commands; QoS/duplicate behavior; baseline latency instrumentation.

**Deliverable:** Documented switch-on-Grid-A/light-on-Grid-B demo and latency report.

**Gate:** AC-01 passes; no grid-specific coupling; test on real broker/network; sub-100ms remains an aspiration, never a promise.

### M5 — Script-free region telemetry
**Goal:** Publish trustworthy population and availability.

**Work:** Count root agents only; initial authoritative count; event-driven changes + periodic reconciliation; region and process availability/heartbeat; process LWT; per-region opt-in/disable; timestamped schema; privacy review.

**Deliverable:** MQTT region count and availability feeds without in-world scripts.

**Gate:** AC-04 passes; count reconciles after missed events; child agents excluded; region unload and unexpected process loss produce documented availability behavior; no avatar identity exposed.

### M6 — Home Assistant discovery and history
**Goal:** Plug-and-play HA sensors and historical avatar-count charts.

**Work:** Stable discovery IDs, device metadata, retained configs, correct JSON value templates, availability; rename/restart behavior; discovery cleanup; HA recorder and history verification; documentation.

**Deliverable:** Example HA dashboard configuration and recorded population history.

**Gate:** AC-05 passes; discovery survives restart without duplicates; offline and idle periods behave as documented; chart/history data demonstrable.

### M7 — Hardening, optional TLS, packaging and v1 release
**Goal:** Production-ready, documented, reproducible release.

**Work:** `UseTLS=true` certificate-valid and certificate-invalid tests, while preserving `UseTLS=false`; credentials/ACL tests; queue overflow, long-run reconnect, security review; compatibility matrix; binaries and dependency notices; installation/upgrade/uninstall; CI release artifacts; operator runbook; complete regression.

**Deliverable:** Tagged v1.0.0 candidate, release ZIP, changelog, install docs, complete test report.

**Gate:** All AC-01–AC-08 pass on supported configuration; TLS on/off verified; no engine patches; security defaults pass; reproducible installation from release package; known limitations published.

## 6. Milestone dependencies and work sequencing

```text
M0 (API proof) -> M1 (module + broker) -> M2 (publish) -> M3 (subscribe)
                                                 |                 |
                                                 +----------------> M4 (cross-grid)
M1 (region hooks) -----------------------------------------------> M5 (telemetry)
M5 ---------------------------------------------------------------> M6 (Home Assistant)
M2 + M3 + M4 + M5 + M6 ------------------------------------------> M7 (release)
```

Telemetry can proceed in parallel with script-bridge development after M1, but keep milestone acceptance sequential for simplicity. Each PR should be small, with tests and documentation.

## 7. Installation and operator requirements

**Development machine:** Git, selected .NET SDK, supported OpenSim source/binaries, C# IDE, local Mosquitto broker, MQTT client CLI, and permission to install region module DLLs/config and restart the simulator. For integration tests use two regions, and eventually two simulator processes/grids. No Home Assistant required before M6.

**Initial test profile:** `BrokerHost=127.0.0.1`, `BrokerPort=1883`, `UseTLS=false`, module explicitly enabled, script access default-deny until operator policy configured. If broker is on a different host, bind/firewall carefully; plaintext MQTT should not be exposed on public networks.

**Deployment:** Operator must control simulator module loading and configuration; install exact compatible DLLs and dependencies; configure broker reachability, stable `GridId`, unique client IDs, script permissions, topic prefixes, optional telemetry and discovery; restart or reload as supported; validate connectivity and removal. Final file paths/section names must be confirmed in M0/M1, not guessed.

**Production recommendations:** TLS for remote brokers, broker ACLs, least-privilege credentials, certificate verification, firewalling, secret injection via environment/secure store, backup config, and monitor offline/LWT topics.

## 8. Testing strategy and evidence

| Layer | Coverage | Milestones |
|---|---|---|
| Unit | Config, JSON schemas, ACLs, topic validation, limits, subscription registry, discovery payloads | M1–M7 |
| MQTT integration | Publish/subscribe, QoS 0/1, retained state, reconnect, outage, authentication, plaintext/TLS modes | M1–M7 |
| OpenSim integration | Module lifecycle, exact script callbacks, multiple scripts, resets, deletion, detach, crossing, unload | M0, M2–M7 |
| Cross-grid end-to-end | Two grids, shared broker, switch/light, latency and duplicate delivery | M4, M7 |
| Telemetry | Root-agent count, changes, reconciliation, zero count, unload, LWT/heartbeat | M5, M7 |
| Home Assistant | Discovery, stable IDs, rename, recorder/history, availability, cleanup | M6, M7 |
| Security/resilience | Default deny, broker ACLs, malformed JSON, wildcards, flooding, queue caps, credentials, cert failures | M2–M7 |
| Performance | Percentiles for publish-to-script, queue depth, memory, region frame impact under load | M4, M7 |

For every accepted milestone, archive exact commit SHA, runtime/OpenSim/broker versions, test commands, expected and actual outcomes, logs without secrets, and any unresolved issues in `docs/evidence/`. Use unit tests in PR CI; gate live OpenSim and HA tests on a documented manual or self-hosted integration environment. Never mark an unrun integration test as passed.

**Definition of Done (every milestone):** Working demo; tests pass; README/docs updated; new config settings documented; no new core/YEngine patches; security and lifecycle implications reviewed; acceptance evidence attached; issues/risks updated.

## 9. Suggested GitHub milestones and starter issues

| Milestone | Suggested issues | Exit artifact |
|---|---|---|
| M0 API Feasibility | Confirm target version; inspect module API; prove request; prove targeted callback; lifecycle hooks; choose license/ADRs | Feasibility report and diagnostic module |
| M1 Module Foundation | Solution/CI; config; load/unload; MQTTnet pin; plaintext broker connection; reconnect | Broker-connected module |
| M2 Script Publish | JSON command; caller identity; ACL; outbound queue; publish demo; tests | Script-to-MQTT demo |
| M3 Script Subscribe | Subscription registry; dispatch; script isolation; cleanup; reconnect restoration | MQTT-to-script demo |
| M4 Cross-Grid Demo | Shared namespace; switch/light; two-grid test; latency report | Cross-grid demo |
| M5 Region Telemetry | Root-agent count; reconciliation; availability/LWT; privacy | Telemetry feed |
| M6 Home Assistant | Discovery; availability; stable IDs; history; cleanup | HA dashboard demonstration |
| M7 Release 1.0 | TLS verification; security audit; load tests; packaging; docs; regression | v1 release candidate |

Recommended labels: `type:feature`, `type:bug`, `type:spike`, `type:docs`, `type:test`, `area:module`, `area:script-api`, `area:mqtt`, `area:telemetry`, `area:home-assistant`, `priority:high`, `blocked`, `security`. Track ADRs and decisions D-01–D-14 as issues, not undocumented assumptions.

## 10. Release and change management

- `main` protected by review and passing CI when feasible; feature branches and pull requests for work.
- Semantic versioning for module releases; version command envelopes and telemetry schema independently.
- Tag milestones only for meaningful demonstrable builds; publish prerelease binaries after integration validation.
- Keep secrets, broker passwords, local certificates and test-world private data out of commits and CI logs.
- Maintain compatibility matrix and dependency/license inventory; package third-party dependencies appropriately.
- Changes to accepted requirements require an explicit spec update and linked ADR/issue.

## 11. Open decisions and risks to resolve

**Blocking before production code:** D-01 exact OpenSim version/runtime; D-02 supported module bridge; D-03 targeted callback; D-04 lifecycle hooks. **Before public release:** D-05 protocol baseline, D-07 permission ownership, D-10 license, D-12 per-region overrides, D-13 event envelope, D-14 region crash availability semantics. Other decisions remain tracked in `PROJECT_SPEC.md` §16.

**Primary risks:** callback targeting unavailable; module binary incompatibility; stale subscriptions after script reset; event-thread safety; per-region LWT semantics with shared broker client; untrusted scripts accessing shared topics; Home Assistant history interpretation; cross-grid WAN latency. Mitigate with M0 spike, bounded queues, authoritative telemetry reconciliation, default-deny policy, explicit availability design, and end-to-end acceptance tests.

## 12. First action checklist (planning only)

1. Add `PROJECT_SPEC.md` and `DEVELOPMENT_PLAN.md` to the repository after review.
2. Confirm the exact OpenSim build and runtime used for the test environment.
3. Decide the repository license and contribution policy.
4. Create GitHub milestones M0–M7 and file the M0 feasibility issues.
5. Establish local broker test profile (`UseTLS=false`, port 1883).
6. Start M0 API validation; do **not** commit to a script callback implementation before proof.