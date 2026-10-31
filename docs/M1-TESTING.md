# M1 — MQTT connectivity test evidence

## Verified 2026-10-31, MQTT 1

Environment: OpenSimulator 0.9.3.1 Nessie Dev (.NET 8), MQTTnet 4.3.7.1207, Mosquitto at 172.16.100.5:1883, MQTT 3.1.1, TLS disabled, local username/password authentication. Credentials are stored only in the simulator's local configuration; no password is recorded here.

- PowerShell `Test-NetConnection 172.16.100.5 -Port 1883`: `TcpTestSucceeded : True`.
- `dotnet restore` and Release `dotnet build`: succeeded with zero warnings/errors.
- Release output contains `OpenSim.MQTT.dll` and `MQTTnet.dll` after enabling `CopyLocalLockFileAssemblies`.
- Simulator MQTT 1 launched normally and the region became ready.
- OpenSimulator log (2026-10-31):
  - `14:25:02.410 INFO [OpenSimMQTT] Connecting to MQTT broker 172.16.100.5:1883`
  - `14:25:02.502 INFO [OpenSimMQTT] MQTT broker connected.`
  - `14:25:02.509 INFO [OpenSimMQTT] Published diagnostic message to opensim/diagnostic/connection`
  - `14:25:02.713 DEBUG [RegionReady]: Region "MQTT 1" is ready`
- MQTTX independently received topic `opensim/diagnostic/connection`, QoS 0, payload `online` (user-confirmed 2026-10-31 14:25:03).

## Graceful shutdown — verified 2026-10-31

On normal MQTT 1 shutdown, the OpenSimulator log reported:

- `14:30:38.961 INFO [OpenSimMQTT] RemoveRegion: MQTT 1`
- `14:30:38.962 INFO [OpenSimMQTT] Close`
- `14:30:38.992 INFO [OpenSimMQTT] MQTT broker disconnected.`

PASS: observed module shutdown callbacks and successful broker disconnect. Cleanup is currently asynchronous, so this does not guarantee completion on every process exit.

## Broker-unavailable startup — verified 2026-10-31

For a controlled negative test, MQTT 1 was configured to connect to 172.16.100.5:1884 instead of the working 1883 port. The region became ready at `14:38:06.783`; the module logged `MqttCommunicationException` connecting to port 1884 at `14:38:08.489`. PASS: broker connection failure did not prevent region readiness. This does not yet test recovery/retry. Restore port 1883 after testing.

## Status

PASS: initial broker connection, authentication, diagnostic publish, subscriber receipt, and normal simulator startup. Still pending: automatic reconnection and recovery, robust shutdown guarantees, and multiple-region/process testing. M1 is not yet fully complete.

## Broker service outage and automatic recovery — verified 2026-10-31

MQTT 1 remained running while the Mosquitto Windows service was stopped and subsequently restarted. OpenSimulator reported unexpected disconnection at `14:48:45.056`, then connection failures and 5-second retry delays at `14:48:47.120`, `14:48:54.185`, `14:49:01.246`, and `14:49:08.302`. Following broker restoration, OpenSimulator logged:

- `14:49:13.306 INFO [OpenSimMQTT] Connecting to MQTT broker 172.16.100.5:1883`
- `14:49:13.313 INFO [OpenSimMQTT] MQTT broker connected.`
- `14:49:13.314 INFO [OpenSimMQTT] Published diagnostic message to opensim/diagnostic/connection`

PASS: disconnected-client detection, retry after connection failure, automatic reconnect, and successful publish API call following reconnect. MQTTX was also disconnected during the outage and did not observe the second message because it reconnected after the QoS 0 non-retained publication. Thus subscriber receipt of the recovery message is **not** independently verified. No simulator restart was required.
