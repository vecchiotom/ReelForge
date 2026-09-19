namespace ReelForge.Shared.Workflows;

/// <summary>
/// Where the source video for a <c>StepType.VideoAnalyze</c> step comes from. Render outputs
/// are NOT <c>ProjectFile</c> rows (they live only as <c>WorkflowStepResult.OutputStorageKey</c>),
/// so the source model must resolve a step-result storage key as a first-class kind rather than
/// assuming everything is addressable by a <c>ProjectFile</c> id.
/// </summary>
public enum VideoSourceKind
{
    /// <summary>An uploaded video/audio file: resolves via <c>project_files.storage_key</c>.</summary>
    ProjectFile,

    /// <summary>A specific step's result: resolves via that step's <c>WorkflowStepResult.OutputStorageKey</c>.</summary>
    StepOutput,

    /// <summary>The latest completed step result in this execution that has a non-null output storage key.</summary>
    PreviousStepOutput
}

/// <summary>
/// Resolves to exactly one S3/MinIO object holding the source video. Never a URL, never a
/// model-supplied string — always closed, typed, and resolved server-side.
/// </summary>
public sealed record VideoSourceRef(
    VideoSourceKind Kind,
    Guid? ProjectFileId = null,  // Kind=ProjectFile   -> project_files.storage_key
    int? StepOrder = null);      // Kind=StepOutput    -> that step's workflow_step_results.output_storage_key
                                 // Kind=PreviousStepOutput -> latest completed result with a non-null key (StepOrder ignored)
