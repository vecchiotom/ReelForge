using System.ClientModel;
using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;
using Azure.AI.OpenAI;
using OpenAI;
using OpenAI.Audio;
using ReelForge.Shared.Data.Models;

namespace ReelForge.Shared.Inference;

/// <summary>
/// Constructs <see cref="ITranscriptionClient"/> instances for either an Azure OpenAI (Whisper
/// deployment) or an OpenAI-compatible (whisper.cpp-server, faster-whisper-server, speaches,
/// LiteLLM, ...) backend, and caches them by <see cref="ResolvedTranscriptionProvider.CacheKey"/>
/// so repeated resolutions of the same configuration reuse the same client/HttpClient. Mirrors
/// <see cref="ChatClientFactory"/> exactly, including the no-key placeholder handling (Risk R5).
/// </summary>
public sealed class TranscriptionClientFactory : ITranscriptionClientFactory
{
    /// <summary>
    /// <see cref="ApiKeyCredential"/> throws on a null/empty string, but OpenAI-compatible
    /// self-hosted ASR deployments (whisper.cpp-server, faster-whisper-server) commonly run with
    /// no key at all. Same convention as <see cref="ChatClientFactory"/>'s NoKeyPlaceholder.
    /// </summary>
    private const string NoKeyPlaceholder = "not-required";

    private readonly ConcurrentDictionary<string, ITranscriptionClient> _clients = new();

    public ITranscriptionClient Get(ResolvedTranscriptionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return _clients.GetOrAdd(provider.CacheKey, _ => Build(provider));
    }

    private static ITranscriptionClient Build(ResolvedTranscriptionProvider provider) => provider.Kind switch
    {
        InferenceProviderKind.AzureOpenAI => BuildAzureOpenAI(provider),
        InferenceProviderKind.OpenAICompatible => BuildOpenAICompatible(provider),
        _ => throw new NotSupportedException($"Unsupported inference provider kind '{provider.Kind}'.")
    };

    private static ITranscriptionClient BuildAzureOpenAI(ResolvedTranscriptionProvider provider)
    {
        AzureOpenAIClientOptions options = new()
        {
            NetworkTimeout = TimeSpan.FromSeconds(provider.TimeoutSeconds)
        };

        AzureOpenAIClient client = new(
            new Uri(provider.Endpoint),
            new ApiKeyCredential(provider.ApiKey),
            options);

        // AzureOpenAIClient.GetAudioClient(...) returns the derived AzureAudioClient at runtime,
        // but it is assignable to the base AudioClient - the same polymorphism ChatClientFactory
        // relies on for .GetChatClient(...).AsIChatClient(). Holding it as AudioClient here means
        // OpenAiAudioTranscriptionClient never needs to know which branch built it.
        AudioClient audioClient = client.GetAudioClient(provider.ModelName);
        return new OpenAiAudioTranscriptionClient(audioClient);
    }

    /// <summary>
    /// OpenAI-compatible backends go through a raw multipart POST instead of the OpenAI SDK's
    /// typed <see cref="AudioTranscriptionOptions"/> so a <c>vad_filter=false</c> form field can be
    /// sent — the SDK has no property for it. This is specifically to work around a real deployed
    /// bug: speaches (the reference "OpenAI-compatible" self-hosted ASR server this app is
    /// documented to use — see CLAUDE.md's <c>whisper</c> service) defaults faster-whisper's VAD
    /// filter ON via a private, environment-variable-immune Pydantic field
    /// (<c>_unstable_vad_filter</c>), and that default was verified live to make segmentation
    /// wildly unstable — the SAME audio, differing by inaudible resampler noise, produced
    /// terminal-sentence-punctuation rates from 11% to 63% depending on VAD, because VAD changes
    /// faster-whisper's internal seek/window alignment, not just which audio it drops. Turning it
    /// off (an OpenAI Whisper API default) collapsed that instability without any latency or
    /// accuracy cost in side-by-side testing. Azure OpenAI deployments do not exhibit this bug and
    /// keep going through the SDK unchanged below.
    /// </summary>
    private static ITranscriptionClient BuildOpenAICompatible(ResolvedTranscriptionProvider provider)
    {
        string apiKey = string.IsNullOrWhiteSpace(provider.ApiKey) ? NoKeyPlaceholder : provider.ApiKey;
        HttpClient httpClient = new()
        {
            BaseAddress = new Uri(provider.Endpoint),
            Timeout = TimeSpan.FromSeconds(provider.TimeoutSeconds)
        };
        if (apiKey != NoKeyPlaceholder)
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        return new RawHttpAudioTranscriptionClient(httpClient, provider.ModelName);
    }

