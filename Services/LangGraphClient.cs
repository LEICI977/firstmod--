using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VivantValley.Services;

/// <summary>HTTP transport for the real local LangGraph service.</summary>
public sealed class LangGraphClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient httpClient;
    private readonly Uri endpoint;

    public LangGraphClient(HttpClient httpClient, string baseUrl, TimeSpan timeout)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        if (!Uri.TryCreate((baseUrl ?? string.Empty).Trim().TrimEnd('/') + "/v1/graph/decision", UriKind.Absolute, out Uri? parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("LangGraphBaseUrl must be an absolute HTTP URL.", nameof(baseUrl));
        }

        endpoint = parsed;
        timeout = timeout <= TimeSpan.Zero ? TimeSpan.FromSeconds(120) : timeout;
        httpClient.Timeout = timeout;
    }

    public async Task<LangGraphResponse> DecideAsync(
        LangGraphRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        string json = JsonSerializer.Serialize(request, JsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await httpClient.PostAsync(
                endpoint,
                content,
                cancellationToken)
            .ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new LangGraphException(
                $"LangGraph request failed (HTTP {(int)response.StatusCode} {response.StatusCode}): {Sanitize(body)}",
                response.StatusCode);
        }

        try
        {
            LangGraphResponse? result = JsonSerializer.Deserialize<LangGraphResponse>(body, JsonOptions);
            if (result?.Decision is null)
                throw new LangGraphException("LangGraph response did not contain a decision.", response.StatusCode);
            return result;
        }
        catch (JsonException exception)
        {
            throw new LangGraphException("LangGraph returned invalid JSON.", response.StatusCode, exception);
        }
    }

    private static string Sanitize(string value)
    {
        string clean = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        return clean.Length <= 500 ? clean : clean[..500] + "...";
    }
}

public sealed class LangGraphException : Exception
{
    public LangGraphException(string message, HttpStatusCode? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode? StatusCode { get; }
}
