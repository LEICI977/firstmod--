using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace VivantValley;

public interface IDeepSeekClient
{
    Task<string> CompleteChatAsync(
        string apiKey,
        DeepSeekChatRequest request,
        CancellationToken cancellationToken = default);

    Task<string> StreamChatAsync(
        string apiKey,
        DeepSeekChatRequest request,
        Action<DeepSeekStreamChunk> onChunk,
        CancellationToken cancellationToken = default);
}

/// <summary>A small client for normal and SSE streaming DeepSeek chat/completions.</summary>
public sealed class DeepSeekClient : IDeepSeekClient, IDisposable
{
    public static readonly Uri DefaultEndpoint = new("https://api.deepseek.com/chat/completions");
    public static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(120);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient httpClient;
    private readonly bool ownsHttpClient;
    private readonly Uri endpoint;
    private readonly TimeSpan requestTimeout;
    private bool disposed;

    public DeepSeekClient()
        : this(new HttpClient(), DefaultEndpoint, DefaultRequestTimeout, ownsHttpClient: true)
    {
    }

    public DeepSeekClient(HttpClient httpClient, Uri? endpoint = null, TimeSpan? requestTimeout = null)
        : this(
            httpClient ?? throw new ArgumentNullException(nameof(httpClient)),
            endpoint ?? DefaultEndpoint,
            requestTimeout ?? DefaultRequestTimeout,
            ownsHttpClient: false)
    {
    }

    private DeepSeekClient(HttpClient httpClient, Uri endpoint, TimeSpan requestTimeout, bool ownsHttpClient)
    {
        if (!endpoint.IsAbsoluteUri)
            throw new ArgumentException("DeepSeek endpoint 必须是绝对 URI。", nameof(endpoint));
        if (requestTimeout <= TimeSpan.Zero && requestTimeout != Timeout.InfiniteTimeSpan)
            throw new ArgumentOutOfRangeException(nameof(requestTimeout), "请求超时必须大于零。 ");

        this.httpClient = httpClient;
        this.endpoint = endpoint;
        this.requestTimeout = requestTimeout;
        this.ownsHttpClient = ownsHttpClient;

        // The linked token below is the single timeout authority. The default
        // HttpClient timeout (100s) would otherwise race our configured value.
        if (ownsHttpClient)
            this.httpClient.Timeout = Timeout.InfiniteTimeSpan;
    }

