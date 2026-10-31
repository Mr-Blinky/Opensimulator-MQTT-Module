using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using log4net;
using Nini.Config;
using Mono.Addins;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;

namespace OpenSim.MQTT;

/// <summary>
/// M1 connectivity experiment. No script access, telemetry or reconnection yet.
/// </summary>
[Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "OpenSimMQTTDiagnostic")]
public sealed class MqttDiagnosticModule : INonSharedRegionModule
{
    private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod()!.DeclaringType);
    private readonly CancellationTokenSource _stop = new();
    private IMqttClient? _client;
    private Task? _connectionTask;
    private bool _enabled;
    private string _host = "";
    private int _port;
    private string _username = "";
    private string _passwordVariable = "";

    public string Name => "OpenSim MQTT Diagnostic";
    public Type? ReplaceableInterface => null;

    public void Initialise(IConfigSource source)
    {
        var config = source.Configs["OpenSimMQTT"];
        _enabled = config?.GetBoolean("Enabled", false) ?? false;
        Log.Info($"[OpenSimMQTT] Initialise; enabled={_enabled}");
        if (!_enabled) return;

        _host = config?.GetString("BrokerHost", "127.0.0.1") ?? "127.0.0.1";
        _port = config?.GetInt("BrokerPort", 1883) ?? 1883;
        _username = config?.GetString("Username", "") ?? "";
        _passwordVariable = config?.GetString("PasswordEnvironmentVariable", "OPENSIM_MQTT_PASSWORD") ?? "OPENSIM_MQTT_PASSWORD";
        if (config?.GetBoolean("UseTLS", false) == true)
        {
            Log.Error("[OpenSimMQTT] M1 test build does not support TLS yet; connection disabled.");
            _enabled = false;
        }
        else if (string.IsNullOrWhiteSpace(_host) || _port < 1 || _port > 65535)
        {
            Log.Error("[OpenSimMQTT] Invalid broker host or port; connection disabled.");
            _enabled = false;
        }
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
        // Start without waiting on network I/O on the simulator's region thread.
        _connectionTask ??= Task.Run(() => ConnectAndPublishAsync(_stop.Token));
    }

    private async Task ConnectAndPublishAsync(CancellationToken token)
    {
        try
        {
            var password = Environment.GetEnvironmentVariable(_passwordVariable);
            if (!string.IsNullOrEmpty(_username) && string.IsNullOrEmpty(password))
            {
                Log.Error($"[OpenSimMQTT] Missing broker password environment variable {_passwordVariable}; not connecting.");
                return;
            }

            _client = new MqttFactory().CreateMqttClient();
            var builder = new MqttClientOptionsBuilder()
                .WithTcpServer(_host, _port)
                .WithClientId("opensim-mqtt-" + Guid.NewGuid().ToString("N"))
                .WithProtocolVersion(MQTTnet.Formatter.MqttProtocolVersion.V311)
                .WithCleanSession();
            if (!string.IsNullOrEmpty(_username))
                builder.WithCredentials(_username, password!);

            Log.Info($"[OpenSimMQTT] Connecting to MQTT broker {_host}:{_port}");
            await _client.ConnectAsync(builder.Build(), token);
            Log.Info("[OpenSimMQTT] MQTT broker connected.");

            var message = new MqttApplicationMessageBuilder()
                .WithTopic("opensim/diagnostic/connection")
                .WithPayload("online")
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce)
                .Build();
            await _client.PublishAsync(message, token);
            Log.Info("[OpenSimMQTT] Published diagnostic message to opensim/diagnostic/connection");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            Log.Info("[OpenSimMQTT] MQTT connection cancelled during shutdown.");
        }
        catch (Exception ex)
        {
            Log.Error("[OpenSimMQTT] MQTT connection/publish failed: " + ex.GetType().Name + ": " + ex.Message);
        }
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
        _stop.Cancel();
        // Never synchronously wait for broker operations on the region shutdown thread.
        _ = Task.Run(async () =>
        {
            try
            {
                if (_connectionTask != null) await _connectionTask;
                if (_client?.IsConnected == true)
                {
                    await _client.DisconnectAsync();
                    Log.Info("[OpenSimMQTT] MQTT broker disconnected.");
                }
                _client?.Dispose();
            }
            catch (Exception ex)
            {
                Log.Warn("[OpenSimMQTT] MQTT cleanup failed: " + ex.Message);
            }
            finally { _stop.Dispose(); }
        });
    }
}
