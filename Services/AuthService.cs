using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Sigil.Models;

namespace Sigil.Services;

public sealed class AuthService
{
    private readonly HttpClient _httpClient = new();

    public AuthStart BeginLogin(AppSettings settings)
    {
        var effective = NormalizeSettings(settings);
        ValidateSettings(effective);
        var state = Guid.NewGuid().ToString("N");
        var (verifier, challenge) = CreatePkcePair();
        var loginUrl = BuildLoginUrl(effective, state, challenge);
        return new AuthStart(state, verifier, loginUrl);
    }

    public string BuildConsentUrl(AppSettings settings, string idToken, string nonce)
    {
        var effective = NormalizeSettings(settings);
        var state = Guid.NewGuid().ToString("N");
        var query = new Dictionary<string, string>
        {
            ["id_token_hint"] = idToken,
            ["nonce"] = nonce,
            ["prompt"] = "consent",
            ["redirect_uri"] = "http://localhost",
            ["response_type"] = "id_token code",
            ["state"] = state,
            ["client_id"] = effective.OAuthConsentClientId,
            ["scope"] = effective.OAuthConsentScopes
        };

        var builder = new UriBuilder($"{effective.OAuthOrigin.TrimEnd('/')}/oauth2/auth")
        {
            Query = BuildQuery(query)
        };

        return builder.Uri.ToString();
    }

    public async Task<OAuthToken> ExchangeCodeAsync(
        AppSettings settings,
        string code,
        string verifier,
        CancellationToken cancellationToken)
    {
        var effective = NormalizeSettings(settings);
        var payload = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["client_id"] = effective.OAuthClientId,
            ["redirect_uri"] = effective.OAuthRedirectUri,
            ["code_verifier"] = verifier
        };

        using var response = await _httpClient.PostAsync(
            $"{effective.OAuthOrigin.TrimEnd('/')}/oauth2/token",
            new FormUrlEncodedContent(payload),
            cancellationToken).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return ParseToken(json);
    }

    public async Task<OAuthToken> RefreshAsync(AppSettings settings, OAuthToken token, CancellationToken cancellationToken)
    {
        var effective = NormalizeSettings(settings);
        var payload = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = token.RefreshToken,
            ["client_id"] = effective.OAuthClientId
        };

        using var response = await _httpClient.PostAsync(
            $"{effective.OAuthOrigin.TrimEnd('/')}/oauth2/token",
            new FormUrlEncodedContent(payload),
            cancellationToken).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var refreshed = ParseToken(json, token.RefreshToken);
        refreshed.SessionId = token.SessionId;
        refreshed.Subject ??= token.Subject;
        refreshed.IdToken ??= token.IdToken;
        refreshed.RuneScapeSessionToken = token.RuneScapeSessionToken;
        return refreshed;
    }

    public async Task<string> GetSessionIdAsync(AppSettings settings, string idToken, CancellationToken cancellationToken)
    {
        var effective = NormalizeSettings(settings);
        var payload = new
        {
            idToken
        };

        using var response = await _httpClient.PostAsync(
            $"{effective.AuthApiBase.TrimEnd('/')}/sessions",
            new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
            cancellationToken).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("sessionId", out var sessionId))
        {
            return sessionId.GetString() ?? string.Empty;
        }

        throw new InvalidOperationException("Session response did not include sessionId.");
    }

    private static OAuthToken ParseToken(string json, string? fallbackRefreshToken = null)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var expiresIn = root.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 900;
        var refreshToken = root.TryGetProperty("refresh_token", out var refresh) ? refresh.GetString() : fallbackRefreshToken;
        var idToken = root.TryGetProperty("id_token", out var id) ? id.GetString() : null;

        return new OAuthToken
        {
            AccessToken = root.GetProperty("access_token").GetString() ?? string.Empty,
            RefreshToken = refreshToken ?? string.Empty,
            TokenType = root.TryGetProperty("token_type", out var type) ? type.GetString() ?? "Bearer" : "Bearer",
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn),
            IdToken = idToken,
            Subject = JwtHelper.TryGetSubject(idToken)
        };
    }

    private static void ValidateSettings(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.OAuthClientId) ||
            string.IsNullOrWhiteSpace(settings.OAuthOrigin) ||
            string.IsNullOrWhiteSpace(settings.OAuthRedirectUri) ||
            string.IsNullOrWhiteSpace(settings.OAuthConsentClientId) ||
            string.IsNullOrWhiteSpace(settings.AuthApiBase))
        {
            throw new InvalidOperationException("OAuth settings are missing. Configure them in settings.json.");
        }
    }

    private static AppSettings NormalizeSettings(AppSettings settings)
    {
        var defaults = new AppSettings();
        return new AppSettings
        {
            OAuthOrigin = defaults.OAuthOrigin,
            OAuthRedirectUri = defaults.OAuthRedirectUri,
            OAuthClientId = defaults.OAuthClientId,
            OAuthScopes = defaults.OAuthScopes,
            OAuthConsentClientId = defaults.OAuthConsentClientId,
            OAuthConsentScopes = defaults.OAuthConsentScopes,
            AuthApiBase = defaults.AuthApiBase,
            Rs3ClientPath = settings.Rs3ClientPath,
            DefaultLauncher = settings.DefaultLauncher,
            LastSelectedAccountId = settings.LastSelectedAccountId
        };
    }

    private string BuildLoginUrl(AppSettings settings, string state, string challenge)
    {
        var query = new Dictionary<string, string>
        {
            ["auth_method"] = string.Empty,
            ["login_type"] = string.Empty,
            ["flow"] = "launcher",
            ["response_type"] = "code",
            ["client_id"] = settings.OAuthClientId,
            ["redirect_uri"] = settings.OAuthRedirectUri,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["prompt"] = "login",
            ["scope"] = settings.OAuthScopes,
            ["state"] = state
        };

        var builder = new UriBuilder($"{settings.OAuthOrigin.TrimEnd('/')}/oauth2/auth")
        {
            Query = BuildQuery(query)
        };

        return builder.Uri.ToString();
    }

    private static (string verifier, string challenge) CreatePkcePair()
    {
        var verifierBytes = RandomNumberGenerator.GetBytes(32);
        var verifier = Base64UrlEncode(verifierBytes);
        using var sha = SHA256.Create();
        var challenge = Base64UrlEncode(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)));
        return (verifier, challenge);
    }

    private static string Base64UrlEncode(byte[] input)
    {
        return Convert.ToBase64String(input)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string BuildQuery(Dictionary<string, string> query)
    {
        var builder = new StringBuilder();
        foreach (var (key, value) in query)
        {
            if (builder.Length > 0)
            {
                builder.Append('&');
            }

            builder.Append(Uri.EscapeDataString(key));
            builder.Append('=');
            builder.Append(Uri.EscapeDataString(value));
        }

        return builder.ToString();
    }
}

public sealed record AuthStart(string State, string Verifier, string LoginUrl);
