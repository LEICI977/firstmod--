using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace VivantValley.Services;

/// <summary>Owns the optional LangGraph executable shipped inside the mod.</summary>
public sealed class LangGraphProcessManager : IDisposable
{
    private readonly string modDirectory;
    private readonly int preferredPort;
    private readonly TimeSpan startupTimeout;
    private readonly Action<string> log;
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private Process? process;
    private bool disposed;
    private DateTimeOffset nextStartAllowedUtc;

    public LangGraphProcessManager(string modDirectory, int preferredPort, TimeSpan startupTimeout, Action<string>? log = null)
    {
        this.modDirectory = Path.GetFullPath(modDirectory ?? throw new ArgumentNullException(nameof(modDirectory)));
        this.preferredPort = Math.Clamp(preferredPort, 0, 65535);
        this.startupTimeout = startupTimeout <= TimeSpan.Zero ? TimeSpan.FromSeconds(30) : startupTimeout;
        this.log = log ?? (_ => { });
    }

    public string? BaseUrl { get; private set; }

    public bool IsRunning => process is { HasExited: false };

    public bool TryStart(out string baseUrl)
    {
        baseUrl = string.Empty;
        if (disposed)
            return false;
        if (DateTimeOffset.UtcNow < nextStartAllowedUtc)
            return false;
        if (IsRunning)
        {
            baseUrl = BaseUrl ?? string.Empty;
            return baseUrl.Length > 0;
        }

        if (process is not null)
        {
            process.Dispose();
            process = null;
        }

        string? executable = ResolveExecutablePath();
        if (executable is null)
        {
            nextStartAllowedUtc = DateTimeOffset.UtcNow.AddSeconds(5);
            log($"未找到随 Mod 发布的 LangGraph 后端；当前平台为 {RuntimeInformation.RuntimeIdentifier}。将使用配置中的外部地址。");
            return false;
        }

        int port = TryReservePreferredPort() ?? ReserveLoopbackPort();
        string url = $"http://127.0.0.1:{port}";
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = $"--host 127.0.0.1 --port {port}",
            WorkingDirectory = Path.GetDirectoryName(executable) ?? modDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        try
        {
            process = Process.Start(startInfo);
            if (process is null)
            {
                log("LangGraph 后端进程启动失败：系统没有返回进程对象。");
                return false;
            }
            process.EnableRaisingEvents = true;
            process.Exited += OnProcessExited;
            _ = DrainOutputAsync(process.StandardOutput, false);
            _ = DrainOutputAsync(process.StandardError, true);
            BaseUrl = url;
            baseUrl = url;
            _ = WaitForHealthAsync(url, lifetimeCancellation.Token);
            log($"已自启动 LangGraph 后端：{url}。");
            nextStartAllowedUtc = DateTimeOffset.UtcNow.AddSeconds(5);
            return true;
        }
        catch (Exception exception)
        {
            process?.Dispose();
            process = null;
            nextStartAllowedUtc = DateTimeOffset.UtcNow.AddSeconds(5);
            log($"LangGraph 后端启动失败：{exception.Message}");
            return false;
        }
    }

    public void Stop()
    {
        Process? current = process;
        process = null;
        BaseUrl = null;
        nextStartAllowedUtc = DateTimeOffset.MinValue;
        if (current is null)
            return;
        try
        {
            if (!current.HasExited)
            {
                current.Kill(entireProcessTree: true);
                current.WaitForExit(2000);
            }
        }
        catch (Exception exception)
        {
            log($"关闭 LangGraph 后端失败：{exception.Message}");
        }
        finally
        {
            current.Dispose();
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        lifetimeCancellation.Cancel();
        Stop();
        lifetimeCancellation.Dispose();
    }

    private async Task WaitForHealthAsync(string baseUrl, CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        DateTimeOffset deadline = DateTimeOffset.UtcNow + startupTimeout;
        Uri healthUri = new(baseUrl + "/health");
        while (!cancellationToken.IsCancellationRequested && DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                using HttpResponseMessage response = await client.GetAsync(healthUri, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    log($"LangGraph 后端健康检查通过：{baseUrl}。");
                    return;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                // Importing LangGraph can take a few seconds on first launch.
            }

            try
            {
                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
        if (IsRunning)
            log($"LangGraph 后端在 {startupTimeout.TotalSeconds:0} 秒内未通过健康检查；后续请求仍会按正常超时处理。");
    }

    private async Task DrainOutputAsync(StreamReader reader, bool isError)
    {
        try
        {
            while (!reader.EndOfStream)
            {
                string? line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(line))
                    log($"LangGraph {(isError ? "stderr" : "stdout")}：{line.Trim()}");
            }
        }
        catch
        {
            // The process can close its pipes while the mod is unloading.
        }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (sender is Process exited && ReferenceEquals(process, exited))
            log($"LangGraph 后端进程已退出，退出码={exited.ExitCode}。");
    }

    private string? ResolveExecutablePath()
    {
        string platformDirectory = OperatingSystem.IsWindows()
            ? "win-x64"
            : OperatingSystem.IsMacOS()
                ? RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64"
                : RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "linux-arm64" : "linux-x64";
        string[] names = OperatingSystem.IsWindows()
            ? new[] { "VivantValley.LangGraph.exe", "VivantValley.LangGraph" }
            : new[] { "VivantValley.LangGraph", "VivantValley.LangGraph.bin" };
        foreach (string name in names)
        {
            string candidate = Path.Combine(modDirectory, "backend", platformDirectory, name);
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    private static int ReserveLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        try
        {
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private int? TryReservePreferredPort()
    {
        if (preferredPort <= 0)
            return null;
        try
        {
            var listener = new TcpListener(IPAddress.Loopback, preferredPort);
            listener.Start();
            listener.Stop();
            return preferredPort;
        }
        catch
        {
            log($"LangGraph 后端端口 {preferredPort} 不可用，将自动选择空闲回环端口。");
            return null;
        }
    }
}
