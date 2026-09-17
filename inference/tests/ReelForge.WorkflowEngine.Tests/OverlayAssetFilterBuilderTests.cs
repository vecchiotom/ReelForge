using System;
using System.Linq;
using FluentAssertions;
using ReelForge.Shared.Workflows;
using ReelForge.WorkflowEngine.Services.Video;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

/// <summary>
/// <see cref="OverlayAssetFilterBuilder"/> — the filter-string construction for Phase 3's
/// rendered-asset motion-graphics overlays (see docs/video-editing.md "Motion graphics (Phase
/// 3)"), the parallel path to <see cref="DrawtextFilterBuilder"/> for an overlay that carries a
/// <c>RenderedAssetLocalPath</c> instead of plain text. Covers input-index mapping, geometry
/// shared with the drawtext path via <see cref="DrawtextFilterBuilder.ComputeAccentBoxPixels"/>,
/// culture-invariant number formatting, the scale+setpts time-shift, and label chaining.
/// </summary>
public class OverlayAssetFilterBuilderTests
{
    private static ResolvedOverlay AssetOverlay(
        string placementId = "p0",
        double startSec = 4.0,
        double endSec = 6.0,
        string localPath = "/scratch/gfx-asset-abc.webm") => new(
            placementId, "Title", SanitizedText: "", SanitizedSubtext: "", DurationMs: 1500, Emphasis: "Normal",
            OutputStartSec: startSec, OutputEndSec: endSec,
            Rect: new VideoAnalysisRect(0.1, 0.8, 0.6, 0.15), TextColor: "Light",
            RenderedAssetLocalPath: localPath);

    [Fact]
    public void Overlay_is_flagged_as_an_asset_overlay_when_a_local_path_is_set()
    {
        AssetOverlay().IsAssetOverlay.Should().BeTrue();
    }

    [Fact]
    public void Chain_starts_from_the_base_label_and_ends_at_vout()
    {
        string chain = OverlayAssetFilterBuilder.BuildFilterChain(
            "[vcut]", new[] { AssetOverlay() }, probedWidth: 1920, probedHeight: 1080,
            inputIndexForIndex: i => i + 1);

        chain.Should().StartWith("[vcut]");
        chain.TrimEnd().Should().EndWith("[vout]");
    }

    [Fact]
    public void Chain_ends_at_a_custom_final_label_when_supplied()
    {
        string chain = OverlayAssetFilterBuilder.BuildFilterChain(
            "[vtxt]", new[] { AssetOverlay() }, probedWidth: 1920, probedHeight: 1080,
            inputIndexForIndex: i => i + 1, finalLabel: "[vout]");

        chain.Should().StartWith("[vtxt]");
        chain.TrimEnd().Should().EndWith("[vout]");
    }

    [Fact]
    public void Overlay_filter_references_the_input_index_the_caller_supplied()
    {
        // Simulates the real VideoCompileStepExecutor wiring: input 0 is the main source video,
        // so overlay index 0 must reference ffmpeg input 1.
        string chain = OverlayAssetFilterBuilder.BuildFilterChain(
            "[vcut]", new[] { AssetOverlay() }, probedWidth: 1920, probedHeight: 1080,
            inputIndexForIndex: i => i + 1);

        chain.Should().Contain("[1:v]scale=");
    }

    [Fact]
    public void Second_overlay_uses_the_second_supplied_input_index()
    {
        string chain = OverlayAssetFilterBuilder.BuildFilterChain(
            "[vcut]", new[] { AssetOverlay(placementId: "p0"), AssetOverlay(placementId: "p1") },
            probedWidth: 1920, probedHeight: 1080,
            inputIndexForIndex: i => i + 1);

        chain.Should().Contain("[1:v]scale=");
        chain.Should().Contain("[2:v]scale=");
    }

    [Fact]
    public void Box_geometry_matches_DrawtextFilterBuilders_own_computation_for_the_same_rect()
    {
        // Rect (0.1, 0.8, 0.6, 0.15) at 1920x1080 is the band (x=192, y=864, w=1152, h=162); both
        // builders shrink it to the same compact ACCENT box via
        // DrawtextFilterBuilder.ComputeAccentBoxPixels (never the raw band — see that method's doc
        // comment), so a rendered asset lands in the exact same visual slot a plain-text overlay
        // would have: h clamps to the already-compact band (162, target 173 exceeds it), w = round(1152*0.82) = 945,
        // x = 192 + (1152-945)/2 = 295, y unchanged since h is unchanged.
        string chain = OverlayAssetFilterBuilder.BuildFilterChain(
            "[vcut]", new[] { AssetOverlay() }, probedWidth: 1920, probedHeight: 1080,
            inputIndexForIndex: i => i + 1);

        chain.Should().Contain("scale=945:162:flags=bilinear");
        chain.Should().Contain("overlay=x=295:y=864:");
    }

    [Fact]
    public void Overlay_filter_carries_eof_action_pass_so_a_short_asset_does_not_freeze_for_the_rest_of_the_window()
    {
        string chain = OverlayAssetFilterBuilder.BuildFilterChain(
            "[vcut]", new[] { AssetOverlay() }, probedWidth: 1920, probedHeight: 1080,
            inputIndexForIndex: i => i + 1);

        chain.Should().Contain("eof_action=pass");
    }

    [Fact]
    public void Overlay_filter_enable_window_uses_culture_invariant_start_and_end_seconds()
    {
        string chain = OverlayAssetFilterBuilder.BuildFilterChain(
            "[vcut]", new[] { AssetOverlay(startSec: 4.5, endSec: 6.25) }, probedWidth: 1920, probedHeight: 1080,
            inputIndexForIndex: i => i + 1);

        chain.Should().Contain("between(t,4.5,6.25)");
    }

    [Fact]
    public void Asset_content_is_time_shifted_via_setpts_to_start_at_the_overlays_output_start_time()
    {
        string chain = OverlayAssetFilterBuilder.BuildFilterChain(
            "[vcut]", new[] { AssetOverlay(startSec: 4.5, endSec: 6.25) }, probedWidth: 1920, probedHeight: 1080,
            inputIndexForIndex: i => i + 1);

        chain.Should().Contain("setpts=PTS+4.5/TB");
    }

    [Fact]
    public void Throws_for_an_empty_overlay_list()
    {
        Action act = () => OverlayAssetFilterBuilder.BuildFilterChain(
            "[vcut]", Array.Empty<ResolvedOverlay>(), probedWidth: 1920, probedHeight: 1080,
            inputIndexForIndex: i => i + 1);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Throws_when_given_a_non_asset_overlay()
    {
        ResolvedOverlay textOnlyOverlay = new(
            "p0", "Title", SanitizedText: "Hello", SanitizedSubtext: "", DurationMs: 1500, Emphasis: "Normal",
            OutputStartSec: 4.0, OutputEndSec: 6.0,
            Rect: new VideoAnalysisRect(0.1, 0.8, 0.6, 0.15), TextColor: "Light");

        textOnlyOverlay.IsAssetOverlay.Should().BeFalse();

        Action act = () => OverlayAssetFilterBuilder.BuildFilterChain(
            "[vcut]", new[] { textOnlyOverlay }, probedWidth: 1920, probedHeight: 1080,
            inputIndexForIndex: i => i + 1);

        act.Should().Throw<ArgumentException>("this builder must only ever receive overlays already partitioned as asset overlays");
    }
}
