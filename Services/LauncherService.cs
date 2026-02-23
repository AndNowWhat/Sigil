using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Sigil.Models;

namespace Sigil.Services;

public sealed class LauncherService
{
    private readonly ProxInjectService _proxInject = new();

    /// <summary>
    /// Launches the RS3 game client directly with session credentials.
    /// The game client reads JX_SESSION_ID, JX_CHARACTER_ID, and JX_DISPLAY_NAME from environment variables.
    /// Returns the started process so the caller can inject into it (e.g. agent DLL).
    /// If a proxy is configured and ProxInject is installed, injects the proxy into the game process.
    /// </summary>
    public async Task<Process> LaunchAsync(AppSettings settings, OAuthToken? token, GameAccount? selectedCharacter, ProxyConfig? proxy = null, Action<string>? log = null)
    {
        var exePath = settings.Rs3ClientPath;

        if (string.IsNullOrWhiteSpace(exePath))
        {
            throw new InvalidOperationException("RS3 client path not configured. Please set the path in settings.");
        }

        if (!File.Exists(exePath))
        {
            throw new FileNotFoundException($"RS3 client not found at: {exePath}");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = false
        };

        // Set the JX_* environment variables that the game client expects
        if (token != null && !string.IsNullOrWhiteSpace(token.SessionId))
        {
            startInfo.Environment["JX_SESSION_ID"] = token.SessionId;
        }

        if (selectedCharacter != null)
        {
            startInfo.Environment["JX_CHARACTER_ID"] = selectedCharacter.AccountId;
            if (!string.IsNullOrWhiteSpace(selectedCharacter.DisplayName))
            {
                startInfo.Environment["JX_DISPLAY_NAME"] = selectedCharacter.DisplayName;
            }
        }

        var process = Process.Start(startInfo);
        if (process == null)
            throw new InvalidOperationException("Failed to start the game process.");

        // Inject proxy into the game process if configured
        if (proxy is { Enabled: true } && !string.IsNullOrWhiteSpace(proxy.Host) && ProxInjectService.IsInstalled)
        {
            try
            {
                // Give the process a moment to initialize before injecting
                await Task.Delay(2000).ConfigureAwait(false);
                await _proxInject.InjectAsync(process.Id, proxy, log).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                log?.Invoke($"Proxy injection warning: {ex.Message}");
            }
        }
        else if (proxy is { Enabled: true } && !ProxInjectService.IsInstalled)
        {
            log?.Invoke("Proxy configured but ProxInject not installed. Game traffic will NOT be proxied. Download it from Advanced Settings.");
        }

        return process;
    }
}
