using System.IO;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VivantValley.Services;

public sealed record AiRuntimeProfile(
    string Provider,
    string BaseUrl,
    Uri Endpoint,
    string Model,
    string ApiKey,
    string ApiKeySource,
    TimeSpan RequestTimeout,
    bool EnableThinking,
    string ReasoningEffort);

public sealed record AiProviderSettingsDraft(
    string Provider,
    string BaseUrl,
    string Model,
    string ReplacementApiKey,
    bool ClearSavedKey);

public static class AiEndpointResolver
{
    private const string ChatCompletionsPath = "/chat/completions";

    public static bool TryResolve(
        string? provider,
        string? input,
        out string normalizedBaseUrl,
        out Uri endpoint,
        out string error)
    {
        normalizedBaseUrl = string.Empty;
        endpoint = null!;
        error = string.Empty;

        string value = (input ?? string.Empty).Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? parsed))
        {
            error = "API 基础地址必须是完整网址。";
            return false;
        }
        if (parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeHttp)
        {
            error = "API 基础地址只支持 HTTP 或 HTTPS。";
            return false;
        }
        if (!string.IsNullOrEmpty(parsed.UserInfo))
        {
            error = "API 基础地址不能包含用户名或密码。";
            return false;
        }
        if (!string.IsNullOrEmpty(parsed.Query) || !string.IsNullOrEmpty(parsed.Fragment))
        {
            error = "API 基础地址不能包含查询参数或片段。";
            return false;
        }
        if (parsed.Scheme == Uri.UriSchemeHttp && !IsLoopbackHost(parsed.Host))
        {
            error = "远程 API 地址必须使用 HTTPS；HTTP 只允许本机地址。";
            return false;
        }

        var baseBuilder = new UriBuilder(parsed);
        string basePath = NormalizePath(baseBuilder.Path);
        if (basePath.EndsWith(ChatCompletionsPath, StringComparison.OrdinalIgnoreCase))
            basePath = basePath[..^ChatCompletionsPath.Length];
        basePath = basePath.TrimEnd('/');
        baseBuilder.Path = basePath.Length == 0 ? "/" : basePath;

        normalizedBaseUrl = baseBuilder.Uri.AbsoluteUri.TrimEnd('/');

        var endpointBuilder = new UriBuilder(baseBuilder.Uri)
        {
            Path = (basePath.Length == 0 ? string.Empty : basePath) + ChatCompletionsPath,
        };
        endpoint = endpointBuilder.Uri;
        return true;
    }

    public static string GetDefaultBaseUrl(string? provider)
        => AiProviderNames.Normalize(provider) == AiProviderNames.OpenAI
            ? "https://api.openai.com/v1"
            : "https://api.deepseek.com";

    private static string NormalizePath(string value)
    {
        string path = (value ?? string.Empty).Trim();
        if (path.Length == 0 || path == "/")
            return string.Empty;
        return "/" + path.Trim('/');
    }

    private static bool IsLoopbackHost(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            return true;
        return IPAddress.TryParse(host, out IPAddress? address) && IPAddress.IsLoopback(address);
    }
}

