using System.Diagnostics;
using System.Net;
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
    private readonly ITranscriptionClientFactory _transcriptionClientFactory;
    private readonly ILogger<InferenceProvidersController> _logger;

    public InferenceProvidersController(
        InferenceApiDbContext db,
        ICurrentUser currentUser,
        ISecretProtector secretProtector,
        IChatClientFactory chatClientFactory,
        ITranscriptionClientFactory transcriptionClientFactory,
        ILogger<InferenceProvidersController> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _secretProtector = secretProtector;
        _chatClientFactory = chatClientFactory;
        _transcriptionClientFactory = transcriptionClientFactory;
        _logger = logger;
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

        if (string.IsNullOrWhiteSpace(request.Endpoint))
        {
            return BadRequest(new { error = "Endpoint is required." });
        }

        if (IsDisallowedEndpoint(request.Endpoint))
        {
            return BadRequest(new { error = "Endpoint must be a public https/http URL; internal/private/loopback addresses are not allowed." });
        }

        if (string.IsNullOrWhiteSpace(request.ModelName))
        {
            return BadRequest(new { error = "ModelName is required." });
        }

        if (request.TimeoutSeconds.HasValue && request.TimeoutSeconds.Value <= 0)
        {
            return BadRequest(new { error = "TimeoutSeconds must be a positive number when provided." });
        }

        if (!TryParseKind(request.Kind, out InferenceProviderKind kind))
        {
            return BadRequest(new { error = $"Invalid kind '{request.Kind}'. Expected one of {KnownKinds}." });
        }

        // Default to Chat when omitted, for backward compatibility with any existing frontend
        // calls made before the Chat/Transcription capability split existed.
        InferenceProviderCapability capability = InferenceProviderCapability.Chat;
        if (!string.IsNullOrWhiteSpace(request.Capability) && !TryParseCapability(request.Capability, out capability))
        {
            return BadRequest(new { error = $"Invalid capability '{request.Capability}'. Expected 'Chat', 'Transcription', or 'Vision'." });
        }

        if (IsUnsupportedCombination(kind, capability, out string unsupported))
        {
            return BadRequest(new { error = unsupported });
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
            Capability = capability,
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
                // Clear IsDefault on every existing row of the SAME capability in the same
                // transaction as the insert, so at most one row per capability is ever the
                // default (also required by the composite (capability, is_default) partial
                // unique index) — a new Transcription default must never clear a Chat default,
                // and vice versa.
                await ClearOtherDefaultsAsync(capability, exceptId: null, ct);
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
                return BadRequest(new { error = $"Invalid kind '{request.Kind}'. Expected one of {KnownKinds}." });
            }

            entity.Kind = kind;
        }

        if (request.Capability != null)
        {
            if (!TryParseCapability(request.Capability, out InferenceProviderCapability capability))
            {
                return BadRequest(new { error = $"Invalid capability '{request.Capability}'. Expected 'Chat', 'Transcription', or 'Vision'." });
            }

            // Changing capability on a row that is currently the default is ambiguous: it would
            // silently move the "default" flag to a different capability bucket, either
            // colliding with that capability's existing default (reported, confusingly, as a
            // name conflict — it's actually the composite (capability, is_default) unique index)
            // or leaving the OLD capability with no default at all. Require the request to
            // explicitly say what should happen to IsDefault in the same call (found by Copilot
            // review).
            if (capability != entity.Capability && entity.IsDefault && !request.IsDefault.HasValue)
            {
                return BadRequest(new
                {
                    error = "This provider is currently the default. Explicitly set isDefault (true or false) " +
                            "in the same request when changing its capability, so the default assignment for " +
                            "both capabilities stays unambiguous."
                });
            }

            entity.Capability = capability;
        }

        // Checked against the POST-UPDATE state rather than against the request, because kind and
        // capability are applied in separate blocks above and either may be absent: a request that
        // only flips kind to Anthropic on a row that is already Transcription would otherwise slip
        // through, as would the mirror case.
        if (IsUnsupportedCombination(entity.Kind, entity.Capability, out string unsupported))
        {
            return BadRequest(new { error = unsupported });
        }

        // string? fields follow the Go admin-user "omit = unchanged" convention: null/absent
        // leaves the stored value untouched, a supplied (non-null) value replaces it.
        if (request.Endpoint != null)
        {
            if (string.IsNullOrWhiteSpace(request.Endpoint))
            {
                return BadRequest(new { error = "Endpoint cannot be empty." });
            }
            if (IsDisallowedEndpoint(request.Endpoint))
            {
                return BadRequest(new { error = "Endpoint must be a public https/http URL; internal/private/loopback addresses are not allowed." });
            }
            entity.Endpoint = request.Endpoint;
        }
        if (request.ModelName != null)
        {
            if (string.IsNullOrWhiteSpace(request.ModelName))
            {
                return BadRequest(new { error = "ModelName cannot be empty." });
            }
            entity.ModelName = request.ModelName;
        }
        if (request.IsEnabled.HasValue) entity.IsEnabled = request.IsEnabled.Value;
        if (request.TimeoutSeconds.HasValue)
        {
            if (request.TimeoutSeconds.Value <= 0)
            {
                return BadRequest(new { error = "TimeoutSeconds must be a positive number." });
            }
            entity.TimeoutSeconds = request.TimeoutSeconds.Value;
        }

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
                // Scoped to entity.Capability (post any Capability change above) so a
                // Transcription default is never cleared by a Chat default being set, and
                // vice versa (see the composite (capability, is_default) unique index).
                await ClearOtherDefaultsAsync(entity.Capability, exceptId: entity.Id, ct);
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

        // The same guard Create/Update/TestUnsaved apply. Without it, an unsupported row — only
        // reachable by a direct database write, which is exactly the case the factory's defensive
        // arm exists for — reaches the factory, whose explanatory NotSupportedException is then
        // swallowed by the catch-all below and reported as the generic "Provider test failed.".
        // That defeats the purpose of the one endpoint whose job is to explain misconfiguration.
        if (IsUnsupportedCombination(entity.Kind, entity.Capability, out string unsupportedSaved))
        {
            return BadRequest(new { error = unsupportedSaved });
        }

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

        TestInferenceProviderResponse result;
        if (entity.Capability == InferenceProviderCapability.Transcription)
        {
            ResolvedTranscriptionProvider resolvedTranscription = new(
                entity.Id, entity.Name, entity.Kind, entity.Endpoint, entity.ModelName,
                apiKey, entity.TimeoutSeconds ?? DefaultTimeoutSeconds);
            result = await RunTranscriptionTestAsync(resolvedTranscription, ct);
        }
        else if (entity.Capability == InferenceProviderCapability.Vision)
        {
            ResolvedInferenceProvider resolvedVision = new(
                entity.Id, entity.Name, entity.Kind, entity.Endpoint, entity.ModelName,
                apiKey, entity.TimeoutSeconds ?? DefaultTimeoutSeconds);
            result = await RunVisionTestAsync(resolvedVision, ct);
        }
        else
        {
            ResolvedInferenceProvider resolved = new(
                entity.Id, entity.Name, entity.Kind, entity.Endpoint, entity.ModelName,
                apiKey, entity.TimeoutSeconds ?? DefaultTimeoutSeconds);
            result = await RunTestAsync(resolved, ct);
        }

        await PersistTestResultAsync(entity, result, ct);
        return Ok(result);
    }

    [HttpPost("test")]
    public async Task<ActionResult<TestInferenceProviderResponse>> TestUnsaved(
        [FromBody] TestInferenceProviderRequest request, CancellationToken ct)
    {
        if (!_currentUser.IsAdmin) return Forbid();

        string? kindStr = request.Kind;
        string? capabilityStr = request.Capability;
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

            if (request.ApiKey == null)
            {
                // When reusing the stored key, also use the stored endpoint, kind, and model name.
                // Only accept caller-supplied endpoint/kind/modelName when providing a fresh apiKey.
                kindStr = saved.Kind.ToString();
                capabilityStr = saved.Capability.ToString();
                endpoint = saved.Endpoint;
                modelName = saved.ModelName;
                timeoutSeconds = saved.TimeoutSeconds ?? timeoutSeconds;

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
            else
            {
                // When caller supplies a fresh apiKey, allow caller-supplied endpoint/kind/modelName
                // (for testing an updated config), but use saved values as fallback if omitted.
                kindStr ??= saved.Kind.ToString();
                capabilityStr ??= saved.Capability.ToString();
                if (request.Endpoint == null) endpoint = saved.Endpoint;
                if (request.ModelName == null) modelName = saved.ModelName;
                timeoutSeconds = saved.TimeoutSeconds ?? timeoutSeconds;
            }
        }

        if (string.IsNullOrWhiteSpace(kindStr) || !TryParseKind(kindStr, out InferenceProviderKind kind))
        {
            return BadRequest(new { error = $"Invalid or missing kind '{kindStr}'. Expected one of {KnownKinds}." });
        }

        InferenceProviderCapability capability = InferenceProviderCapability.Chat;
        if (!string.IsNullOrWhiteSpace(capabilityStr) && !TryParseCapability(capabilityStr, out capability))
        {
            return BadRequest(new { error = $"Invalid capability '{capabilityStr}'. Expected 'Chat', 'Transcription', or 'Vision'." });
        }

        if (IsUnsupportedCombination(kind, capability, out string unsupportedCombination))
        {
            return BadRequest(new { error = unsupportedCombination });
        }

        TestInferenceProviderResponse result;
        if (capability == InferenceProviderCapability.Transcription)
        {
            ResolvedTranscriptionProvider resolvedTranscription = new(
                request.Id, "test", kind, endpoint, modelName, apiKey, timeoutSeconds);
            result = await RunTranscriptionTestAsync(resolvedTranscription, ct);
        }
        else if (capability == InferenceProviderCapability.Vision)
        {
            ResolvedInferenceProvider resolvedVision = new(
                request.Id, "test", kind, endpoint, modelName, apiKey, timeoutSeconds);
            result = await RunVisionTestAsync(resolvedVision, ct);
        }
        else
        {
            ResolvedInferenceProvider resolved = new(
                request.Id, "test", kind, endpoint, modelName, apiKey, timeoutSeconds);
            result = await RunTestAsync(resolved, ct);
        }

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
            // Reasoning models (e.g. Qwen3 with thinking mode on) spend output tokens on a
            // hidden reasoning chain before emitting any visible content — a too-tight budget
            // gets exhausted mid-thought and comes back with content: null, which used to read
            // as success here (no emptiness check) even though nothing useful was returned.
            ChatOptions options = new() { MaxOutputTokens = PingMaxOutputTokens(provider.Kind) };

            ChatResponse response = await chatClient.GetResponseAsync(messages, options, ct);
            stopwatch.Stop();

            string? preview = response.Text;
            if (!string.IsNullOrEmpty(preview) && preview.Length > 200)
            {
                preview = preview[..200];
            }

            bool ok = !string.IsNullOrWhiteSpace(preview);
            return new TestInferenceProviderResponse(ok, stopwatch.ElapsedMilliseconds, ok ? null : "Provider returned an empty response.", preview);
        }
        catch (Exception ex)
        {
            // Never let a bad endpoint/model/key escape as a 500 — the whole point of this
            // endpoint is to let an admin validate a config before (or after) saving it.
            stopwatch.Stop();
            _logger.LogError(ex, "Inference provider test failed");
            return new TestInferenceProviderResponse(false, stopwatch.ElapsedMilliseconds, "Provider test failed.", null);
        }
    }

    /// <summary>
    /// Transcribes a short, in-memory synthesized silent WAV as a 1-shot connectivity ping for a
    /// Transcription-capability provider — mirrors <see cref="RunTestAsync"/>'s chat-side "ping"
    /// message. Generated in pure C# (no ffmpeg/fixture file) since this API service has no
    /// video-processing dependency and shouldn't gain one just for a health-check.
    /// </summary>
    private async Task<TestInferenceProviderResponse> RunTranscriptionTestAsync(ResolvedTranscriptionProvider provider, CancellationToken ct)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            ITranscriptionClient client = _transcriptionClientFactory.Get(provider);
            await using MemoryStream wav = BuildSilentWav();

            TranscriptResult result = await client.TranscribeAsync(wav, "ping.wav", language: null, wordTimestamps: false, ct);
            stopwatch.Stop();

            string? preview = result.Text;
            if (!string.IsNullOrEmpty(preview) && preview.Length > 200)
            {
                preview = preview[..200];
            }

            return new TestInferenceProviderResponse(true, stopwatch.ElapsedMilliseconds, null, preview);
        }
        catch (Exception ex)
        {
            // Same discipline as RunTestAsync: never let a bad endpoint/model/key escape as a 500.
            stopwatch.Stop();
            _logger.LogError(ex, "Transcription provider test failed");
            return new TestInferenceProviderResponse(false, stopwatch.ElapsedMilliseconds, "Provider test failed.", null);
        }
    }

    /// <summary>
    /// Sends <see cref="TinyTestJpeg"/> through a Vision-capability provider as a 1-shot
    /// connectivity ping — mirrors <see cref="RunTestAsync"/>'s chat-side "ping" message and
    /// <see cref="RunTranscriptionTestAsync"/>'s synthesized-audio ping. Reuses
    /// <see cref="IChatClientFactory"/> unchanged (a vision call is just a chat call with an
    /// image content part — see <c>IInferenceProviderResolver.ResolveVisionAsync</c>), so no new
    /// client factory is needed for this test path either.
    /// </summary>
    private async Task<TestInferenceProviderResponse> RunVisionTestAsync(ResolvedInferenceProvider provider, CancellationToken ct)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            IChatClient chatClient = _chatClientFactory.Get(provider);
            ChatMessage message = new(
                ChatRole.User,
                new List<AIContent>
                {
                    new TextContent("Reply with only the word ok."),
                    new DataContent(TinyTestJpeg, "image/jpeg")
                });
            // See the matching comment in RunTestAsync — a reasoning model needs headroom beyond
            // its hidden chain-of-thought before content ever appears.
            ChatOptions options = new() { MaxOutputTokens = PingMaxOutputTokens(provider.Kind) };

            ChatResponse response = await chatClient.GetResponseAsync(new[] { message }, options, ct);
            stopwatch.Stop();

            string? preview = response.Text;
            if (!string.IsNullOrEmpty(preview) && preview.Length > 200)
            {
                preview = preview[..200];
            }

            bool ok = !string.IsNullOrWhiteSpace(preview);
            return new TestInferenceProviderResponse(ok, stopwatch.ElapsedMilliseconds, ok ? null : "Provider returned an empty response.", preview);
        }
        catch (Exception ex)
        {
            // Same discipline as RunTestAsync/RunTranscriptionTestAsync: never let a bad
            // endpoint/model/key/non-vision-capable-model escape as a 500.
            stopwatch.Stop();
            _logger.LogError(ex, "Vision provider test failed");
            return new TestInferenceProviderResponse(false, stopwatch.ElapsedMilliseconds, "Provider test failed.", null);
        }
    }

    /// <summary>
    /// A trivial, structurally valid 1x1-pixel baseline JPEG (SOI/APP0/.../EOI), embedded as a
    /// literal rather than generated (this API service has no image-encoding dependency and
    /// shouldn't gain one just for a health-check) — used only as the image content part of
    /// <see cref="RunVisionTestAsync"/>'s connectivity ping.
    /// </summary>
    private static readonly byte[] TinyTestJpeg = Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/2wBDAQkJCQwLDBgNDRgyIRwhMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjL/wAARCAABAAEDASIAAhEBAxEB/8QAFQABAQAAAAAAAAAAAAAAAAAAAAj/xAAUEAEAAAAAAAAAAAAAAAAAAAAA/8QAFQEBAQAAAAAAAAAAAAAAAAAAAAX/xAAUEQEAAAAAAAAAAAAAAAAAAAAA/9oADAMBAAIRAxEAPwCdABmX/9k=");

    /// <summary>
    /// Builds a ~0.3 second, 16 kHz mono 16-bit PCM WAV of all-zero (silent) samples, with a
    /// correct 44-byte canonical WAV header, for use as a minimal ASR connectivity ping.
    /// </summary>
    private static MemoryStream BuildSilentWav()
    {
        const int sampleRateHz = 16_000;
        const short channels = 1;
        const short bitsPerSample = 16;
        const double durationSeconds = 0.3;

        int bytesPerSample = bitsPerSample / 8;
        int sampleCount = (int)(sampleRateHz * durationSeconds);
        int dataSize = sampleCount * channels * bytesPerSample;
        int byteRate = sampleRateHz * channels * bytesPerSample;
        short blockAlign = (short)(channels * bytesPerSample);

        MemoryStream stream = new();
        using (BinaryWriter writer = new(stream, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            // RIFF header
            writer.Write("RIFF"u8.ToArray());
            writer.Write(36 + dataSize);
            writer.Write("WAVE"u8.ToArray());

            // fmt subchunk (PCM)
            writer.Write("fmt "u8.ToArray());
            writer.Write(16);
            writer.Write((short)1); // PCM
            writer.Write(channels);
            writer.Write(sampleRateHz);
            writer.Write(byteRate);
            writer.Write(blockAlign);
            writer.Write(bitsPerSample);

            // data subchunk — all-zero samples, i.e. silence
            writer.Write("data"u8.ToArray());
            writer.Write(dataSize);
            writer.Write(new byte[dataSize]);
        }

        stream.Position = 0;
        return stream;
    }

    private async Task PersistTestResultAsync(InferenceProvider entity, TestInferenceProviderResponse result, CancellationToken ct)
    {
        entity.LastTestAt = DateTime.UtcNow;
        entity.LastTestOk = result.Ok;
        entity.LastTestError = result.Error;
        await _db.SaveChangesAsync(ct);
    }

    private async Task ClearOtherDefaultsAsync(InferenceProviderCapability capability, Guid? exceptId, CancellationToken ct)
    {
        // R4/WS4: scoped to the SAME capability — a Chat default and a Transcription default
        // coexist independently, per the composite (capability, is_default) filtered unique index.
        IQueryable<InferenceProvider> query = _db.InferenceProviders
            .Where(p => p.IsDefault && p.Capability == capability);
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

        // Whitespace-only counts as clearing, not as storing. Storing it would show the admin a
        // confident `hasApiKey: true` with a last-four, while every consumer trims the value back
        // to empty and behaves as though no key were configured at all.
        if (apiKey.Trim().Length == 0)
        {
            // Explicit empty string: clear the key.
            entity.ApiKeyEncrypted = null;
            entity.ApiKeyLastFour = null;
            return;
        }

        // Trimmed before storage so the stored value matches what consumers actually use, and so
        // a stray pasted space cannot produce a second, redundant client-cache entry.
        apiKey = apiKey.Trim();
        entity.ApiKeyEncrypted = _secretProtector.Protect(apiKey);
        entity.ApiKeyLastFour = apiKey.Length <= 4 ? apiKey : apiKey[^4..];
    }

    private static bool IsDisallowedEndpoint(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return true;
        }

        if (!IPAddress.TryParse(uri.Host, out var ip))
        {
            try
            {
                var addresses = Dns.GetHostAddresses(uri.Host);
                if (addresses.Length == 0) return true;
                ip = addresses[0];
            }
            catch
            {
                return true;
            }
        }

        return IPAddress.IsLoopback(ip) ||
            ip.IsIPv6LinkLocal ||
            (ip.GetAddressBytes() is { Length: 4 } b && (
                b[0] == 10 ||
                (b[0] == 172 && b[1] >= 16 && b[1] <= 31) ||
                (b[0] == 192 && b[1] == 168) ||
                (b[0] == 169 && b[1] == 254)));
    }

    /// <summary>
    /// Output-token budget for a one-word connectivity ping. 128 is ample headroom for "ping" plus
    /// a short reasoning preamble on an OpenAI-shaped backend.
    /// <para>
    /// Anthropic needs far more. The SDK's default <c>AnthropicThinkingMode.Adaptive</c> leaves
    /// thinking ON at the model's default effort, and thinking tokens are charged against
    /// <c>max_tokens</c> — so a 128-token ceiling is realistically consumed before any visible
    /// content, and a correctly configured provider would report the misleading "Provider returned
    /// an empty response", which is then persisted to LastTestError.
    /// </para>
    /// </summary>
    private static int PingMaxOutputTokens(InferenceProviderKind kind) =>
        kind == InferenceProviderKind.Anthropic ? 4096 : 128;

    /// <summary>
    /// Rendered from the enum rather than hand-listed, so adding a provider kind cannot leave an
    /// error message quietly advertising a stale set of valid values.
    /// </summary>
    private static readonly string KnownKinds =
        string.Join(", ", Enum.GetNames<InferenceProviderKind>().Select(n => $"'{n}'"));

    /// <summary>
    /// Rejects kind/capability combinations that cannot ever work, at the boundary where the row
    /// is written rather than hours later inside a workflow step. Today that is exactly one pair:
    /// Anthropic exposes no speech-to-text API, so it can never serve Transcription. Chat and
    /// Vision both resolve through <c>IChatClientFactory</c> and are fine.
    /// </summary>
    private static bool IsUnsupportedCombination(
        InferenceProviderKind kind,
        InferenceProviderCapability capability,
        out string error)
    {
        if (kind == InferenceProviderKind.Anthropic && capability == InferenceProviderCapability.Transcription)
        {
            error = "Anthropic providers cannot serve the Transcription capability — Anthropic " +
                    "exposes no speech-to-text API. Use a provider of kind 'AzureOpenAI' or " +
                    "'OpenAICompatible' for transcription.";
            return true;
        }

        error = string.Empty;
        return false;
    }

    private static bool TryParseKind(string? value, out InferenceProviderKind kind) =>
        Enum.TryParse(value, ignoreCase: true, out kind);

    private static bool TryParseCapability(string? value, out InferenceProviderCapability capability) =>
        Enum.TryParse(value, ignoreCase: true, out capability);

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: "23505" };

    private static InferenceProviderResponse MapToResponse(InferenceProvider p) => new(
        p.Id, p.Name, p.Kind.ToString(), p.Capability.ToString(), p.Endpoint, p.ModelName,
        !string.IsNullOrEmpty(p.ApiKeyEncrypted), p.ApiKeyLastFour,
        p.IsDefault, p.IsEnabled, p.TimeoutSeconds, p.CreatedAt, p.UpdatedAt,
        p.LastTestAt, p.LastTestOk, p.LastTestError);
}
