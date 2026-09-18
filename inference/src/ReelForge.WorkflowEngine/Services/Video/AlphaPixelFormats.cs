namespace ReelForge.WorkflowEngine.Services.Video;

/// <summary>
/// Recognizes whether an ffprobe-reported <c>pix_fmt</c> string carries a real alpha plane.
/// Pure — no I/O, no ffmpeg. Same allowlist discipline as <see cref="OverlayTextSanitizer"/>/
/// <c>VideoCompileStepExecutor</c>'s codec/color allowlists: an unrecognized format is treated as
/// NOT having alpha rather than guessed at, since the consequence of getting this wrong the wrong
/// way (trusting alpha that isn't really there) is exactly the bug this class exists to catch —
/// a rendered-asset overlay silently compositing as a solid, opaque block over the edited video
/// (see docs/video-editing.md "Motion graphics (Phase 3)").
/// </summary>
public static class AlphaPixelFormats
{
    // Every alpha-carrying pix_fmt name ffprobe can report for the containers/codecs
    // MotionGraphicsPlanner's render recipe actually produces or a well-behaved Remotion render
    // could plausibly produce: VP9/WebM's yuva420p (the documented recipe), ProRes 4444's
    // yuva444p10le/yuva444p12le, QuickTime Animation's argb/rgba, and their bit-depth siblings.
    private static readonly HashSet<string> Formats = new(StringComparer.OrdinalIgnoreCase)
    {
        "yuva420p", "yuva422p", "yuva444p",
        "yuva420p9le", "yuva420p9be", "yuva420p10le", "yuva420p10be",
        "yuva422p9le", "yuva422p9be", "yuva422p10le", "yuva422p10be", "yuva422p16le", "yuva422p16be",
        "yuva444p9le", "yuva444p9be", "yuva444p10le", "yuva444p10be",
        "yuva444p12le", "yuva444p12be", "yuva444p16le", "yuva444p16be",
        "rgba", "bgra", "argb", "abgr",
        "rgba64le", "rgba64be", "bgra64le", "bgra64be",
        "gbrap", "gbrap10le", "gbrap10be", "gbrap12le", "gbrap12be", "gbrap16le", "gbrap16be",
        "ya8", "ya16le", "ya16be",
    };

    public static bool HasAlpha(string? pixFmt) =>
        !string.IsNullOrWhiteSpace(pixFmt) && Formats.Contains(pixFmt);
}
