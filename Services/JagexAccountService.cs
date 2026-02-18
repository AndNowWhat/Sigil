using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Sigil.Models;

namespace Sigil.Services;

public sealed class JagexAccountService
{
    private readonly HttpClient _httpClient = new(new HttpClientHandler { UseCookies = false });
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<IReadOnlyList<GameAccount>> GetGameAccountsAsync(
        AppSettings settings,
        string sessionId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new InvalidOperationException("SessionId is missing.");
        }

        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{settings.AuthApiBase.TrimEnd('/')}/accounts");
        request.Headers.Add("Accept", "application/json");
        request.Headers.Add("Authorization", $"Bearer {sessionId}");

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var accounts = JsonSerializer.Deserialize<List<GameAccount>>(json, JsonOptions) ?? new List<GameAccount>();
        return accounts;
    }

    /// <summary>
    /// Creates a new RS3 character slot on the account. The character's display name is set
    /// in-game on first login. Returns the updated list of active characters.
    /// </summary>
    public async Task<IReadOnlyList<GameAccount>> CreateGameAccountAsync(
        string rsSessionToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rsSessionToken))
        {
            throw new InvalidOperationException(
                "RuneScape session token is missing. Re-add the account to enable character creation.");
        }

        const string url = "https://account.runescape.com/api/users/current/accounts/create";
        var body = new { clientLanguageCode = "en", receiveEmails = false, thirdPartyConsent = false };

        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("Accept", "application/json, text/plain, */*");
        request.Headers.Add("Cookie", $"runescape-accounts__session-token={rsSessionToken}");
        request.Content = new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Character creation failed: HTTP {(int)response.StatusCode} {response.StatusCode}.\nResponse: {(string.IsNullOrWhiteSpace(json) ? "(empty)" : json[..Math.Min(500, json.Length)])}");
        }

        // 204 No Content = created successfully; fetch the updated list separately
        if (string.IsNullOrWhiteSpace(json))
        {
            return await GetRuneScapeAccountsAsync(rsSessionToken, cancellationToken).ConfigureAwait(false);
        }

        RuneScapeAccountsResponse? result;
        try
        {
            result = JsonSerializer.Deserialize<RuneScapeAccountsResponse>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return await GetRuneScapeAccountsAsync(rsSessionToken, cancellationToken).ConfigureAwait(false);
        }

        // If the response parsed but active list is empty, fetch fresh to be safe
        if (result?.Active == null || result.Active.Count == 0)
        {
            return await GetRuneScapeAccountsAsync(rsSessionToken, cancellationToken).ConfigureAwait(false);
        }

        return result.Active;
    }

    /// <summary>
    /// Fetches the current character list from the RS account portal.
    /// </summary>
    public async Task<IReadOnlyList<GameAccount>> GetRuneScapeAccountsAsync(
        string rsSessionToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rsSessionToken))
            throw new InvalidOperationException("RuneScape session token is missing.");

        var request = new HttpRequestMessage(
            HttpMethod.Get,
            "https://account.runescape.com/api/users/current/accounts");
        request.Headers.Add("Accept", "application/json, text/plain, */*");
        request.Headers.Add("Cookie", $"runescape-accounts__session-token={rsSessionToken}");

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Failed to fetch characters: HTTP {(int)response.StatusCode}.\nResponse: {(string.IsNullOrWhiteSpace(json) ? "(empty)" : json[..Math.Min(500, json.Length)])}");
        }

        if (string.IsNullOrWhiteSpace(json))
            return new List<GameAccount>();

        var result = JsonSerializer.Deserialize<RuneScapeAccountsResponse>(json, JsonOptions);
        return result?.Active ?? new List<GameAccount>();
    }

    private sealed class RuneScapeAccountsResponse
    {
        [JsonPropertyName("active")]
        public List<GameAccount> Active { get; set; } = new();

        [JsonPropertyName("archived")]
        public List<GameAccount> Archived { get; set; } = new();
    }
}
