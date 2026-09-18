using System.ComponentModel;
using Microsoft.Extensions.AI;
using ReelForge.Shared.Skills;
using ReelForge.WorkflowEngine.Services.Skills;

namespace ReelForge.WorkflowEngine.Agents.Tools;

/// <summary>
/// Agent tools that load skill instructions on demand. Replaces the old live-fetch-from-GitHub
/// RemotionSkillsAgentTools (SearchRemotionSkills/ReadRemotionSkill/ListAllRemotionSkills) with the
/// named-skill pattern: an agent is told the names and one-line descriptions of the skills it has
/// (see <see cref="Services.Skills.SkillPromptBuilder"/>), and loads one's full instructions only
/// by calling <see cref="UseSkill"/> with that exact name — never via search.
///
/// One instance of this class is bound to exactly one agent run's RESOLVED skill set (see
/// <c>ReelForgeAgentBase.CreateAgentAsync</c> / <see cref="SkillAgentToolsFactory"/>) — that
/// binding, not the prompt text, is the real enforcement boundary: calling <see cref="UseSkill"/>
/// or <see cref="ReadSkillResource"/> for a skill outside this instance's allowed set always
/// returns a clear rejection message, never the skill's content.
/// </summary>
public sealed class SkillAgentTools
{
    private readonly ISkillCorpusService _corpusService;
    private readonly IReadOnlyList<SkillDescriptor> _allowedSkills;
    private readonly ILogger _logger;

    public SkillAgentTools(
        ISkillCorpusService corpusService,
        IReadOnlyList<SkillDescriptor> allowedSkills,
        ILogger logger)
    {
        _corpusService = corpusService;
        _allowedSkills = allowedSkills;
        _logger = logger;
    }

    [Description(
        "Load the full instructions for a named skill. Returns the complete skill document: " +
        "guidance, code examples, and best practices. The skills available to you are listed in " +
        "your instructions — call this with one of those exact names.")]
    public async Task<string> UseSkill(
        [Description("Exact skill name, e.g. 'remotion-markup'.")] string name)
    {
        SkillDescriptor? allowed = FindAllowed(name);
        if (allowed == null)
        {
            _logger.LogInformation("Tool call UseSkill rejected — '{SkillName}' not in this agent's resolved skill set", name);
            return NotAvailableMessage(name);
        }

        string? body = await _corpusService.LoadSkillBodyAsync(allowed.Name, CancellationToken.None);
        if (body == null)
        {
            _logger.LogWarning("Tool call UseSkill for '{SkillName}' — skill is allowed but failed to load from the corpus", allowed.Name);
            return $"Skill '{allowed.Name}' is available to you but could not be loaded from the skill " +
                   "corpus (it may have failed validation at startup — check GET /skills/status).";
        }

        _logger.LogInformation("Tool call UseSkill loaded '{SkillName}' ({Length} chars)", allowed.Name, body.Length);
        return body;
    }

    [Description(
        "Read a supplementary reference file belonging to a skill you have already loaded with " +
        "UseSkill. Only call this for a path the skill's own instructions told you to read.")]
    public async Task<string> ReadSkillResource(
        [Description("Skill name, e.g. 'remotion-markup'.")] string skill,
        [Description("Relative resource path exactly as given in the skill's own instructions.")] string resourcePath)
    {
        SkillDescriptor? allowed = FindAllowed(skill);
        if (allowed == null)
        {
            _logger.LogInformation("Tool call ReadSkillResource rejected — '{SkillName}' not in this agent's resolved skill set", skill);
            return NotAvailableMessage(skill);
        }

        string? content = await _corpusService.ReadSkillResourceAsync(allowed.Name, resourcePath, CancellationToken.None);
        if (content == null)
        {
            _logger.LogInformation(
                "Tool call ReadSkillResource found=false skill={SkillName} path={ResourcePath}", allowed.Name, resourcePath);
            return $"Resource '{resourcePath}' was not found under skill '{allowed.Name}'.";
        }

        _logger.LogInformation(
            "Tool call ReadSkillResource found=true skill={SkillName} path={ResourcePath}", allowed.Name, resourcePath);
        return content;
    }

    private SkillDescriptor? FindAllowed(string name) =>
        !string.IsNullOrWhiteSpace(name)
            ? _allowedSkills.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            : null;

    private string NotAvailableMessage(string requestedName)
    {
        string available = _allowedSkills.Count > 0
            ? string.Join(", ", _allowedSkills.Select(s => s.Name))
            : "(none)";
        return $"Skill '{requestedName}' is not available to you. Skills available to you: {available}.";
    }
}

/// <summary>
/// Constructs <see cref="SkillAgentTools"/> instances bound to a specific resolved skill set, and
/// exposes them as <see cref="AITool"/>s ready to add to an agent's tool list. A fresh
/// <see cref="SkillAgentTools"/> instance is created per call — the enforcement boundary lives in
/// that instance's own <c>_allowedSkills</c> field, closed over by the <see cref="AIFunction"/>
/// delegates <see cref="AIFunctionFactory.Create(Delegate, AIFunctionFactoryOptions?)"/> wraps.
/// </summary>
public interface ISkillAgentToolsFactory
{
    IReadOnlyList<AITool> CreateBoundTools(IReadOnlyList<SkillDescriptor> allowedSkills);
}

public sealed class SkillAgentToolsFactory : ISkillAgentToolsFactory
{
    private readonly ISkillCorpusService _corpusService;
    private readonly ILogger<SkillAgentTools> _logger;

    public SkillAgentToolsFactory(ISkillCorpusService corpusService, ILogger<SkillAgentTools> logger)
    {
        _corpusService = corpusService;
        _logger = logger;
    }

    public IReadOnlyList<AITool> CreateBoundTools(IReadOnlyList<SkillDescriptor> allowedSkills)
    {
        SkillAgentTools instance = new(_corpusService, allowedSkills, _logger);
        return
        [
            AIFunctionFactory.Create(instance.UseSkill),
            AIFunctionFactory.Create(instance.ReadSkillResource)
        ];
    }
}
