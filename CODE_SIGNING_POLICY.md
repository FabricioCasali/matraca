# Code Signing Policy

Free code signing provided by [SignPath.io](https://signpath.io), certificate by
[SignPath Foundation](https://signpath.org).

## Project

**Matraca** — an offline voice dictation (speech-to-text) tray application for Windows.
It transcribes the user's speech locally with [Whisper](https://github.com/ggerganov/whisper.cpp)
(via [Whisper.net](https://github.com/sandrohanea/whisper.net)) and types the resulting text into
whatever window has the text cursor.

- Source: <https://github.com/FabricioCasali/matraca>
- License: [MIT](LICENSE)

## Team roles

This is a single-maintainer project. All three roles are held by the same person:

| Role | Person | GitHub |
|---|---|---|
| Author | Fabricio Casali | [@FabricioCasali](https://github.com/FabricioCasali) |
| Reviewer | Fabricio Casali | [@FabricioCasali](https://github.com/FabricioCasali) |
| Approver | Fabricio Casali | [@FabricioCasali](https://github.com/FabricioCasali) |

Multi-factor authentication is enabled on the GitHub account and on the SignPath account.

## Build and release process

- Releases are built exclusively by GitHub Actions from the tagged commit
  (see [`.github/workflows/release.yml`](.github/workflows/release.yml)). No binary is ever
  built on a developer machine and uploaded by hand.
- Binaries build verifiably from the source in this repository: the workflow runs
  `installer/build-installer.ps1`, which publishes the .NET project and compiles the Inno Setup
  installer. There is no unpublished or proprietary step.
- The installer bundles only this project's own code plus the NuGet dependencies declared in
  `Matraca.csproj` — all open source.
- Every release is submitted to SignPath through the trusted-build-system integration and
  **requires manual approval** before it is signed.
- File metadata (product name, company, version, description, copyright) is set in
  `Matraca.csproj` and stamped into the executable at build time.

## Privacy policy

**Transcription is local.** The audio captured from your microphone is processed entirely on
your own machine by the Whisper model. Audio is never uploaded anywhere, and is never written to
disk — it lives in memory only for as long as it takes to transcribe.

The application collects **no telemetry, no analytics and no crash reports**, and it does not
phone home or check for updates.

Three features write or transmit data, all of them under the user's explicit control:

| Feature | Default | What it does |
|---|---|---|
| Log file | on | Writes diagnostics to `%LOCALAPPDATA%\Matraca\matraca.log`, including the transcribed text. Local only. |
| Dictation history (`history`) | on | Stores recent transcriptions as plain text in `%LOCALAPPDATA%\Matraca\history.json`. Local only. Can be disabled and cleared from the app. |
| Text post-processing (`postProcess`) | **off** | When explicitly enabled **and** supplied with an API key, sends the transcribed **text** (never the audio) to the Anthropic API to fix punctuation and remove speech fillers. This is the only feature that transmits anything off the machine. |

Downloading a Whisper model from the first-run screen contacts Hugging Face
(`huggingface.co`) to fetch the model file. This is an ordinary file download that only happens
when the user asks for it.

## System changes

The installer:

- Installs to `Program Files\Matraca` and creates a Start Menu shortcut.
- Optionally adds a startup shortcut ("Start with Windows"), only if the user ticks that option.
- Optionally installs the `uiAccess` build so dictation works in elevated windows, only if the
  user ticks that option.
- Registers a standard uninstaller in Windows "Apps & features".

The application installs a global keyboard hook, which is required to capture the dictation
hotkey system-wide. It does not modify any other system configuration.

Uninstalling removes the program files and shortcuts. Configuration, log and history files in
`%LOCALAPPDATA%\Matraca` are left in place so a reinstall keeps your settings; delete that
folder to remove them.
