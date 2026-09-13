using System.Collections.Generic;
using System.Globalization;
using FluentAssertions;
using ReelForge.Shared.Workflows;
using ReelForge.WorkflowEngine.Services.Video;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

/// <summary>
/// <see cref="DrawtextFilterBuilder"/> — the filter-string construction for Phase 3 overlays.
/// The single most important property under test: overlay TEXT never appears in the returned
/// filter string (see docs/video-editing.md "Motion graphics (Phase 3)") — only a scratch
/// textfile path does. Also covers geometry computed from probed dimensions, culture-invariant
/// number formatting, and presence of <c>expansion=none</c>.
/// </summary>
public class DrawtextFilterBuilderTests
{
    private static ResolvedOverlay Overlay(
        string placementId = "p0",
        string text = "SECRET OVERLAY TEXT MARKER",
        string subtext = "",
        double startSec = 4.0,
        double endSec = 6.0) => new(
            placementId, "LowerThird", text, subtext, DurationMs: 1500, Emphasis: "Normal",
            OutputStartSec: startSec, OutputEndSec: endSec,
            Rect: new VideoAnalysisRect(0.1, 0.8, 0.6, 0.15), TextColor: "Light");

    [Fact]
    public void Overlay_text_never_appears_in_the_returned_filter_string()
    {
        ResolvedOverlay overlay = Overlay(text: "SECRET OVERLAY TEXT MARKER", subtext: "ANOTHER SECRET LINE");

        string chain = DrawtextFilterBuilder.BuildFilterChain(
            "[vcut]", new[] { overlay }, probedWidth: 1920, probedHeight: 1080,
            fontSizePct: 5, fadeMs: 300, fontColor: "white", boxColor: "black@0.45",
            fontFilePath: "/fonts/DejaVuSans.ttf",
            textFilePathForIndex: slot => $"/scratch/ov-{slot}.txt");

        chain.Should().NotContain("SECRET OVERLAY TEXT MARKER");
        chain.Should().NotContain("ANOTHER SECRET LINE");
        // The text only ever appears in the separately-written textfile CONTENT (a different
        // value entirely — ResolvedOverlay.SanitizedText/SanitizedSubtext — never concatenated
        // into this filter chain string).
        overlay.SanitizedText.Should().Be("SECRET OVERLAY TEXT MARKER");
        overlay.SanitizedSubtext.Should().Be("ANOTHER SECRET LINE");
    }

    [Fact]
    public void Filter_chain_references_the_textfile_path_via_textfile_option()
    {
        string chain = DrawtextFilterBuilder.BuildFilterChain(
            "[vcut]", new[] { Overlay() }, probedWidth: 1920, probedHeight: 1080,
            fontSizePct: 5, fadeMs: 300, fontColor: "white", boxColor: "black@0.45",
            fontFilePath: "/fonts/DejaVuSans.ttf",
            textFilePathForIndex: slot => $"/scratch/ov-{slot}.txt");

        chain.Should().Contain("textfile='/scratch/ov-0.txt'");
    }

    [Fact]
    public void Every_drawtext_filter_carries_expansion_none()
    {
        string chain = DrawtextFilterBuilder.BuildFilterChain(
            "[vcut]", new[] { Overlay(subtext: "Second line") }, probedWidth: 1920, probedHeight: 1080,
            fontSizePct: 5, fadeMs: 300, fontColor: "white", boxColor: "black@0.45",
            fontFilePath: "/fonts/DejaVuSans.ttf",
            textFilePathForIndex: slot => $"/scratch/ov-{slot}.txt");

        int drawtextCount = System.Text.RegularExpressions.Regex.Matches(chain, "drawtext=").Count;
        int expansionNoneCount = System.Text.RegularExpressions.Regex.Matches(chain, "expansion=none").Count;
        drawtextCount.Should().BeGreaterThan(0);
        expansionNoneCount.Should().Be(drawtextCount);
    }

    [Fact]
    public void Chain_starts_from_the_base_label_and_ends_at_vout()
    {
        string chain = DrawtextFilterBuilder.BuildFilterChain(
            "[vcut]", new[] { Overlay() }, probedWidth: 1920, probedHeight: 1080,
            fontSizePct: 5, fadeMs: 300, fontColor: "white", boxColor: "black@0.45",
            fontFilePath: "/fonts/DejaVuSans.ttf",
            textFilePathForIndex: slot => $"/scratch/ov-{slot}.txt");

        chain.Should().StartWith("[vcut]");
        chain.Should().Contain("[vout]");
        chain.TrimEnd().Should().EndWith("[vout]");
    }

