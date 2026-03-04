using Microsoft.EntityFrameworkCore;
using ReelForge.Inference.Agents;
using ReelForge.Inference.Data.Models;

namespace ReelForge.Inference.Data;

/// <summary>
/// Seeds built-in agent definitions and the default workflow.
/// </summary>
public static class DatabaseSeeder
{
    /// <summary>Applies pending migrations and seeds initial data.</summary>
    public static async Task SeedAsync(IServiceProvider services)
    {
        using IServiceScope scope = services.CreateScope();
        ReelForgeDbContext db = scope.ServiceProvider.GetRequiredService<ReelForgeDbContext>();
        IAgentRegistry agentRegistry = scope.ServiceProvider.GetRequiredService<IAgentRegistry>();

        await db.Database.MigrateAsync();

        await SeedAgentDefinitionsAsync(db, agentRegistry);
    }

    private static async Task SeedAgentDefinitionsAsync(ReelForgeDbContext db, IAgentRegistry agentRegistry)
    {
        IReadOnlyList<IReelForgeAgent> agents = agentRegistry.GetAll();

        foreach (IReelForgeAgent agent in agents)
        {
            bool exists = await db.AgentDefinitions.AnyAsync(a => a.AgentType == agent.AgentType && a.IsBuiltIn);
            if (exists) continue;

            AgentDefinition definition = new()
            {
                Id = Guid.NewGuid(),
                Name = agent.Name,
                Description = agent.Description,
                SystemPrompt = agent.SystemPrompt,
                AgentType = agent.AgentType,
                IsBuiltIn = true,
                OwnerId = null,
                CreatedAt = DateTime.UtcNow
            };

            db.AgentDefinitions.Add(definition);
        }

        await db.SaveChangesAsync();
    }
}
