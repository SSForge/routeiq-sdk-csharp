using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RouteIQ.Sdk;

public sealed class ToolHandle : IDisposable
{
    private static readonly Dictionary<string, string> PermissionMap = new()
    {
        ["READ_ONLY"]  = "1",
        ["READ_WRITE"] = "2",
        ["PRIVILEGED"] = "3",
    };

    private readonly Activity? _span;
    private readonly long _startTick = Stopwatch.GetTimestamp();
    private bool _done;

    internal ToolHandle(
        RouteIQClient riq,
        TaskHandle task,
        StepHandle step,
        string name,
        Dictionary<string, object>? args,
        string permission)
    {
        _span = riq.ActivitySource.StartActivity($"tool:{name}");
        if (_span == null) return;

        var argsJson  = JsonSerializer.Serialize(args ?? new Dictionary<string, object>());
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(argsJson));
        var argsHash  = Convert.ToHexString(hashBytes)[..16].ToLower();
        var perm      = PermissionMap.GetValueOrDefault(permission, "1");

        foreach (var (k, v) in riq.Envelope(task, step))
            if (v != null) _span.SetTag(k, v);

        _span.SetTag("routeiq.event.type",            "7");
        _span.SetTag("routeiq.tool.name",              name);
        _span.SetTag("routeiq.tool.arguments_hash",    argsHash);
        _span.SetTag("routeiq.tool.permission_level",  perm);
    }

    public void Success(double? latencyMs = null) => Finish("1", null, latencyMs);
    public void Fail(string? errorCode = null, double? latencyMs = null) => Finish("2", errorCode, latencyMs);

    private void Finish(string status, string? errorCode, double? latencyMs)
    {
        if (_done || _span == null) return;
        _done = true;
        var elapsed = (Stopwatch.GetTimestamp() - _startTick) * 1000.0 / Stopwatch.Frequency;
        _span.SetTag("routeiq.tool.result_status", status);
        _span.SetTag("routeiq.tool.latency_ms",    latencyMs ?? elapsed);
        if (errorCode != null) _span.SetTag("routeiq.tool.error_code", errorCode);
    }

    public void Dispose()
    {
        if (!_done) Success();
        _span?.Dispose();
    }
}
