using FluentAssertions;
using ReelForge.WorkflowEngine.Services.Video;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

/// <summary>
/// Exact filter-string tests for <see cref="ColorGradeFilterBuilder"/> — the deterministic
/// words-to-ffmpeg mapping at the heart of the colour-grading contract (see docs/video-editing.md
/// "Color grading"): every number in the chain must come from the builder's own first-party
/// tables, "None" must always mean literally no filter, and unknown strength/tone words must
/// normalize rather than fail or leak through.
/// </summary>
public class ColorGradeFilterBuilderTests
{
    [Fact]
    public void None_look_returns_null_even_with_non_neutral_tones()
    {
        // "None" declines the WHOLE grade — the invariant is "the plan said None ⇒ the compiled
        // bytes carry no grade filter whatsoever", so tone words must not sneak a colorlevels in.
        ColorGradeFilterBuilder.BuildFilterChain("None", "Strong", "Lifted", "Softened").Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Sepia")]
    [InlineData("warm")] // case-sensitive by design — the schema words are exact
    public void Unknown_or_missing_look_returns_null(string? look)
    {
        ColorGradeFilterBuilder.BuildFilterChain(look, "Normal", "Neutral", "Neutral").Should().BeNull();
    }

    [Fact]
    public void Warm_at_Normal_strength_produces_the_exact_table_chain()
    {
        string? chain = ColorGradeFilterBuilder.BuildFilterChain("Warm", "Normal", "Neutral", "Neutral");

        chain.Should().Be("colorbalance=rs=0.1:bs=-0.1:rm=0.05:bm=-0.05,eq=saturation=1.06");
    }

    [Fact]
    public void Subtle_strength_halves_every_delta()
    {
        string? chain = ColorGradeFilterBuilder.BuildFilterChain("Warm", "Subtle", "Neutral", "Neutral");

        chain.Should().Be("colorbalance=rs=0.05:bs=-0.05:rm=0.025:bm=-0.025,eq=saturation=1.03");
    }

    [Fact]
    public void Strong_strength_scales_deltas_by_one_and_a_half()
    {
        string? chain = ColorGradeFilterBuilder.BuildFilterChain("Vibrant", "Strong", "Neutral", "Neutral");

        chain.Should().Be("eq=contrast=1.09:saturation=1.42");
    }

    [Fact]
    public void Mono_desaturates_fully_regardless_of_strength()
    {
        string? subtle = ColorGradeFilterBuilder.BuildFilterChain("Mono", "Subtle", "Neutral", "Neutral");

        subtle.Should().Be("eq=contrast=1.03,hue=s=0",
            "the hue=s=0 desaturation must never scale with strength — only the eq contrast does");
    }

    [Fact]
    public void Filmic_composes_colorbalance_then_eq_in_the_documented_stage_order()
    {
        string? chain = ColorGradeFilterBuilder.BuildFilterChain("Filmic", "Normal", "Neutral", "Neutral");

        chain.Should().Be("colorbalance=bs=0.04,eq=contrast=1.1:saturation=0.9:gamma=0.97");
    }

    [Fact]
    public void Lifted_shadows_and_softened_highlights_fold_into_one_colorlevels_instance()
    {
        string? chain = ColorGradeFilterBuilder.BuildFilterChain("Muted", "Normal", "Lifted", "Softened");

        chain.Should().Be(
            "eq=contrast=0.96:saturation=0.78," +
            "colorlevels=rimin=-0.04:gimin=-0.04:bimin=-0.04:romax=0.95:gomax=0.95:bomax=0.95");
    }

    [Fact]
    public void Deepened_shadows_and_brightened_highlights_use_input_level_shifts()
    {
        string? chain = ColorGradeFilterBuilder.BuildFilterChain("Cool", "Normal", "Deepened", "Brightened");

        chain.Should().Be(
            "colorbalance=rs=-0.1:bs=0.1:rm=-0.05:bm=0.05,eq=saturation=1.03," +
            "colorlevels=rimin=0.04:gimin=0.04:bimin=0.04:rimax=0.94:gimax=0.94:bimax=0.94");
    }

    [Theory]
    [InlineData(null, "Normal")]
    [InlineData("", "Normal")]
    [InlineData("Loud", "Normal")]
    [InlineData("Subtle", "Subtle")]
    public void Unknown_strength_words_normalize_to_Normal(string? word, string expected)
    {
        ColorGradeFilterBuilder.NormalizeStrength(word).Should().Be(expected);
    }

    [Fact]
    public void Unknown_tone_words_normalize_to_Neutral_and_emit_no_colorlevels()
    {
        ColorGradeFilterBuilder.NormalizeShadowTone("Crushed").Should().Be("Neutral");
        ColorGradeFilterBuilder.NormalizeHighlightTone("Blown").Should().Be("Neutral");

        string? chain = ColorGradeFilterBuilder.BuildFilterChain("Warm", "Normal", "Crushed", "Blown");
        chain.Should().NotContain("colorlevels");
    }

    [Fact]
    public void Every_emitted_number_uses_invariant_decimal_points()
    {
        // The R8 locale rule: no chain may ever carry a comma-decimal, whatever the host culture.
        foreach (string look in new[] { "Warm", "Cool", "Filmic", "Vibrant", "Muted", "Mono" })
        {
            string? chain = ColorGradeFilterBuilder.BuildFilterChain(look, "Strong", "Lifted", "Softened");
            chain.Should().NotBeNull();
            // Commas in a chain are exclusively filter separators: every "," must be followed by
            // a filter name (a letter), never a digit.
            System.Text.RegularExpressions.Regex.IsMatch(chain!, @",\d").Should().BeFalse(
                $"look {look} produced a comma-decimal: {chain}");
        }
    }
}
