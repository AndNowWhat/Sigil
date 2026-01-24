using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Sigil.Models;

namespace Sigil.Services;

public sealed class LauncherService
{
    /// <summary>
    /// Launches the RS3 game client directly with session credentials.
    /// The game client reads JX_SESSION_ID, JX_CHARACTER_ID, and JX_DISPLAY_NAME from environment variables.
    /// </summary>
    public Task LaunchAsync(AppSettings settings, OAuthToken? token, GameAccount? selectedCharacter)
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
        // These are the standard Jagex environment variables for session authentication
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

        Process.Start(startInfo);
        return Task.CompletedTask;
    }
}
