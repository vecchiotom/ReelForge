using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.AI;
using ReelForge.Shared.Inference;
using ReelForge.Shared.Workflows;

namespace ReelForge.WorkflowEngine.Services.Video;

/// <summary>
/// Captions one shot's keyframe via <see cref="IChatClientFactory"/> — reused unchanged, since a
/// vision call is just a chat-completions call with an image content part alongside the text
/// prompt. Structured output uses the exact same <c>ChatResponseFormat.ForJsonSchema&lt;T&gt;()</c>
/// mechanism <c>ReelForgeAgentBase</c>/<c>AgentStepExecutor</c> use for agent steps.
/// </summary>
public sealed class VisionShotCaptioner : IShotCaptioner
{
    private static readonly JsonSerializerOptions ResponseJsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Cap on each of <see cref="VideoShotCaption.Subjects"/>/<see cref="VideoShotCaption.OnScreenText"/>/
    /// <see cref="VideoShotCaption.Tags"/> — a captioning backend returning an unbounded list must
    /// not be able to inflate the analysis artifact/bounded view without limit (Item H cleanup).
    /// </summary>
    private const int MaxListItems = 20;

    /// <summary>Cap on each individual list-item string's length (list fields are short tags/snippets, not prose — a smaller fixed budget than <see cref="VideoShotCaption.Summary"/>'s caller-configurable <c>maxCaptionChars</c>).</summary>
    private const int MaxListItemChars = 200;

    private readonly IChatClientFactory _chatClientFactory;
    private readonly ILogger<VisionShotCaptioner> _logger;

    public VisionShotCaptioner(IChatClientFactory chatClientFactory, ILogger<VisionShotCaptioner> logger)
    {
        _chatClientFactory = chatClientFactory;
        _logger = logger;
    }

    public async Task<VideoShotCaption> CaptionAsync(
        ResolvedInferenceProvider provider, ShotCaptionRequest request, int maxCaptionChars, CancellationToken ct)
    {
        IChatClient client = _chatClientFactory.Get(provider);
        byte[] jpegBytes = await File.ReadAllBytesAsync(request.KeyframePath, ct).ConfigureAwait(false);

        string prompt =
            $"""
             You are looking at a single still frame — a representative keyframe from one shot of
             a source video. Describe only what is visible in THIS frame; you have no information
             about what happens before or after it, and you must never guess, state, or imply any
             timestamp, duration, or frame number.

             Respond with a short structured description:
             - summary: one or two sentences describing the frame overall (<= {maxCaptionChars} characters)
             - subjects: the people/objects/subjects visibly present
             - action: what is happening in the frame, if anything
             - setting: where this appears to take place
             - mood: the overall tone/mood the frame conveys
             - shotScale: your best guess at framing — typically "Wide", "Medium", or "CloseUp"
             - cameraAngle: your best guess at camera angle (e.g. "Eye level", "Low angle", "High angle", "Overhead")
             - onScreenText: any text visibly burned into the frame (subtitles, titles, UI text) — empty if none
             - tags: a few short free-text tags summarizing the frame
             """;

        ChatMessage message = new(
            ChatRole.User,
            new List<AIContent>
            {
                new TextContent(prompt),
                new DataContent(jpegBytes, "image/jpeg")
            });

        ChatOptions options = new()
        {
            ResponseFormat = ChatResponseFormat.ForJsonSchema<VideoShotCaption>(),
            MaxOutputTokens = 500
        };

        ChatResponse response = await client.GetResponseAsync(new[] { message }, options, ct).ConfigureAwait(false);

        string text = response.Text ?? string.Empty;
        VideoShotCaption? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<VideoShotCaption>(text, ResponseJsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Vision captioning for shot '{request.ShotId}' returned non-JSON or unparseable output.", ex);
        }

        if (parsed is null)
        {
            throw new InvalidOperationException(
                $"Vision captioning for shot '{request.ShotId}' returned no parseable structured output.");
        }

        parsed = ApplyCaps(parsed, maxCaptionChars);

        // SAFETY (see IShotCaptioner's remarks): ShotId here is whatever the model happened to
        // echo back, or the parser's default, and is NEVER used for binding. The caller
        // (VideoAnalyzeStepExecutor) always overwrites this field with the id it actually
        // requested (request.ShotId) before attaching the caption to a shot — logged here only
        // for diagnostics, not trusted.
        if (!string.Equals(parsed.ShotId, request.ShotId, StringComparison.Ordinal))
        {
            _logger.LogDebug(
                "Vision captioning for requested shot {RequestedShotId} returned a different (ignored) modelShotId {ModelShotId}.",
                request.ShotId, parsed.ShotId);
        }

        return parsed;
    }

    /// <summary>
    /// Post-processes a freshly-deserialized <see cref="VideoShotCaption"/>: clamps
    /// <paramref name="maxCaptionChars"/> to non-negative (a raw <c>str[..n]</c> slice with a
    /// negative <c>n</c> throws <see cref="ArgumentOutOfRangeException"/>), grapheme-safe-truncates
    /// every string field to it, and caps every list field to <see cref="MaxListItems"/> entries
    /// of at most <see cref="MaxListItemChars"/> characters each — so neither a misconfigured
    /// caller nor an unbounded captioning-backend response can inflate the analysis
    /// artifact/bounded view or throw (Item H cleanup). Factored out as a pure, directly
    /// unit-testable method — the rest of <see cref="CaptionAsync"/> requires a live
    /// <see cref="IChatClient"/> and isn't independently unit tested without it.
    /// </summary>
    internal static VideoShotCaption ApplyCaps(VideoShotCaption parsed, int maxCaptionChars)
    {
        int safeCaptionChars = Math.Max(0, maxCaptionChars);
        return parsed with
        {
            Summary = OverlayTextSanitizer.TruncateByTextElements(parsed.Summary ?? string.Empty, safeCaptionChars),
            Action = OverlayTextSanitizer.TruncateByTextElements(parsed.Action ?? string.Empty, safeCaptionChars),
            Setting = OverlayTextSanitizer.TruncateByTextElements(parsed.Setting ?? string.Empty, safeCaptionChars),
            Mood = OverlayTextSanitizer.TruncateByTextElements(parsed.Mood ?? string.Empty, safeCaptionChars),
            ShotScale = OverlayTextSanitizer.TruncateByTextElements(parsed.ShotScale ?? string.Empty, safeCaptionChars),
            CameraAngle = OverlayTextSanitizer.TruncateByTextElements(parsed.CameraAngle ?? string.Empty, safeCaptionChars),
            Subjects = CapList(parsed.Subjects),
            OnScreenText = CapList(parsed.OnScreenText),
            Tags = CapList(parsed.Tags)
        };
    }

    /// <summary>
    /// Caps a model-returned list field to at most <see cref="MaxListItems"/> entries, each
    /// truncated (grapheme-safe) to at most <see cref="MaxListItemChars"/> characters — so an
    /// unbounded/oversized list from a captioning backend can't inflate the analysis
    /// artifact/bounded view (Item H cleanup). Null input (a nullable-unaware deserialization) and
    /// null entries both become empty rather than throwing.
    /// </summary>
    private static IReadOnlyList<string> CapList(IReadOnlyList<string>? items)
    {
        if (items is null || items.Count == 0)
            return Array.Empty<string>();

        return items
            .Take(MaxListItems)
            .Select(item => OverlayTextSanitizer.TruncateByTextElements(item ?? string.Empty, MaxListItemChars))
            .ToList();
    }
}
