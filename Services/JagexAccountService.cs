using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Sigil.Models;

namespace Sigil.Services;

public sealed class JagexAccountService
{
    private readonly HttpClient _httpClient = new();
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
}
