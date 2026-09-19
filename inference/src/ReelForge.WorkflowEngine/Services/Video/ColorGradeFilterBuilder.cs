namespace ReelForge.WorkflowEngine.Services.Video;

/// <summary>
/// Deterministically maps a <c>ColorGradePlanOutput</c>'s enum WORDS to a concrete ffmpeg video
/// filter chain (see docs/video-editing.md "Color grading"). Pure C#, no LLM, no I/O: every
/// number that reaches the filtergraph comes from the first-party parameter tables below — the
/// model contributes only words (<c>Look</c>/<c>Strength</c>/<c>ShadowTone</c>/
/// <c>HighlightTone</c>, all validated against the allowlists here), the exact discipline
/// <c>MusicMixFilterBuilder</c> applies to Intensity/Ducking words and
/// <c>DrawtextFilterBuilder</c> applies to Duration/Emphasis words.
///
/// <para>
/// Only four battle-tested core filters are ever emitted — <c>colorbalance</c>, <c>eq</c>,
/// <c>hue</c>, <c>colorlevels</c> — composed in that fixed order (colour cast first, then
/// global contrast/saturation/gamma, then the mono desaturation, then black/white-point
/// shaping). The chain is appended to the compile filtergraph's cut stage BEFORE any
/// overlay/insert stage, so motion graphics are always painted clean on top of graded footage.
/// </para>
///
/// <para>
/// The numeric tables are deliberately INTERNAL constants, not <c>VideoCompileStepConfig</c>
/// knobs: exposing raw eq/colorbalance numbers as workflow-author config would recreate, one
/// layer up, exactly the free-numeric-parameter surface the words-only contract exists to close
/// off, for no capability the curated looks don't already deliver. A future need for custom
/// looks should add a NAMED look to this table, not a numeric pass-through.
/// </para>
/// </summary>
public static class ColorGradeFilterBuilder
{
    /// <summary>The named looks a plan may choose. "None" is the explicit whole-grade decline — <see cref="BuildFilterChain"/> returns null for it.</summary>
    public static readonly IReadOnlySet<string> AllowedLooks =
        new HashSet<string>(StringComparer.Ordinal) { "None", "Warm", "Cool", "Filmic", "Vibrant", "Muted", "Mono" };

    /// <summary>The strength words a plan may choose. Unknown/empty normalizes to "Normal".</summary>
    public static readonly IReadOnlySet<string> AllowedStrengths =
        new HashSet<string>(StringComparer.Ordinal) { "Subtle", "Normal", "Strong" };

    /// <summary>The shadow-tone words a plan may choose. Unknown/empty normalizes to "Neutral".</summary>
    public static readonly IReadOnlySet<string> AllowedShadowTones =
        new HashSet<string>(StringComparer.Ordinal) { "Neutral", "Lifted", "Deepened" };

    /// <summary>The highlight-tone words a plan may choose. Unknown/empty normalizes to "Neutral".</summary>
    public static readonly IReadOnlySet<string> AllowedHighlightTones =
        new HashSet<string>(StringComparer.Ordinal) { "Neutral", "Softened", "Brightened" };

    /// <summary>Normalizes a strength word: a recognized word passes through, anything else becomes "Normal".</summary>
    public static string NormalizeStrength(string? strength) =>
        strength is not null && AllowedStrengths.Contains(strength) ? strength : "Normal";

    /// <summary>Normalizes a shadow-tone word: a recognized word passes through, anything else becomes "Neutral".</summary>
    public static string NormalizeShadowTone(string? tone) =>
        tone is not null && AllowedShadowTones.Contains(tone) ? tone : "Neutral";

    /// <summary>Normalizes a highlight-tone word: a recognized word passes through, anything else becomes "Neutral".</summary>
    public static string NormalizeHighlightTone(string? tone) =>
        tone is not null && AllowedHighlightTones.Contains(tone) ? tone : "Neutral";

