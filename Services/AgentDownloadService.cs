using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Sigil.Services;

/// <summary>
/// Reads the loader's token.bin and downloads the agent from the BWU API
/// so we can inspect what the loader gets (~29.6 MB). Saves to a file for analysis.
/// </summary>
public sealed class AgentDownloadService
{
    private const string AgentDownloadUrl = "https://botwithus.com/api/download/agent";

    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(2)
    };

    /// <summary>
    /// Reads the Bearer token from the loader's token.bin.
    /// Tries: raw UTF-8 string (trimmed), then JSON with access_token / token / bearer.
    /// </summary>
    /// <param name="tokenBinPath">Full path to token.bin (e.g. loader folder).</param>
    /// <returns>The token string to use as Authorization: Bearer &lt;token&gt;.</returns>
    public string ReadTokenFromFile(string tokenBinPath)
    {
        if (string.IsNullOrWhiteSpace(tokenBinPath) || !File.Exists(tokenBinPath))
            throw new FileNotFoundException("token.bin not found.", tokenBinPath);

        var bytes = File.ReadAllBytes(tokenBinPath);
        if (bytes.Length == 0)
            throw new InvalidOperationException("token.bin is empty.");

        // Try UTF-8 string first (raw token)
        var asUtf8 = Encoding.UTF8.GetString(bytes).Trim();
        if (!string.IsNullOrWhiteSpace(asUtf8))
        {
            // If it looks like JSON, parse for token fields
            if (asUtf8.StartsWith('{'))
            {
                try
                {
                    using var doc = JsonDocument.Parse(asUtf8);
                    var root = doc.RootElement;
                    foreach (var key in new[] { "access_token", "token", "bearer", "accessToken" })
                    {
                        if (root.TryGetProperty(key, out var prop))
                        {
                            var value = prop.GetString();
                            if (!string.IsNullOrWhiteSpace(value))
                                return value;
                        }
                    }
                }
                catch
                {
                    // Not valid JSON or no token field; use raw string if it looks like a token
                }
            }
            // Use as raw Bearer token if it's not empty and doesn't look like binary
            if (asUtf8.Length < 10_000 && asUtf8.IndexOf('\0') < 0)
                return asUtf8;
        }

        throw new InvalidOperationException(
            "Could not read a token from token.bin. Expected UTF-8 text (raw token or JSON with access_token/token).");
    }

    /// <summary>
    /// Downloads the agent from the BWU API using the given Bearer token
    /// and saves it to <paramref name="saveFilePath"/> for inspection.
    /// </summary>
    /// <param name="bearerToken">Token from token.bin (Authorization: Bearer).</param>
    /// <param name="saveFilePath">Where to save the response (e.g. agent_download.bin).</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Number of bytes written and whether response looks like a PE (MZ header).</returns>
    public async Task<AgentDownloadResult> DownloadAgentAsync(
        string bearerToken,
        string saveFilePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(bearerToken))
            throw new ArgumentException("Bearer token is required.", nameof(bearerToken));

        // API returns a presigned S3 URL (body is the URL string). We GET that URL to download the actual DLL.
        using var request = new HttpRequestMessage(HttpMethod.Get, AgentDownloadUrl);
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + bearerToken);
        request.Headers.TryAddWithoutValidation("Accept", "*/*");

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException(
                $"Agent download failed: {response.StatusCode}. {body}");
        }

        var downloadUrl = (await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)).Trim();
        if (string.IsNullOrEmpty(downloadUrl) || !downloadUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("API did not return a download URL.");

        using var downloadRequest = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
        using var downloadResponse = await _httpClient.SendAsync(
            downloadRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        downloadResponse.EnsureSuccessStatusCode();

        await using (var stream = await downloadResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        using (var file = File.Create(saveFilePath))
        {
            await stream.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
        }

        var length = new FileInfo(saveFilePath).Length;
        byte[] header = new byte[2];
        using (var fs = File.OpenRead(saveFilePath))
            _ = await fs.ReadAsync(header.AsMemory(0, 2), cancellationToken).ConfigureAwait(false);

        var isPe = header.Length >= 2 && header[0] == 0x4D && header[1] == 0x5A; // MZ

        return new AgentDownloadResult(saveFilePath, length, isPe);
    }

    /// <summary>
    /// Reads token from token.bin and downloads the agent to a file.
    /// </summary>
    /// <param name="tokenBinPath">Path to loader's token.bin.</param>
    /// <param name="saveFilePath">Where to save (default: temp folder, agent_download_YYYYMMDD_HHmmss.bin).</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Path, size, and whether the file starts with MZ (PE).</returns>
    public async Task<AgentDownloadResult> DownloadAgentToFileAsync(
        string tokenBinPath,
        string? saveFilePath = null,
        CancellationToken cancellationToken = default)
    {
        var token = ReadTokenFromFile(tokenBinPath);
        saveFilePath ??= Path.Combine(
            Path.GetTempPath(),
            $"agent_download_{DateTime.Now:yyyyMMdd_HHmmss}.dll");
        return await DownloadAgentAsync(token, saveFilePath, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Result of downloading the agent for inspection.</summary>
/// <param name="FilePath">Path where the response was saved.</param>
/// <param name="ByteCount">Number of bytes written.</param>
/// <param name="StartsWithMz">True if the file starts with MZ (PE image).</param>
public readonly record struct AgentDownloadResult(string FilePath, long ByteCount, bool StartsWithMz);
