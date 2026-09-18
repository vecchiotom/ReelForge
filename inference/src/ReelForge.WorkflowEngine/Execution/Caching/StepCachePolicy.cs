using ReelForge.Shared.Agents;
using ReelForge.Shared.Data.Models;

namespace ReelForge.WorkflowEngine.Execution.Caching;

/// <inheritdoc cref="IStepCachePolicy"/>
public sealed class StepCachePolicy : IStepCachePolicy
{
    public bool IsCacheable(WorkflowStep step)
    {
        // A per-step override always wins, in either direction, over the built-in table below —
        // the workflow author is explicitly asserting they know better for this one step.
        if (step.CacheMode == StepCacheMode.Never)
            return false;
        if (step.CacheMode == StepCacheMode.Always)
            return true;

        return step.StepType switch
        {
            // Deterministic, non-LLM step types: same inputs always produce the same output, and
            // none of them has a side effect outside the output they return (Extract is pure
            // projection; VideoAnalyze/VideoCompile only touch WorkflowEngine-owned scratch space
            // and a fresh S3 object of their own; a room's own transcript/synthesis output is what
            // gets cached, not any external mutation).
            StepType.Extract => true,
            StepType.VideoAnalyze => true,
            StepType.VideoCompile => true,
            StepType.EditRoom => true,
            StepType.ColorGradeRoom => true,

            // GraphicsRoom is deliberately NOT in the list above even though it is structurally
            // the same shared-room machinery as EditRoom/ColorGradeRoom: its director/seats carry
            // the same full sandbox+Remotion+render tool grant as MotionGraphicsPlanner (minus
            // WriteProjectFile — see ToolGroupCatalog's rationale for AgentType.
            // MotionGraphicsDirector), and its standalone synthesis call may render a real overlay
            // asset into the sandbox/storage as a side effect of producing its output. Replaying a
            // cached MotionGraphicsPlanOutput would silently skip that render, leaving a plan that
            // references an asset key nothing ever produced this execution. Spelled out here
            // rather than falling through to the Agent-step tool-scope check below, since
            // GraphicsRoom is never a StepType.Agent step in the first place.
            StepType.GraphicsRoom => false,

            // An Agent step is cacheable only if the agent's own granted tools carry no
            // state-mutating side effect OUTSIDE the output text/keys this cache actually stores.
            // ProjectWrite (WriteProjectFile), SandboxAuthoring (write/install/build inside the
            // sandbox workspace) and SandboxRender (render + upload a real media artifact) are all
            // side effects a downstream step or a human reviewer can observe independently of the
            // agent's returned Output — replaying a cached Output would silently skip whichever of
            // those actually happened, leaving referenced files/renders that were never produced
            // this run. This is exactly why AuthorAgent, RemotionComponentTranslator,
            // MotionGraphicsPlanner and MotionGraphicsDirector are excluded: every one of them is
            // granted at least one of those three groups (see ToolGroupCatalog.GroupsFor).
            StepType.Agent => IsAgentStepCacheable(step),

            // Conditional/ForEach/Parallel: control-flow/fan-out step types whose "output" is a
            // structural artifact of the workflow graph itself (a branch choice, a merged array of
            // OTHER steps' outputs) rather than a value worth memoizing on its own.
            //
            // ReviewLoop especially: its entire reason to exist is to re-judge FRESH output
            // against deterministic facts computed by the step it is reviewing — serving a stale
            // verdict from a cache would defeat the review loop's purpose outright, not just miss
            // an optimization.
            _ => false
        };
    }

    /// <summary>
    /// The Agent-step arm of <see cref="IsCacheable"/>: cacheable only when NONE of the agent
    /// type's granted <see cref="ToolGroup"/>s is <see cref="ToolGroup.ProjectWrite"/>,
    /// <see cref="ToolGroup.SandboxAuthoring"/>, or <see cref="ToolGroup.SandboxRender"/>. Reads
    /// the built-in scoping table directly from <see cref="ToolGroupCatalog"/> — the same single
    /// source of truth <c>AgentToolProvider</c> uses at runtime — so this policy can never drift
    /// from what an agent is actually granted.
    /// </summary>
    private static bool IsAgentStepCacheable(WorkflowStep step)
    {
        AgentType agentType = step.AgentDefinition?.AgentType ?? AgentType.Custom;
        IReadOnlyList<ToolGroup> groups = ToolGroupCatalog.GroupsFor(agentType);

        return !groups.Contains(ToolGroup.ProjectWrite)
            && !groups.Contains(ToolGroup.SandboxAuthoring)
            && !groups.Contains(ToolGroup.SandboxRender);
    }
}