    /// <summary>
    /// Adapts an <see cref="OpenAI.Audio.AudioClient"/> (plain, or Azure's derived
    /// <c>AzureAudioClient</c>) to ReelForge's own <see cref="ITranscriptionClient"/> so callers
    /// never touch the SDK type directly.
    /// </summary>
    private sealed class OpenAiAudioTranscriptionClient : ITranscriptionClient
    {
        private readonly AudioClient _audioClient;

        public OpenAiAudioTranscriptionClient(AudioClient audioClient)
        {
            _audioClient = audioClient;
        }

        public async Task<TranscriptResult> TranscribeAsync(
            Stream wav,
            string fileName,
            string? language,
            bool wordTimestamps,
            CancellationToken ct)
        {
            AudioTranscriptionOptions options = new()
            {
                ResponseFormat = AudioTranscriptionFormat.Verbose,
                TimestampGranularities = wordTimestamps
                    ? AudioTimestampGranularities.Word | AudioTimestampGranularities.Segment
                    : AudioTimestampGranularities.Segment
            };

            if (!string.IsNullOrWhiteSpace(language))
            {
                options.Language = language;
            }

            ClientResult<AudioTranscription> result =
                await _audioClient.TranscribeAudioAsync(wav, fileName, options, ct);
            AudioTranscription transcription = result.Value;

            List<TranscriptSegment> segments = transcription.Segments
                .Select(s => new TranscriptSegment(s.Text, s.StartTime.TotalSeconds, s.EndTime.TotalSeconds))
                .ToList();

            List<TranscriptWord> words = transcription.Words
                .Select(w => new TranscriptWord(w.Word, w.StartTime.TotalSeconds, w.EndTime.TotalSeconds))
                .ToList();

            return new TranscriptResult(transcription.Text, segments, words, transcription.Language);
        }
    }

    /// <summary>
    /// Raw multipart <c>/audio/transcriptions</c> client for OpenAI-compatible backends — see
    /// <see cref="BuildOpenAICompatible"/>'s remarks for why this bypasses the OpenAI SDK entirely
    /// rather than merely wrapping <see cref="AudioClient"/>: the SDK does not expose a
    /// <c>vad_filter</c> option and this is the one field that actually needs sending.
    /// </summary>
    private sealed class RawHttpAudioTranscriptionClient : ITranscriptionClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _modelName;

        public RawHttpAudioTranscriptionClient(HttpClient httpClient, string modelName)
        {
            _httpClient = httpClient;
            _modelName = modelName;
        }

        public async Task<TranscriptResult> TranscribeAsync(
            Stream wav,
            string fileName,
            string? language,
            bool wordTimestamps,
            CancellationToken ct)
        {
            using MultipartFormDataContent form = new();

            StreamContent fileContent = new(wav);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
            form.Add(fileContent, "file", fileName);
            form.Add(new StringContent(_modelName), "model");
            form.Add(new StringContent("verbose_json"), "response_format");
            form.Add(new StringContent("false"), "vad_filter");
            form.Add(new StringContent("segment"), "timestamp_granularities[]");
            if (wordTimestamps)
            {
                form.Add(new StringContent("word"), "timestamp_granularities[]");
            }
            if (!string.IsNullOrWhiteSpace(language))
            {
                form.Add(new StringContent(language), "language");
            }

            using HttpResponseMessage response =
                await _httpClient.PostAsync("audio/transcriptions", form, ct).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"Transcription request failed with {(int)response.StatusCode} {response.StatusCode}: {body}");
            }

            using JsonDocument doc = JsonDocument.Parse(body);
            JsonElement root = doc.RootElement;

            string text = root.TryGetProperty("text", out JsonElement textEl) ? textEl.GetString() ?? "" : "";
            string? responseLanguage =
                root.TryGetProperty("language", out JsonElement langEl) ? langEl.GetString() : null;

            List<TranscriptSegment> segments = new();
            if (root.TryGetProperty("segments", out JsonElement segmentsEl) && segmentsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement seg in segmentsEl.EnumerateArray())
                {
                    segments.Add(new TranscriptSegment(
                        seg.GetProperty("text").GetString() ?? "",
                        seg.GetProperty("start").GetDouble(),
                        seg.GetProperty("end").GetDouble()));
                }
            }

            List<TranscriptWord> words = new();
            if (root.TryGetProperty("words", out JsonElement wordsEl) && wordsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement w in wordsEl.EnumerateArray())
                {
                    words.Add(new TranscriptWord(
                        w.GetProperty("word").GetString() ?? "",
                        w.GetProperty("start").GetDouble(),
                        w.GetProperty("end").GetDouble()));
                }
            }

            return new TranscriptResult(text, segments, words, responseLanguage);
        }
    }
}
