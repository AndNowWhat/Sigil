using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Sigil.Models;

namespace Sigil.Services;

public sealed class SteamLaunchService
{
    private readonly SteamAccountService _steamAccountService;
    private readonly ProxInjectService _proxInject = new();

    public SteamLaunchService(SteamAccountService steamAccountService)
    {
        _steamAccountService = steamAccountService;
    }

    public async Task<bool> EnsureActiveAccountAsync(AppSettings settings, AccountProfile profile, Action<string>? log = null)
    {
        var targetAccountName = profile.SteamAccountName;
        if (string.IsNullOrWhiteSpace(targetAccountName))
        {
            throw new InvalidOperationException("Steam account name is missing for this profile.");
        }

        var current = _steamAccountService.GetCurrentAccount(settings);
        if (current != null && string.Equals(current.AccountName, targetAccountName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var candidates = _steamAccountService.GetLocalAccounts(settings);
        var target = candidates.FirstOrDefault(a =>
            string.Equals(a.AccountName, targetAccountName, StringComparison.OrdinalIgnoreCase));

        if (target == null)
        {
            throw new InvalidOperationException($"Steam account '{targetAccountName}' was not found locally.");
        }

        if (!_steamAccountService.CanAutoLogin(target))
        {
            log?.Invoke($"Steam account '{target.AccountName}' is not remembered for auto-login.");
            return false;
        }

        log?.Invoke($"Switching Steam to '{target.AccountName}'...");
        await RestartSteamAsync(settings, target.AccountName).ConfigureAwait(false);

        var verified = await WaitForAccountAsync(settings, target.AccountName).ConfigureAwait(false);
        if (!verified)
        {
            log?.Invoke($"Steam did not switch to '{target.AccountName}'.");
        }

        return verified;
    }

    public Task<Process> LaunchAsync(AppSettings settings, AccountProfile profile, ProxyConfig? proxy = null, Action<string>? log = null)
    {
        var steamExe = _steamAccountService.GetSteamExePath(settings);
        var appId = _steamAccountService.GetRuneScapeAppId(settings);

        var startInfo = new ProcessStartInfo
        {
            FileName = steamExe,
            WorkingDirectory = Path.GetDirectoryName(steamExe) ?? string.Empty,
            Arguments = $"-applaunch {appId}",
            UseShellExecute = true
        };

        var process = Process.Start(startInfo);
        if (process == null)
        {
            throw new InvalidOperationException("Failed to launch RuneScape through Steam.");
        }

        if (proxy is { Enabled: true } && !string.IsNullOrWhiteSpace(proxy.Host) && ProxInjectService.IsInstalled)
        {
            log?.Invoke("Steam launches RuneScape via Steam bootstrap. Proxy injection is not applied on the initial Steam process.");
        }
        else if (proxy is { Enabled: true } && !ProxInjectService.IsInstalled)
        {
            log?.Invoke("Proxy configured but ProxInject not installed. Game traffic will NOT be proxied. Download it from Advanced Settings.");
        }

        return Task.FromResult(process);
    }

    private async Task RestartSteamAsync(AppSettings settings, string accountName)
    {
        var steamExe = _steamAccountService.GetSteamExePath(settings);

        foreach (var process in Process.GetProcessesByName("steam"))
        {
            try
            {
                process.CloseMainWindow();
            }
            catch
            {
                // Fall back to shutdown command below.
            }
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = steamExe,
                Arguments = "-shutdown",
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore; Steam may already be closing.
        }

        var shutdownDeadline = DateTime.UtcNow.AddSeconds(20);
        while (Process.GetProcessesByName("steam").Length > 0 && DateTime.UtcNow < shutdownDeadline)
        {
            await Task.Delay(500).ConfigureAwait(false);
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = steamExe,
            Arguments = $"-login \"{accountName}\"",
            UseShellExecute = true
        });
    }

    private async Task<bool> WaitForAccountAsync(AppSettings settings, string accountName)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(1000).ConfigureAwait(false);
            var current = _steamAccountService.GetCurrentAccount(settings);
            if (current != null && string.Equals(current.AccountName, accountName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
