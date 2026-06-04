using System.Diagnostics;

namespace RouteIQ.Sdk;

public sealed class TaskHandle : IDisposable
{
    private readonly RouteIQClient _riq;
    private readonly Activity? _span;
    private bool _done;
    private int _stepIndex;
    private readonly List<string> _toolSequence = new();

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

    public StepHandle Step(string? action = null, string? rationale = null, string? model = null)
    {
        _stepIndex++;
        return new StepHandle(_riq, this, action, rationale, model, _stepIndex);
    }

    public void Escalate(string? reason = null, string? target = null)
    {
        using var escSpan = _riq.ActivitySource.StartActivity($"escalation:{TaskId}");
        if (escSpan == null) return;

        foreach (var (k, v) in _riq.Envelope(this))
            if (v != null) escSpan.SetTag(k, v);

        escSpan.SetTag("routeiq.event.type",              "8");
        escSpan.SetTag("routeiq.escalation.triggered",    "true");
        if (reason != null) escSpan.SetTag("routeiq.escalation.reason", reason.Length <= 256 ? reason : reason[..256]);
        if (target != null) escSpan.SetTag("routeiq.escalation.target", target);
    }

    internal void RecordTool(string name) => _toolSequence.Add(name);

    private int MaxSameToolCount()
    {
        if (_toolSequence.Count == 0) return 0;
        int maxCount = 1, cur = 1;
        for (int i = 1; i < _toolSequence.Count; i++)
        {
            cur = _toolSequence[i] == _toolSequence[i - 1] ? cur + 1 : 1;
            if (cur > maxCount) maxCount = cur;
        }
        return maxCount;
    }

    public void Complete(int tokens = 0, int tokensIn = 0, int tokensOut = 0,
                         double? costUsd = null, string? cohort = null)
        => Finish("1", tokens, tokensIn, tokensOut, costUsd, cohort);

    public void Fail(string? category = null)
        => Finish("2", 0, 0, 0, null, null, failureCategory: category);

    private void Finish(string status, int tokens = 0, int tokensIn = 0, int tokensOut = 0,
                        double? costUsd = null, string? cohort = null, string? failureCategory = null)
    {
        if (_done || _span == null) return;
        _done = true;
        _span.SetTag("routeiq.task.completion_status", status);
        if (tokensIn  > 0)  _span.SetTag("routeiq.task.tokens_in",       tokensIn);
        if (tokensOut > 0)  _span.SetTag("routeiq.task.tokens_out",      tokensOut);
        int total = tokens > 0 ? tokens : tokensIn + tokensOut;
        if (total > 0)      _span.SetTag("routeiq.task.total_tokens",    total);
        if (costUsd != null) _span.SetTag("routeiq.task.cost_usd",       costUsd);
        if (cohort != null)  _span.SetTag("routeiq.task.cohort",         cohort);
        if (failureCategory != null) _span.SetTag("routeiq.task.failure_category", failureCategory);
        int same = MaxSameToolCount();
        if (same > 1) _span.SetTag("routeiq.same_tool_count", same);
    }

    public void Dispose()
    {
        if (!_done) Complete();
        _span?.Dispose();
    }
}
