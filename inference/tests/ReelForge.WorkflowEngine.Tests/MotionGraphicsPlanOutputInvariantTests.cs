using FluentAssertions;
using ReelForge.Shared.Data.OutputSchemas;
using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
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

    /// <summary>
    /// Tracked screen inserts (docs/video-editing.md "Tracked screen inserts (Phase 5)") are the
    /// single easiest place in the codebase to accidentally violate the rushcut invariant: a
    /// motion-tracking transform is inherently per-frame numeric data (positions, corners, times),
    /// and every one of those numbers must come from deterministic C# (<c>ChromaQuadTracker</c> /
    /// <c>VideoCompileStepExecutor</c>) — never the model. If a future change adds e.g. a corner
    /// coordinate, scale, or per-frame value "to give the model more control", this test fails
    /// the build.
    /// </summary>
    [Fact]
    public void ScreenInsert_has_no_numeric_or_time_bearing_property()
    {
        AssertNoBannedProperties(typeof(ScreenInsert));
    }

    [Fact]
    public void ScreenInsert_properties_are_exactly_the_opaque_id_the_rendered_asset_key_and_prose()
    {
        // Stronger than the banned-type check: the model's whole contribution to a tracked
        // composite is one offered region id + one self-rendered asset key + a prose reason.
        // Any NEW property here — even a string one like "corner hints" — should be a conscious,
        // reviewed decision, not a quiet addition.
        typeof(ScreenInsert)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .Should().BeEquivalentTo(
                nameof(ScreenInsert.RegionId),
                nameof(ScreenInsert.RenderedAssetStorageKey),
                nameof(ScreenInsert.Reason));
    }

    [Fact]
    public void MotionGraphicsPlanOutput_Inserts_defaults_to_an_empty_list_and_round_trips_camelCase_JSON()
    {
        new MotionGraphicsPlanOutput().Inserts.Should().BeEmpty(
            "a plan produced before screen inserts existed must deserialize with zero inserts, not null");

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var plan = new MotionGraphicsPlanOutput
        {
            Inserts = { new ScreenInsert { RegionId = "r0", RenderedAssetStorageKey = "projects/x/outputFiles/y/screen.mp4", Reason = "demo" } }
        };

        string json = JsonSerializer.Serialize(plan, options);
        json.Should().Contain("\"inserts\":");
        json.Should().Contain("\"regionId\":");

        MotionGraphicsPlanOutput? roundTripped = JsonSerializer.Deserialize<MotionGraphicsPlanOutput>(json, options);
        roundTripped!.Inserts.Should().HaveCount(1);
        roundTripped.Inserts[0].RegionId.Should().Be("r0");
    }

    /// <summary>
    /// The rendered-asset overlay path (docs/video-editing.md "Motion graphics (Phase 3)") added
    /// <see cref="MotionGraphicsOverlay.RenderedAssetStorageKey"/> as a plain <c>string</c> — this
    /// does NOT trip the reflection guard above (a string can never carry a timestamp/coordinate
    /// on its own; only the numeric/time-bearing CLR types in <see cref="BannedPropertyTypes"/>
    /// do), but this test locks in the exact property name/type/default so a future rename is a
    /// visible test failure rather than a silent JSON-shape break for
    /// <c>VideoCompileStepExecutor.ResolveGraphicsAsync</c>, which reads this field by name.
    /// </summary>
    [Fact]
    public void MotionGraphicsOverlay_RenderedAssetStorageKey_is_a_string_that_defaults_to_empty()
    {
        PropertyInfo? property = typeof(MotionGraphicsOverlay).GetProperty(nameof(MotionGraphicsOverlay.RenderedAssetStorageKey));

        property.Should().NotBeNull();
        property!.PropertyType.Should().Be(typeof(string));
        new MotionGraphicsOverlay().RenderedAssetStorageKey.Should().BeEmpty(
            "an overlay with no rendered asset must default to the pre-existing plain-text behavior");
    }

    [Fact]
    public void MotionGraphicsOverlay_round_trips_RenderedAssetStorageKey_through_camelCase_JSON()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var overlay = new MotionGraphicsOverlay
        {
            PlacementId = "p0",
            Kind = "Title",
            RenderedAssetStorageKey = "projects/11111111-1111-1111-1111-111111111111/outputFiles/22222222-2222-2222-2222-222222222222/overlay.webm"
        };

        string json = JsonSerializer.Serialize(overlay, options);
        json.Should().Contain("\"renderedAssetStorageKey\":");

        MotionGraphicsOverlay? roundTripped = JsonSerializer.Deserialize<MotionGraphicsOverlay>(json, options);
        roundTripped.Should().NotBeNull();
        roundTripped!.RenderedAssetStorageKey.Should().Be(overlay.RenderedAssetStorageKey);
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
