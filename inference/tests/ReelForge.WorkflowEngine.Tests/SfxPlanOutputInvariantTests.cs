using FluentAssertions;
using ReelForge.Shared.Data.OutputSchemas;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

/// <summary>
/// Guards the sound-effects extension of the rushcut invariant (see
/// <c>VideoEditDecisionOutputInvariantTests</c>/<c>MotionGraphicsPlanOutputInvariantTests</c>/
/// <c>MusicPlanOutputInvariantTests</c>/<c>ColorGradePlanOutputInvariantTests</c>): the
/// sound-designer agent's structured output must be structurally incapable of expressing a
/// timestamp, a millisecond offset, a dB value, or a duration. The model contributes only two
/// opaque offered ids per cue (an <c>x{n}</c> SFX-clip id and an <c>s{n}</c>/<c>g{n}</c>/<c>t{n}</c>
/// cut-anchor id) plus enum-word Timing/Volume choices that <c>VideoCompileStepExecutor</c> alone
/// resolves to output-timeline seconds and gains. If a future change adds e.g. a numeric
/// OffsetMs/GainDb property here, this test must fail.
/// </summary>
public class SfxPlanOutputInvariantTests
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

    [Theory]
    [InlineData(typeof(SfxPlanOutput))]
    [InlineData(typeof(SfxCue))]
    public void No_numeric_or_time_bearing_property_anywhere_in_the_schema(Type type)
    {
        PropertyInfo[] offending = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => BannedPropertyTypes.Contains(p.PropertyType))
            .ToArray();

        offending.Should().BeEmpty(
            $"{type.Name} must never expose a numeric or time-bearing property — the model must " +
            "not be able to emit a timestamp, an offset, a dB value, or a duration; found: " +
            string.Join(", ", offending.Select(p => $"{p.PropertyType.Name} {p.Name}")));
    }

    [Fact]
    public void SfxCue_every_property_is_a_string()
    {
        PropertyInfo[] nonString = typeof(SfxCue)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType != typeof(string))
            .ToArray();

        nonString.Should().BeEmpty(
            "every SfxCue property must be a plain string — SfxId/AnchorId are opaque offered ids " +
            "and Timing/Volume are enum WORDS resolved entirely server-side; found: " +
            string.Join(", ", nonString.Select(p => $"{p.PropertyType.Name} {p.Name}")));
    }

    [Fact]
    public void Shapes_are_pinned_so_they_never_silently_gain_a_property()
    {
        // ResolveSfxAsync validates exactly SfxId (against OfferedSfxIds) and AnchorId (against
        // OfferedIds) — pin both property sets so a future id-bearing or numeric addition fails
        // HERE and forces that validation to be revisited, rather than silently shipping an
        // unvalidated field (the same reasoning as ColorGradePlanOutput's own shape pin).
        typeof(SfxPlanOutput)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .Should().BeEquivalentTo(["Cues", "PlanRationale"]);

        typeof(SfxCue)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .Should().BeEquivalentTo(["SfxId", "AnchorId", "Timing", "Volume", "Reason"]);
    }

    [Fact]
    public void SfxPlanOutput_round_trips_through_camelCase_JSON()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var plan = new SfxPlanOutput
        {
            Cues =
            [
                new SfxCue
                {
                    SfxId = "x0",
                    AnchorId = "s2",
                    Timing = "OnCut",
                    Volume = "Normal",
                    Reason = "A whoosh marks the section change into the demo."
                }
            ],
            PlanRationale = "One restrained cue at the section change; the rest of the edit needs none."
        };

        string json = JsonSerializer.Serialize(plan, options);
        json.Should().Contain("\"cues\":");
        json.Should().Contain("\"sfxId\":");
        json.Should().Contain("\"anchorId\":");
        json.Should().Contain("\"planRationale\":");

        SfxPlanOutput? roundTripped = JsonSerializer.Deserialize<SfxPlanOutput>(json, options);
        roundTripped.Should().NotBeNull();
        roundTripped!.Cues.Should().HaveCount(1);
        roundTripped.Cues[0].SfxId.Should().Be("x0");
        roundTripped.Cues[0].AnchorId.Should().Be("s2");
        roundTripped.Cues[0].Timing.Should().Be("OnCut");
        roundTripped.Cues[0].Volume.Should().Be("Normal");
    }

    [Fact]
    public void Empty_cue_list_deserializes_as_a_valid_plan()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        SfxPlanOutput? plan = JsonSerializer.Deserialize<SfxPlanOutput>(
            "{\"cues\":[],\"planRationale\":\"No effect suits this edit.\"}", options);

        plan.Should().NotBeNull();
        plan!.Cues.Should().BeEmpty("an empty cue list is a fully valid outcome, never an error");
    }
}
