using FluentAssertions;
using ReelForge.Shared.Data.OutputSchemas;
using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

/// <summary>
/// Guards the colour-grading extension of the rushcut invariant (see
/// <c>VideoEditDecisionOutputInvariantTests</c>/<c>MotionGraphicsPlanOutputInvariantTests</c>/
/// <c>MusicPlanOutputInvariantTests</c>): the colorist agents' structured output must be
/// structurally incapable of expressing an RGB value, a curve/level number, a
/// gamma/gain/contrast/saturation value, a percentage, or a timestamp. The model contributes only
/// enum WORDS (a named look plus strength/shadow/highlight words) that
/// <c>VideoCompileStepExecutor</c>/<c>ColorGradeFilterBuilder</c> alone resolve to concrete
/// ffmpeg filter parameters from first-party tables. If a future change adds e.g. a numeric
/// Gamma/SaturationPct property here, this test must fail.
/// </summary>
public class ColorGradePlanOutputInvariantTests
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
    public void ColorGradePlanOutput_has_no_numeric_or_time_bearing_property()
    {
        PropertyInfo[] offending = typeof(ColorGradePlanOutput)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => BannedPropertyTypes.Contains(p.PropertyType))
            .ToArray();

        offending.Should().BeEmpty(
            "ColorGradePlanOutput must never expose a numeric or time-bearing property — the model " +
            "must not be able to emit an RGB/level/gamma/gain value, a percentage, or a timestamp; found: " +
            string.Join(", ", offending.Select(p => $"{p.PropertyType.Name} {p.Name}")));
    }

    [Fact]
    public void ColorGradePlanOutput_every_property_is_a_string()
    {
        PropertyInfo[] nonString = typeof(ColorGradePlanOutput)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType != typeof(string))
            .ToArray();

        nonString.Should().BeEmpty(
            "every ColorGradePlanOutput property must be a plain string — Look/Strength/ShadowTone/" +
            "HighlightTone are enum WORDS resolved entirely server-side; found: " +
            string.Join(", ", nonString.Select(p => $"{p.PropertyType.Name} {p.Name}")));
    }

    [Fact]
    public void ColorGradePlanOutput_shape_is_pinned_so_it_never_silently_gains_an_id_or_list_property()
    {
        // ColorGradeRoomStepExecutor.FilterToOfferedIds is a deliberate structural no-op on the
        // documented ground that this type carries no ids and no collections at all. Pin the
        // exact property set so a future addition (say, a per-shot grade list) fails HERE and
        // forces that executor decision to be revisited, rather than silently shipping an
        // unfiltered id-bearing decision.
        string[] names = typeof(ColorGradePlanOutput)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToArray();

        names.Should().BeEquivalentTo(
            ["Look", "Strength", "ShadowTone", "HighlightTone", "Reason", "PlanRationale"]);
    }

    [Fact]
    public void ColorGradePlanOutput_round_trips_through_camelCase_JSON()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var plan = new ColorGradePlanOutput
        {
            Look = "Warm",
            Strength = "Subtle",
            ShadowTone = "Lifted",
            HighlightTone = "Neutral",
            Reason = "The measured temperature words lean cool on most shots; a subtle warm lift suits the interview.",
            PlanRationale = "One restrained warm treatment keeps every kept shot consistent."
        };

        string json = JsonSerializer.Serialize(plan, options);
        json.Should().Contain("\"look\":");
        json.Should().Contain("\"shadowTone\":");
        json.Should().Contain("\"planRationale\":");

        ColorGradePlanOutput? roundTripped = JsonSerializer.Deserialize<ColorGradePlanOutput>(json, options);
        roundTripped.Should().NotBeNull();
        roundTripped!.Look.Should().Be(plan.Look);
        roundTripped.Strength.Should().Be(plan.Strength);
        roundTripped.ShadowTone.Should().Be(plan.ShadowTone);
        roundTripped.HighlightTone.Should().Be(plan.HighlightTone);
    }
}
