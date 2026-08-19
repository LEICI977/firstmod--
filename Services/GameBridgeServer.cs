using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace VivantValley.Services;

/// <summary>
/// Loopback-only HTTP server used by LangGraph tools to request game-side
/// effects. TcpListener avoids Windows HTTP.sys URL ACL requirements. The
/// supplied executor must marshal all Stardew access to the game thread; this
/// server never touches Game1 itself.
/// </summary>
public sealed class GameBridgeServer : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly TcpListener listener;
    private readonly Func<GameBridgeToolRequest, Task<GameBridgeToolResult>> executor;
    private readonly CancellationTokenSource lifetime = new();
    private readonly string token = Guid.NewGuid().ToString("N");
    private readonly Uri baseUri;
    private Task? listenerTask;
    private int disposed;

    public GameBridgeServer(
        Func<GameBridgeToolRequest, Task<GameBridgeToolResult>> executor,
        int port = 8124)
    {
        this.executor = executor ?? throw new ArgumentNullException(nameof(executor));
        if (port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port));

        listener = new TcpListener(IPAddress.Loopback, port);
        baseUri = new Uri($"http://127.0.0.1:{port}/", UriKind.Absolute);
    }

    public LangGraphBridgeAccess Access => new()
    {
        BaseUrl = baseUri.ToString().TrimEnd('/'),
        Token = token,
    };

    public void Start()
    {
        if (listenerTask is not null)
            return;

        listener.Start();
        listenerTask = Task.Run(ListenLoopAsync);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        lifetime.Cancel();
        listener.Stop();
        try
        {
            listenerTask?.GetAwaiter().GetResult();
        }
        catch (Exception) when (lifetime.IsCancellationRequested)
        {
            // Expected when closing the listener during mod shutdown.
        }
        lifetime.Dispose();
    }

    private async Task ListenLoopAsync()
    {
        while (!lifetime.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(lifetime.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException) when (lifetime.IsCancellationRequested)
            {
                break;
            }

            _ = Task.Run(() => HandleClientAsync(client), lifetime.Token);
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                NetworkStream stream = client.GetStream();
                BridgeHttpRequest request = await ReadRequestAsync(stream, lifetime.Token).ConfigureAwait(false);
                if (request.Method.Equals("GET", StringComparison.OrdinalIgnoreCase)
                    && request.Path == "/health")
                {
                    await WriteJsonAsync(stream, 200, new { status = "ok", bridge = "smapi" }, lifetime.Token)
                        .ConfigureAwait(false);
                    return;
                }

                if (!request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase)
                    || request.Path != "/v1/game/execute-tool")
                {
                    await WriteJsonAsync(stream, 404, new { error = "not_found" }, lifetime.Token).ConfigureAwait(false);
                    return;
                }

                if (!request.Authorization.Equals("Bearer " + token, StringComparison.Ordinal))
                {
                    await WriteJsonAsync(stream, 401, new { error = "unauthorized" }, lifetime.Token).ConfigureAwait(false);
                    return;
                }

                GameBridgeToolRequest? toolRequest = JsonSerializer.Deserialize<GameBridgeToolRequest>(
                    request.Body,
                    JsonOptions);
                if (toolRequest is null)
                {
                    await WriteJsonAsync(stream, 400, new { error = "invalid_json" }, lifetime.Token).ConfigureAwait(false);
                    return;
                }

                GameBridgeToolResult result = await executor(toolRequest).ConfigureAwait(false);
                await WriteJsonAsync(stream, 200, result, lifetime.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                try
                {
                    await WriteJsonAsync(
                            client.GetStream(),
                            500,
                            new { error = Sanitize(exception.Message) },
                            lifetime.Token)
                        .ConfigureAwait(false);
                }
                catch (Exception) when (lifetime.IsCancellationRequested)
                {
                }
            }
        }
    }

    private static async Task<BridgeHttpRequest> ReadRequestAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        using var headerBuffer = new MemoryStream();
        byte[] oneByte = new byte[1];
        byte[] delimiter = { 13, 10, 13, 10 };
        while (headerBuffer.Length <= 32_768)
        {
            int read = await stream.ReadAsync(oneByte, cancellationToken).ConfigureAwait(false);
            if (read == 0)
                throw new InvalidOperationException("client closed the bridge request");
            headerBuffer.WriteByte(oneByte[0]);
            if (EndsWith(headerBuffer.GetBuffer(), (int)headerBuffer.Length, delimiter))
                break;
        }

        byte[] headerBytes = headerBuffer.ToArray();
        int separator = FindSequence(headerBytes, delimiter);
        if (separator < 0)
            throw new InvalidOperationException("invalid HTTP headers");

        string headerText = Encoding.ASCII.GetString(headerBytes, 0, separator);
        string[] lines = headerText.Split("\r\n", StringSplitOptions.None);
        string[] requestLine = lines[0].Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (requestLine.Length < 2)
            throw new InvalidOperationException("invalid HTTP request line");

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in lines.Skip(1))
        {
            int colon = line.IndexOf(':');
            if (colon > 0)
                headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }

        int contentLength = 0;
        if (headers.TryGetValue("Content-Length", out string? lengthText)
            && (!int.TryParse(lengthText, out contentLength) || contentLength < 0))
        {
            throw new InvalidOperationException("invalid content length");
        }
        if (contentLength > 128_000)
            throw new InvalidOperationException("request is too large");

        int bodyOffset = separator + delimiter.Length;
        using var body = new MemoryStream();
        if (headerBytes.Length > bodyOffset)
            body.Write(headerBytes, bodyOffset, headerBytes.Length - bodyOffset);
        byte[] buffer = new byte[8192];
        while (body.Length < contentLength)
        {
            int wanted = (int)Math.Min(buffer.Length, contentLength - body.Length);
            int read = await stream.ReadAsync(buffer.AsMemory(0, wanted), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                throw new InvalidOperationException("client closed the bridge body");
            body.Write(buffer, 0, read);
        }
        if (body.Length != contentLength)
            throw new InvalidOperationException("invalid request body length");

        return new BridgeHttpRequest(
            requestLine[0],
            requestLine[1],
            headers.TryGetValue("Authorization", out string? authorization) ? authorization : string.Empty,
            body.ToArray());
    }

    private static async Task WriteJsonAsync(
        NetworkStream stream,
        int statusCode,
        object value,
        CancellationToken cancellationToken)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        string status = statusCode switch
        {
            200 => "OK",
            400 => "Bad Request",
            401 => "Unauthorized",
            404 => "Not Found",
            500 => "Internal Server Error",
            _ => "Error",
        };
        byte[] header = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {statusCode} {status}\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
    }

    private static bool EndsWith(byte[] buffer, int length, byte[] suffix)
        => length >= suffix.Length
           && buffer.AsSpan(length - suffix.Length, suffix.Length).SequenceEqual(suffix);

    private static int FindSequence(byte[] buffer, byte[] sequence)
    {
        for (int index = 0; index <= buffer.Length - sequence.Length; index++)
        {
            if (buffer.AsSpan(index, sequence.Length).SequenceEqual(sequence))
                return index;
        }
        return -1;
    }

    private static string Sanitize(string value)
    {
        string clean = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        return clean.Length <= 300 ? clean : clean[..300] + "...";
    }

    private sealed record BridgeHttpRequest(
        string Method,
        string Path,
        string Authorization,
        byte[] Body);
}
