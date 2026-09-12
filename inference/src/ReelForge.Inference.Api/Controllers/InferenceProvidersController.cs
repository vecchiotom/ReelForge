using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.AI;
using Npgsql;
using ReelForge.Inference.Api.Controllers.Dto;
using ReelForge.Inference.Api.Data;
using ReelForge.Shared.Auth;
using ReelForge.Shared.Data.Models;
using ReelForge.Shared.Inference;

namespace ReelForge.Inference.Api.Controllers;

/// <summary>
/// Admin-only CRUD for configured chat-completion inference providers (Azure OpenAI or an
/// OpenAI-compatible endpoint), plus connectivity testing. Deliberately routed under
/// <c>api/v1/inference-providers</c> rather than <c>api/v1/admin/*</c> — nginx routes
/// <c>/api/v1/admin/*</c> to the Go API, so this controller would 404 there.
/// </summary>
[ApiController]
[Route("api/v1/inference-providers")]
[Authorize]
public class InferenceProvidersController : ControllerBase
{
    private readonly InferenceApiDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ISecretProtector _secretProtector;
    private readonly IChatClientFactory _chatClientFactory;

    public InferenceProvidersController(
        InferenceApiDbContext db,
        ICurrentUser currentUser,
        ISecretProtector secretProtector,
        IChatClientFactory chatClientFactory)
    {
        _db = db;
        _currentUser = currentUser;
        _secretProtector = secretProtector;
        _chatClientFactory = chatClientFactory;
    }

    [HttpGet]
    public async Task<ActionResult<List<InferenceProviderResponse>>> List(CancellationToken ct)
    {
        if (!_currentUser.IsAdmin) return Forbid();

        List<InferenceProvider> providers = await _db.InferenceProviders
            .OrderBy(p => p.Name)
            .ToListAsync(ct);

        return Ok(providers.Select(MapToResponse));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<InferenceProviderResponse>> Get(Guid id, CancellationToken ct)
    {
        if (!_currentUser.IsAdmin) return Forbid();

        InferenceProvider? entity = await _db.InferenceProviders.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (entity == null) return NotFound();

        return Ok(MapToResponse(entity));
    }

    [HttpPost]
    public async Task<ActionResult<InferenceProviderResponse>> Create(
        [FromBody] CreateInferenceProviderRequest request, CancellationToken ct)
    {
        if (!_currentUser.IsAdmin) return Forbid();

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { error = "Name is required." });
        }

        if (!TryParseKind(request.Kind, out InferenceProviderKind kind))
        {
            return BadRequest(new { error = $"Invalid kind '{request.Kind}'. Expected 'AzureOpenAI' or 'OpenAICompatible'." });
        }

        bool nameTaken = await _db.InferenceProviders.AnyAsync(p => p.Name == request.Name, ct);
        if (nameTaken)
        {
            return Conflict(new { error = $"A provider named '{request.Name}' already exists." });
        }

