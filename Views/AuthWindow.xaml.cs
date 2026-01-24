using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Sigil.Models;
using Sigil.Services;
using System.Text.Json;

namespace Sigil.Views;

public partial class AuthWindow : Window
{
    private readonly AuthService _authService;
    private readonly AppSettings _settings;
    private readonly Action<string>? _log;
    private readonly TaskCompletionSource<OAuthToken> _tcs = new();
    private string? _expectedState;
    private string? _verifier;
    private string? _expectedNonce;
    private bool _completed;
    private OAuthToken? _pendingToken;

    public AuthWindow(AuthService authService, AppSettings settings, Action<string>? log)
    {
        InitializeComponent();
        _authService = authService;
        _settings = settings;
        _log = log;
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    public Task<OAuthToken> AuthenticateAsync()
    {
        Show();
        return _tcs.Task;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await Browser.EnsureCoreWebView2Async();
            Browser.CoreWebView2.NavigationStarting += OnNavigationStarting;
            Browser.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            StartLogin();
        }
        catch (Exception ex)
        {
            Fail($"WebView2 failed to initialize: {ex.Message}");
        }
    }

    private void StartLogin()
    {
        try
        {
            var start = _authService.BeginLogin(_settings);
            _expectedState = start.State;
            _verifier = start.Verifier;
            StatusText.Text = "Sign in to your Jagex account in the browser.";
            _log?.Invoke("Login URL opened.");
            Browser.CoreWebView2.Navigate(start.LoginUrl);
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }
    }

    private async void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (_completed || string.IsNullOrWhiteSpace(e.Uri))
        {
            return;
        }

        if (IsDiagnosticUrl(e.Uri))
        {
            _log?.Invoke($"Navigating: {e.Uri}");
        }

        if (TryParseRedirect(e.Uri, out var code, out var state))
        {
            e.Cancel = true;
            await HandleAuthorizationCodeAsync(code, state);
            return;
        }

