[English](README.md) | **Português (Brasil)**

# Matraca — ditado por voz (speech-to-text) pra prompts

App de bandeja (tray) em C#/.NET 8 que transcreve sua voz e **cola o texto na janela em foco**.
Pensado pra ditar prompts no Claude Code (terminal), mas funciona em qualquer lugar com cursor de
texto: navegador, Word, chat, etc.

Motor de transcrição: **Whisper** via [Whisper.net](https://github.com/sandrohanea/whisper.net)
(binding do whisper.cpp), rodando na **GPU via Vulkan**. Reaproveita o modelo `ggml-large-v3-turbo.bin`
já baixado pelo app Vibe — **não precisa do Vibe rodando**.

## Como funciona

1. Você aperta sua tecla de atalho → começa a gravar (beep + ícone muda).
2. Fala o prompt.
3. Aperta a tecla de novo → para, transcreve (~0,3s na RTX 4070 Ti) e **cola no campo em foco**.
4. Você revisa e dá Enter. (Não envia sozinho — config `autoEnter`.)

O texto vai pra **onde quer que o cursor esteja** — o app cola via clipboard + `Ctrl+V`, preservando
o que você já tinha copiado. Durante a gravação, uma **moldura colorida** marca a janela que vai
receber o texto — se um pop-up roubar o foco, você vê antes de colar.

## Primeiro uso — descobrir sua tecla

O `appsettings.json` já vem com `"hotkey": "discover"`. Rode o app:

```powershell
dotnet run --project .   # na pasta do projeto
# ou rode o exe compilado:
# .\bin\Debug\net8.0-windows\Matraca.exe
```

Um ícone aparece na bandeja em **MODO DESCOBERTA**. Aperte a tecla custom do seu teclado: um balão
mostra o código e o nome sugerido (ex.: `F24`), e também grava no `matraca.log`. Coloque esse nome no
`appsettings.json` e reinicie o app:

```json
{
  "hotkey": "F24"
}
```

Pronto — agora a tecla é o gatilho do ditado. (Ou use a tela de configurações, abaixo.)

## Instalação (recomendado)

Baixe/gere o instalador e execute:

```powershell
# gerar o instalador (requer .NET 8 SDK e Inno Setup 6):
installer\build-installer.ps1 -Version 1.0.0
# saida: installer\output\matraca-setup-1.0.0.exe
```

O instalador é self-contained (não precisa de .NET instalado) e oferece duas opções:

- **Iniciar com o Windows** — atalho na pasta de inicialização.
- **Ditar em janelas elevadas (Admin)** — instala a variante com `uiAccess` e assina o exe com um
  certificado local criado na hora (necessário pro Windows honrar o uiAccess). Sem essa opção o app
  funciona normalmente, só não captura o atalho quando a janela em foco é elevada.

## Configuração

**Menu da bandeja → Configurações...** abre a tela de parametrização: tecla de atalho (clique em
*Capturar* e pressione a tecla), modo de ditado, idioma, moldura de foco, VAD, GPU etc. Salva em
`%LOCALAPPDATA%\Matraca\appsettings.json` e oferece reiniciar o app para aplicar.

O mesmo arquivo pode ser editado na mão (`appsettings.json`):

| Campo | Default | O que faz |
|---|---|---|
| `modelPath` | modelo do Vibe | Caminho do `.bin` ggml do Whisper. Aceita variáveis (`%LOCALAPPDATA%`). |
| `language` | `pt` | Idioma do áudio. `pt` lida bem com termos em inglês embutidos. |
| `hotkey` | `discover` | Tecla de atalho: `F13`–`F24`, media keys (`MediaPlayPause`, etc.), número (`0xB6`) ou `discover`. |
| `mode` | `toggle` | `toggle` (aperta liga / aperta desliga), `hold` (segura pra falar), `live`/`push` (ver abaixo). |
| `autoEnter` | `false` | Se `true`, pressiona Enter depois de colar (envia na hora). |
| `beep` | `true` | Sons de início (subindo) / fim (descendo) de gravação. |
| `silenceMs` | `700` | (modo live) duração da pausa que finaliza uma frase. |
| `vadThreshold` | `0.012` | (modo live) energia mínima (RMS) p/ considerar que há fala. Aumente se pegar ruído; diminua se cortar fala baixa. |
| `idleUnloadMinutes` | `5` | Descarrega o modelo (libera ~1,5 GB de VRAM) após N min sem uso. Recarrega sozinho no próximo ditado. `0` = nunca descarrega. |
| `gpu` | `auto` | `auto` (GPU se houver, senão CPU), `vulkan` (força GPU) ou `cpu` (força CPU). |
| `focusBorder` | `true` | Desenha uma moldura colorida na janela em foco enquanto grava — mostra **onde o texto vai ser colado** (útil quando um pop-up rouba o foco). A moldura segue o foco em tempo real e não interfere em cliques nem no foco. |
| `focusBorderColor` | `#E81123` | Cor da moldura (hex HTML). |
| `focusBorderThickness` | `4` | Espessura da moldura em pixels (1–40). |
| `focusBorderOpacity` | `0.9` | Opacidade da moldura (0.1–1.0). |

### Concorrência de GPU (VRAM)

Enquanto o modelo está carregado ele ocupa **~1,5–2 GB de VRAM**. Duas formas de lidar quando você
precisa da GPU pra outra coisa:

- **`idleUnloadMinutes`** (automático): depois de ocioso, o app **libera a VRAM sozinho** e recarrega
  (~2–8s) quando você voltar a ditar. É o comportamento padrão (5 min).
- **`gpu: "cpu"`** (manual): roda **100% na CPU**, VRAM zero — porém a transcrição fica **lenta
  (~13s por frase)** com o modelo large. Bom pra quando a GPU está totalmente ocupada. Trocar entre
  `cpu`/`vulkan`/`auto` exige **reiniciar o app** (o runtime nativo é fixado por processo).

> Durante a transcrição o uso de GPU é só uma **rajada de ~0,3s**; não é carga contínua.

### Modo `live` (ditado por pausa / VAD)

Com `"mode": "live"`, aperta o atalho pra **iniciar a sessão** e aperta de novo pra **encerrar**.
Durante a sessão, o app grava contínuo e, **a cada pausa** sua (>= `silenceMs`), transcreve aquela
frase e cola — enquanto você continua falando a próxima. Dá a sensação de "ir escrevendo" conforme
você fala, frase a frase (não letra a letra — isso é proposital, fica estável e cola limpo).

Dica: o texto é colado com um espaço ao final de cada frase, então as frases se encadeiam naturalmente.
O modo `push` é igual, mas só enquanto a tecla está pressionada (push-to-talk).

## Build / publicar

```powershell
dotnet build . -c Release
# exe portátil (usa o .NET 8 já instalado):
dotnet publish . -c Release -r win-x64 --self-contained false
```

## Diagnóstico

- Log: `%LOCALAPPDATA%\Matraca\matraca.log`. Menu da bandeja → "Abrir matraca.log".
- Testar a transcrição com um WAV (16 kHz mono) sem usar o mic:
  ```powershell
  Matraca.exe --transcribe caminho\audio.wav
  # resultado e backend (Vulkan/CPU) vão pro matraca.log
  ```

## Notas

- **Antivírus/Defender**: o app instala um *hook* global de teclado (necessário pra capturar a tecla
  de atalho). É comportamento normal de apps de hotkey, mas pode gerar alerta heurístico.
- **GPU**: usa Vulkan (só precisa do driver NVIDIA — sem CUDA Toolkit). Para máxima velocidade no
  futuro, dá pra instalar o CUDA Toolkit 12.4+/13 e trocar o pacote `Whisper.net.Runtime.Vulkan` por
  `Whisper.net.Runtime.Cuda` no `.csproj`.
- A 1ª transcrição após abrir o app pode demorar alguns segundos (carga do modelo na VRAM); as
  seguintes são quase instantâneas.

## Licença

[MIT](LICENSE).
