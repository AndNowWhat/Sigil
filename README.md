# Sigil

A lightweight account manager for RuneScape 3 that lets you manage multiple Jagex accounts, create characters, and launch the game directly.

## Features

- Manage multiple Jagex accounts
- Select which character to launch with
- Direct game client launch (bypasses Jagex Launcher UI)
- Create new character slots (up to 20 per account)
- Auto character creation — queues and fills an account to 20 with configurable delay
- Scrollable activity log with timestamps
- Secure token storage via Windows Credential Manager
- Modern dark-themed UI

## How It Works

Sigil authenticates with Jagex's OAuth system and stores your session tokens securely. When you launch the game, it passes your credentials directly to the RS3 client via environment variables (`JX_SESSION_ID`, `JX_CHARACTER_ID`, `JX_DISPLAY_NAME`), skipping the Jagex Launcher entirely.

## Requirements

- Windows 10/11
- .NET 7.0 or later
- WebView2 Runtime
- RS3 client installed (default path: `C:\ProgramData\Jagex\launcher\rs2client.exe`)

## Usage

1. Click **+ Add** to add a Jagex account and log in via the browser window
2. Select your account and character from the lists
3. Click **Launch Game**

**Character creation:**
- **+ Create** — creates one new character slot
- **Auto** — queues automatic creation until the account reaches 20 characters (respects the configured delay between creations)

## Configuration

Expand **Advanced Settings** at the bottom of the window:

| Setting | Description |
|---|---|
| RS3 Client Path | Path to the RS3 game executable |
| Character creation delay | Seconds to wait between character creations (default 60) |
| Refresh Token | Manually refresh the authentication token |
| Save Settings | Persist configuration changes |

## Building

```bash
dotnet build
```

## Credits

Inspired by [Bolt Launcher](https://codeberg.org/Adamcake/Bolt).

## License

GNU Affero General Public License v3.0 — see [LICENSE](LICENSE) for details.
