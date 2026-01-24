using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Sigil.Storage;

public static class JsonFileStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static async Task<T> LoadAsync<T>(string path, T defaultValue, bool protect)
    {
        if (!File.Exists(path))
        {
            return defaultValue;
        }

        var text = await File.ReadAllTextAsync(path).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(text))
        {
            return defaultValue;
        }

        if (protect)
        {
            var bytes = Convert.FromBase64String(text);
            var unprotected = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
            text = Encoding.UTF8.GetString(unprotected);
        }

        return JsonSerializer.Deserialize<T>(text, SerializerOptions) ?? defaultValue;
    }

    public static async Task SaveAsync<T>(string path, T value, bool protect)
    {
        var json = JsonSerializer.Serialize(value, SerializerOptions);
        if (protect)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            json = Convert.ToBase64String(protectedBytes);
        }

        await File.WriteAllTextAsync(path, json).ConfigureAwait(false);
    }
}
