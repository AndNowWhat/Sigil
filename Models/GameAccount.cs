namespace Sigil.Models;

public sealed class GameAccount
{
    public string AccountId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string UserHash { get; set; } = string.Empty;

    public string DisplayLabel => string.IsNullOrWhiteSpace(DisplayName) ? AccountId : DisplayName;
}
