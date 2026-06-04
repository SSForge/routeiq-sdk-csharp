using System.Diagnostics;

namespace RouteIQ.Sdk;

public sealed class StepHandle : IDisposable
{
    private readonly RouteIQClient _riq;
    private readonly TaskHandle _task;
    private readonly Activity? _span;
    private bool _done;

    public string StepId { get; } = Guid.NewGuid().ToString();

    internal StepHandle(RouteIQClient riq, TaskHandle task,
                        string? action, string? rationale, string? model, int index)
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
        if (model     != null) _span.SetTag("routeiq.step.model",            model);
    }

    public ToolHandle Tool(string name, Dictionary<string, object>? args = null, string permission = "READ_ONLY")
        => new(_riq, _task, this, name, args, permission);

    public void Guardrail(string type, bool blocked)
    {
        using var gSpan = _riq.ActivitySource.StartActivity($"guardrail:{type}");
        if (gSpan == null) return;

        foreach (var (k, v) in _riq.Envelope(_task, this))
            if (v != null) gSpan.SetTag(k, v);

        gSpan.SetTag("routeiq.event.type",       "9");
        gSpan.SetTag("routeiq.guardrail.type",    type);
        gSpan.SetTag("routeiq.guardrail.blocked", blocked.ToString().ToLower());
    }

    public void Replan(string reason)
    {
        _span?.SetTag("routeiq.replan.triggered", "true");
        _span?.SetTag("routeiq.replan.reason", reason.Length <= 256 ? reason : reason[..256]);
    }

    public void Complete(int tokensIn = 0, int tokensOut = 0) => Finish("1", null, tokensIn, tokensOut);
    public void Fail(string? category = null) => Finish("2", category, 0, 0);

    private void Finish(string status, string? category, int tokensIn, int tokensOut)
    {
        if (_done || _span == null) return;
        _done = true;
        _span.SetTag("routeiq.step.completion_status", status);
        if (category  != null) _span.SetTag("routeiq.step.failure_category", category);
        if (tokensIn  > 0)     _span.SetTag("routeiq.step.tokens_in",        tokensIn);
        if (tokensOut > 0)     _span.SetTag("routeiq.step.tokens_out",       tokensOut);
    }

    public void Dispose()
    {
        if (!_done) Complete();
        _span?.Dispose();
    }
}
