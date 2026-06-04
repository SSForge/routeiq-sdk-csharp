using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("RouteIQ.Tests")]

namespace RouteIQ.Sdk;

public sealed class RouteIQClient : IDisposable
{
    private const string SdkVersion = "0.3.0";

    public string AgentId { get; }
    public string TenantId { get; }
    public string? Model { get; }
    public string Environment { get; }
    public string AgentVersion { get; }
    public string SessionId { get; }
    public string? SystemId { get; }
    public string? UserId { get; }
    public double? SloSuccessTarget { get; }
    public double? SloP95MsTarget { get; }

    internal ActivitySource ActivitySource { get; }
    private readonly TracerProvider _provider;

    public RouteIQClient(
        string agentId,
        string otlpEndpoint = "http://localhost:4317",
        string tenantId = "default",
        string? model = null,
        string environment = "production",
        string agentVersion = "1.0.0",
        string? apiKey = null,
        string? systemId = null,
        string? userId = null,
        double? sloSuccessTarget = null,
        double? sloP95MsTarget = null)
    {
        AgentId          = agentId;
        TenantId         = tenantId;
        Model            = model;
        Environment      = environment;
        AgentVersion     = agentVersion;
        SessionId        = Guid.NewGuid().ToString();
        SystemId         = systemId;
        UserId           = userId;
        SloSuccessTarget = sloSuccessTarget;
        SloP95MsTarget   = sloP95MsTarget;
        ActivitySource   = new ActivitySource("routeiq.sdk", SdkVersion);

        _provider = OpenTelemetry.Sdk.CreateTracerProviderBuilder()
            .SetResourceBuilder(ResourceBuilder.CreateDefault()
                .AddService(agentId, serviceVersion: agentVersion)
                .AddAttributes(new[] { new KeyValuePair<string, object>("routeiq.sdk.version", SdkVersion) }))
            .AddSource("routeiq.sdk")
            .AddOtlpExporter(o =>
            {
                o.Endpoint = new Uri(otlpEndpoint);
                if (apiKey != null)
                    o.Headers = $"authorization=Bearer {apiKey}";
            })
            .Build()!;
    }

    /// For tests: inject a pre-built TracerProvider.
    internal RouteIQClient(string agentId, TracerProvider provider)
    {
        AgentId        = agentId;
        TenantId       = "test-tenant";
        Model          = "gpt-4o";
        Environment    = "test";
        AgentVersion   = "1.0.0";
        SessionId      = Guid.NewGuid().ToString();
        ActivitySource = new ActivitySource("routeiq.sdk", SdkVersion);
        _provider      = provider;
    }

    public TaskHandle Task(string intent, string? taskType = null)
        => new(this, intent, taskType);

    public void Flush() => _provider.ForceFlush();

    public void Dispose() => _provider.Dispose();

    internal Dictionary<string, object?> Envelope(TaskHandle? task = null, StepHandle? step = null)
    {
        var attrs = new Dictionary<string, object?>
        {
            ["routeiq.agent.id"]    = AgentId,
            ["routeiq.tenant.id"]   = TenantId,
            ["routeiq.environment"] = Environment,
            ["routeiq.session.id"]  = SessionId,
        };
        if (SystemId         != null) attrs["routeiq.system.id"]          = SystemId;
        if (UserId           != null) attrs["routeiq.user.id"]             = UserId;
        if (SloSuccessTarget != null) attrs["routeiq.slo.success_target"]  = SloSuccessTarget;
        if (SloP95MsTarget   != null) attrs["routeiq.slo.p95_ms_target"]   = SloP95MsTarget;
        if (task != null)
        {
            attrs["routeiq.task.id"] = task.TaskId;
            attrs["routeiq.run.id"]  = task.RunId;
        }
        if (step != null) attrs["routeiq.step.id"] = step.StepId;
        if (Model != null) attrs["routeiq.version.model.name"] = Model;
        attrs["routeiq.version.agent"] = AgentVersion;
        return attrs;
    }
}
