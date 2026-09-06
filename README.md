**English** | [Português (Brasil)](README.pt-BR.md)

# Matraca

Local voice dictation for any app with a text cursor. Matraca stays in the system tray, transcribes
with Whisper, and delivers the text to the selected window. It was built for dictating prompts into
terminals, but also works in editors, browsers, and chat apps.

Audio stays in memory on your computer. It is never uploaded or written to disk.

> "Matraca" is Brazilian Portuguese for someone who never stops talking.

## Availability

| Channel | Platform | Status |
|---|---|---|
| [v1.1.0](https://github.com/FabricioCasali/matraca/releases/tag/v1.1.0) | Windows x64 | Latest public release, with the previous native UI. |
| `main` / 2.0 | Windows x64 and Apple Silicon macOS | In development. The shared architecture works, but final packaging and physical checks are still pending. |

To install the public Windows release, download
[`matraca-setup-1.1.0.exe`](https://github.com/FabricioCasali/matraca/releases/download/v1.1.0/matraca-setup-1.1.0.exe).
The current installer is not digitally signed, so Windows may show a warning before running it.

The sections below describe the current `main` branch, which will become version 2.0. There is no
public macOS release or `.dmg` yet.

## What is in 2.0

- Local Whisper transcription using Vulkan on Windows and Metal on macOS, with a CPU fallback.
- One shared interface for Windows and Mac, with light and dark themes.
- Four dictation modes: `toggle`, `hold`, `live`, and `push`.
- A recording HUD, microphone meter, and a visible voice detection threshold.
- Direct Unicode delivery without changing the clipboard, or paste with clipboard restoration.
- A border that shows the target window and a hotkey that pins a destination.
- Local history with search, copy, redelivery, and deletion.
- Context vocabulary for names, acronyms, and technical terms.
- Hot configuration reload. Only switching between GPU and CPU requires a restart.
- Optional review through Anthropic, DeepSeek, or an OpenAI-compatible endpoint.
- Local DeepSeek token and estimated cost tracking, plus an on-demand balance check.

## Using Matraca

Choose a global hotkey, place the cursor at the destination, and dictate. The selected mode controls
the recording cycle:

| Mode | Behavior |
|---|---|
| `toggle` | Press once to start and again to stop. The complete segment is delivered at the end. |
| `hold` | Hold the hotkey while speaking. The complete segment is delivered when you release it. |
| `live` | Press to open a session. Each pause closes and delivers one phrase; press again to end the session. |
| `push` | Works like `live`, but the session only remains open while you hold the hotkey. |

The defaults are `live`, `F15`, Unicode delivery, and no automatic Enter. A mode change applies to
the next dictation session.

On first run, the interface lets you choose a Whisper model, download it from the whisper.cpp
repository on Hugging Face, or select an existing `.bin` file. It also captures the hotkey and, on
Mac, guides you through Microphone and Accessibility permissions.

## Target window

While recording, a non-activating border follows the window that will receive the text. It does not
accept clicks or steal focus.

Set `pinHotkey` to pin the currently focused window. Later dictations keep going to that destination
until you press the hotkey again. With `pinDelivery: "focus"`, Matraca brings the target forward,
delivers the text, and restores the previous focus. `nofocus` support depends on the field and the
platform; terminals and Chromium/Electron apps usually reject this kind of silent delivery.

## Configuration

The interface saves `appsettings.json` under these directories:

| Platform | Data directory |
|---|---|
| Windows | `%LOCALAPPDATA%\Matraca` |
| macOS | `~/Library/Application Support/Matraca` |

Paths in the file can use `%MATRACA_DATA%`, which resolves to the current platform's data directory.
Both platforms use the same field names and canonical hotkey names.

### Dictation and audio

| Field | Default | Effect |
|---|---|---|
| `modelPath` | `%MATRACA_DATA%\models\ggml-large-v3-turbo.bin` | Whisper ggml model file. |
| `language` | `pt` | Audio language. |
| `hotkey` | `F15` | Global hotkey. Supports keys such as `F13` through `F24`, media keys, numpad keys, and combinations such as `Ctrl+Alt+X`. |
| `mode` | `live` | `toggle`, `hold`, `live`, or `push`. |
| `inputDevice` | empty | Microphone name. Empty uses the operating system default. |
| `beep` / `beepVolume` | `true` / `0.8` | Start and stop sounds and their volume. |
| `startSound` / `stopSound` | empty | Optional local sound files. |
| `silenceMs` | `450` | Pause that closes a phrase in continuous modes. |
| `phraseMaxSeconds` | `6` | After this much continuous speech, a short pause closes the phrase. |
| `vadThreshold` | `0.012` | General voice detection threshold. |
| `micSensitivity` | `{}` | Thresholds by microphone name. The audio screen adjusts them against the live meter. |
| `vocabulary` | `[]` | Terms passed to Whisper as initial context. |
| `gpu` | `auto` | `auto`, `gpu`, or `cpu`. The legacy `vulkan` alias still works on Windows. |
| `idleUnloadMinutes` | `5` | Unloads the model after this idle period. `0` keeps it loaded. |

### Delivery and storage

| Field | Default | Effect |
|---|---|---|
| `autoEnter` | `false` | Presses Enter after delivering the text. |
| `pasteMethod` | `unicode` | `unicode` types directly; `clipboard` pastes and restores the previous clipboard contents. |
| `pinHotkey` | `none` | Hotkey that pins or releases the target window. |
| `pinDelivery` | `focus` | `focus` delivers and restores focus; `nofocus` tries to deliver without activating the target. |
| `focusBorder` | `true` | Shows the target border. |
| `focusBorderColor*` | colors by state | Normal, busy, and pinned colors. |
| `focusBorderThickness` / `focusBorderOpacity` | `4` / `0.9` | Border thickness and opacity. |
| `history` | `true` | Keeps recent transcriptions as plain text on the computer. |
| `historyMaxItems` | `100` | Maximum history entries. |

## AI review

AI review is optional and disabled by default. When enabled, Matraca sends the transcribed text and
the cleanup instruction to the selected provider. Audio is never included in the request. The
default instruction fixes punctuation, capitalization, and diacritics and removes hesitations
without summarizing, translating, or rewriting the dictation.

If the request fails, times out, is refused, or returns empty text, Matraca delivers the original
transcription.

| Provider | Configuration |
|---|---|
| Anthropic | `postProcessProvider: "anthropic"`, a model, and `postProcessApiKey` or `ANTHROPIC_API_KEY`. |
| DeepSeek | `postProcessProvider: "deepseek"`, a model, a reasoning level, and `postProcessDeepSeekApiKey` or `DEEPSEEK_API_KEY`. |
| OpenAI-compatible | `postProcessProvider: "openai-compatible"`, a model, an endpoint, and `postProcessOpenAiApiKey` or `OPENAI_API_KEY`. An empty endpoint uses the OpenAI API. HTTP is accepted only for local addresses; remote endpoints require HTTPS. |

Other fields include `postProcessPrompt`, `postProcessReasoning` (`off`, `low`, `high`, or `max`),
and `postProcessTimeoutMs`, which defaults to `8000`.

For DeepSeek responses, history records the request token counts and estimated cost. `ai-usage.json`
stores only daily totals by provider and model, without dictation text. The balance button calls
`https://api.deepseek.com/user/balance` only on user request and reuses the result for 30 seconds.

## Privacy and network access

- Audio exists only in memory during capture and transcription.
- There is no telemetry, analytics, automatic crash reporting, or update check.
- The log and history can contain transcribed text and stay in Matraca's local data directory.
- AI review transmits text only after the user enables it.
- Model downloads contact `huggingface.co` when requested.
- The balance check contacts DeepSeek only after a manual action and does not send dictation text.

API keys entered in the interface are written to the local `appsettings.json`. Use the environment
variables listed above if you do not want to store them in that file. See the full
[privacy policy](CODE_SIGNING_POLICY.md#privacy-policy).

## Building and testing

The repository requires the .NET 8 SDK. The complete solution must build even when the command runs
outside Windows:

```bash
dotnet build Matraca.sln -c Release -p:EnableWindowsTargeting=true
dotnet test Matraca.sln -c Release
```

### Windows

Install [Inno Setup 6](https://jrsoftware.org/isinfo.php) to produce the self-contained x64
installer:

```powershell
.\installer\build-installer.ps1 -Version 0.0.0
# output: installer\output\matraca-setup-0.0.0.exe
```

The installer can add a startup shortcut. It can also install the `uiAccess` variant for dictation
into elevated windows. That option creates and trusts a local certificate used to sign the installed
executable.

### macOS

The current project requires Apple Silicon, macOS 15 or later, Xcode Command Line Tools, and a local
code-signing identity named `Matraca Dev`. Set `MATRACA_SIGN_IDENTITY` to use a different identity:

```bash
bash Matraca.Mac/pack.sh
# output: Matraca.Mac/bin/Matraca.app
```

This package is for development. It is not notarized, and there is no public `.dmg` yet.

## Architecture

```text
Matraca.Core      pipeline, configuration, Whisper, VAD, history, and delivery queue
Matraca.Web       shared HTML, CSS, and JavaScript for the interface and HUD
Matraca.Windows   Windows hotkey, audio, delivery, tray, and WebView2 host
Matraca.Mac       macOS event tap, AudioQueue, Accessibility, tray, and WKWebView host
tests             Core, boundary, and contract tests
```

## Troubleshooting

- Windows: `%LOCALAPPDATA%\Matraca\matraca.log`
- macOS: `~/Library/Application Support/Matraca/matraca.log`
- The tray menu opens the log.
- On Windows, test a 16 kHz mono WAV without using the microphone:

```powershell
Matraca.exe --transcribe path\to\audio.wav
```

The global keyboard hook may trigger a heuristic antivirus warning. Matraca uses it to capture the
hotkey in any application. To capture the hotkey while an elevated window is focused, select the
installer's `uiAccess` option.

## Code signing

The workflow builds Windows releases from the tagged commit. If the SignPath integration is
configured, the installer requires manual approval before signing. Without that configuration, the
workflow publishes an unsigned installer, as it did for `v1.1.0`.

The Mac development package uses a local identity. Distribution to other users still requires Apple
signing and notarization. Read the [code signing policy](CODE_SIGNING_POLICY.md).

## License

[MIT](LICENSE).
