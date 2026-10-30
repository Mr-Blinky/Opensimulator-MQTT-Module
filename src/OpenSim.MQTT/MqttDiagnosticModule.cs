using System;
using System.Reflection;
using log4net;
using Nini.Config;
using Mono.Addins;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;

namespace OpenSim.MQTT;

/// <summary>
/// M0 only: diagnostic region-module lifecycle test. No MQTT traffic.
/// </summary>
[Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "OpenSimMQTTDiagnostic")]
public sealed class MqttDiagnosticModule : INonSharedRegionModule
{
    private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod()!.DeclaringType);
    private bool _enabled;

    public string Name => "OpenSim MQTT Diagnostic";
    public Type? ReplaceableInterface => null;

    public void Initialise(IConfigSource source)
    {
        // Disabled unless explicitly enabled in the simulator configuration.
        _enabled = source.Configs["OpenSimMQTT"]?.GetBoolean("Enabled", false) ?? false;
        Log.Info($"[OpenSimMQTT] Initialise; enabled={_enabled}");
    }

    public void AddRegion(Scene scene)
    {
        if (!_enabled) return;
        Log.Info($"[OpenSimMQTT] AddRegion: {scene.RegionInfo.RegionName}");
    }

    public void RegionLoaded(Scene scene)
    {
        if (!_enabled) return;
        Log.Info($"[OpenSimMQTT] RegionLoaded: {scene.RegionInfo.RegionName}");
    }

    public void RemoveRegion(Scene scene)
    {
        if (!_enabled) return;
        Log.Info($"[OpenSimMQTT] RemoveRegion: {scene.RegionInfo.RegionName}");
    }

    public void Close()
    {
        if (!_enabled) return;
        Log.Info("[OpenSimMQTT] Close");
    }
}
