using System.Text.Json;
using System.Text.Json.Serialization;

namespace VivantValley.Services;

/// <summary>Loopback client for the optional local microphone and Whisper service.</summary>
public sealed class SpeechToTextClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient httpClient;
    private readonly Uri startEndpoint;
    private readonly Uri stopEndpoint;

    public SpeechToTextClient(HttpClient httpClient, string baseUrl)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        if (!Uri.TryCreate((baseUrl ?? string.Empty).Trim().TrimEnd('/') + "/v1/stt/start", UriKind.Absolute, out Uri? start)
            || (start.Scheme != Uri.UriSchemeHttp && start.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("Speech service base URL must be an absolute HTTP URL.", nameof(baseUrl));
        }

        startEndpoint = start;
        stopEndpoint = new Uri(start, "/v1/stt/stop");
    }

    public async Task StartRecordingAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.PostAsync(
                startEndpoint,
                content: null,
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> StopAndTranscribeAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.PostAsync(
                stopEndpoint,
                content: null,
                cancellationToken)
            .ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"语音识别服务失败：{Sanitize(body)}");

        SpeechToTextResponse? result = JsonSerializer.Deserialize<SpeechToTextResponse>(body, JsonOptions);
        string text = (result?.Text ?? string.Empty).Trim();
        return text;
    }

    public async Task CancelRecordingAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.PostAsync(
                new Uri(startEndpoint, "/v1/stt/cancel"),
                content: null,
                cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            _ = response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        throw new InvalidOperationException($"语音录音服务失败：{Sanitize(body)}");
    }

    private static string Sanitize(string value)
    {
        string clean = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        return clean.Length <= 300 ? clean : clean[..300] + "...";
    }

    private sealed class SpeechToTextResponse
    {
        [JsonPropertyName("text")]
        public string Text { get; init; } = string.Empty;
    }
}
