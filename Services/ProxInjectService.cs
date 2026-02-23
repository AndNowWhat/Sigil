using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Sigil.Models;
using Sigil.Storage;

namespace Sigil.Services;

public sealed class ProxInjectService
{
    private const string ReleaseUrl =
        "https://github.com/PragmaTwice/proxinject/releases/download/v0.5.0-pre/proxinject-v0.5.0-pre-x64.zip";
    private const string CliExeName = "proxinjector-cli.exe";

    public static string DefaultInstallDir =>
        Path.Combine(AppPaths.AppDataRoot, "proxinject");

    public static string DefaultCliPath =>
        Path.Combine(DefaultInstallDir, "release", CliExeName);

    public static bool IsInstalled => File.Exists(DefaultCliPath);

    /// <summary>
    /// Downloads and extracts proxinject to the Sigil app data folder.
    /// </summary>
    public async Task DownloadAsync(Action<string>? log = null, CancellationToken ct = default)
    {
        var dir = DefaultInstallDir;
        Directory.CreateDirectory(dir);

        var zipPath = Path.Combine(dir, "proxinject.zip");

        log?.Invoke($"Downloading proxinject from {ReleaseUrl} ...");

        using (var http = new HttpClient())
        {
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Sigil/1.0");
            using var response = await http.GetAsync(ReleaseUrl, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            await File.WriteAllBytesAsync(zipPath, bytes, ct).ConfigureAwait(false);
        }

        log?.Invoke("Extracting...");
        ZipFile.ExtractToDirectory(zipPath, dir, overwriteFiles: true);
        File.Delete(zipPath);

        if (!File.Exists(DefaultCliPath))
            throw new FileNotFoundException(
                $"Expected {CliExeName} not found after extraction. Check the zip contents in {dir}.");

        log?.Invoke("ProxInject installed successfully.");
    }

    /// <summary>
    /// Injects a SOCKS5/HTTP proxy into a running process by PID.
    /// </summary>
    public async Task InjectAsync(int processId, ProxyConfig proxy, Action<string>? log = null)
    {
        var cliPath = DefaultCliPath;
        if (!File.Exists(cliPath))
            throw new InvalidOperationException("ProxInject CLI not found. Please download it first.");

        // proxinjector-cli uses host:port format (no scheme prefix)
        var proxyAddress = $"{proxy.Host}:{proxy.Port}";
        var args = $"-i {processId} -p {proxyAddress}";

        log?.Invoke($"Running: {CliExeName} {args}");

        var psi = new ProcessStartInfo
        {
            FileName = cliPath,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi);
        if (proc == null)
            throw new InvalidOperationException("Failed to start proxinjector-cli.");

        var stdout = await proc.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
        var stderr = await proc.StandardError.ReadToEndAsync().ConfigureAwait(false);
        await proc.WaitForExitAsync().ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(stdout))
            log?.Invoke($"proxinject: {stdout.Trim()}");
        if (!string.IsNullOrWhiteSpace(stderr))
            log?.Invoke($"proxinject error: {stderr.Trim()}");

        if (proc.ExitCode != 0)
            log?.Invoke($"proxinject exited with code {proc.ExitCode}");
    }

    /// <summary>
    /// The URL users need to approve before downloading.
    /// </summary>
    public static string DownloadUrl => ReleaseUrl;
}
