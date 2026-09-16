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
        double endSec = 6.0,
        string emphasis = "Normal") => new(
            placementId, "LowerThird", text, subtext, DurationMs: 1500, Emphasis: emphasis,
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
        // Rect (0.1, 0.8, 0.6, 0.15) at 1920x1080 is the band: x=192, y=864, w=1152, h=162. The
        // OVERLAY() helper sets no PlacementRegion (defaults to "", vertically centered), and the
        // drawn box is a compact ACCENT box shrunk from that band by ComputeAccentBoxPixels's
        // default 16%-of-frame-height / 82%-of-band-width, never the raw band itself (see that
        // method's doc comment for why): height target = round(1080*0.16) = 173, clamped to the
        // band's own 162 (already compact) -> h=162 (unchanged); width = round(1152*0.82) = 945,
        // centered -> x = 192 + (1152-945)/2 = 295; height unchanged from the band -> y = 864.
        string chain = DrawtextFilterBuilder.BuildFilterChain(
            "[vcut]", new[] { Overlay() }, probedWidth: 1920, probedHeight: 1080,
            fontSizePct: 5, fadeMs: 300, fontColor: "white", boxColor: "black@0.45",
            fontFilePath: "/fonts/DejaVuSans.ttf",
            textFilePathForIndex: slot => $"/scratch/ov-{slot}.txt");

        chain.Should().Contain("drawbox=x=295:y=864:w=945:h=162:color=black@0.45:t=fill:");
    }

    [Fact]
    public void ComputeAccentBoxPixels_shrinks_the_literal_full_LowerThird_band_to_a_compact_accent()
    {
        // Regression test for the reported defect: a rendered/drawn overlay stretch-filling the
        // FULL named LowerThird band (FrameGridAnalyzer's literal definition — full width, bottom
        // third of the frame) visibly "blocked the whole screen" on a real 1440x2558 output. The
        // band here: x=0, y=round(2558*2/3)=1705, w=1440, h=round(2558/3)=853.
        var fullLowerThirdBand = new VideoAnalysisRect(0.0, 2.0 / 3.0, 1.0, 1.0 / 3.0);

        (int x, int y, int w, int h) = DrawtextFilterBuilder.ComputeAccentBoxPixels(
            fullLowerThirdBand, "LowerThird", probedWidth: 1440, probedHeight: 2558);

        // height = min(853, round(2558*0.16)=409) = 409; width = round(1440*0.82) = 1181;
        // x = (1440-1181)/2 = 129; y (LowerThird, bottom-anchored) = 1705+853-409 = 2149.
        x.Should().Be(129);
        y.Should().Be(2149);
        w.Should().Be(1181);
        h.Should().Be(409);

        double coverageRatio = w * (double)h / (1440.0 * 2558.0);
        coverageRatio.Should().BeLessThan(0.20, "a compact accent overlay must not cover a large fraction of the frame");
        h.Should().BeLessThan(853, "the drawn box must be shorter than the full named band it was offered");
        w.Should().BeLessThan(1440, "the drawn box must not be full-bleed edge-to-edge");
    }

    [Theory]
    [InlineData("UpperThird", 254, 100)]
    [InlineData("LowerThird", 254, 240)]
    [InlineData("CenterBand", 254, 170)]
    [InlineData("", 254, 170)] // unrecognized/unset region centers, same as CenterBand
    public void ComputeAccentBoxPixels_anchors_to_the_expected_edge_per_region(string region, int expectedX, int expectedY)
    {
        // Band: x=200, y=100, w=600, h=300 at 1000x1000. height target = round(1000*0.16) = 160
        // (< bandH=300, no clamp needed); width = round(600*0.82) = 492; x = 200+(600-492)/2 = 254.
        var band = new VideoAnalysisRect(0.2, 0.1, 0.6, 0.3);

        (int x, int y, int w, int h) = DrawtextFilterBuilder.ComputeAccentBoxPixels(
            band, region, probedWidth: 1000, probedHeight: 1000);

        x.Should().Be(expectedX);
        y.Should().Be(expectedY);
        w.Should().Be(492);
        h.Should().Be(160);
    }

    [Fact]
    public void ComputeAccentBoxPixels_never_exceeds_the_bands_own_height_even_when_the_band_is_already_compact()
    {
        // Band height 0.05*1080=54px — already smaller than the 16%-of-frame-height target
        // (round(1080*0.16)=173) — so the accent box must clamp DOWN to the band's own height,
        // never grow past the safe zone it was offered.
        var compactBand = new VideoAnalysisRect(0.1, 0.8, 0.6, 0.05);

        (int _, int _, int _, int h) = DrawtextFilterBuilder.ComputeAccentBoxPixels(
            compactBand, "LowerThird", probedWidth: 1920, probedHeight: 1080);

        h.Should().Be(54);
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
    public void Strong_emphasis_produces_a_larger_fontsize_than_normal_for_otherwise_identical_config()
    {
        // Item C: Emphasis previously reached ResolvedOverlay but was never read by the filter
        // builder. Strong should nudge the font size up (fontSizePct * 1.25, clamped to [2, 12]).
        string normalChain = DrawtextFilterBuilder.BuildFilterChain(
            "[vcut]", new[] { Overlay(emphasis: "Normal") }, probedWidth: 1920, probedHeight: 1080,
            fontSizePct: 5, fadeMs: 300, fontColor: "white", boxColor: "black@0.45",
            fontFilePath: "/fonts/DejaVuSans.ttf",
            textFilePathForIndex: slot => $"/scratch/ov-{slot}.txt");

        string strongChain = DrawtextFilterBuilder.BuildFilterChain(
            "[vcut]", new[] { Overlay(emphasis: "Strong") }, probedWidth: 1920, probedHeight: 1080,
            fontSizePct: 5, fadeMs: 300, fontColor: "white", boxColor: "black@0.45",
            fontFilePath: "/fonts/DejaVuSans.ttf",
            textFilePathForIndex: slot => $"/scratch/ov-{slot}.txt");

        int ExtractMainFontSize(string chain)
        {
            System.Text.RegularExpressions.Match match =
                System.Text.RegularExpressions.Regex.Match(chain, @"fontsize=(\d+)");
            match.Success.Should().BeTrue();
            return int.Parse(match.Groups[1].Value);
        }

        int normalFontSize = ExtractMainFontSize(normalChain);
        int strongFontSize = ExtractMainFontSize(strongChain);

        strongFontSize.Should().BeGreaterThan(normalFontSize);
    }

    [Fact]
    public void Subtle_emphasis_produces_a_smaller_or_equal_fontsize_than_normal()
    {
        int normalFontSize = DrawtextFilterBuilder.ComputeFontSize(probedHeight: 1080, fontSizePct: 5, emphasis: "Normal");
        int subtleFontSize = DrawtextFilterBuilder.ComputeFontSize(probedHeight: 1080, fontSizePct: 5, emphasis: "Subtle");

        subtleFontSize.Should().BeLessThanOrEqualTo(normalFontSize);
    }

    [Fact]
    public void Emphasis_scaling_stays_within_the_existing_fontSizePct_clamp_range()
    {
        // At the top of the valid fontSizePct range (12), Strong (x1.25) must still clamp to 12,
        // not escape to an effective 15%.
        int atMaxNormal = DrawtextFilterBuilder.ComputeFontSize(probedHeight: 1080, fontSizePct: 12, emphasis: "Normal");
        int atMaxStrong = DrawtextFilterBuilder.ComputeFontSize(probedHeight: 1080, fontSizePct: 12, emphasis: "Strong");

        atMaxStrong.Should().Be(atMaxNormal);
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
    public void BoxColor_none_skips_the_drawbox_entirely_but_still_chains_drawtext_to_vout()
    {
        string chain = DrawtextFilterBuilder.BuildFilterChain(
            "[vcut]", new[] { Overlay() }, probedWidth: 1920, probedHeight: 1080,
            fontSizePct: 5, fadeMs: 300, fontColor: "white", boxColor: "none",
            fontFilePath: "/fonts/DejaVuSans.ttf",
            textFilePathForIndex: slot => $"/scratch/ov-{slot}.txt");

        chain.Should().NotContain("drawbox=");
        chain.Should().Contain("drawtext=");
        chain.Should().StartWith("[vcut]drawtext=");
        chain.TrimEnd().Should().EndWith("[vout]");
    }

    [Fact]
    public void BoxColor_none_is_case_insensitive_and_still_skips_the_drawbox()
    {
        string chain = DrawtextFilterBuilder.BuildFilterChain(
            "[vcut]", new[] { Overlay() }, probedWidth: 1920, probedHeight: 1080,
            fontSizePct: 5, fadeMs: 300, fontColor: "white", boxColor: "None",
            fontFilePath: "/fonts/DejaVuSans.ttf",
            textFilePathForIndex: slot => $"/scratch/ov-{slot}.txt");

        chain.Should().NotContain("drawbox=");
    }

    [Fact]
    public void EscapeFilterPath_embeds_a_literal_single_quote_using_the_close_escape_reopen_idiom()
    {
        // Item G: ffmpeg's filter parser does not process backslash escapes inside a single-quoted
        // value — the correct way to embed a literal ' in a '...'-wrapped value is to close the
        // quote, insert an escaped quote via a backslash OUTSIDE any quotes, then reopen: '\''.
        DrawtextFilterBuilder.EscapeFilterPath("it's").Should().Be("it'\\''s");
    }

    [Fact]
    public void EscapeFilterPath_no_longer_escapes_colon_or_backslash()
    {
        // Both are unnecessary (and were incorrect) inside a single-quoted value: the surrounding
        // quotes already protect ':', and '\' has no special meaning inside single quotes at all.
        DrawtextFilterBuilder.EscapeFilterPath("a:b").Should().Be("a:b");
        DrawtextFilterBuilder.EscapeFilterPath(@"a\b").Should().Be(@"a\b");
    }

    [Fact]
    public void Path_with_a_single_quote_produces_a_filter_string_ffmpeg_would_parse_back_to_the_original_path()
    {
        // Worked example proving the escaping is correct in isolation (real scratch paths never
        // contain a literal quote — see DrawtextFilterBuilder.EscapeFilterPath's remarks — so this
        // is defense-in-depth verification, not a reachable production path).
        string chain = DrawtextFilterBuilder.BuildFilterChain(
            "[vcut]", new[] { Overlay() }, probedWidth: 1920, probedHeight: 1080,
            fontSizePct: 5, fadeMs: 300, fontColor: "white", boxColor: "black@0.45",
            fontFilePath: "/fonts/DejaVuSans.ttf",
            textFilePathForIndex: slot => "/scratch/it's/ov-" + slot + ".txt");

        chain.Should().Contain("textfile='/scratch/it'\\''s/ov-0.txt'");
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