        if (TryParseConsent(e.Uri, out var idToken, out var consentCode))
        {
            e.Cancel = true;
            await HandleConsentAsync(idToken, consentCode);
        }
    }

    private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (_completed || Browser.CoreWebView2 == null)
        {
            return;
        }

        var uri = Browser.CoreWebView2.Source?.ToString();
        if (IsDiagnosticUrl(uri))
        {
            _log?.Invoke($"Navigation completed: {uri}");
        }

        if (!_completed && uri != null && uri.Contains("launcher-redirect", StringComparison.OrdinalIgnoreCase))
        {
            await TryExtractRedirectFromPageAsync();
        }
    }

    private bool TryParseRedirect(string uri, out string? code, out string? state)
    {
        code = null;
        state = null;

        if (uri.StartsWith("jagex:", StringComparison.OrdinalIgnoreCase))
        {
            var raw = uri.Substring("jagex:".Length).Replace(',', '&');
            var parsedQuery = ParseQuery(raw);
            if (parsedQuery.TryGetValue("error", out var error))
            {
                var description = parsedQuery.TryGetValue("error_description", out var detail) ? detail : string.Empty;
                Fail($"OAuth error: {error}. {description}".Trim());
                return false;
            }

            code = parsedQuery.TryGetValue("code", out var parsedCode) ? parsedCode : null;
            state = parsedQuery.TryGetValue("state", out var parsedState) ? parsedState : null;
            return !string.IsNullOrWhiteSpace(code);
        }

        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
        {
            return false;
        }

        if (!Uri.TryCreate(_settings.OAuthRedirectUri, UriKind.Absolute, out var redirect))
        {
            return false;
        }

        if (!string.Equals(parsed.Host, redirect.Host, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(parsed.AbsolutePath, redirect.AbsolutePath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var query = ParseQuery(parsed.Query);
        if (query.TryGetValue("error", out var redirectError))
        {
            var description = query.TryGetValue("error_description", out var detail) ? detail : string.Empty;
            Fail($"OAuth error: {redirectError}. {description}".Trim());
            return false;
        }

        code = query.TryGetValue("code", out var parsedCodeFromRedirect) ? parsedCodeFromRedirect : null;
        state = query.TryGetValue("state", out var parsedStateFromRedirect) ? parsedStateFromRedirect : null;
        return !string.IsNullOrWhiteSpace(code);
    }

    private bool IsDiagnosticUrl(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            return false;
        }

        if (uri.StartsWith("jagex:", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (uri.Contains("launcher-redirect", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return uri.Contains("localhost", StringComparison.OrdinalIgnoreCase);
    }

    private async Task TryExtractRedirectFromPageAsync()
    {
        try
        {
            var script = "JSON.stringify(Array.from(document.querySelectorAll('a[href^=\"jagex:\"]')).map(a => a.href))";
            var json = await Browser.ExecuteScriptAsync(script);
            var links = ParseWebViewJsonArray(json);
            foreach (var link in links)
            {
                if (TryParseRedirect(link, out var code, out var state))
                {
                    _log?.Invoke("Extracted jagex scheme redirect from page.");
                    await HandleAuthorizationCodeAsync(code, state);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            _log?.Invoke($"Failed to inspect redirect page: {ex.Message}");
        }
    }

    private async Task HandleAuthorizationCodeAsync(string? code, string? state)
    {
        if (state != _expectedState || string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(_verifier))
        {
            Fail("OAuth response invalid or state mismatch.");
            return;
        }

        try
        {
            StatusText.Text = "Exchanging code for tokens...";
            _log?.Invoke("Authorization code received. Exchanging for tokens.");
            var token = await _authService.ExchangeCodeAsync(_settings, code, _verifier, CancellationToken.None);
            if (string.IsNullOrWhiteSpace(token.IdToken))
            {
                Fail("Login did not return an id_token.");
                return;
            }

            var nonce = Guid.NewGuid().ToString("N");
            _expectedNonce = nonce;
            _pendingToken = token;
            StatusText.Text = "Requesting consent...";
            _log?.Invoke("Token exchange complete. Requesting consent.");
            var consentUrl = _authService.BuildConsentUrl(_settings, token.IdToken, nonce);
            Browser.CoreWebView2.Navigate(consentUrl);
        }
        catch (Exception ex)
        {
            Fail($"Token exchange failed: {ex.Message}");
        }
    }

    private async Task HandleConsentAsync(string? idToken, string? consentCode)
    {
        if (string.IsNullOrWhiteSpace(idToken) || string.IsNullOrWhiteSpace(consentCode))
        {
            Fail("Consent response was incomplete.");
            return;
        }

        var nonce = JwtHelper.TryGetNonce(idToken);
        if (string.IsNullOrWhiteSpace(nonce) || nonce != _expectedNonce)
        {
            Fail("Consent nonce validation failed.");
            return;
        }

        try
        {
            StatusText.Text = "Creating session...";
            _log?.Invoke("Consent complete. Creating session.");
            var sessionId = await _authService.GetSessionIdAsync(_settings, idToken, CancellationToken.None);
            if (_pendingToken == null)
            {
                Fail("No OAuth token is available.");
                return;
            }

            _pendingToken.IdToken = idToken;
            _pendingToken.SessionId = sessionId;
            _pendingToken.Subject ??= JwtHelper.TryGetSubject(idToken);
            _log?.Invoke($"Session created. SessionId={sessionId}");
            Complete(_pendingToken);
        }
        catch (Exception ex)
        {
            Fail($"Session creation failed: {ex.Message}");
        }
    }

    private static bool TryParseConsent(string uri, out string? idToken, out string? code)
    {
        idToken = null;
        code = null;

        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
        {
            return false;
        }

        if (!string.Equals(parsed.Host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var query = ParseQuery(parsed.Query);
        if (query.Count == 0 && !string.IsNullOrWhiteSpace(parsed.Fragment))
        {
            query = ParseQuery(parsed.Fragment);
        }

        idToken = query.TryGetValue("id_token", out var qIdToken) ? qIdToken : null;
        code = query.TryGetValue("code", out var qCode) ? qCode : null;
        return !string.IsNullOrWhiteSpace(idToken);
    }

    private static Dictionary<string, string> ParseQuery(string queryOrFragment)
    {
        var query = queryOrFragment.TrimStart('?', '#');
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(query))
        {
            return dict;
        }

        var pairs = query.Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var pair in pairs)
        {
            var parts = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0]);
            var value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
            dict[key] = value;
        }

        return dict;
    }

    private static string[] ParseWebViewJsonArray(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                var list = new List<string>();
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        list.Add(item.GetString() ?? string.Empty);
                    }
                }

                return list.ToArray();
            }

            if (doc.RootElement.ValueKind == JsonValueKind.String)
            {
                var inner = doc.RootElement.GetString();
                if (string.IsNullOrWhiteSpace(inner))
                {
                    return Array.Empty<string>();
                }

                using var innerDoc = JsonDocument.Parse(inner);
                if (innerDoc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    var list = new List<string>();
                    foreach (var item in innerDoc.RootElement.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            list.Add(item.GetString() ?? string.Empty);
                        }
                    }

                    return list.ToArray();
                }
            }
        }
        catch
        {
            return Array.Empty<string>();
        }

        return Array.Empty<string>();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        Fail("Login canceled.");
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_completed)
        {
            _tcs.TrySetCanceled();
        }
    }

    private void Complete(OAuthToken token)
    {
        if (_completed)
        {
            return;
        }

        _completed = true;
        _tcs.TrySetResult(token);
        Close();
    }

    private void Fail(string message)
    {
        if (_completed)
        {
            return;
        }

        _completed = true;
        _log?.Invoke($"Auth failed: {message}");
        _tcs.TrySetException(new InvalidOperationException(message));
        Close();
    }
}
