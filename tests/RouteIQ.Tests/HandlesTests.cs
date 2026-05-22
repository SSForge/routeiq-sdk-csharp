using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Trace;
using RouteIQ.Sdk;
using Xunit;

namespace RouteIQ.Tests;

public class HandlesTests
{
    private static (RouteIQClient riq, List<Activity> spans) MakeTestRiq()
    {
        var spans = new List<Activity>();

        var provider = OpenTelemetry.Sdk.CreateTracerProviderBuilder()
            .AddSource("routeiq.sdk")
            .AddInMemoryExporter(spans)
            .Build()!;

        var riq = new RouteIQClient("test-agent", provider);
        return (riq, spans);
    }

    private static string? Attr(Activity span, string key) =>
        span.TagObjects.FirstOrDefault(t => t.Key == key).Value?.ToString();

    // ── TaskHandle ──────────────────────────────────────────────────────────

    [Fact]
    public void Task_SpanNameStartsWithTask()
    {
        var (riq, spans) = MakeTestRiq();
        using (var _ = riq.Task("find Paris")) { }
        Assert.True(spans.Any(s => s.DisplayName.StartsWith("task:")));
    }

    [Fact]
    public void Task_EnvelopeAttributes()
    {
        var (riq, spans) = MakeTestRiq();
        string taskId;
        using (var task = riq.Task("find Paris"))
        {
            taskId = task.TaskId;
        }
        var span = spans.First(s => s.DisplayName.StartsWith("task:"));
        Assert.Equal("test-agent",    Attr(span, "routeiq.agent.id"));
        Assert.Equal(riq.SessionId,   Attr(span, "routeiq.session.id"));
        Assert.Equal(taskId,          Attr(span, "routeiq.task.id"));
        Assert.Equal("find Paris",    Attr(span, "routeiq.task.input_intent"));
        Assert.Equal("gpt-4o",        Attr(span, "routeiq.version.model.name"));
    }

    [Fact]
    public void Task_CompleteSetsSucess()
    {
        var (riq, spans) = MakeTestRiq();
        using (var task = riq.Task("q"))
        {
            task.Complete(tokens: 100, cohort: "test");
        }
        var span = spans.First(s => s.DisplayName.StartsWith("task:"));
        Assert.Equal("1",    Attr(span, "routeiq.task.completion_status"));
        Assert.Equal("100",  Attr(span, "routeiq.task.total_tokens"));
        Assert.Equal("test", Attr(span, "routeiq.task.cohort"));
    }

    [Fact]
    public void Task_FailSetsFailure()
    {
        var (riq, spans) = MakeTestRiq();
        using (var task = riq.Task("q"))
        {
            task.Fail("tool_error");
        }
        var span = spans.First(s => s.DisplayName.StartsWith("task:"));
        Assert.Equal("2",          Attr(span, "routeiq.task.completion_status"));
        Assert.Equal("tool_error", Attr(span, "routeiq.task.failure_category"));
    }

    [Fact]
    public void Task_AutoSucceedsOnDispose()
    {
        var (riq, spans) = MakeTestRiq();
        using (var _ = riq.Task("q")) { }
        var span = spans.First(s => s.DisplayName.StartsWith("task:"));
        Assert.Equal("1", Attr(span, "routeiq.task.completion_status"));
    }

    // ── StepHandle ──────────────────────────────────────────────────────────

    [Fact]
    public void Step_SpanNameStartsWithStep()
    {
        var (riq, spans) = MakeTestRiq();
        using (var task = riq.Task("q"))
        {
            using (var _ = task.Step("tool_call")) { }
        }
        Assert.True(spans.Any(s => s.DisplayName.StartsWith("step:")));
    }

    [Fact]
    public void Step_CarriesTaskId()
    {
        var (riq, spans) = MakeTestRiq();
        string taskId, stepId;
        using (var task = riq.Task("q"))
        {
            taskId = task.TaskId;
            using var step = task.Step();
            stepId = step.StepId;
        }
        var span = spans.First(s => s.DisplayName.StartsWith("step:"));
        Assert.Equal(taskId, Attr(span, "routeiq.task.id"));
        Assert.Equal(stepId, Attr(span, "routeiq.step.id"));
    }

