# Sigil

A lightweight account manager for RuneScape 3 that allows you to manage multiple Jagex accounts and launch the game directly with your selected account.

## Features

- Manage multiple Jagex accounts
- Select which character to launch with
- Direct game client launch (bypasses Jagex Launcher UI)
- Secure token storage using Windows Credential Manager
- Modern dark-themed UI

## How It Works

Sigil authenticates with Jagex's OAuth system and stores your session tokens securely. When you launch the game, it passes your credentials directly to the RS3 client via environment variables (`JX_SESSION_ID`, `JX_CHARACTER_ID`, `JX_DISPLAY_NAME`), allowing you to skip the Jagex Launcher's account selection.

## Requirements

- Windows 10/11
- .NET 7.0 or later
- RS3 client installed (default path: `C:\ProgramData\Jagex\launcher\rs2client.exe`)

## Usage

1. Click **+ Add** to add a Jagex account
2. Log in via the browser window
3. Give the account a display name
4. Select your account and character
5. Click **Launch Game**

## Building

```bash
dotnet build
```

## Configuration

Advanced settings are available in the expander at the bottom of the window:
- **RS3 Client Path**: Path to the game executable
- **Refresh Token**: Manually refresh your authentication token
- **Save Settings**: Persist configuration changes

## Credits

Inspired by [Bolt Launcher](https://codeberg.org/Adamcake/Bolt).

## License

GNU Affero General Public License v3.0 - see [LICENSE](LICENSE) for details.
