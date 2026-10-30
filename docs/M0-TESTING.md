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

Stop MQTT 1. Identify its actual `OpenSim.exe` / `OpenSim.dll` directory (usually `bin` within the simulator instance). Copy **only** the built `OpenSim.MQTT.dll` into that same directory. Do not copy OpenSim or Nini DLLs from the build output.

Merge the `[OpenSimMQTT]` section from `config/OpenSimMQTT.ini.example` into a configuration file **actually loaded by MQTT 1**. Do not assume the example file is auto-included.

Restart MQTT 1 and look for `[OpenSimMQTT]` messages. Shut it down normally and check for `RemoveRegion` / `Close` messages. If the module does not load, capture the simulator startup log.

## 4. Acceptance

- Project compiles against the installed OpenSim build.
- MQTT 1 starts with no module errors.
- Module reports its region lifecycle.
- MQTT 1 shuts down cleanly.
- MQTT 2, ROBUST, and YEngine remain untouched.

This is only the **first part of M0**. Bidirectional script messaging follows after lifecycle validation.
