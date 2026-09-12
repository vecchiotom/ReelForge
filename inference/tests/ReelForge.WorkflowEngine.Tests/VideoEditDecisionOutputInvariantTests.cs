using FluentAssertions;
using ReelForge.Shared.Data.OutputSchemas;
using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

/// <summary>
/// Guards the single most important invariant in the video-editing feature (plan §4.2/§4.3):
/// the story-editor agent's structured output must be structurally incapable of expressing a
/// timestamp, duration, or frame number. The model contributes only opaque ids drawn from a set
/// VideoAnalyzeStepExecutor actually offered it; VideoCompileStepExecutor alone resolves those
/// ids to frame-accurate times from the full analysis artifact. If a future change adds e.g.
/// a numeric StartSec/DurationMs/TimeSpan/DateTime property here, this test must fail.
/// </summary>
public class VideoEditDecisionOutputInvariantTests
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
    public void VideoEditDecisionOutput_has_no_numeric_or_time_bearing_property()
    {
        AssertNoBannedProperties(typeof(VideoEditDecisionOutput));
    }

    [Fact]
    public void VideoEditKeepSpan_has_no_numeric_or_time_bearing_property()
    {
        AssertNoBannedProperties(typeof(VideoEditKeepSpan));
    }

    private static void AssertNoBannedProperties(Type type)
    {
        PropertyInfo[] offending = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => BannedPropertyTypes.Contains(p.PropertyType))
            .ToArray();

        offending.Should().BeEmpty(
            $"{type.Name} must never expose a numeric or time-bearing property — " +
            "the model must not be able to emit a timestamp; found: " +
            string.Join(", ", offending.Select(p => $"{p.PropertyType.Name} {p.Name}")));
    }
}
