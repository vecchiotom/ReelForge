namespace ReelForge.Shared.Agents;

/// <summary>
/// A coherent, reusable bundle of agent tool/function names. This is the single source of truth
/// for "which tools does an agent type get" — both <c>AgentToolProvider.GetTools</c> (the real,
/// load-bearing WorkflowEngine scoping decision that actually constructs the runnable
/// <c>AIFunction</c>s) and <c>DatabaseSeeder.GetAvailableToolsJson</c> (the Inference API's
/// display-only "Available Tools" metadata) now derive their answer from
/// <see cref="ToolGroupCatalog"/> instead of maintaining two hand-written mappings that can (and,
/// twice, did) drift apart. See <see cref="ToolGroupCatalog"/> for the actual per-<c>AgentType</c>
/// scoping decision and the group→function-name membership.
/// </summary>
public enum ToolGroup
{
    /// <summary>
    /// ListProjectFiles, ReadProjectFile, SearchProjectFiles, GetDeterministicContextFiles —
    /// read-only project-file context. Granted to every real (non-deterministic) agent type.
    /// </summary>
    ProjectRead,

    /// <summary>
    /// WriteProjectFile — persist a new/updated project file. Granted only to agents that
    /// produce a project-file artifact of their own (RemotionComponentTranslator, AuthorAgent).
    /// </summary>
    ProjectWrite,

    /// <summary>
    /// GetSandboxStatus, ListSandboxFiles, ReadSandboxFile — read-only sandbox inspection,
    /// reused verbatim by every agent type that ever looks inside the sandbox.
    /// </summary>
    SandboxBrowse,

    /// <summary>
    /// GetSandbox — full sandbox metadata. A step up from <see cref="SandboxBrowse"/>;
    /// Director/Scriptwriter deliberately do NOT get this group (see
    /// <see cref="ToolGroupCatalog.GroupsFor"/>), every other sandbox-touching agent does.
    /// </summary>
    SandboxMetadata,

    /// <summary>
    /// CheckLintAndTypeErrors — an objective TypeScript/lint quality signal.
    /// </summary>
    SandboxLint,

    /// <summary>
    /// EnsureSandbox, WriteSandboxFile, DeleteSandboxPath, InstallNpmPackages,
    /// RunSandboxNpmScript — full sandbox code-authoring access (create/resume the sandbox,
    /// write/delete files, install packages, run build/lint scripts), short of rendering.
    /// </summary>
    SandboxAuthoring,

    /// <summary>
    /// RunSandboxRemotionCommand, RenderVideoAndUploadToStorage, CompleteSandbox — the
    /// render-and-finalize step. Granted only to AuthorAgent (the pipeline's FINAL deliverable
    /// video) and MotionGraphicsPlanner (a small, optional overlay ASSET — never the final
    /// video).
    /// </summary>
    SandboxRender,

    /// <summary>
    /// FailWorkflow — abort the workflow with a human-readable reason. Granted to every real
    /// (non-deterministic) agent type.
    /// </summary>
    WorkflowControl,
}
