using System;
using System.Collections.Generic;

namespace Sigil.Models;

public sealed class AccountProfile
{
    public string AccountId { get; set; } = Guid.NewGuid().ToString("N");
    public string DisplayName { get; set; } = "New Account";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastUsedAt { get; set; }
    public List<GameAccount> GameAccounts { get; set; } = new();
    public ProxyConfig? Proxy { get; set; }
}
