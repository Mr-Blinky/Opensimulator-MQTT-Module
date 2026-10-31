using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Threading.Channels;
using log4net;
using Nini.Config;
using Mono.Addins;
using OpenMetaverse;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;

namespace OpenSim.MQTT;

/// <summary>
/// M1 connectivity plus an M2 restricted script publishing bridge.
/// </summary>
[Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "OpenSimMQTTDiagnostic")]
public sealed class MqttDiagnosticModule : INonSharedRegionModule
{
    private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod()!.DeclaringType);
    private readonly CancellationTokenSource _stop = new();
    private IMqttClient? _client;
    private IScriptModuleComms? _scriptComms;
    private Task? _connectionTask;
    private Task? _publishTask;
    private readonly Channel<PublishRequest> _publishQueue = Channel.CreateBounded<PublishRequest>(
        new BoundedChannelOptions(32) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true, SingleWriter = false });
    private sealed record PublishRequest(UUID ScriptId, string RequestId, string Topic, string Payload);
    private bool _enabled;
    private string _host = "";
    private int _port;
    private string _username = "";
    private string _password = "";
    private int _retrySeconds = 5;

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
        _password = config?.GetString("Password", "") ?? "";
        _retrySeconds = config?.GetInt("ReconnectSeconds", 5) ?? 5;
        if (config?.GetBoolean("UseTLS", false) == true)
        {
            Log.Error("[OpenSimMQTT] M1 test build does not support TLS yet; connection disabled.");
            _enabled = false;
        }
        else if (string.IsNullOrWhiteSpace(_host) || _port < 1 || _port > 65535 || _retrySeconds < 1 || _retrySeconds > 3600)
        {
            Log.Error("[OpenSimMQTT] Invalid broker host, port or retry interval; connection disabled.");
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
        _scriptComms = scene.RequestModuleInterface<IScriptModuleComms>();
        if (_scriptComms == null)
            Log.Warn("[OpenSimMQTT] ScriptModuleComms unavailable; diagnostic script ping disabled.");
        else
        {
            _scriptComms.OnScriptCommand += OnScriptCommand;
            Log.Info("[OpenSimMQTT] Diagnostic script command handler registered.");
        }
        // Start without waiting on network I/O on the simulator's region thread.
        _connectionTask ??= Task.Run(() => ConnectAndPublishAsync(_stop.Token));
        _publishTask ??= Task.Run(() => ProcessPublishQueueAsync(_stop.Token));
    }

    private async Task ConnectAndPublishAsync(CancellationToken token)
    {
        if (!string.IsNullOrEmpty(_username) && string.IsNullOrEmpty(_password))
        {
            Log.Error("[OpenSimMQTT] Username is set but broker password is missing; not connecting.");
            return;
        }

        try
        {
            _client = new MqttFactory().CreateMqttClient();
            var builder = new MqttClientOptionsBuilder()
                .WithTcpServer(_host, _port)
                .WithClientId("opensim-mqtt-" + Guid.NewGuid().ToString("N"))
                .WithProtocolVersion(MQTTnet.Formatter.MqttProtocolVersion.V311)
                .WithCleanSession();
            if (!string.IsNullOrEmpty(_username))
                builder.WithCredentials(_username, _password);

            var options = builder.Build();
            var message = new MqttApplicationMessageBuilder()
                .WithTopic("opensim/diagnostic/connection")
                .WithPayload("online")
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce)
                .Build();

            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (!_client.IsConnected)
                    {
                        Log.Info($"[OpenSimMQTT] Connecting to MQTT broker {_host}:{_port}");
                        await _client.ConnectAsync(options, token);
                        Log.Info("[OpenSimMQTT] MQTT broker connected.");
                        await _client.PublishAsync(message, token);
                        Log.Info("[OpenSimMQTT] Published diagnostic message to opensim/diagnostic/connection");
                    }

                    // Polling also detects disconnections after the initial successful publish.
                    await Task.Delay(TimeSpan.FromSeconds(1), token);
                    if (!_client.IsConnected && !token.IsCancellationRequested)
                        Log.Warn("[OpenSimMQTT] MQTT connection lost; reconnecting.");
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log.Warn("[OpenSimMQTT] MQTT connection/publish failed: " +
                        ex.GetType().Name + ": " + ex.Message +
                        $"; retrying in {_retrySeconds} seconds.");
                    await Task.Delay(TimeSpan.FromSeconds(_retrySeconds), token);
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Expected when shutdown cancels the retry delay.
        }
        catch (Exception ex)
        {
            Log.Error("[OpenSimMQTT] MQTT worker stopped unexpectedly: " + ex);
        }
    }

    // Script-originated publishing is restricted to a small diagnostic namespace.
    private void OnScriptCommand(UUID scriptId, string requestId, string module, string command, string argument)
    {
        if (!string.Equals(module, "OpenSimMQTT", StringComparison.OrdinalIgnoreCase)) return;
        if (string.Equals(command, "ping", StringComparison.OrdinalIgnoreCase))
        {
            Log.Info("[OpenSimMQTT] Script ping received; sending targeted reply.");
            Reply(scriptId, 71001, "OpenSimMQTT:pong", requestId);
            return;
        }
        if (!string.Equals(command, "publish", StringComparison.OrdinalIgnoreCase)) return;

        int separator = argument?.IndexOf('\n') ?? -1;
        if (separator < 1)
        {
            Reply(scriptId, 71003, "Invalid publish argument", requestId);
            return;
        }
        string topic = argument!.Substring(0, separator);
        string payload = argument.Substring(separator + 1);
        if (!topic.StartsWith("shared/demo/", StringComparison.Ordinal) ||
            topic.Length <= "shared/demo/".Length || topic.IndexOfAny(new[] { '#', '+', '\0' }) >= 0 ||
            topic.Contains("//", StringComparison.Ordinal) || Encoding.UTF8.GetByteCount(topic) > 256 ||
            Encoding.UTF8.GetByteCount(payload) > 1024)
        {
            Reply(scriptId, 71003, "Topic or payload rejected", requestId);
            return;
        }
        if (_client?.IsConnected != true)
        {
            Reply(scriptId, 71003, "Broker disconnected", requestId);
            return;
        }
        if (!_publishQueue.Writer.TryWrite(new PublishRequest(scriptId, requestId, topic, payload)))
        {
            Reply(scriptId, 71003, "Publish queue full", requestId);
        }
    }

    private async Task ProcessPublishQueueAsync(CancellationToken token)
    {
        try
        {
            await foreach (var request in _publishQueue.Reader.ReadAllAsync(token))
            {
                try
                {
                    if (_client?.IsConnected != true)
                        throw new InvalidOperationException("Broker disconnected");
                    var message = new MqttApplicationMessageBuilder()
                        .WithTopic(request.Topic)
                        .WithPayload(request.Payload)
                        .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce)
                        .WithRetainFlag(false)
                        .Build();
                    await _client.PublishAsync(message, token);
                    Reply(request.ScriptId, 71002, "OpenSimMQTT:published", request.RequestId);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    Log.Warn("[OpenSimMQTT] Script publish failed: " + ex.Message);
                    Reply(request.ScriptId, 71003, "Publish failed", request.RequestId);
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private void Reply(UUID scriptId, int code, string message, string requestId)
    {
        try { _scriptComms?.DispatchReply(scriptId, code, message, requestId); }
        catch (Exception ex) { Log.Warn("[OpenSimMQTT] Script reply failed: " + ex.Message); }
    }

    public void RemoveRegion(Scene scene)
    {
        if (!_enabled) return;
        Log.Info($"[OpenSimMQTT] RemoveRegion: {scene.RegionInfo.RegionName}");
        if (_scriptComms != null)
        {
            _scriptComms.OnScriptCommand -= OnScriptCommand;
            _scriptComms = null;
        }
    }

    public void Close()
    {
        if (!_enabled) return;
        Log.Info("[OpenSimMQTT] Close");
        _stop.Cancel();
        _publishQueue.Writer.TryComplete();
        // Never synchronously wait for broker operations on the region shutdown thread.
        _ = Task.Run(async () =>
        {
            try
            {
                if (_connectionTask != null) await _connectionTask;
                if (_publishTask != null) await _publishTask;
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
