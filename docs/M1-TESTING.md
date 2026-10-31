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

## Status

PASS: initial broker connection, authentication, diagnostic publish, subscriber receipt, and normal simulator startup. Still pending: clean disconnect verification, broker outage/nonblocking behavior, reconnection, and multiple-region/process testing. M1 is not yet fully complete.
