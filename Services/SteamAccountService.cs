using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Sigil.Models;

namespace Sigil.Services;

public sealed class SteamAccountService
{
    private const string RuneScapeAppName = "RuneScape";
    private static readonly Regex UserBlockRegex = new(
        "\"(?<id>\\d{17})\"\\s*\\{(?<body>.*?)\\n\\s*\\}",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex FieldRegex = new(
        "\"(?<key>[^\"]+)\"\\s*\"(?<value>[^\"]*)\"",
        RegexOptions.Compiled);

    public IReadOnlyList<SteamAccount> GetLocalAccounts(AppSettings settings)
    {
        var loginUsersPath = Path.Combine(GetSteamRoot(settings), "config", "loginusers.vdf");
        if (!File.Exists(loginUsersPath))
        {
            throw new FileNotFoundException($"Steam loginusers.vdf not found at: {loginUsersPath}");
        }

        var text = File.ReadAllText(loginUsersPath);
        var accounts = new List<SteamAccount>();

        foreach (Match match in UserBlockRegex.Matches(text))
        {
            var body = match.Groups["body"].Value;
            var fields = FieldRegex.Matches(body)
                .ToDictionary(m => m.Groups["key"].Value, m => m.Groups["value"].Value, StringComparer.OrdinalIgnoreCase);

            var account = new SteamAccount
            {
                SteamId64 = match.Groups["id"].Value,
                AccountName = GetField(fields, "AccountName"),
                PersonaName = GetField(fields, "PersonaName"),
                RememberPassword = GetField(fields, "RememberPassword") == "1",
                AllowAutoLogin = GetField(fields, "AllowAutoLogin") == "1",
                IsMostRecent = GetField(fields, "MostRecent") == "1"
            };

            if (string.IsNullOrWhiteSpace(account.AccountName))
            {
                continue;
            }

            accounts.Add(account);
        }

        return accounts
            .OrderByDescending(a => a.IsMostRecent)
            .ThenBy(a => a.PersonaName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.AccountName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public string GetSteamRoot(AppSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.SteamExePath))
        {
            var configuredDirectory = Path.GetDirectoryName(settings.SteamExePath);
            if (!string.IsNullOrWhiteSpace(configuredDirectory) && Directory.Exists(configuredDirectory))
            {
                return configuredDirectory;
            }
        }

        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        var steamPath = key?.GetValue("SteamPath")?.ToString();
        if (!string.IsNullOrWhiteSpace(steamPath) && Directory.Exists(steamPath))
        {
            return steamPath.Replace('/', Path.DirectorySeparatorChar);
        }

        throw new InvalidOperationException("Steam installation path could not be detected.");
    }

    public string GetSteamExePath(AppSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.SteamExePath) && File.Exists(settings.SteamExePath))
        {
            return settings.SteamExePath;
        }

        var path = Path.Combine(GetSteamRoot(settings), "steam.exe");
        if (File.Exists(path))
        {
            return path;
        }

        throw new FileNotFoundException($"steam.exe not found at: {path}");
    }

    public string GetRuneScapeClientPath(AppSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.SteamRs3ClientPath) && File.Exists(settings.SteamRs3ClientPath))
        {
            return settings.SteamRs3ClientPath;
        }

        var steamRoot = GetSteamRoot(settings);
        var libraryPaths = GetLibraryPaths(steamRoot);
        foreach (var libraryPath in libraryPaths)
        {
            var candidate = Path.Combine(libraryPath, "steamapps", "common", "RuneScape", "launcher", "rs2client.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("Steam RuneScape client was not found in any Steam library.");
    }

    public string GetRuneScapeAppId(AppSettings settings)
    {
        foreach (var libraryPath in GetLibraryPaths(GetSteamRoot(settings)))
        {
            var manifestDirectory = Path.Combine(libraryPath, "steamapps");
            if (!Directory.Exists(manifestDirectory))
            {
                continue;
            }

            foreach (var manifestPath in Directory.GetFiles(manifestDirectory, "appmanifest_*.acf"))
            {
                var text = File.ReadAllText(manifestPath);
                if (!text.Contains($"\"name\"\t\t\"{RuneScapeAppName}\"", StringComparison.OrdinalIgnoreCase) &&
                    !Regex.IsMatch(text, "\"name\"\\s+\"RuneScape\"", RegexOptions.IgnoreCase) &&
                    !Regex.IsMatch(text, "\"installdir\"\\s+\"RuneScape\"", RegexOptions.IgnoreCase))
                {
                    continue;
                }

                var match = Regex.Match(text, "\"appid\"\\s+\"(?<id>\\d+)\"", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    return match.Groups["id"].Value;
                }
            }
        }

        throw new FileNotFoundException("Steam RuneScape appmanifest was not found in any Steam library.");
    }

    public SteamAccount? GetCurrentAccount(AppSettings settings)
    {
        var accounts = GetLocalAccounts(settings);

        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        var autoLoginUser = key?.GetValue("AutoLoginUser")?.ToString();
        if (!string.IsNullOrWhiteSpace(autoLoginUser))
        {
            return accounts.FirstOrDefault(a =>
                string.Equals(a.AccountName, autoLoginUser, StringComparison.OrdinalIgnoreCase));
        }

        return accounts.FirstOrDefault(a => a.IsMostRecent);
    }

    public bool CanAutoLogin(SteamAccount account)
    {
        return account.RememberPassword;
    }

    private static IReadOnlyList<string> GetLibraryPaths(string steamRoot)
    {
        var paths = new List<string> { steamRoot };
        var libraryFoldersPath = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(libraryFoldersPath))
        {
            return paths;
        }

        var text = File.ReadAllText(libraryFoldersPath);
        foreach (Match match in Regex.Matches(text, "\"path\"\\s*\"(?<path>[^\"]+)\"", RegexOptions.IgnoreCase))
        {
            var path = match.Groups["path"].Value.Replace(@"\\", @"\");
            if (!string.IsNullOrWhiteSpace(path) &&
                Directory.Exists(path) &&
                !paths.Any(existing => string.Equals(existing, path, StringComparison.OrdinalIgnoreCase)))
            {
                paths.Add(path);
            }
        }

        return paths;
    }

    private static string GetField(IReadOnlyDictionary<string, string> fields, string key)
    {
        return fields.TryGetValue(key, out var value) ? value : string.Empty;
    }
}
