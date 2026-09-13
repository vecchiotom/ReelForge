using System.ClientModel;
using System.Collections.Concurrent;
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

    private static ITranscriptionClient BuildOpenAICompatible(ResolvedTranscriptionProvider provider)
    {
        string apiKey = string.IsNullOrWhiteSpace(provider.ApiKey) ? NoKeyPlaceholder : provider.ApiKey;

        OpenAIClientOptions options = new()
        {
            Endpoint = new Uri(provider.Endpoint),
            NetworkTimeout = TimeSpan.FromSeconds(provider.TimeoutSeconds)
        };

        OpenAIClient client = new(new ApiKeyCredential(apiKey), options);

        AudioClient audioClient = client.GetAudioClient(provider.ModelName);
        return new OpenAiAudioTranscriptionClient(audioClient);
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
}
