using System.Diagnostics;

namespace RouteIQ.Sdk;

public sealed class TaskHandle : IDisposable
{
    private readonly RouteIQClient _riq;
    private readonly Activity? _span;
    private bool _done;
    private int _stepIndex;

    public string TaskId { get; } = Guid.NewGuid().ToString();
    public string RunId  { get; } = Guid.NewGuid().ToString();

    internal TaskHandle(RouteIQClient riq, string intent, string? taskType)
    {
        _riq  = riq;
        _span = riq.ActivitySource.StartActivity($"task:{TaskId}");
        if (_span == null) return;

        foreach (var (k, v) in riq.Envelope(this))
            if (v != null) _span.SetTag(k, v);

        _span.SetTag("routeiq.event.type",        "1");
        _span.SetTag("routeiq.task.input_intent",  intent.Length <= 256 ? intent : intent[..256]);
        if (taskType != null) _span.SetTag("routeiq.task.type", taskType);
    }

    public StepHandle Step(string? action = null, string? rationale = null)
    {
        _stepIndex++;
        return new StepHandle(_riq, this, action, rationale, _stepIndex);
    }

    public void Complete(int tokens = 0, double? costUsd = null, string? cohort = null)
        => Finish("1", tokens, costUsd, cohort);

    public void Fail(string? category = null)
        => Finish("2", failureCategory: category);

    private void Finish(string status, int tokens = 0, double? costUsd = null,
                        string? cohort = null, string? failureCategory = null)
    {
        if (_done || _span == null) return;
        _done = true;
        _span.SetTag("routeiq.task.completion_status", status);
        if (tokens > 0)      _span.SetTag("routeiq.task.total_tokens",     tokens);
        if (costUsd != null)  _span.SetTag("routeiq.task.cost_usd",         costUsd);
        if (cohort != null)   _span.SetTag("routeiq.task.cohort",           cohort);
        if (failureCategory != null) _span.SetTag("routeiq.task.failure_category", failureCategory);
    }

    public void Dispose()
    {
        if (!_done) Complete();
        _span?.Dispose();
    }
}
