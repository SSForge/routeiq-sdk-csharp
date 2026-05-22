using System.Diagnostics;

namespace RouteIQ.Sdk;

public sealed class StepHandle : IDisposable
{
    private readonly RouteIQClient _riq;
    private readonly TaskHandle _task;
    private readonly Activity? _span;
    private bool _done;

    public string StepId { get; } = Guid.NewGuid().ToString();

    internal StepHandle(RouteIQClient riq, TaskHandle task, string? action, string? rationale, int index)
    {
        _riq  = riq;
        _task = task;
        _span = riq.ActivitySource.StartActivity($"step:{StepId}");
        if (_span == null) return;

        foreach (var (k, v) in riq.Envelope(task, this))
            if (v != null) _span.SetTag(k, v);

        _span.SetTag("routeiq.event.type",  "4");
        _span.SetTag("routeiq.step.index",  index);
        if (action    != null) _span.SetTag("routeiq.step.selected_action",  action);
        if (rationale != null) _span.SetTag("routeiq.step.action_rationale", rationale);
    }

    public ToolHandle Tool(string name, Dictionary<string, object>? args = null, string permission = "READ_ONLY")
        => new(_riq, _task, this, name, args, permission);

    public void Complete() => Finish("1");
    public void Fail(string? category = null) => Finish("2", category);

    private void Finish(string status, string? category = null)
    {
        if (_done || _span == null) return;
        _done = true;
        _span.SetTag("routeiq.step.completion_status", status);
        if (category != null) _span.SetTag("routeiq.step.failure_category", category);
    }

    public void Dispose()
    {
        if (!_done) Complete();
        _span?.Dispose();
    }
}