    [Fact]
    public void Step_IndexIncrements()
    {
        var (riq, spans) = MakeTestRiq();
        using var task = riq.Task("q");
        using (var _ = task.Step()) { }
        using (var _ = task.Step()) { }
        var indices = spans
            .Where(s => s.DisplayName.StartsWith("step:"))
            .Select(s => int.Parse(Attr(s, "routeiq.step.index")!))
            .OrderBy(x => x)
            .ToList();
        Assert.Equal(new[] { 1, 2 }, indices);
    }

    // ── ToolHandle ──────────────────────────────────────────────────────────

    [Fact]
    public void Tool_SpanNameIsToolName()
    {
        var (riq, spans) = MakeTestRiq();
        using var task = riq.Task("q");
        using var step = task.Step();
        using (var _ = step.Tool("search", new() { ["query"] = "Paris" })) { }
        Assert.True(spans.Any(s => s.DisplayName == "tool:search"));
    }

    [Fact]
    public void Tool_SuccessSetsStatus1()
    {
        var (riq, spans) = MakeTestRiq();
        using var task = riq.Task("q");
        using var step = task.Step();
        using (var tool = step.Tool("search"))
        {
            tool.Success(latencyMs: 50.0);
        }
        var span = spans.First(s => s.DisplayName == "tool:search");
        Assert.Equal("1",  Attr(span, "routeiq.tool.result_status"));
        Assert.Equal("50", Attr(span, "routeiq.tool.latency_ms"));
    }

    [Fact]
    public void Tool_FailSetsStatus2()
    {
        var (riq, spans) = MakeTestRiq();
        using var task = riq.Task("q");
        using var step = task.Step();
        using (var tool = step.Tool("search"))
        {
            tool.Fail("TIMEOUT");
        }
        var span = spans.First(s => s.DisplayName == "tool:search");
        Assert.Equal("2",       Attr(span, "routeiq.tool.result_status"));
        Assert.Equal("TIMEOUT", Attr(span, "routeiq.tool.error_code"));
    }

    [Fact]
    public void Tool_AutoSucceedsOnDispose()
    {
        var (riq, spans) = MakeTestRiq();
        using var task = riq.Task("q");
        using var step = task.Step();
        using (var _ = step.Tool("search")) { }
        var span = spans.First(s => s.DisplayName == "tool:search");
        Assert.Equal("1", Attr(span, "routeiq.tool.result_status"));
    }

    [Fact]
    public void Tool_ArgsHashIs16Chars()
    {
        var (riq, spans) = MakeTestRiq();
        using var task = riq.Task("q");
        using var step = task.Step();
        using (var _ = step.Tool("search", new() { ["query"] = "Paris" })) { }
        var span = spans.First(s => s.DisplayName == "tool:search");
        Assert.Equal(16, Attr(span, "routeiq.tool.arguments_hash")!.Length);
    }

    [Fact]
    public void Tool_PermissionLevelMaps()
    {
        var (riq, spans) = MakeTestRiq();
        using var task = riq.Task("q");
        using var step = task.Step();
        using (var _ = step.Tool("write_file", permission: "READ_WRITE")) { }
        var span = spans.First(s => s.DisplayName == "tool:write_file");
        Assert.Equal("2", Attr(span, "routeiq.tool.permission_level"));
    }

    [Fact]
    public void SessionId_ConsistentAcrossSpans()
    {
        var (riq, spans) = MakeTestRiq();
        using var task = riq.Task("q");
        using var step = task.Step();
        using (var _ = step.Tool("search")) { }
        var ids = spans.Select(s => Attr(s, "routeiq.session.id")).Distinct().ToList();
        Assert.Single(ids);
        Assert.Equal(riq.SessionId, ids[0]);
    }
}
