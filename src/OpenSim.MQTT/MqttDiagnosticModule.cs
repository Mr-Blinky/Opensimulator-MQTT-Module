using System;
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
    private bool _enabled;

    public string Name => "OpenSim MQTT Diagnostic";
    public Type? ReplaceableInterface => null;

    public void Initialise(IConfigSource source)
    {
        // Disabled unless explicitly enabled in the simulator configuration.
        _enabled = source.Configs["OpenSimMQTT"]?.GetBoolean("Enabled", false) ?? false;
        Console.WriteLine($"[OpenSimMQTT] Initialise; enabled={_enabled}");
    }

    public void AddRegion(Scene scene)
    {
        if (!_enabled) return;
        Console.WriteLine($"[OpenSimMQTT] AddRegion: {scene.RegionInfo.RegionName}");
    }

    public void RegionLoaded(Scene scene)
    {
        if (!_enabled) return;
        Console.WriteLine($"[OpenSimMQTT] RegionLoaded: {scene.RegionInfo.RegionName}");
    }

    public void RemoveRegion(Scene scene)
    {
        if (!_enabled) return;
        Console.WriteLine($"[OpenSimMQTT] RemoveRegion: {scene.RegionInfo.RegionName}");
    }

    public void Close()
    {
        if (!_enabled) return;
        Console.WriteLine("[OpenSimMQTT] Close");
    }
}
