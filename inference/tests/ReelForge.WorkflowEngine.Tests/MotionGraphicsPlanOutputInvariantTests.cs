using FluentAssertions;
using ReelForge.Shared.Data.OutputSchemas;
using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

/// <summary>
/// Guards the Phase 3 extension of the rushcut invariant (see
/// <c>VideoEditDecisionOutputInvariantTests</c>): the motion-graphics planner agent's structured
/// output must be structurally incapable of expressing a timestamp, duration, frame number, or
/// pixel coordinate. The model contributes only an opaque placement id drawn from a set
/// <c>VideoAnalyzeStepExecutor</c> actually offered it, plus enum-word choices (Duration/
/// Emphasis) that <c>VideoCompileStepExecutor</c> alone resolves to concrete values. If a future
/// change adds e.g. a numeric X/Y/DurationMs/TimeSpan/DateTime property here, this test must fail.
/// </summary>
public class MotionGraphicsPlanOutputInvariantTests
{
    private static readonly Type[] BannedPropertyTypes =
    [
        typeof(int), typeof(int?),
        typeof(long), typeof(long?),
        typeof(float), typeof(float?),
        typeof(double), typeof(double?),
        typeof(decimal), typeof(decimal?),
        typeof(TimeSpan), typeof(TimeSpan?),
        typeof(DateTime), typeof(DateTime?),
        typeof(DateTimeOffset), typeof(DateTimeOffset?)
    ];

    [Fact]
    public void MotionGraphicsPlanOutput_has_no_numeric_or_time_bearing_property()
    {
        AssertNoBannedProperties(typeof(MotionGraphicsPlanOutput));
    }

    [Fact]
    public void MotionGraphicsOverlay_has_no_numeric_or_time_bearing_property()
    {
        AssertNoBannedProperties(typeof(MotionGraphicsOverlay));
    }

    private static void AssertNoBannedProperties(Type type)
    {
        PropertyInfo[] offending = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => BannedPropertyTypes.Contains(p.PropertyType))
            .ToArray();

        offending.Should().BeEmpty(
            $"{type.Name} must never expose a numeric or time-bearing property — " +
            "the model must not be able to emit a timestamp or coordinate; found: " +
            string.Join(", ", offending.Select(p => $"{p.PropertyType.Name} {p.Name}")));
    }
}
