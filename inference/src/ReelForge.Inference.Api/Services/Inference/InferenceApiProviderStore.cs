using Microsoft.EntityFrameworkCore;
using ReelForge.Inference.Api.Data;
using ReelForge.Shared.Data.Models;
using ReelForge.Shared.Inference;

namespace ReelForge.Inference.Api.Services.Inference;

/// <summary>
/// Reads inference providers and per-agent overrides from the tables the Inference API owns.
/// </summary>
public sealed class InferenceApiProviderStore : IInferenceProviderStore
{
    private readonly InferenceApiDbContext _db;

    public InferenceApiProviderStore(InferenceApiDbContext db)
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
