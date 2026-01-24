using System;
using System.Text;
using System.Text.Json;

namespace Sigil.Services;

internal static class JwtHelper
{
    public static string? TryGetSubject(string? idToken)
    {
        return TryGetClaim(idToken, "sub");
    }

    public static string? TryGetNonce(string? idToken)
    {
        return TryGetClaim(idToken, "nonce");
    }

    public static string? TryGetClaim(string? idToken, string claim)
    {
        if (string.IsNullOrWhiteSpace(idToken))
        {
            return null;
        }

        var parts = idToken.Split('.');
        if (parts.Length < 2)
        {
            return null;
        }

        try
        {
            var payloadBytes = Base64UrlDecode(parts[1]);
            var doc = JsonDocument.Parse(payloadBytes);
            if (doc.RootElement.TryGetProperty(claim, out var value))
            {
                return value.GetString();
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var output = input.Replace('-', '+').Replace('_', '/');
        switch (output.Length % 4)
        {
            case 2:
                output += "==";
                break;
            case 3:
                output += "=";
                break;
        }

        return Convert.FromBase64String(output);
    }
}