        DateTime now = DateTime.UtcNow;
        InferenceProvider entity = new()
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Kind = kind,
            Endpoint = request.Endpoint,
            ModelName = request.ModelName,
            IsDefault = request.IsDefault,
            IsEnabled = request.IsEnabled,
            TimeoutSeconds = request.TimeoutSeconds,
            CreatedAt = now,
            UpdatedAt = now
        };
        ApplyApiKey(entity, request.ApiKey);

        await using IDbContextTransaction tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            if (entity.IsDefault)
            {
                // Clear IsDefault on every existing row in the same transaction as the insert,
                // so at most one row is ever the default (also required by the partial unique
                // index on is_default).
                await ClearOtherDefaultsAsync(exceptId: null, ct);
            }

            _db.InferenceProviders.Add(entity);
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            await tx.RollbackAsync(ct);
            return Conflict(new { error = $"A provider named '{request.Name}' already exists." });
        }

        return CreatedAtAction(nameof(Get), new { id = entity.Id }, MapToResponse(entity));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<InferenceProviderResponse>> Update(
        Guid id, [FromBody] UpdateInferenceProviderRequest request, CancellationToken ct)
    {
        if (!_currentUser.IsAdmin) return Forbid();

        InferenceProvider? entity = await _db.InferenceProviders.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (entity == null) return NotFound();

        if (request.Name != null)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest(new { error = "Name cannot be empty." });
            }

            bool nameTaken = await _db.InferenceProviders.AnyAsync(p => p.Id != id && p.Name == request.Name, ct);
            if (nameTaken)
            {
                return Conflict(new { error = $"A provider named '{request.Name}' already exists." });
            }

            entity.Name = request.Name;
        }

        if (request.Kind != null)
        {
            if (!TryParseKind(request.Kind, out InferenceProviderKind kind))
            {
                return BadRequest(new { error = $"Invalid kind '{request.Kind}'. Expected 'AzureOpenAI' or 'OpenAICompatible'." });
            }

            entity.Kind = kind;
        }

        // string? fields follow the Go admin-user "omit = unchanged" convention: null/absent
        // leaves the stored value untouched, a supplied (non-null) value replaces it.
        if (request.Endpoint != null) entity.Endpoint = request.Endpoint;
        if (request.ModelName != null) entity.ModelName = request.ModelName;
        if (request.IsEnabled.HasValue) entity.IsEnabled = request.IsEnabled.Value;
        if (request.TimeoutSeconds.HasValue) entity.TimeoutSeconds = request.TimeoutSeconds.Value;

        // ApiKey has one extra state beyond the usual convention: null/absent = unchanged,
        // "" = explicitly clear the stored key, anything else = replace it (R3/B.5).
        ApplyApiKey(entity, request.ApiKey);

        entity.UpdatedAt = DateTime.UtcNow;

        await using IDbContextTransaction tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            if (request.IsDefault == true)
            {
                entity.IsDefault = true;
                await ClearOtherDefaultsAsync(exceptId: entity.Id, ct);
            }
            else if (request.IsDefault == false)
            {
                entity.IsDefault = false;
            }

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            await tx.RollbackAsync(ct);
            return Conflict(new { error = $"A provider named '{entity.Name}' already exists." });
        }

        return Ok(MapToResponse(entity));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (!_currentUser.IsAdmin) return Forbid();

        InferenceProvider? entity = await _db.InferenceProviders.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (entity == null) return NotFound();

        if (entity.IsDefault)
        {
            return Conflict(new
            {
                error = "Cannot delete the default inference provider. Mark a different provider as default first."
            });
        }

        // Agents referencing this provider via InferenceProviderId fall back to the global
        // default (OnDelete(SetNull) on the FK), so no cascade cleanup is needed here.
        _db.InferenceProviders.Remove(entity);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/test")]
    public async Task<ActionResult<TestInferenceProviderResponse>> TestSaved(Guid id, CancellationToken ct)
    {
        if (!_currentUser.IsAdmin) return Forbid();

        InferenceProvider? entity = await _db.InferenceProviders.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (entity == null) return NotFound();

        string apiKey = string.Empty;
        if (!string.IsNullOrEmpty(entity.ApiKeyEncrypted))
        {
            if (!_secretProtector.TryUnprotect(entity.ApiKeyEncrypted, out apiKey))
            {
                // R3 degradation path: a lost/mismatched Data Protection key ring must surface
                // as a clean, readable failure — never an unhandled exception or a 500.
                TestInferenceProviderResponse degraded = new(false, 0, "stored key could not be decrypted", null);
                await PersistTestResultAsync(entity, degraded, ct);
                return Ok(degraded);
            }
        }

        ResolvedInferenceProvider resolved = new(
            entity.Id, entity.Name, entity.Kind, entity.Endpoint, entity.ModelName,
            apiKey, entity.TimeoutSeconds ?? DefaultTimeoutSeconds);

        TestInferenceProviderResponse result = await RunTestAsync(resolved, ct);
        await PersistTestResultAsync(entity, result, ct);
        return Ok(result);
    }

    [HttpPost("test")]
    public async Task<ActionResult<TestInferenceProviderResponse>> TestUnsaved(
        [FromBody] TestInferenceProviderRequest request, CancellationToken ct)
    {
        if (!_currentUser.IsAdmin) return Forbid();

        string? kindStr = request.Kind;
        string endpoint = request.Endpoint ?? string.Empty;
        string modelName = request.ModelName ?? string.Empty;
        string apiKey = request.ApiKey ?? string.Empty;
        int timeoutSeconds = DefaultTimeoutSeconds;

        if (request.Id.HasValue)
        {
            InferenceProvider? saved = await _db.InferenceProviders.FirstOrDefaultAsync(p => p.Id == request.Id.Value, ct);
            if (saved == null)
            {
                return NotFound(new { error = "Referenced provider not found." });
            }

            kindStr ??= saved.Kind.ToString();
            if (request.Endpoint == null) endpoint = saved.Endpoint;
            if (request.ModelName == null) modelName = saved.ModelName;
            timeoutSeconds = saved.TimeoutSeconds ?? timeoutSeconds;

            if (request.ApiKey == null)
            {
                // Reuse the stored, decrypted key instead of requiring the caller to resend it.
                if (!string.IsNullOrEmpty(saved.ApiKeyEncrypted))
                {
                    if (!_secretProtector.TryUnprotect(saved.ApiKeyEncrypted, out string storedKey))
                    {
                        return Ok(new TestInferenceProviderResponse(false, 0, "stored key could not be decrypted", null));
                    }

                    apiKey = storedKey;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(kindStr) || !TryParseKind(kindStr, out InferenceProviderKind kind))
        {
            return BadRequest(new { error = $"Invalid or missing kind '{kindStr}'. Expected 'AzureOpenAI' or 'OpenAICompatible'." });
        }

        ResolvedInferenceProvider resolved = new(
            request.Id, "test", kind, endpoint, modelName, apiKey, timeoutSeconds);

        TestInferenceProviderResponse result = await RunTestAsync(resolved, ct);
        return Ok(result);
    }

    private const int DefaultTimeoutSeconds = 300;

    private async Task<TestInferenceProviderResponse> RunTestAsync(ResolvedInferenceProvider provider, CancellationToken ct)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            IChatClient chatClient = _chatClientFactory.Get(provider);
            List<ChatMessage> messages = new() { new ChatMessage(ChatRole.User, "ping") };
            ChatOptions options = new() { MaxOutputTokens = 8 };

            ChatResponse response = await chatClient.GetResponseAsync(messages, options, ct);
            stopwatch.Stop();

            string? preview = response.Text;
            if (!string.IsNullOrEmpty(preview) && preview.Length > 200)
            {
                preview = preview[..200];
            }

            return new TestInferenceProviderResponse(true, stopwatch.ElapsedMilliseconds, null, preview);
        }
        catch (Exception ex)
        {
            // Never let a bad endpoint/model/key escape as a 500 — the whole point of this
            // endpoint is to let an admin validate a config before (or after) saving it.
            stopwatch.Stop();
            return new TestInferenceProviderResponse(false, stopwatch.ElapsedMilliseconds, ex.Message, null);
        }
    }

    private async Task PersistTestResultAsync(InferenceProvider entity, TestInferenceProviderResponse result, CancellationToken ct)
    {
        entity.LastTestAt = DateTime.UtcNow;
        entity.LastTestOk = result.Ok;
        entity.LastTestError = result.Error;
        await _db.SaveChangesAsync(ct);
    }

    private async Task ClearOtherDefaultsAsync(Guid? exceptId, CancellationToken ct)
    {
        IQueryable<InferenceProvider> query = _db.InferenceProviders.Where(p => p.IsDefault);
        if (exceptId.HasValue)
        {
            query = query.Where(p => p.Id != exceptId.Value);
        }

        await query.ExecuteUpdateAsync(setters => setters.SetProperty(p => p.IsDefault, false), ct);
    }

    private void ApplyApiKey(InferenceProvider entity, string? apiKey)
    {
        if (apiKey == null)
        {
            // Omitted/null: leave the stored key untouched.
            return;
        }

        if (apiKey.Length == 0)
        {
            // Explicit empty string: clear the key.
            entity.ApiKeyEncrypted = null;
            entity.ApiKeyLastFour = null;
            return;
        }

        entity.ApiKeyEncrypted = _secretProtector.Protect(apiKey);
        entity.ApiKeyLastFour = apiKey.Length <= 4 ? apiKey : apiKey[^4..];
    }

    private static bool TryParseKind(string? value, out InferenceProviderKind kind) =>
        Enum.TryParse(value, ignoreCase: true, out kind);

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: "23505" };

    private static InferenceProviderResponse MapToResponse(InferenceProvider p) => new(
        p.Id, p.Name, p.Kind.ToString(), p.Endpoint, p.ModelName,
        !string.IsNullOrEmpty(p.ApiKeyEncrypted), p.ApiKeyLastFour,
        p.IsDefault, p.IsEnabled, p.TimeoutSeconds, p.CreatedAt, p.UpdatedAt,
        p.LastTestAt, p.LastTestOk, p.LastTestError);
}
