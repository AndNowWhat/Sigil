namespace Sigil.Models;

public sealed class SteamAccount
{
    public string SteamId64 { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string PersonaName { get; set; } = string.Empty;
    public bool RememberPassword { get; set; }
    public bool AllowAutoLogin { get; set; }
    public bool IsMostRecent { get; set; }

    public string DisplayLabel =>
        string.IsNullOrWhiteSpace(PersonaName)
            ? $"{AccountName} ({SteamId64})"
            : $"{PersonaName} ({AccountName})";
}
