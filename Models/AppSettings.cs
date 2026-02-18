namespace Sigil.Models;

public sealed class AppSettings
{
    public string? LastSelectedAccountId { get; set; }
    public LauncherType DefaultLauncher { get; set; } = LauncherType.Rs3Client;

    // Path to RS3 client executable - launched DIRECTLY with JX_SESSION_ID
    public string? Rs3ClientPath { get; set; } = @"C:\ProgramData\Jagex\launcher\rs2client.exe";

    // Seconds to wait between character creations (and between retries)
    public int CharacterCreationDelaySeconds { get; set; } = 60;

    public string OAuthOrigin { get; set; } = "https://account.jagex.com";
    public string OAuthRedirectUri { get; set; } = "https://secure.runescape.com/m=weblogin/launcher-redirect";
    public string OAuthClientId { get; set; } = "com_jagex_auth_desktop_launcher";
    public string OAuthScopes { get; set; } = "openid offline gamesso.token.create user.profile.read";
    public string OAuthConsentClientId { get; set; } = "1fddee4e-b100-4f4e-b2b0-097f9088f9d2";
    public string OAuthConsentScopes { get; set; } = "openid offline";
    public string AuthApiBase { get; set; } = "https://auth.jagex.com/game-session/v1";
}
