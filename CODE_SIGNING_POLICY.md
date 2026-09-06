# Code signing and privacy policy

## Project

Matraca is a local voice dictation application for Windows and macOS. It transcribes speech with
[Whisper](https://github.com/ggerganov/whisper.cpp), through
[Whisper.net](https://github.com/sandrohanea/whisper.net), and delivers the resulting text to the
selected application.

- Source: <https://github.com/FabricioCasali/matraca>
- License: [MIT](LICENSE)
- Public release: Windows x64 [v1.1.0](https://github.com/FabricioCasali/matraca/releases/tag/v1.1.0)
- macOS status: development only; no public or notarized package is available

## Team roles

This is a single-maintainer project. Fabricio Casali holds the author, reviewer, and approver roles.

| Role | Person | GitHub |
|---|---|---|
| Author | Fabricio Casali | [@FabricioCasali](https://github.com/FabricioCasali) |
| Reviewer | Fabricio Casali | [@FabricioCasali](https://github.com/FabricioCasali) |
| Approver | Fabricio Casali | [@FabricioCasali](https://github.com/FabricioCasali) |

Multi-factor authentication is enabled on the GitHub and SignPath accounts.

## Windows build and release process

The Windows workflow is defined in [`.github/workflows/release.yml`](.github/workflows/release.yml).

- GitHub Actions builds and tests pushes and pull requests on `windows-latest`.
- A `v*` tag builds the self-contained x64 installer from the tagged commit.
- `installer/build-installer.ps1` publishes the standard and `uiAccess` variants and compiles the
  Inno Setup installer. The process uses only the repository source and declared NuGet packages.
- The workflow submits a tagged build to SignPath only when its SignPath token and organization are
  configured. Signing requires manual approval.
- If SignPath is not configured, the workflow emits a warning and publishes the unsigned installer.
- Binaries are not built on a maintainer machine and uploaded by hand.

SignPath can provide free Windows code signing through the
[SignPath Foundation](https://signpath.org). This repository has the integration points, but signing
is conditional rather than guaranteed for every release.

The published `matraca-setup-1.1.0.exe` is unsigned. This was verified on September 6, 2026 with
Windows `Get-AuthenticodeSignature`; GitHub reports SHA-256
`b186738f2805667a2984d1d8a4f48fa99440a4c18e4a47031161c776318cfe18` for that asset.

The optional Windows installer task named `uiAccess` is separate from release signing. It creates
and trusts a certificate on the user's computer, then signs the installed executable locally so
Windows allows global hotkeys while an elevated application has focus.

## macOS signing

`Matraca.Mac/pack.sh` produces a self-contained Apple Silicon `.app` for development. It signs the
bundle with a local identity named `Matraca Dev`, or the identity selected through
`MATRACA_SIGN_IDENTITY`, so macOS can associate Microphone and Accessibility permissions with a
stable application identity.

This local development signature is not an Apple distribution signature. The project does not yet
publish a `.dmg`, and the app is not notarized. SignPath covers the Windows process; public macOS
distribution requires a separate Apple signing and notarization process.

## Privacy policy

### Audio and transcription

Microphone audio is processed on the user's computer. It stays in memory only for capture and
transcription. Matraca never writes audio to disk and never uploads it.

Matraca collects no telemetry or analytics, sends no automatic crash reports, and performs no
update checks.

### Local data

| Data | Default | Location and contents |
|---|---|---|
| Configuration | on | `appsettings.json` in Matraca's data directory. It contains settings and may contain API keys entered through the interface. |
| Log | on | `matraca.log` in the data directory. Diagnostics can include transcribed and reviewed text. |
| Dictation history (`history`) | on | `history.json` in the data directory. It stores recent transcriptions as plain text and may include usage metadata for a review request. It can be disabled and cleared from the app. |
| AI usage ledger | after supported review calls | `ai-usage.json` in the data directory. It stores daily token and estimated cost totals by provider and model. It does not store dictation text. |
| Whisper models | on user request | The `models` subdirectory contains model files selected or downloaded by the user. |

The data directory is `%LOCALAPPDATA%\Matraca` on Windows and
`~/Library/Application Support/Matraca` on macOS.

API keys saved through the interface remain in the local configuration file. The interface does not
send stored keys back to its web view after saving them. Users can avoid writing keys to the file by
setting `ANTHROPIC_API_KEY`, `DEEPSEEK_API_KEY`, or `OPENAI_API_KEY` in the application environment.

### Network access

| Action | Default | Destination | Data sent |
|---|---|---|---|
| Whisper model download | only when requested | `huggingface.co` | An ordinary model file request. No audio or transcription is sent. |
| AI text review (`postProcess`) | **off** | Anthropic, DeepSeek, OpenAI, or the OpenAI-compatible endpoint selected by the user | The cleanup instruction and transcribed text. Audio is never sent. |
| DeepSeek balance check | only when the user presses the balance button | `https://api.deepseek.com/user/balance` | The DeepSeek API credential and normal HTTP metadata. No dictation text is sent. |

Remote OpenAI-compatible review endpoints must use HTTPS. Plain HTTP is accepted only for a local
loopback endpoint. If AI review fails, times out, is refused, or returns empty text, Matraca delivers
the original local transcription.

### System changes

The Windows installer:

- Installs the application under `Program Files\Matraca` and creates a Start Menu shortcut.
- Adds a startup shortcut only when the user selects "Start with Windows."
- Installs and locally signs the `uiAccess` variant only when the user selects dictation into
  elevated windows.
- Registers a standard uninstaller under Windows Apps & features.

The application installs no service. Apart from the startup shortcut and local certificate selected
through the installer, it modifies no other system settings. It runs a global keyboard hook on
Windows or an event tap on macOS to capture the configured dictation hotkey.

Uninstalling on Windows removes the program files and shortcuts. It leaves the local data directory
in place so a reinstall keeps the user's settings. Delete that directory to remove the configuration,
logs, history, usage ledger, and downloaded models.
