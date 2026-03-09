using System.ComponentModel;
using System.Text.Json;
using ReelForge.WorkflowEngine.Services.RemotionSkills;

namespace ReelForge.WorkflowEngine.Agents.Tools;

/// <summary>
/// Agent tools that expose the Remotion skills knowledge base.
/// Agents can search for relevant Remotion topics and read detailed
/// documentation, code examples, and best practices.
/// </summary>
public class RemotionSkillsAgentTools
{
    private readonly RemotionSkillsService _skillsService;

    public RemotionSkillsAgentTools(RemotionSkillsService skillsService)
    {
        _skillsService = skillsService;
    }

    [Description(
        "Search the Remotion skills knowledge base for documentation on a specific topic. " +
        "Returns a list of matching skill files with topic names and descriptions. " +
        "Use this to find the right skill file before reading it. " +
        "Example queries: 'animations', 'transitions', '3d', 'audio', 'compositions', 'timing'.")]
    public async Task<string> SearchRemotionSkills(
        [Description("Search query — a topic keyword like 'animations', 'transitions', 'fonts', '3d', 'audio', etc.")] string query)
    {
        IReadOnlyList<SkillFileEntry> results = await _skillsService.SearchSkillsAsync(query);

        if (results.Count == 0)
            return "No matching Remotion skill files found. Try a different keyword.";

        var summaries = results.Select(e => new { e.Topic, e.Description, e.RelativePath });
        return JsonSerializer.Serialize(summaries, new JsonSerializerOptions { WriteIndented = true });
    }

    [Description(
        "Read a Remotion skill document by topic name or file path. " +
        "Returns the full markdown content with documentation, code examples, and best practices. " +
        "Always call SearchRemotionSkills first to find the correct topic name. " +
        "Example topics: 'animations', 'compositions', 'transitions', 'timing', 'sequencing'.")]
    public async Task<string> ReadRemotionSkill(
        [Description("Topic name (e.g. 'animations', 'transitions') or relative path (e.g. 'rules/animations.md')")] string topicOrPath)
    {
        string? content = await _skillsService.ReadSkillAsync(topicOrPath);

        if (content == null)
            return $"Remotion skill '{topicOrPath}' not found. Use SearchRemotionSkills to discover available topics.";

        return content;
    }

    [Description(
        "List all available Remotion skill topics. Returns the complete index of Remotion documentation " +
        "covering animations, compositions, timing, transitions, audio, 3D, text, and more.")]
    public async Task<string> ListAllRemotionSkills()
    {
        IReadOnlyList<SkillFileEntry> entries = await _skillsService.ListSkillsAsync();

        var summaries = entries.Select(e => new { e.Topic, e.Description });
        return JsonSerializer.Serialize(summaries, new JsonSerializerOptions { WriteIndented = true });
    }
}
