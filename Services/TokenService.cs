using System;
using System.Threading.Tasks;
using System.IO;
using Sigil.Models;
using Sigil.Storage;

namespace Sigil.Services;

public sealed class TokenService
{
    private const string CredentialPrefix = "Sigil:Jagex:";
    private const bool ProtectTokenFile = true;

    public async Task SaveAsync(string accountId, OAuthToken token)
    {
        if (string.IsNullOrWhiteSpace(token.RefreshToken))
        {
            throw new InvalidOperationException("Refresh token is missing.");
        }

        CredentialManager.WriteSecret(GetTarget(accountId), token.RefreshToken);

        var cache = new TokenCache
        {
            AccessToken = token.AccessToken,
            IdToken = token.IdToken,
            ExpiresAt = token.ExpiresAt,
            Subject = token.Subject,
            SessionId = token.SessionId,
            RuneScapeSessionToken = token.RuneScapeSessionToken
        };

        await JsonFileStore.SaveAsync(
            AppPaths.TokenFile(accountId),
            cache,
            ProtectTokenFile).ConfigureAwait(false);
    }

    public async Task<OAuthToken?> LoadAsync(string accountId)
    {
        var refreshToken = CredentialManager.ReadSecret(GetTarget(accountId));
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return null;
        }

        var cache = await JsonFileStore.LoadAsync(
            AppPaths.TokenFile(accountId),
            new TokenCache(),
            ProtectTokenFile).ConfigureAwait(false);

        return new OAuthToken
        {
            AccessToken = cache.AccessToken ?? string.Empty,
            RefreshToken = refreshToken,
            TokenType = "Bearer",
            ExpiresAt = cache.ExpiresAt == default ? DateTimeOffset.UtcNow : cache.ExpiresAt,
            IdToken = cache.IdToken,
            Subject = cache.Subject,
            SessionId = cache.SessionId,
            RuneScapeSessionToken = cache.RuneScapeSessionToken
        };
    }

    public Task DeleteAsync(string accountId)
    {
        CredentialManager.DeleteSecret(GetTarget(accountId));
        var path = AppPaths.TokenFile(accountId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private static string GetTarget(string accountId) => $"{CredentialPrefix}{accountId}";

    private sealed class TokenCache
    {
        public string? AccessToken { get; set; }
        public string? IdToken { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
        public string? Subject { get; set; }
        public string? SessionId { get; set; }
        public string? RuneScapeSessionToken { get; set; }
    }
}
