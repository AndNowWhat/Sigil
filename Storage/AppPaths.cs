using System;
using System.IO;

namespace Sigil.Storage;

public static class AppPaths
{
    public static string AppDataRoot
    {
        get
        {
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Sigil");
            Directory.CreateDirectory(root);
            return root;
        }
    }

    public static string AccountsFile => Path.Combine(AppDataRoot, "accounts.json");
    public static string SettingsFile => Path.Combine(AppDataRoot, "settings.json");

    public static string TokensDirectory
    {
        get
        {
            var root = Path.Combine(AppDataRoot, "tokens");
            Directory.CreateDirectory(root);
            return root;
        }
    }

    public static string TokenFile(string accountId) => Path.Combine(TokensDirectory, $"{accountId}.json");
}
