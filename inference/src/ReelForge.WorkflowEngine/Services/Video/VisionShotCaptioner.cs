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

        if (parsed.Summary.Length > maxCaptionChars)
            parsed = parsed with { Summary = parsed.Summary[..maxCaptionChars] };

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
}