/// <summary>Routes the existing chat contract through the currently selected provider.</summary>
public sealed class AiProviderClient : IDeepSeekClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient httpClient;
    private readonly Func<AiRuntimeProfile> profileAccessor;

    public AiProviderClient(HttpClient httpClient, Func<AiRuntimeProfile> profileAccessor)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.profileAccessor = profileAccessor ?? throw new ArgumentNullException(nameof(profileAccessor));
    }

    public async Task<string> CompleteChatAsync(
        string apiKey,
        DeepSeekChatRequest request,
        CancellationToken cancellationToken = default)
    {
        AiRuntimeProfile profile = profileAccessor();
        Validate(profile, request, expectedStream: false);
        using HttpResponseMessage response = await SendAsync(profile, request, cancellationToken)
            .ConfigureAwait(false);
        string body = await ReadBodyAsync(response, profile, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, body, profile);
        return ParseAssistantContent(body, response.StatusCode, profile);
    }

    public async Task<string> StreamChatAsync(
        string apiKey,
        DeepSeekChatRequest request,
        Action<DeepSeekStreamChunk> onChunk,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(onChunk);
        AiRuntimeProfile profile = profileAccessor();
        Validate(profile, request, expectedStream: true);

        using HttpResponseMessage response = await SendAsync(profile, request, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            string body = await ReadBodyAsync(response, profile, cancellationToken).ConfigureAwait(false);
            EnsureSuccess(response, body, profile);
        }

        using var timeout = CreateTimeoutSource(profile, cancellationToken);
        var content = new StringBuilder();
        try
        {
            await using Stream stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, leaveOpen: false);
            while (true)
            {
                string? line = await reader.ReadLineAsync().WaitAsync(timeout.Token).ConfigureAwait(false);
                if (line is null)
                    break;
                if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                    continue;

                string data = line[5..].TrimStart();
                if (data.Equals("[DONE]", StringComparison.Ordinal))
                    break;
                if (data.Length == 0)
                    continue;

                DeepSeekStreamChunk chunk = ParseStreamChunk(data, response.StatusCode, profile);
                if (chunk.ContentDelta.Length > 0)
                    content.Append(chunk.ContentDelta);
                if (chunk.ContentDelta.Length > 0 || chunk.ReasoningDelta.Length > 0)
                    onChunk(chunk);
            }
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DeepSeekTimeoutException(
                $"读取 {profile.Provider} 流式响应在 {profile.RequestTimeout.TotalSeconds:0.#} 秒后超时。",
                exception);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or ObjectDisposedException)
        {
            throw new DeepSeekApiException(
                $"读取 {profile.Provider} 流式响应失败：{Sanitize(exception.Message, profile.ApiKey)}",
                response.StatusCode,
                exception);
        }

        string reply = content.ToString().Trim();
        if (reply.Length == 0)
            throw new DeepSeekApiException($"{profile.Provider} 返回了空的流式回复。", response.StatusCode);
        return reply;
    }

    private async Task<HttpResponseMessage> SendAsync(
        AiRuntimeProfile profile,
        DeepSeekChatRequest request,
        CancellationToken cancellationToken)
    {
        string json = SerializeRequest(profile, request);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, profile.Endpoint);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", profile.ApiKey);
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(
            request.Stream ? "text/event-stream" : "application/json"));
        httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var timeout = CreateTimeoutSource(profile, cancellationToken);
        try
        {
            return await httpClient.SendAsync(
                    httpRequest,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DeepSeekTimeoutException(
                $"{profile.Provider} API 请求在 {profile.RequestTimeout.TotalSeconds:0.#} 秒后超时。",
                exception);
        }
        catch (HttpRequestException exception)
        {
            throw new DeepSeekApiException(
                $"无法连接 {profile.Provider} API：{Sanitize(exception.Message, profile.ApiKey)}",
                statusCode: null,
                exception);
        }
    }

    private static string SerializeRequest(AiRuntimeProfile profile, DeepSeekChatRequest request)
    {
        object payload = profile.Provider == AiProviderNames.OpenAI
            ? new OpenAiChatRequest
            {
                Model = profile.Model,
                Messages = request.Messages,
                MaxCompletionTokens = request.MaxTokens,
                Stream = request.Stream,
            }
            : new DeepSeekProviderRequest
            {
                Model = profile.Model,
                Messages = request.Messages,
                Thinking = new DeepSeekThinkingOptions
                {
                    Type = profile.EnableThinking ? "enabled" : "disabled",
                },
                ReasoningEffort = profile.ReasoningEffort,
                MaxTokens = request.MaxTokens,
                Stream = request.Stream,
            };
        return JsonSerializer.Serialize(payload, payload.GetType(), JsonOptions);
    }

    private static async Task<string> ReadBodyAsync(
        HttpResponseMessage response,
        AiRuntimeProfile profile,
        CancellationToken cancellationToken)
    {
        using var timeout = CreateTimeoutSource(profile, cancellationToken);
        try
        {
            return await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DeepSeekTimeoutException(
                $"读取 {profile.Provider} 响应在 {profile.RequestTimeout.TotalSeconds:0.#} 秒后超时。",
                exception);
        }
        catch (Exception exception) when (exception is HttpRequestException or ObjectDisposedException)
        {
            throw new DeepSeekApiException(
                $"读取 {profile.Provider} 响应失败：{Sanitize(exception.Message, profile.ApiKey)}",
                response.StatusCode,
                exception);
        }
    }

    private static void Validate(AiRuntimeProfile profile, DeepSeekChatRequest request, bool expectedStream)
    {
        if (string.IsNullOrWhiteSpace(profile.ApiKey))
            throw new DeepSeekConfigurationException($"{profile.Provider} API Key 为空，请先打开 AI 设置。");
        if (string.IsNullOrWhiteSpace(profile.Model))
            throw new DeepSeekConfigurationException($"{profile.Provider} 模型不能为空。");
        ArgumentNullException.ThrowIfNull(request);
        if (request.Messages is null || request.Messages.Count == 0)
            throw new DeepSeekConfigurationException("聊天消息不能为空。");
        if (request.MaxTokens < 1)
            throw new DeepSeekConfigurationException("最大输出 Token 必须大于零。");
        if (request.Stream != expectedStream)
            throw new DeepSeekConfigurationException(expectedStream ? "流式请求必须启用 stream。" : "普通请求不能启用 stream。");
        if (request.Messages.Any(message =>
                message is null
                || string.IsNullOrWhiteSpace(message.Role)
                || string.IsNullOrWhiteSpace(message.Content)))
        {
            throw new DeepSeekConfigurationException("每条聊天消息都必须包含角色和内容。");
        }
    }

    private static CancellationTokenSource CreateTimeoutSource(
        AiRuntimeProfile profile,
        CancellationToken cancellationToken)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (profile.RequestTimeout != Timeout.InfiniteTimeSpan)
            source.CancelAfter(profile.RequestTimeout);
        return source;
    }

    private static void EnsureSuccess(
        HttpResponseMessage response,
        string responseBody,
        AiRuntimeProfile profile)
    {
        if (response.IsSuccessStatusCode)
            return;

        string providerMessage = ExtractProviderError(responseBody, profile.ApiKey);
        string prefix = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                => $"{profile.Provider} API 鉴权失败，请检查 API Key。",
            (HttpStatusCode)429
                => $"{profile.Provider} API 请求过于频繁或额度不足（HTTP 429）。",
            _ => $"{profile.Provider} API 请求失败（HTTP {(int)response.StatusCode} {response.StatusCode}）。",
        };
        throw new DeepSeekApiException(
            providerMessage.Length == 0 ? prefix : prefix + " 服务端信息：" + providerMessage,
            response.StatusCode);
    }

    private static string ParseAssistantContent(
        string json,
        HttpStatusCode statusCode,
        AiRuntimeProfile profile)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("choices", out JsonElement choices)
                || choices.ValueKind != JsonValueKind.Array
                || choices.GetArrayLength() == 0
                || !choices[0].TryGetProperty("message", out JsonElement message)
                || !message.TryGetProperty("content", out JsonElement content)
                || content.ValueKind != JsonValueKind.String)
            {
                throw new DeepSeekApiException(
                    $"{profile.Provider} 响应缺少 choices[0].message.content。",
                    statusCode);
            }

            string value = content.GetString()?.Trim() ?? string.Empty;
            if (value.Length == 0)
                throw new DeepSeekApiException($"{profile.Provider} 返回了空回复。", statusCode);
            return value;
        }
        catch (JsonException exception)
        {
            throw new DeepSeekApiException(
                $"{profile.Provider} 返回了无法解析的 JSON：{Sanitize(exception.Message, profile.ApiKey)}",
                statusCode,
                exception);
        }
    }

    private static DeepSeekStreamChunk ParseStreamChunk(
        string json,
        HttpStatusCode statusCode,
        AiRuntimeProfile profile)
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
                    $"{profile.Provider} 流式响应返回错误：{Sanitize(providerMessage, profile.ApiKey)}",
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
            string reasoning = profile.Provider == AiProviderNames.DeepSeek
                ? TryGetString(delta, "reasoning_content")
                : string.Empty;
            return new DeepSeekStreamChunk(content, reasoning);
        }
        catch (JsonException exception)
        {
            throw new DeepSeekApiException(
                $"{profile.Provider} 返回了无法解析的流式 JSON：{Sanitize(exception.Message, profile.ApiKey)}",
                statusCode,
                exception);
        }
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
                    return Sanitize(message.GetString() ?? string.Empty, apiKey);
                }
                if (error.ValueKind == JsonValueKind.String)
                    return Sanitize(error.GetString() ?? string.Empty, apiKey);
            }
        }
        catch (JsonException)
        {
        }
        return string.Empty;
    }

    private static string TryGetString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out JsonElement value)
           && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static string Sanitize(string value, string apiKey)
    {
        string sanitized = value ?? string.Empty;
        if (!string.IsNullOrEmpty(apiKey))
            sanitized = sanitized.Replace(apiKey, "[REDACTED]", StringComparison.Ordinal);
        sanitized = sanitized.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return sanitized.Length <= 500 ? sanitized : sanitized[..500] + "…";
    }

    private sealed class DeepSeekProviderRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; init; } = string.Empty;

        [JsonPropertyName("messages")]
        public List<DeepSeekChatMessage> Messages { get; init; } = new();

        [JsonPropertyName("thinking")]
        public DeepSeekThinkingOptions Thinking { get; init; } = new();

        [JsonPropertyName("reasoning_effort")]
        public string ReasoningEffort { get; init; } = "low";

        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; init; }

        [JsonPropertyName("stream")]
        public bool Stream { get; init; }
    }

    private sealed class OpenAiChatRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; init; } = string.Empty;

        [JsonPropertyName("messages")]
        public List<DeepSeekChatMessage> Messages { get; init; } = new();

        [JsonPropertyName("max_completion_tokens")]
        public int MaxCompletionTokens { get; init; }

        [JsonPropertyName("stream")]
        public bool Stream { get; init; }
    }
}
