using ReelForge.Shared.Inference;
using ReelForge.Shared.Workflows;

namespace ReelForge.WorkflowEngine.Services.Video;

/// <summary>
/// One shot's captioning request: the id the executor is asking about, the local scratch path of
/// its extracted keyframe JPEG, and the sample time (for logging/diagnostics only — never sent to
/// the model, and never part of the response contract).
/// </summary>
public sealed record ShotCaptionRequest(string ShotId, string KeyframePath, double AtSec);

/// <summary>
/// Produces a short structured scene description of one shot's representative keyframe via a
/// vision-capable chat-completions call (Phase 2 — see docs/video-editing.md "Vision captioning").
/// </summary>
/// <remarks>
/// <b>Critical safety property:</b> the shot-id &lt;-&gt; caption binding must never be
/// model-controlled. An implementation MAY populate <see cref="VideoShotCaption.ShotId"/> on the
/// value it returns however it likes (including leaving whatever the model emitted), because the
/// caller — <c>VideoAnalyzeStepExecutor</c> — always overwrites it with
/// <see cref="ShotCaptionRequest.ShotId"/> (the id it actually requested) before attaching the
/// caption to a shot. This mirrors the id-anchored discipline
/// <c>VideoEditDecisionOutput</c>/<c>VideoCompileStepExecutor</c> already use for the story-editor
/// agent's output: never trust an identifier the model echoes back for anything that matters.
/// </remarks>
public interface IShotCaptioner
{
    Task<VideoShotCaption> CaptionAsync(
        ResolvedInferenceProvider provider, ShotCaptionRequest request, int maxCaptionChars, CancellationToken ct);
}
