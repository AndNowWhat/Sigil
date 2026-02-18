using System;

namespace Sigil.Models;

public sealed class OAuthToken
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public string TokenType { get; set; } = "Bearer";
    public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddMinutes(10);
    public string? IdToken { get; set; }
    public string? Subject { get; set; }
    public string? SessionId { get; set; }
    public string? RuneScapeSessionToken { get; set; }

    public bool IsExpired(TimeSpan? skew = null)
    {
        var effectiveSkew = skew ?? TimeSpan.FromMinutes(1);
        return DateTimeOffset.UtcNow >= ExpiresAt.Subtract(effectiveSkew);
    }
}