    /// <summary>
    /// Builds the comma-joined video filter chain for the given (already-validated) words, or
    /// null when nothing should be applied at all: <paramref name="look"/> "None" declines the
    /// whole grade — tone words are then deliberately ignored too, so "the plan said None" always
    /// means "the compiled bytes carry no grade filter whatsoever", a property the invariant
    /// tests pin. An unrecognized look also returns null (the caller reports it as
    /// <c>unknown_look_word</c> rather than guessing a substitute). Strength/tone words are
    /// normalized per the Normalize* helpers.
    /// </summary>
    public static string? BuildFilterChain(string? look, string? strength, string? shadowTone, string? highlightTone)
    {
        if (look is null || !AllowedLooks.Contains(look) || look == "None")
            return null;

        double mult = NormalizeStrength(strength) switch
        {
            "Subtle" => 0.5,
            "Strong" => 1.5,
            _ => 1.0
        };

        var parts = new List<string>(4);

        // 1. Colour cast (colorbalance) — shadow/midtone shifts on the red/blue axes.
        (double rs, double bs, double rm, double bm) = look switch
        {
            "Warm" => (0.10, -0.10, 0.05, -0.05),
            "Cool" => (-0.10, 0.10, -0.05, 0.05),
            "Filmic" => (0.0, 0.04, 0.0, 0.0), // a slight teal in the shadows
            _ => (0.0, 0.0, 0.0, 0.0)
        };
        if (rs != 0 || bs != 0 || rm != 0 || bm != 0)
        {
            var cb = new List<string>(4);
            if (rs != 0) cb.Add($"rs={FfmpegArgvFormat.Number(Math.Round(rs * mult, 4))}");
            if (bs != 0) cb.Add($"bs={FfmpegArgvFormat.Number(Math.Round(bs * mult, 4))}");
            if (rm != 0) cb.Add($"rm={FfmpegArgvFormat.Number(Math.Round(rm * mult, 4))}");
            if (bm != 0) cb.Add($"bm={FfmpegArgvFormat.Number(Math.Round(bm * mult, 4))}");
            parts.Add("colorbalance=" + string.Join(":", cb));
        }

        // 2. Global contrast/saturation/gamma (eq) — table values are the NORMAL-strength
        // targets; strength scales each parameter's DELTA from its 1.0 identity.
        (double contrast, double saturation, double gamma) = look switch
        {
            "Warm" => (1.0, 1.06, 1.0),
            "Cool" => (1.0, 1.03, 1.0),
            "Filmic" => (1.10, 0.90, 0.97),
            "Vibrant" => (1.06, 1.28, 1.0),
            "Muted" => (0.96, 0.78, 1.0),
            "Mono" => (1.06, 1.0, 1.0),
            _ => (1.0, 1.0, 1.0)
        };
        double Scale(double target) => Math.Round(1.0 + (target - 1.0) * mult, 4);
        var eq = new List<string>(3);
        if (contrast != 1.0) eq.Add($"contrast={FfmpegArgvFormat.Number(Scale(contrast))}");
        if (saturation != 1.0) eq.Add($"saturation={FfmpegArgvFormat.Number(Scale(saturation))}");
        if (gamma != 1.0) eq.Add($"gamma={FfmpegArgvFormat.Number(Scale(gamma))}");
        if (eq.Count > 0)
            parts.Add("eq=" + string.Join(":", eq));

        // 3. Mono is a full desaturation regardless of strength — a "slightly less black and
        // white" Subtle mono would just read as broken colour, so only its eq contrast scales.
        if (look == "Mono")
            parts.Add("hue=s=0");

        // 4. Black/white-point shaping (colorlevels) — both tone words fold into ONE
        // colorlevels instance. Values are input-level shifts: a negative *imin lifts blacks, a
        // sub-1.0 *omax softens highlights, a sub-1.0 *imax stretches toward white.
        string normalizedShadow = NormalizeShadowTone(shadowTone);
        string normalizedHighlight = NormalizeHighlightTone(highlightTone);
        var levels = new List<string>(6);
        double shadowShift = normalizedShadow switch
        {
            "Lifted" => Math.Round(-0.04 * mult, 4),
            "Deepened" => Math.Round(0.04 * mult, 4),
            _ => 0.0
        };
        if (shadowShift != 0)
        {
            string s = FfmpegArgvFormat.Number(shadowShift);
            levels.Add($"rimin={s}");
            levels.Add($"gimin={s}");
            levels.Add($"bimin={s}");
        }

        switch (normalizedHighlight)
        {
            case "Softened":
                {
                    string v = FfmpegArgvFormat.Number(Math.Round(1.0 - 0.05 * mult, 4));
                    levels.Add($"romax={v}");
                    levels.Add($"gomax={v}");
                    levels.Add($"bomax={v}");
                    break;
                }
            case "Brightened":
                {
                    string v = FfmpegArgvFormat.Number(Math.Round(1.0 - 0.06 * mult, 4));
                    levels.Add($"rimax={v}");
                    levels.Add($"gimax={v}");
                    levels.Add($"bimax={v}");
                    break;
                }
        }

        if (levels.Count > 0)
            parts.Add("colorlevels=" + string.Join(":", levels));

        return parts.Count > 0 ? string.Join(",", parts) : null;
    }
}
