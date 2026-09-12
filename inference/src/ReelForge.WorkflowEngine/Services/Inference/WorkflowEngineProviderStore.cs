using Microsoft.EntityFrameworkCore;
using ReelForge.Shared.Data.Models;
using ReelForge.Shared.Inference;
using ReelForge.WorkflowEngine.Data;

namespace ReelForge.WorkflowEngine.Services.Inference;

/// <summary>
/// Reads inference providers and per-agent overrides through the WorkflowEngine's read-only,
/// excluded-from-migrations mapping of the tables the Inference API owns.
/// </summary>
public sealed class WorkflowEngineProviderStore : IInferenceProviderStore
{
    private readonly WorkflowEngineDbContext _db;

    public WorkflowEngineProviderStore(WorkflowEngineDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<InferenceProvider>> LoadEnabledAsync(CancellationToken ct)
    {
        return await _db.InferenceProviders
            .AsNoTracking()
            .Where(p => p.IsEnabled)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyDictionary<Guid, Guid?>> LoadAgentOverridesAsync(CancellationToken ct)
    {
        return await _db.AgentDefinitions
            .AsNoTracking()
            .Select(a => new { a.Id, a.InferenceProviderId })
            .ToDictionaryAsync(a => a.Id, a => a.InferenceProviderId, ct);
    }
}
