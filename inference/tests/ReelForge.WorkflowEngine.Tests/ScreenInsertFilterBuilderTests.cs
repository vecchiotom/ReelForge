using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FluentAssertions;
using ReelForge.WorkflowEngine.Services.Video;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

/// <summary>
/// Tests for <see cref="ScreenInsertFilterBuilder"/> — the pure filter-string builder behind
/// tracked screen inserts (docs/video-editing.md "Tracked screen inserts (Phase 5)"). The exact
/// recipe (bordered content + identically-warped white mask + alphamerge + overlay) was validated
/// against a real ffmpeg run during design; these tests pin the string construction: per-frame
/// corner expressions, label plumbing, input-index mapping, and culture-invariant number
/// formatting.
/// </summary>
public class ScreenInsertFilterBuilderTests
{
    private static ResolvedScreenInsert MakeInsert(
        string regionId = "r0",
        double outStart = 2.0,
        double outEnd = 8.0,
        IReadOnlyList<InsertQuadFrame>? keyframes = null) =>
        new(
            regionId, "/scratch/insert-asset.mp4", outStart, outEnd,
            keyframes ??
            [
                new InsertQuadFrame(0, 576, 216, 1152, 238, 595, 756, 1171, 778),
                new InsertQuadFrame(60, 672, 216, 1248, 238, 691, 756, 1267, 778)
            ],
            30, 1);

    [Fact]
    public void BuildPiecewiseExpr_single_keyframe_is_a_constant()
    {
        ScreenInsertFilterBuilder.BuildPiecewiseExpr([(0L, 576.0)]).Should().Be("576");
    }

    [Fact]
    public void BuildPiecewiseExpr_two_keyframes_lerp_on_in_and_hold_the_last_value()
    {
        string expr = ScreenInsertFilterBuilder.BuildPiecewiseExpr([(0L, 100.0), (10L, 200.0)]);

        expr.Should().Be("if(lt(in,10),100+(in-0)*10,200)");
    }

    [Fact]
    public void BuildPiecewiseExpr_three_keyframes_nest_ifs_in_frame_order()
    {
        string expr = ScreenInsertFilterBuilder.BuildPiecewiseExpr([(0L, 0.0), (10L, 10.0), (20L, 30.0)]);

        expr.Should().Be("if(lt(in,10),0+(in-0)*1,if(lt(in,20),10+(in-10)*2,30))");
    }

    [Fact]
    public void BuildPiecewiseExpr_collapses_duplicate_frame_indices_keeping_the_last()
    {
        string expr = ScreenInsertFilterBuilder.BuildPiecewiseExpr([(0L, 1.0), (0L, 2.0)]);

        expr.Should().Be("2");
    }

    [Fact]
    public void BuildPiecewiseExpr_is_culture_invariant()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            string expr = ScreenInsertFilterBuilder.BuildPiecewiseExpr([(0L, 1.5), (10L, 2.75)]);

            expr.Should().NotContain(",5", "decimal separators must be dots regardless of host culture");
            expr.Should().Contain("1.5");
            expr.Should().Contain("0.125");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Single_insert_builds_content_mask_alphamerge_overlay_chain_ending_at_vout()
    {
        string chain = ScreenInsertFilterBuilder.BuildFilterChain(
            "[vcut]", [MakeInsert()], 1920, 1080, i => 1);

        string[] segments = chain.Split(';');
        segments.Should().HaveCount(4);

        // Content branch: the asset input, scaled to the bordered inner size, conformed to the
        // canonical fps, padded with the load-bearing 1px black border, warped, delayed.
        segments[0].Should().StartWith("[1:v]scale=1918:1078:flags=bilinear,fps=30/1,format=yuv444p,pad=1920:1080:1:1:black,perspective=");
        segments[0].Should().Contain("sense=destination:eval=frame");
        segments[0].Should().EndWith("setpts=PTS-STARTPTS+2/TB[sic0]");

        // Mask branch: an identically-warped synthesized white plate.
        segments[1].Should().StartWith("color=c=white:size=1918x1078:rate=30/1:duration=7,format=gray,pad=1920:1080:1:1:black,perspective=");
        segments[1].Should().EndWith("[sim0]");

        segments[2].Should().Be("[sic0][sim0]alphamerge[sia0]");
        segments[3].Should().Be("[vcut][sia0]overlay=x=0:y=0:eof_action=pass:enable='between(t,2,8)'[vout]");

        // Content and mask must share EXACTLY the same perspective expressions — anything else
        // would warp the mask differently from the content and leak the plate at the edges.
        string ContentPerspective(string s) => s[s.IndexOf("perspective=", StringComparison.Ordinal)..s.IndexOf(",setpts", StringComparison.Ordinal)];
        ContentPerspective(segments[0]).Should().Be(ContentPerspective(segments[1]));
    }

    [Fact]
    public void Corner_expressions_animate_per_frame_from_the_keyframes()
    {
        string chain = ScreenInsertFilterBuilder.BuildFilterChain(
            "[vcut]", [MakeInsert()], 1920, 1080, i => 1);

        chain.Should().Contain("x0='if(lt(in,60),576+(in-0)*1.6,672)'");
        chain.Should().Contain(":y0='216'", "a coordinate identical at every keyframe collapses to a constant");
    }

    [Fact]
    public void Two_inserts_chain_through_an_intermediate_label_and_map_their_own_input_indices()
    {
        var inserts = new List<ResolvedScreenInsert> { MakeInsert("r0"), MakeInsert("r1", 10.0, 12.0) };

        string chain = ScreenInsertFilterBuilder.BuildFilterChain(
            "[vcut]", inserts, 1920, 1080, i => 5 + i, finalLabel: "[vins]");

        chain.Should().Contain("[5:v]scale=");
        chain.Should().Contain("[6:v]scale=");
        chain.Should().Contain("[vcut][sia0]overlay=x=0:y=0:eof_action=pass:enable='between(t,2,8)'[si0]");
        chain.Should().Contain("[si0][sia1]overlay=x=0:y=0:eof_action=pass:enable='between(t,10,12)'[vins]");
    }

    [Fact]
    public void Empty_inserts_list_throws_the_caller_must_keep_label_plumbing_untouched()
    {
        Action act = () => ScreenInsertFilterBuilder.BuildFilterChain("[vcut]", [], 1920, 1080, i => 1);

        act.Should().Throw<ArgumentException>();
    }
}
