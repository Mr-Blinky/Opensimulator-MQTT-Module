# M0 diagnostic module — Windows test

This is a **first compilation and module-loading experiment**, not a functioning MQTT client.
It does not touch YEngine, ROBUST, or OpenSimulator source.

## 1. Check the actual DLL location

On the OpenSim host, run:

```powershell
Get-ChildItem 'D:\Opensim\Builds\rev-latest' -Recurse -Filter OpenSim.Region.Framework.dll | Select-Object -First 5 FullName
```

The project defaults to `D:\Opensim\Builds\rev-latest`. If your DLL is somewhere else, supply that directory as `OpenSimBinPath`.

## 2. Build

Clone this repository to `D:\Opensim\Source\Opensimulator-MQTT-Module`, then:

```powershell
cd 'D:\Opensim\Source\Opensimulator-MQTT-Module'
dotnet build .\src\OpenSim.MQTT\OpenSim.MQTT.csproj -c Release -p:OpenSimBinPath='D:\Opensim\Builds\rev-latest'
```

If compilation fails, **stop and share the exact error**. The OSgrid build's interfaces and assembly references need to be verified against the installed revision.

## 3. Deploy only to MQTT 1

Stop MQTT 1. Its module deployment directory is `D:\Opensim\Simulators\MQTT 1` (no additional `bin` directory). Copy **only** the built `OpenSim.MQTT.dll` there. Do not copy OpenSim or Nini DLLs from the build output.

Merge the `[OpenSimMQTT]` section from `config/OpenSimMQTT.ini.example` into a configuration file **actually loaded by MQTT 1**. Do not assume the example file is auto-included.

Restart MQTT 1 and look for `[OpenSimMQTT]` messages. Shut it down normally and check for `RemoveRegion` / `Close` messages. If the module does not load, capture the simulator startup log.

## 4. Acceptance

- Project compiles against the installed OpenSim build.
- MQTT 1 starts with no module errors.
- Module reports its region lifecycle.
- MQTT 1 shuts down cleanly.
- MQTT 2, ROBUST, and YEngine remain untouched.

This is only the **first part of M0**. Bidirectional script messaging follows after lifecycle validation.

## 5. Verified diagnostic results — 30 October 2026

Environment: Windows 11 Pro; OpenSimulator 0.9.3.1 Nessie Dev (OSgrid commit `ef8a36b40c`); .NET 8; SDK 8.0.426. Configuration file: `D:\Opensim\Simulators\MQTT 1\config-include\SimCustom.ini`.

- Release build succeeded against `D:\Opensim\Builds\rev-latest` (DLLs directly in this directory).
- Mono.Addins discovery confirmed: `Plugin Loaded: OpenSim.MQTT`; one non-shared region module loaded.
- `Enabled = true`: observed `Initialise; enabled=True`, `AddRegion: MQTT 1`, and `RegionLoaded: MQTT 1`.
- Normal shutdown: observed `RemoveRegion: MQTT 1` and `Close` at 18:52:19.
- `Enabled = false`: observed `Initialise; enabled=False` at 19:00:05. The code returns early from active lifecycle handlers when disabled; absence of subsequent region log messages has not been independently supplied as evidence.

**Result:** M0 diagnostic module foundation and lifecycle validated; disabled configuration parsing validated. This does **not** complete the full M0 feasibility gate in `DEVELOPMENT_PLAN.md`: script-originated command, script-targeted asynchronous callback, and relevant script lifecycle cleanup remain unverified. Keep issue #1 open until that evidence exists. No broker communication has been implemented yet.
