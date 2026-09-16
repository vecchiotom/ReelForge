using FluentAssertions;
using ReelForge.Shared.Data.OutputSchemas;
using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

/// <summary>
/// Guards the background-music extension of the rushcut invariant (see
/// <c>VideoEditDecisionOutputInvariantTests</c>/<c>MotionGraphicsPlanOutputInvariantTests</c>): the
/// music-supervisor agent's structured output must be structurally incapable of expressing a dB
/// value, volume, level, percentage, or timestamp/duration. The model contributes only an opaque
/// music-track id drawn from a set <c>VideoAnalyzeStepExecutor</c> actually offered it, plus
/// enum-word choices (Intensity/Ducking/Fit) that <c>VideoCompileStepExecutor</c> alone resolves to
/// concrete dB levels/ffmpeg behavior. If a future change adds e.g. a numeric VolumeDb/StartSec
/// property here, this test must fail.
/// </summary>
public class MusicPlanOutputInvariantTests
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
    public void MusicPlanOutput_has_no_numeric_or_time_bearing_property()
    {
        PropertyInfo[] offending = typeof(MusicPlanOutput)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => BannedPropertyTypes.Contains(p.PropertyType))
            .ToArray();

        offending.Should().BeEmpty(
            "MusicPlanOutput must never expose a numeric or time-bearing property — the model " +
            "must not be able to emit a dB value, a volume, a level, or a timestamp; found: " +
            string.Join(", ", offending.Select(p => $"{p.PropertyType.Name} {p.Name}")));
    }

    [Fact]
    public void MusicPlanOutput_every_property_is_a_string()
    {
        PropertyInfo[] nonString = typeof(MusicPlanOutput)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType != typeof(string))
            .ToArray();

        nonString.Should().BeEmpty(
            "every MusicPlanOutput property must be a plain string — TrackId is an opaque offered " +
            "id, and Intensity/Ducking/Fit are enum WORDS resolved entirely server-side; found: " +
            string.Join(", ", nonString.Select(p => $"{p.PropertyType.Name} {p.Name}")));
    }

    [Fact]
    public void MusicPlanOutput_round_trips_through_camelCase_JSON()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var plan = new MusicPlanOutput
        {
            TrackId = "m2",
            Intensity = "Balanced",
            Ducking = "Normal",
            Fit = "LoopToFit",
            Reason = "Calm piano bed suits the dialogue-heavy edit.",
            PlanRationale = "Picked a quiet bed to stay out of the way of narration."
        };

        string json = JsonSerializer.Serialize(plan, options);
        json.Should().Contain("\"trackId\":");
        json.Should().Contain("\"planRationale\":");

        MusicPlanOutput? roundTripped = JsonSerializer.Deserialize<MusicPlanOutput>(json, options);
        roundTripped.Should().NotBeNull();
        roundTripped!.TrackId.Should().Be(plan.TrackId);
        roundTripped.Intensity.Should().Be(plan.Intensity);
        roundTripped.Ducking.Should().Be(plan.Ducking);
        roundTripped.Fit.Should().Be(plan.Fit);
    }
}
