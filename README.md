# OpenSimulator MQTT Module

Standalone C# MQTT integration for OpenSimulator, intended to support in-world script messaging, cross-simulator communication, external integrations and region telemetry **without modifying OpenSimulator core or YEngine**.

## Project status — 31 October 2026

**M1 core connection management: verified on one simulator/region (MQTT 1).** The module now connects to Mosquitto, publishes a diagnostic MQTT message, retries after broker outages, reconnects automatically and disconnects on normal shutdown. The in-world script API, cross-region messaging and telemetry are **not implemented yet**.

| Area | Status |
| --- | --- |
| M0: module registration and lifecycle callbacks | Verified |
| M0: script-to-module API and targeted async callback proof | Pending |
| M1: MQTTnet integration and authenticated broker connection | Verified |
| M1: diagnostic publish received in MQTTX | Verified |
| M1: broker-unavailable startup without blocking region readiness | Verified |
| M1: reconnect after a real broker service outage | Verified |
| M1: normal shutdown/disconnect, including after reconnect | Verified in tests; asynchronous cleanup is best-effort |
| M1: multiple regions / multiple simulator processes | Not tested |
| M2: in-world script publish API | Not implemented |
| M3+: inbound script delivery, cross-region messaging, telemetry, Home Assistant, TLS/hardening | Planned |

See [M0 test evidence](docs/M0-TESTING.md), [M1 test evidence](docs/M1-TESTING.md), the [project specification](PROJECT_SPEC.md) and [development plan](DEVELOPMENT_PLAN.md).

## Verified development environment

- OpenSimulator **0.9.3.1 Nessie Dev**, OSgrid source commit `ef8a36b40c`.
- Windows 11 Pro; .NET 8 SDK; standalone `INonSharedRegionModule`.
- MQTTnet **4.3.7.1207**, MQTT protocol **3.1.1**.
- Mosquitto on a local network at **172.16.100.5:1883**, username/password authentication, **TLS disabled for LAN tests**.
- Tested simulator: **MQTT 1**. No changes to OpenSimulator core or YEngine.

These addresses and credentials are development-environment examples, not production defaults.

## Build

The project references DLLs from an existing OpenSimulator build. By default, it expects them in `D:\Opensim\Builds\rev-latest`; override the MSBuild property `OpenSimBinPath` if needed.

```powershell
cd D:\Opensim\Source\Opensimulator-MQTT-Module
dotnet restore .\src\OpenSim.MQTT\OpenSim.MQTT.csproj
dotnet build .\src\OpenSim.MQTT\OpenSim.MQTT.csproj -c Release
```

The Release output directory `src\OpenSim.MQTT\bin\Release\net8.0` should contain both `OpenSim.MQTT.dll` and `MQTTnet.dll`.

## Local configuration

Place the following section in a configuration file loaded by the simulator (for the verified MQTT 1 setup, `config-include\SimCustom.ini`):

```ini
[OpenSimMQTT]
Enabled = true
BrokerHost = 172.16.100.5
BrokerPort = 1883
UseTLS = false
Username = opensim
Password = <your-local-broker-password>
ReconnectSeconds = 5
```

The module reads `Password` directly from the local INI file. **Do not commit real passwords to Git**, and protect the local configuration file. The current M1 implementation intentionally rejects `UseTLS = true`; TLS is planned for a later hardening milestone.

See [the example configuration](config/OpenSimMQTT.ini.example).

## Deploy and verify (MQTT 1)

1. Stop the MQTT 1 simulator.
2. Copy `OpenSim.MQTT.dll` and `MQTTnet.dll` from the Release output to the simulator's main directory alongside `OpenSim.exe`.
3. Configure the local `[OpenSimMQTT]` section.
4. Connect MQTTX to the broker and subscribe to `opensim/diagnostic/#` **before** starting MQTT 1.
5. Start MQTT 1 and check its log for `MQTT broker connected.` and `Published diagnostic message`.

On successful connection, the module publishes:

| Property | Value |
| --- | --- |
| Topic | `opensim/diagnostic/connection` |
| Payload | `online` |
| QoS | 0 |
| Retained | No |

The message is sent after each successful connection, including reconnection. An MQTTX client that is disconnected during a broker outage can miss this QoS 0, non-retained message.

## Reliability demonstrated

On **31 October 2026**, MQTT 1 successfully:

- Started and became region-ready even when the configured broker port was unavailable.
- Detected a real Mosquitto Windows service outage, retried connections at approximately five-second intervals, and reconnected when the service returned.
- Published the diagnostic message after recovery without a simulator restart.
- Disconnected from the broker during normal shutdown, including after the outage/recovery test.

The initial diagnostic message was independently received by MQTTX. During the outage/recovery test, MQTTX reconnected too late to observe the second message; the successful post-reconnect publish is supported by the module log, **not** by independent MQTTX receipt.

**Current limitations:** this is a diagnostic implementation, not yet a script-facing API. Reconnection is basic; shutdown cleanup is asynchronous and not guaranteed to finish in every exit scenario. The current module is non-shared, and multiple-region/multiple-simulator behaviour has not been validated. Avoid treating it as production-ready.

## Next work

Investigate OpenSimulator's supported script API extension points, then implement and verify an in-world script-to-module publish request and targeted asynchronous callback, without changes to core or YEngine. After that, continue with inbound delivery, multi-region behaviour and security hardening.
