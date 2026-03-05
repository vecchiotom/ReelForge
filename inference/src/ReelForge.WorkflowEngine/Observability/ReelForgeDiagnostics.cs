using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace ReelForge.WorkflowEngine.Observability;

/// <summary>
/// OpenTelemetry instrumentation for the Workflow Engine.
/// </summary>
public static class ReelForgeDiagnostics
{
    public const string ServiceName = "ReelForge.WorkflowEngine";

    public static readonly ActivitySource ActivitySource = new(ServiceName);

    private static readonly Meter Meter = new(ServiceName);

    public static readonly UpDownCounter<long> ActiveWorkflows =
        Meter.CreateUpDownCounter<long>("reelforge.workflows.active", "workflows", "Number of currently executing workflows");

    public static readonly Counter<long> CompletedWorkflows =
        Meter.CreateCounter<long>("reelforge.workflows.completed", "workflows", "Total completed workflows");

    public static readonly Histogram<long> StepDuration =
        Meter.CreateHistogram<long>("reelforge.step.duration_ms", "ms", "Duration of individual step executions");
}
