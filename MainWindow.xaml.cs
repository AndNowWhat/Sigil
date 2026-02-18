using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using Sigil.Models;
using Sigil.Services;
using Sigil.Storage;
using Sigil.Views;

namespace Sigil
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private readonly AccountStore _accountStore = new();
        private readonly SettingsStore _settingsStore = new();
        private readonly TokenService _tokenService = new();
        private readonly AuthService _authService = new();
        private readonly LauncherService _launcherService = new();
        private readonly JagexAccountService _jagexAccountService = new();
        private readonly CharacterCreationQueueService _creationQueue;

        private AccountProfile? _selectedAccount;
        private GameAccount? _selectedCharacter;
        private string _statusText = "Ready";
        private string _queueStatusText = string.Empty;
        private bool _isQueueActive;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            Loaded += OnLoaded;
            Closed += OnWindowClosed;
            LogLines.CollectionChanged += (_, _) => LogScrollViewer.ScrollToBottom();

            _creationQueue = new CharacterCreationQueueService(_jagexAccountService);
            _creationQueue.CharacterCreated   += OnQueueCharacterCreated;
            _creationQueue.BatchCompleted     += OnQueueBatchCompleted;
            _creationQueue.PendingCountChanged += OnQueueCountChanged;
            _creationQueue.StatusUpdated      += OnQueueStatus;
        }

        public ObservableCollection<AccountProfile> Accounts { get; } = new();
        public ObservableCollection<GameAccount> SelectedCharacters { get; } = new();
        public ObservableCollection<string> LogLines { get; } = new();
        private AppSettings _settings = new();

        public AppSettings Settings
        {
            get => _settings;
            private set
            {
                _settings = value;
                OnPropertyChanged();
            }
        }

        public AccountProfile? SelectedAccount
        {
            get => _selectedAccount;
            set
            {
                if (_selectedAccount == value) return;

                _selectedAccount = value;
                OnPropertyChanged();
                UpdateSelectedCharacters();
                Settings.LastSelectedAccountId = _selectedAccount?.AccountId;
                _ = _settingsStore.SaveAsync(Settings);
                _ = UpdateStatusAsync();
            }
        }

        public string StatusText
        {
            get => _statusText;
            set
            {
                _statusText = value;
                OnPropertyChanged();
                AppendLog(value);
            }
        }

        private void AppendLog(string message)
        {
            LogLines.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
            while (LogLines.Count > 100)
                LogLines.RemoveAt(0);
        }

        public GameAccount? SelectedCharacter
        {
            get => _selectedCharacter;
            set
            {
                if (_selectedCharacter == value) return;
                _selectedCharacter = value;
                OnPropertyChanged();
            }
        }

        public string CharacterCountText =>
            $"{_selectedAccount?.GameAccounts.Count ?? 0} / 20";

        public bool CanCreateCharacter =>
            _selectedAccount != null && (_selectedAccount.GameAccounts.Count < 20);

        public bool CanAutoCreate =>
            _selectedAccount != null && _selectedAccount.GameAccounts.Count < 20;

        public string QueueStatusText
        {
            get => _queueStatusText;
            private set { _queueStatusText = value; OnPropertyChanged(); }
        }

        public bool IsQueueActive
        {
            get => _isQueueActive;
            private set { _isQueueActive = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            Settings = await _settingsStore.LoadAsync();
            var accounts = await _accountStore.LoadAsync();
            Accounts.Clear();
            foreach (var account in accounts)
            {
                Accounts.Add(account);
            }

            SelectedAccount = Accounts.FirstOrDefault(a => a.AccountId == Settings.LastSelectedAccountId)
                ?? Accounts.FirstOrDefault();

            await UpdateStatusAsync();
        }

        private async void OnAddAccount(object sender, RoutedEventArgs e)
        {
            try
            {
                StatusText = "Opening browser for login...";
                var authWindow = new AuthWindow(_authService, Settings, null)
                {
                    Owner = this
                };
                var token = await authWindow.AuthenticateAsync();
                var accountId = token.Subject ?? Guid.NewGuid().ToString("N");

                if (Accounts.Any(a => a.AccountId == accountId))
                {
                    MessageBox.Show("This account already exists.", "Sigil");
                    StatusText = "Ready";
                    return;
                }

                var dialog = new InputDialog("Display name for this account:", "Add Account")
                {
                    Owner = this
                };

                dialog.ShowDialog();
                var displayName = dialog.Response;
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    StatusText = "Canceled";
                    return;
                }

                var profile = new AccountProfile
                {
                    AccountId = accountId,
                    DisplayName = displayName.Trim()
                };

                Accounts.Add(profile);
                await LoadCharactersAsync(profile, token);
                await _accountStore.SaveAsync(Accounts);
                await _tokenService.SaveAsync(profile.AccountId, token);
                SelectedAccount = profile;

                StatusText = token.RuneScapeSessionToken != null
                    ? "Account added (RS session token captured)"
                    : "Account added (RS session token NOT captured — character creation unavailable)";
            }
            catch (OperationCanceledException)
            {
                StatusText = "Canceled";
            }
            catch (Exception ex)
            {
                StatusText = $"Failed: {ex.Message}";
                MessageBox.Show(ex.Message, "Sigil");
            }
        }

        private async void OnRemoveAccount(object sender, RoutedEventArgs e)
        {
            if (SelectedAccount == null) return;

            var name = SelectedAccount.DisplayName;
            if (MessageBox.Show($"Remove {name}?", "Sigil", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            {
                return;
            }

            var accountId = SelectedAccount.AccountId;
            Accounts.Remove(SelectedAccount);
            SelectedAccount = Accounts.FirstOrDefault();
            await _accountStore.SaveAsync(Accounts);
            await _tokenService.DeleteAsync(accountId);
            StatusText = "Account removed";
        }

        private async void OnRefreshToken(object sender, RoutedEventArgs e)
        {
            if (SelectedAccount == null) return;

            try
            {
                StatusText = "Refreshing token...";
                var token = await _tokenService.LoadAsync(SelectedAccount.AccountId);
                if (token == null)
                {
                    MessageBox.Show("No token stored. Add account again.", "Sigil");
                    return;
                }

                var refreshed = await _authService.RefreshAsync(Settings, token, CancellationToken.None);
                await _tokenService.SaveAsync(SelectedAccount.AccountId, refreshed);
                StatusText = "Token refreshed";
            }
            catch (Exception ex)
            {
                StatusText = $"Refresh failed: {ex.Message}";
                MessageBox.Show(ex.Message, "Sigil");
            }
        }

        private async void OnLaunch(object sender, RoutedEventArgs e)
        {
            if (SelectedAccount == null)
            {
                StatusText = "Select an account first";
                return;
            }

            try
            {
                StatusText = "Launching...";
                var token = await _tokenService.LoadAsync(SelectedAccount.AccountId);

                if (token == null)
                {
                    StatusText = "No token. Re-add account.";
                    return;
                }

                if (token.IsExpired())
                {
                    StatusText = "Refreshing token...";
                    token = await _authService.RefreshAsync(Settings, token, CancellationToken.None);
                    await _tokenService.SaveAsync(SelectedAccount.AccountId, token);
                }

                SelectedAccount.LastUsedAt = DateTimeOffset.UtcNow;
                await _accountStore.SaveAsync(Accounts);

                var character = SelectedCharacter ?? SelectedAccount.GameAccounts.FirstOrDefault();
                await _launcherService.LaunchAsync(Settings, token, character);

                StatusText = "Game launched";
            }
            catch (Exception ex)
            {
                StatusText = $"Launch failed: {ex.Message}";
                MessageBox.Show(ex.Message, "Sigil");
            }
        }

        private async void OnCreateCharacter(object sender, RoutedEventArgs e)
        {
            if (!CanCreateCharacter) return;

            var result = MessageBox.Show(
                "Create a new character slot?\n\nThe character's name will be set in-game on first login.",
                "Create Character",
                MessageBoxButton.YesNo);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                StatusText = "Creating character...";
                var token = await _tokenService.LoadAsync(SelectedAccount!.AccountId);

                if (token == null)
                {
                    StatusText = "No token. Re-add account.";
                    return;
                }

                if (string.IsNullOrWhiteSpace(token.RuneScapeSessionToken))
                {
                    MessageBox.Show(
                        "RuneScape session token not found.\nRemove and re-add this account to enable character creation.",
                        "Sigil");
                    StatusText = "Ready";
                    return;
                }

                var activeAccounts = await _jagexAccountService.CreateGameAccountAsync(
                    token.RuneScapeSessionToken,
                    CancellationToken.None);

                SelectedAccount.GameAccounts = activeAccounts.ToList();
                await _accountStore.SaveAsync(Accounts);
                UpdateSelectedCharacters();
                StatusText = $"Character created. You now have {SelectedAccount.GameAccounts.Count} character(s).";
            }
            catch (Exception ex)
            {
                StatusText = $"Failed to create character: {ex.Message}";
                MessageBox.Show(ex.Message, "Sigil");
            }
        }

        private async void OnBrowseRs3Client(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Executable (*.exe)|*.exe",
                Title = "Select RS3 Client"
            };

            if (dialog.ShowDialog(this) == true)
            {
                Settings.Rs3ClientPath = dialog.FileName;
                await _settingsStore.SaveAsync(Settings);
                OnPropertyChanged(nameof(Settings));
                StatusText = "Client path updated";
            }
        }

        private async void OnSaveSettings(object sender, RoutedEventArgs e)
        {
            await _settingsStore.SaveAsync(Settings);
            StatusText = "Settings saved";
        }

        private async void OnShowTokenInfo(object sender, RoutedEventArgs e)
        {
            if (SelectedAccount == null)
            {
                MessageBox.Show("No account selected.", "Token Info");
                return;
            }

            var token = await _tokenService.LoadAsync(SelectedAccount.AccountId);
            if (token == null)
            {
                MessageBox.Show("No token stored for this account.", "Token Info");
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Account:             {SelectedAccount.DisplayName}");
            sb.AppendLine($"AccountId:           {SelectedAccount.AccountId}");
            sb.AppendLine($"Subject:             {token.Subject ?? "(null)"}");
            sb.AppendLine($"ExpiresAt:           {token.ExpiresAt:u}  (expired={token.IsExpired()})");
            sb.AppendLine($"AccessToken:         {(string.IsNullOrEmpty(token.AccessToken) ? "(empty)" : token.AccessToken[..Math.Min(16, token.AccessToken.Length)] + "…")}");
            sb.AppendLine($"RefreshToken:        {(string.IsNullOrEmpty(token.RefreshToken) ? "(empty)" : token.RefreshToken[..Math.Min(16, token.RefreshToken.Length)] + "…")}");
            sb.AppendLine($"IdToken:             {(string.IsNullOrEmpty(token.IdToken) ? "(null)" : token.IdToken[..Math.Min(16, token.IdToken.Length)] + "…")}");
            sb.AppendLine($"SessionId:           {(string.IsNullOrEmpty(token.SessionId) ? "(null)" : token.SessionId[..Math.Min(16, token.SessionId.Length)] + "…")}");
            sb.AppendLine();
            sb.AppendLine($"RuneScapeSessionToken: {(string.IsNullOrEmpty(token.RuneScapeSessionToken) ? "⚠ NOT SET" : $"✓ SET (length={token.RuneScapeSessionToken.Length}, value={token.RuneScapeSessionToken[..Math.Min(16, token.RuneScapeSessionToken.Length)]}…)")}");

            var debug = new Sigil.Views.CookieDebugWindow(sb.ToString()) { Owner = this };
            debug.Show();
        }

        private async Task UpdateStatusAsync()
        {
            if (SelectedAccount == null)
            {
                StatusText = "Add an account to get started";
                return;
            }

            var token = await _tokenService.LoadAsync(SelectedAccount.AccountId);
            if (token == null)
            {
                StatusText = "Token missing. Re-add account.";
                return;
            }

            StatusText = token.IsExpired()
                ? "Token expired. Will refresh on launch."
                : "Ready to launch";
        }

        private async Task LoadCharactersAsync(AccountProfile profile, OAuthToken token)
        {
            if (string.IsNullOrWhiteSpace(token.SessionId)) return;

            try
            {
                var accounts = await _jagexAccountService
                    .GetGameAccountsAsync(Settings, token.SessionId, CancellationToken.None);
                profile.GameAccounts = accounts.ToList();
                UpdateSelectedCharacters();
            }
            catch
            {
                // Silently fail - characters are optional
            }
        }

        private void UpdateSelectedCharacters()
        {
            SelectedCharacters.Clear();
            if (SelectedAccount == null)
            {
                SelectedCharacter = null;
                OnPropertyChanged(nameof(CharacterCountText));
                OnPropertyChanged(nameof(CanCreateCharacter));
                OnPropertyChanged(nameof(CanAutoCreate));
                return;
            }

            foreach (var account in SelectedAccount.GameAccounts)
            {
                SelectedCharacters.Add(account);
            }

            SelectedCharacter = SelectedCharacters.FirstOrDefault();
            OnPropertyChanged(nameof(CharacterCountText));
            OnPropertyChanged(nameof(CanCreateCharacter));
            OnPropertyChanged(nameof(CanAutoCreate));
        }

        // ── Queue service event handlers ──────────────────────────────────────

        private void OnQueueCharacterCreated(string accountId, IReadOnlyList<GameAccount> updated)
        {
            Dispatcher.InvokeAsync(() =>
            {
                var acct = Accounts.FirstOrDefault(a => a.AccountId == accountId);
                if (acct != null)
                {
                    acct.GameAccounts = updated.ToList();
                    if (SelectedAccount?.AccountId == accountId)
                        UpdateSelectedCharacters();
                    _ = _accountStore.SaveAsync(Accounts.ToList());
                }
            });
        }

        private void OnQueueBatchCompleted(string accountId, int created, int skipped)
        {
            Dispatcher.InvokeAsync(() =>
            {
                var name = Accounts.FirstOrDefault(a => a.AccountId == accountId)?.DisplayName ?? accountId;
                StatusText = $"[{name}] Done: {created} created, {skipped} skipped.";
                OnPropertyChanged(nameof(CanAutoCreate));
            });
        }

        private void OnQueueCountChanged(int pending)
        {
            Dispatcher.InvokeAsync(() =>
            {
                IsQueueActive = pending > 0 || _creationQueue.IsActive;
                QueueStatusText = pending > 0 ? $"Queue: {pending} account(s) pending" : string.Empty;
            });
        }

        private void OnQueueStatus(string msg)
        {
            Dispatcher.InvokeAsync(() => StatusText = msg);
        }

        // ── Button handlers ───────────────────────────────────────────────────

        private async void OnAutoCreateCharacters(object sender, RoutedEventArgs e)
        {
            if (SelectedAccount == null) return;
            var token = await _tokenService.LoadAsync(SelectedAccount.AccountId);
            if (token == null || string.IsNullOrWhiteSpace(token.RuneScapeSessionToken))
            {
                MessageBox.Show(
                    "RuneScape session token not found.\nRemove and re-add this account.",
                    "Sigil");
                return;
            }
            StatusText = $"Queuing auto-creation for {SelectedAccount.DisplayName}...";
            var queued = await _creationQueue.EnqueueAsync(SelectedAccount, token.RuneScapeSessionToken,
                Settings.CharacterCreationBatchSize, Settings.CharacterCreationBatchWindowMinutes);
            if (!queued)
                StatusText = $"{SelectedAccount.DisplayName} already at max or already queued.";
        }

        private void OnCancelQueue(object sender, RoutedEventArgs e)
        {
            _creationQueue.CancelAll();
            StatusText = "Queue cancelled.";
            IsQueueActive = false;
            QueueStatusText = string.Empty;
        }

        private void OnWindowClosed(object? sender, EventArgs e)
        {
            _creationQueue.Dispose();
        }
    }
}