    public async Task<string> CompleteChatAsync(
        string apiKey,
        DeepSeekChatRequest request,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        string normalizedKey = ValidateRequest(apiKey, request, expectedStream: false);

        string json = JsonSerializer.Serialize(request, SerializerOptions);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", normalizedKey);
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (requestTimeout != Timeout.InfiniteTimeSpan)
            timeoutSource.CancelAfter(requestTimeout);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(
                    httpRequest,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeoutSource.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DeepSeekTimeoutException(
                $"DeepSeek API 请求在 {requestTimeout.TotalSeconds:0.#} 秒后超时。",
                exception);
        }
        catch (HttpRequestException exception)
        {
            throw new DeepSeekApiException(
                "无法连接 DeepSeek API：" + SanitizeDiagnostic(exception.Message, normalizedKey),
                statusCode: null,
                exception);
        }

        using (response)
        {
            string responseBody;
            try
            {
                responseBody = await response.Content
                    .ReadAsStringAsync(timeoutSource.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                throw new DeepSeekTimeoutException(
                    $"读取 DeepSeek API 响应在 {requestTimeout.TotalSeconds:0.#} 秒后超时。",
                    exception);
            }
            catch (Exception exception) when (exception is HttpRequestException or ObjectDisposedException)
            {
                throw new DeepSeekApiException(
                    "读取 DeepSeek API 响应失败：" + SanitizeDiagnostic(exception.Message, normalizedKey),
                    response.StatusCode,
                    exception);
            }

            if (!response.IsSuccessStatusCode)
            {
                string providerMessage = ExtractProviderError(responseBody, normalizedKey);
                throw new DeepSeekApiException(
                    BuildHttpErrorMessage(response.StatusCode, providerMessage),
                    response.StatusCode);
            }

            return ParseAssistantContent(responseBody, response.StatusCode, normalizedKey);
        }
    }

    public async Task<string> StreamChatAsync(
        string apiKey,
        DeepSeekChatRequest request,
        Action<DeepSeekStreamChunk> onChunk,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (onChunk is null)
            throw new ArgumentNullException(nameof(onChunk));

        string normalizedKey = ValidateRequest(apiKey, request, expectedStream: true);
        string json = JsonSerializer.Serialize(request, SerializerOptions);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", normalizedKey);
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (requestTimeout != Timeout.InfiniteTimeSpan)
            timeoutSource.CancelAfter(requestTimeout);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(
                    httpRequest,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeoutSource.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DeepSeekTimeoutException(
                $"DeepSeek 流式请求在 {requestTimeout.TotalSeconds:0.#} 秒后超时。",
                exception);
        }
        catch (HttpRequestException exception)
        {
            throw new DeepSeekApiException(
                "无法连接 DeepSeek API：" + SanitizeDiagnostic(exception.Message, normalizedKey),
                statusCode: null,
                exception);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                string responseBody = await response.Content
                    .ReadAsStringAsync(timeoutSource.Token)
                    .ConfigureAwait(false);
                string providerMessage = ExtractProviderError(responseBody, normalizedKey);
                throw new DeepSeekApiException(
                    BuildHttpErrorMessage(response.StatusCode, providerMessage),
                    response.StatusCode);
            }

            var content = new StringBuilder();
            try
            {
                await using Stream stream = await response.Content
                    .ReadAsStreamAsync(timeoutSource.Token)
                    .ConfigureAwait(false);
                using var reader = new StreamReader(
                    stream,
                    Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: false,
                    bufferSize: 4096,
                    leaveOpen: false);

                while (true)
                {
                    string? line = await reader.ReadLineAsync()
                        .WaitAsync(timeoutSource.Token)
                        .ConfigureAwait(false);
                    if (line is null)
                        break;
                    if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string data = line[5..].TrimStart();
                    if (data.Equals("[DONE]", StringComparison.Ordinal))
                        break;
                    if (data.Length == 0)
                        continue;

                    DeepSeekStreamChunk chunk = ParseStreamChunk(data, response.StatusCode, normalizedKey);
                    if (chunk.ContentDelta.Length > 0)
                        content.Append(chunk.ContentDelta);
                    if (chunk.ContentDelta.Length > 0 || chunk.ReasoningDelta.Length > 0)
                        onChunk(chunk);
                }
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                throw new DeepSeekTimeoutException(
                    $"读取 DeepSeek 流式响应在 {requestTimeout.TotalSeconds:0.#} 秒后超时。",
                    exception);
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or ObjectDisposedException)
            {
                throw new DeepSeekApiException(
                    "读取 DeepSeek 流式响应失败：" + SanitizeDiagnostic(exception.Message, normalizedKey),
                    response.StatusCode,
                    exception);
            }

            string reply = content.ToString().Trim();
            if (reply.Length == 0)
                throw new DeepSeekApiException("DeepSeek API 返回了空的流式回复。", response.StatusCode);
            return reply;
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        if (ownsHttpClient)
            httpClient.Dispose();
    }

    private static string ValidateRequest(string apiKey, DeepSeekChatRequest request, bool expectedStream)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new DeepSeekConfigurationException("DeepSeek API Key 为空，请先在模组配置中填写。");
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.Model))
            throw new DeepSeekConfigurationException("DeepSeek model 不能为空。");
        if (request.Messages is null || request.Messages.Count == 0)
            throw new DeepSeekConfigurationException("DeepSeek messages 不能为空。");
        if (request.Thinking is null || string.IsNullOrWhiteSpace(request.Thinking.Type))
            throw new DeepSeekConfigurationException("DeepSeek thinking.type 不能为空。");
        if (string.IsNullOrWhiteSpace(request.ReasoningEffort))
            throw new DeepSeekConfigurationException("DeepSeek reasoning_effort 不能为空。");
        if (request.MaxTokens < 1)
            throw new DeepSeekConfigurationException("DeepSeek max_tokens 必须大于零。");
        if (request.Stream != expectedStream)
        {
            throw new DeepSeekConfigurationException(
                expectedStream
                    ? "流式请求必须设置 stream=true。"
                    : "普通请求必须设置 stream=false。");
        }

        foreach (DeepSeekChatMessage? message in request.Messages)
        {
            if (message is null || string.IsNullOrWhiteSpace(message.Role) || string.IsNullOrWhiteSpace(message.Content))
                throw new DeepSeekConfigurationException("每条 DeepSeek message 都必须包含非空 role 和 content。");
        }

        return apiKey.Trim();
    }

    private static string ParseAssistantContent(string json, HttpStatusCode statusCode, string apiKey)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("choices", out JsonElement choices)
                || choices.ValueKind != JsonValueKind.Array
                || choices.GetArrayLength() == 0
                || !choices[0].TryGetProperty("message", out JsonElement message)
                || message.ValueKind != JsonValueKind.Object
                || !message.TryGetProperty("content", out JsonElement content)
                || content.ValueKind != JsonValueKind.String)
            {
                throw new DeepSeekApiException(
                    "DeepSeek API 响应缺少 choices[0].message.content。",
                    statusCode);
            }

            string? value = content.GetString();
            if (string.IsNullOrWhiteSpace(value))
                throw new DeepSeekApiException("DeepSeek API 返回了空回复。", statusCode);

            return value;
        }
        catch (JsonException exception)
        {
            throw new DeepSeekApiException(
                "DeepSeek API 返回了无法解析的 JSON：" + SanitizeDiagnostic(exception.Message, apiKey),
                statusCode,
                exception);
        }
    }

    private static DeepSeekStreamChunk ParseStreamChunk(
        string json,
        HttpStatusCode statusCode,
        string apiKey)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.TryGetProperty("error", out JsonElement error))
            {
                string providerMessage = error.ValueKind == JsonValueKind.Object
                                         && error.TryGetProperty("message", out JsonElement message)
                                         && message.ValueKind == JsonValueKind.String
                    ? message.GetString() ?? string.Empty
                    : error.ToString();
                throw new DeepSeekApiException(
                    BuildHttpErrorMessage(statusCode, SanitizeDiagnostic(providerMessage, apiKey)),
                    statusCode);
            }

            if (!root.TryGetProperty("choices", out JsonElement choices)
                || choices.ValueKind != JsonValueKind.Array
                || choices.GetArrayLength() == 0
                || !choices[0].TryGetProperty("delta", out JsonElement delta)
                || delta.ValueKind != JsonValueKind.Object)
            {
                return new DeepSeekStreamChunk(string.Empty, string.Empty);
            }

            string content = TryGetString(delta, "content");
            string reasoning = TryGetString(delta, "reasoning_content");
            return new DeepSeekStreamChunk(content, reasoning);
        }
        catch (JsonException exception)
        {
            throw new DeepSeekApiException(
                "DeepSeek API 返回了无法解析的流式 JSON："
                + SanitizeDiagnostic(exception.Message, apiKey),
                statusCode,
                exception);
        }
    }

    private static string TryGetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement value)
               && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static string ExtractProviderError(string responseBody, string apiKey)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
            return string.Empty;

        try
        {
            using JsonDocument document = JsonDocument.Parse(responseBody);
            if (document.RootElement.TryGetProperty("error", out JsonElement error))
            {
                if (error.ValueKind == JsonValueKind.Object
                    && error.TryGetProperty("message", out JsonElement message)
                    && message.ValueKind == JsonValueKind.String)
                {
                    return SanitizeDiagnostic(message.GetString() ?? string.Empty, apiKey);
                }

                if (error.ValueKind == JsonValueKind.String)
                    return SanitizeDiagnostic(error.GetString() ?? string.Empty, apiKey);
            }
        }
        catch (JsonException)
        {
            // Do not echo an unstructured provider response; it may contain sensitive data.
        }

        return string.Empty;
    }

    private static string BuildHttpErrorMessage(HttpStatusCode statusCode, string providerMessage)
    {
        string prefix = statusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                => "DeepSeek API 鉴权失败，请检查 API Key 是否有效。",
            (HttpStatusCode)429
                => "DeepSeek API 请求过于频繁或额度不足（HTTP 429）。",
            _ => $"DeepSeek API 请求失败（HTTP {(int)statusCode} {statusCode}）。",
        };

        return string.IsNullOrWhiteSpace(providerMessage)
            ? prefix
            : prefix + " 服务端信息：" + providerMessage;
    }

    private static string SanitizeDiagnostic(string value, string apiKey)
    {
        string sanitized = value ?? string.Empty;
        if (!string.IsNullOrEmpty(apiKey))
            sanitized = sanitized.Replace(apiKey, "[REDACTED]", StringComparison.Ordinal);

        sanitized = sanitized.Replace('\r', ' ').Replace('\n', ' ').Trim();
        const int maximumLength = 500;
        return sanitized.Length <= maximumLength
            ? sanitized
            : sanitized.Substring(0, maximumLength) + "…";
    }

    private void ThrowIfDisposed()
    {
        if (disposed)
            throw new ObjectDisposedException(nameof(DeepSeekClient));
    }
}

public class DeepSeekApiException : Exception
{
    public DeepSeekApiException(string message, HttpStatusCode? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode? StatusCode { get; }
}

public sealed class DeepSeekTimeoutException : DeepSeekApiException
{
    public DeepSeekTimeoutException(string message, Exception? innerException = null)
        : base(message, statusCode: null, innerException)
    {
    }
}

public sealed class DeepSeekConfigurationException : DeepSeekApiException
{
    public DeepSeekConfigurationException(string message)
        : base(message)
    {
    }
}
