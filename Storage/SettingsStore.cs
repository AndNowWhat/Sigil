using System.Threading.Tasks;
using Sigil.Models;

namespace Sigil.Storage;

public sealed class SettingsStore
{
    private const bool ProtectSettingsFile = false;

    public async Task<AppSettings> LoadAsync()
    {
        var settings = await JsonFileStore.LoadAsync(
            AppPaths.SettingsFile,
            new AppSettings(),
            ProtectSettingsFile).ConfigureAwait(false);

        if (ApplyDefaults(settings))
        {
            await SaveAsync(settings).ConfigureAwait(false);
        }

        return settings;
    }

    public Task SaveAsync(AppSettings settings)
    {
        return JsonFileStore.SaveAsync(
            AppPaths.SettingsFile,
            settings,
            ProtectSettingsFile);
    }

    private static bool ApplyDefaults(AppSettings settings)
    {
        var defaults = new AppSettings();
        var changed = false;

        if (settings.OAuthOrigin != defaults.OAuthOrigin)
        {
            settings.OAuthOrigin = defaults.OAuthOrigin;
            changed = true;
        }

        if (settings.OAuthRedirectUri != defaults.OAuthRedirectUri)
        {
            settings.OAuthRedirectUri = defaults.OAuthRedirectUri;
            changed = true;
        }

        if (settings.OAuthClientId != defaults.OAuthClientId)
        {
            settings.OAuthClientId = defaults.OAuthClientId;
            changed = true;
        }

        if (settings.OAuthScopes != defaults.OAuthScopes)
        {
            settings.OAuthScopes = defaults.OAuthScopes;
            changed = true;
        }

        if (settings.OAuthConsentClientId != defaults.OAuthConsentClientId)
        {
            settings.OAuthConsentClientId = defaults.OAuthConsentClientId;
            changed = true;
        }

        if (settings.OAuthConsentScopes != defaults.OAuthConsentScopes)
        {
            settings.OAuthConsentScopes = defaults.OAuthConsentScopes;
            changed = true;
        }

        if (settings.AuthApiBase != defaults.AuthApiBase)
        {
            settings.AuthApiBase = defaults.AuthApiBase;
            changed = true;
        }

        return changed;
    }
}
