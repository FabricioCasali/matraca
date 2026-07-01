**English** | [Português (Brasil)](README.pt-BR.md)

# Matraca — voice dictation (speech-to-text) for prompts

Windows tray app in C#/.NET 8 that transcribes your voice and **pastes the text into the focused
window**. Built for dictating prompts into Claude Code (terminal), but works anywhere there's a text
cursor: browser, Word, chat, etc.

Transcription engine: **Whisper** via [Whisper.net](https://github.com/sandrohanea/whisper.net)
(whisper.cpp binding), running on the **GPU via Vulkan**. It can reuse the `ggml-large-v3-turbo.bin`
model already downloaded by the Vibe app — **Vibe doesn't need to be running**.

*"Matraca" is Brazilian Portuguese for someone who never stops talking.*

## How it works

1. Press your hotkey → recording starts (beep + tray icon changes).
2. Speak your prompt.
3. Press the hotkey again → it stops, transcribes (~0.3s on an RTX 4070 Ti) and **pastes into the
   focused field**.
4. You review and hit Enter. (It never submits on its own — see the `autoEnter` setting.)

The text goes **wherever the cursor is** — the app pastes via clipboard + `Ctrl+V`, preserving
whatever you had copied before. While recording, a **colored border** highlights the window that
will receive the text — if a pop-up steals focus, you see it before pasting.

## First run — discover your key

`appsettings.json` ships with `"hotkey": "discover"`. Run the app:

```powershell
dotnet run --project .   # from the project folder
# or run the compiled exe:
# .\bin\Debug\net8.0-windows\Matraca.exe
```

A tray icon appears in **DISCOVER MODE**. Press the key you want to use: a balloon shows its code
and suggested name (e.g. `F24`), also written to `matraca.log`. Put that name in `appsettings.json`
and restart:

```json
{
  "hotkey": "F24"
}
```

Done — that key now triggers dictation. (Or use the settings window, below.)

## Installation (recommended)

Download/build the installer and run it:

```powershell
# build the installer (requires the .NET 8 SDK and Inno Setup 6):
installer\build-installer.ps1 -Version 1.0.0
# output: installer\output\matraca-setup-1.0.0.exe
```

The installer is self-contained (no .NET runtime required) and offers two options:

- **Start with Windows** — shortcut in the startup folder.
- **Dictate into elevated (Admin) windows** — installs the `uiAccess` variant and signs the exe with
  a locally generated certificate (required by Windows to honor uiAccess). Without this option the
  app works normally; it just can't capture the hotkey while an elevated window has focus.

## Configuration

**Tray menu → Configurações...** opens the settings window: hotkey (click *Capture* and press the
key), dictation mode, language, focus border, VAD, GPU, etc. It saves to
`%LOCALAPPDATA%\Matraca\appsettings.json` and offers to restart the app to apply.

The same file can be edited by hand (`appsettings.json`):

| Field | Default | What it does |
|---|---|---|
| `modelPath` | Vibe's model | Path to the Whisper ggml `.bin`. Environment variables allowed (`%LOCALAPPDATA%`). |
| `language` | `pt` | Audio language. Use `en` for English; `pt` handles embedded English terms well. |
| `hotkey` | `discover` | Hotkey: `F13`–`F24`, media keys (`MediaPlayPause`, etc.), a number (`0xB6`) or `discover`. **Single key only** — combos like `Ctrl+Alt+X` are not supported; the configured key is reserved for dictation (it no longer reaches other apps). |
| `mode` | `toggle` | `toggle` (press on / press off), `hold` (hold to talk), `live`/`push` (see below). |
| `autoEnter` | `false` | If `true`, presses Enter after pasting (submits immediately). |
| `beep` | `true` | Start (rising) / stop (falling) recording sounds. |
| `silenceMs` | `700` | (live mode) pause length that ends a sentence. |
| `vadThreshold` | `0.012` | (live mode) minimum energy (RMS) to count as speech. Raise if it picks up noise; lower if it clips quiet speech. |
| `idleUnloadMinutes` | `5` | Unloads the model (frees ~1.5 GB of VRAM) after N idle minutes. Reloads automatically on the next dictation. `0` = never unload. |
| `gpu` | `auto` | `auto` (GPU if available, else CPU), `vulkan` (force GPU) or `cpu` (force CPU). |
| `focusBorder` | `true` | Draws a colored border around the focused window while recording — shows **where the text will be pasted** (handy when a pop-up steals focus). Follows focus in real time and never interferes with clicks or focus. |
| `focusBorderColor` | `#E81123` | Border color (HTML hex). |
| `focusBorderThickness` | `4` | Border thickness in pixels (1–40). |
| `focusBorderOpacity` | `0.9` | Border opacity (0.1–1.0). |

### GPU concurrency (VRAM)

While loaded, the model takes **~1.5–2 GB of VRAM**. Two ways to handle needing the GPU for
something else:

- **`idleUnloadMinutes`** (automatic): after being idle, the app **frees the VRAM on its own** and
  reloads (~2–8s) when you dictate again. This is the default (5 min).
- **`gpu: "cpu"`** (manual): runs **100% on the CPU**, zero VRAM — but transcription becomes **slow
  (~13s per sentence)** with the large model. Good for when the GPU is fully busy. Switching between
  `cpu`/`vulkan`/`auto` requires **restarting the app** (the native runtime is fixed per process).

> During transcription the GPU load is just a **~0.3s burst**; it's not continuous.

### `live` mode (pause-based dictation / VAD)

With `"mode": "live"`, press the hotkey to **start a session** and again to **end it**. During the
session the app records continuously and, **at each pause** (>= `silenceMs`), transcribes that
sentence and pastes it — while you keep talking. It feels like the text "types itself" as you speak,
sentence by sentence (not letter by letter — that's intentional; it stays stable and pastes clean).

Tip: each sentence is pasted with a trailing space, so sentences chain naturally.
`push` mode is the same, but only while the key is held down (push-to-talk).

## Build / publish

```powershell
dotnet build . -c Release
# portable exe (uses an installed .NET 8 runtime):
dotnet publish . -c Release -r win-x64 --self-contained false
```

## Troubleshooting

- Log: `%LOCALAPPDATA%\Matraca\matraca.log`. Tray menu → "Abrir matraca.log".
- Test transcription with a WAV file (16 kHz mono) without using the mic:
  ```powershell
  Matraca.exe --transcribe path\to\audio.wav
  # result and backend (Vulkan/CPU) go to matraca.log
  ```

## Notes

- **Antivirus/Defender**: the app installs a global keyboard *hook* (required to capture the
  hotkey). This is normal for hotkey apps, but may trigger a heuristic alert.
- **GPU**: uses Vulkan (only the NVIDIA driver is needed — no CUDA Toolkit). For maximum speed you
  can install CUDA Toolkit 12.4+/13 and swap the `Whisper.net.Runtime.Vulkan` package for
  `Whisper.net.Runtime.Cuda` in the `.csproj`.
- The first transcription after launching may take a few seconds (model load into VRAM); subsequent
  ones are near-instant.

## License

[MIT](LICENSE).
