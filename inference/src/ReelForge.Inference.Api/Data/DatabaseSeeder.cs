using Microsoft.EntityFrameworkCore;
using ReelForge.Shared.Data.Models;

namespace ReelForge.Inference.Api.Data;

/// <summary>
/// Seeds built-in agent definitions on startup.
/// </summary>
public static class DatabaseSeeder
{
    private static readonly Dictionary<AgentType, (string Name, string Description, string Color)> BuiltInAgents = new()
    {
        { AgentType.CodeStructureAnalyzer, ("CodeStructureAnalyzer", "Maps the overall directory/module structure of the webapp source.", "#3B82F6") },
        { AgentType.DependencyAnalyzer, ("DependencyAnalyzer", "Enumerates frameworks, libraries, and major dependencies.", "#2563EB") },
        { AgentType.ComponentInventoryAnalyzer, ("ComponentInventoryAnalyzer", "Enumerates all UI components, their props and basic responsibilities.", "#1D4ED8") },
        { AgentType.RouteAndApiAnalyzer, ("RouteAndApiAnalyzer", "Extracts all routes, API endpoints, and navigation structure.", "#60A5FA") },
        { AgentType.StyleAndThemeExtractor, ("StyleAndThemeExtractor", "Extracts color palette, typography, spacing, and branding tokens.", "#93C5FD") },
        { AgentType.RemotionComponentTranslator, ("RemotionComponentTranslator", "Produces Remotion React component code that recreates app screens as video frames.", "#10B981") },
        { AgentType.AnimationStrategyAgent, ("AnimationStrategy", "Defines transition timing, animation sequencing, and scene ordering.", "#34D399") },
        { AgentType.DirectorAgent, ("Director", "Composes the overall video narrative structure.", "#8B5CF6") },
        { AgentType.ScriptwriterAgent, ("Scriptwriter", "Writes the voiceover/caption script for each scene.", "#A78BFA") },
        { AgentType.AuthorAgent, ("Author", "Assembles all outputs into a RenderManifest for Remotion.", "#7C3AED") },
        { AgentType.ReviewAgent, ("Review", "Scores output quality and provides structured feedback.", "#F59E0B") },
        { AgentType.FileSummarizerAgent, ("FileSummarizer", "Produces concise summaries of uploaded files.", "#14B8A6") },
    };

    /// <summary>Applies pending migrations and seeds initial data.</summary>
    public static async Task SeedAsync(IServiceProvider services)
    {
        using IServiceScope scope = services.CreateScope();
        InferenceApiDbContext db = scope.ServiceProvider.GetRequiredService<InferenceApiDbContext>();

        await db.Database.MigrateAsync();

        await SeedAgentDefinitionsAsync(db);
    }

    private static async Task SeedAgentDefinitionsAsync(InferenceApiDbContext db)
    {
        foreach (var (agentType, (name, description, color)) in BuiltInAgents)
        {
            AgentDefinition? existing = await db.AgentDefinitions
                .FirstOrDefaultAsync(a => a.AgentType == agentType && a.IsBuiltIn);

            if (existing != null)
            {
                if (existing.Color == null)
                    existing.Color = color;
                continue;
            }

            db.AgentDefinitions.Add(new AgentDefinition
            {
                Id = Guid.NewGuid(),
                Name = name,
                Description = description,
                SystemPrompt = string.Empty,
                AgentType = agentType,
                IsBuiltIn = true,
                OwnerId = null,
                Color = color,
                CreatedAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();
    }
}
