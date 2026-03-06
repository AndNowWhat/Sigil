using System;
using System.Collections.Generic;

namespace Sigil.Models;

public sealed class AccountProfile
{
    public string AccountId { get; set; } = Guid.NewGuid().ToString("N");
    public string DisplayName { get; set; } = "New Account";
    public AccountProvider Provider { get; set; } = AccountProvider.Jagex;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastUsedAt { get; set; }
    public List<GameAccount> GameAccounts { get; set; } = new();
    public ProxyConfig? Proxy { get; set; }
    public string? SteamId64 { get; set; }
    public string? SteamAccountName { get; set; }
    public string? SteamPersonaName { get; set; }
    public string? SteamCharacterName { get; set; }

    public string ProviderLabel => Provider == AccountProvider.Steam ? "Steam" : "Jagex";
    public string ListDisplayName => $"{DisplayName} [{ProviderLabel}]";
}