    [Fact]
    public void Box_geometry_is_computed_from_probed_dimensions_and_normalized_rect()
    {
        // Rect (0.1, 0.8, 0.6, 0.15) at 1920x1080 -> x=192, y=864, w=1152, h=162.
        string chain = DrawtextFilterBuilder.BuildFilterChain(
            "[vcut]", new[] { Overlay() }, probedWidth: 1920, probedHeight: 1080,
            fontSizePct: 5, fadeMs: 300, fontColor: "white", boxColor: "black@0.45",
            fontFilePath: "/fonts/DejaVuSans.ttf",
            textFilePathForIndex: slot => $"/scratch/ov-{slot}.txt");

        chain.Should().Contain("drawbox=x=192:y=864:w=1152:h=162:color=black@0.45:t=fill:");
    }

    [Fact]
    public void Fontsize_is_computed_from_probed_height_and_percentage()
    {
        // 1080 * 5% = 54.
        string chain = DrawtextFilterBuilder.BuildFilterChain(
            "[vcut]", new[] { Overlay() }, probedWidth: 1920, probedHeight: 1080,
            fontSizePct: 5, fadeMs: 300, fontColor: "white", boxColor: "black@0.45",
            fontFilePath: "/fonts/DejaVuSans.ttf",
            textFilePathForIndex: slot => $"/scratch/ov-{slot}.txt");

        chain.Should().Contain("fontsize=54:");
    }

    [Fact]
    public void Fontsize_never_drops_below_12px_even_at_a_tiny_percentage()
    {
        DrawtextFilterBuilder.ComputeFontSize(probedHeight: 100, fontSizePct: 2).Should().BeGreaterThanOrEqualTo(12);
    }

    [Fact]
    public void Numbers_in_the_filter_string_are_culture_invariant_regardless_of_current_culture()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE"); // decimal comma culture

            ResolvedOverlay overlay = Overlay(startSec: 4.5, endSec: 6.25);
            string chain = DrawtextFilterBuilder.BuildFilterChain(
                "[vcut]", new[] { overlay }, probedWidth: 1920, probedHeight: 1080,
                fontSizePct: 5, fadeMs: 300, fontColor: "white", boxColor: "black@0.45",
                fontFilePath: "/fonts/DejaVuSans.ttf",
                textFilePathForIndex: slot => $"/scratch/ov-{slot}.txt");

            chain.Should().Contain("between(t,4.5,6.25)");
            chain.Should().NotContain("4,5");
            chain.Should().NotContain("6,25");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Empty_overlays_list_throws_rather_than_silently_producing_an_empty_chain()
    {
        System.Action act = () => DrawtextFilterBuilder.BuildFilterChain(
            "[vcut]", System.Array.Empty<ResolvedOverlay>(), 1920, 1080, 5, 300, "white", "black@0.45",
            "/fonts/DejaVuSans.ttf", slot => $"/scratch/ov-{slot}.txt");

        act.Should().Throw<System.ArgumentException>();
    }

    [Fact]
    public void Multiple_overlays_chain_through_intermediate_labels_and_the_last_one_reaches_vout()
    {
        var overlays = new List<ResolvedOverlay> { Overlay("p0", "First"), Overlay("p1", "Second", startSec: 10, endSec: 12) };

        string chain = DrawtextFilterBuilder.BuildFilterChain(
            "[vcut]", overlays, 1920, 1080, 5, 300, "white", "black@0.45",
            "/fonts/DejaVuSans.ttf", slot => $"/scratch/ov-{slot}.txt");

        chain.Should().Contain("[gfx0b]");
        chain.Should().Contain("[gfx0]");
        chain.Should().Contain("[gfx1b]");
        chain.TrimEnd().Should().EndWith("[vout]");
        // Only one [vout] — the last overlay's drawtext, not an earlier one.
        System.Text.RegularExpressions.Regex.Matches(chain, System.Text.RegularExpressions.Regex.Escape("[vout]")).Count.Should().Be(1);
    }
}
