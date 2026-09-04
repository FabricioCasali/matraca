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

O texto vai pra **onde quer que o cursor esteja** — por padrão o app **digita direto** (SendInput
Unicode), sem encostar no seu clipboard; se preferir, dá pra voltar pra colagem por clipboard +
`Ctrl+V` (config `pasteMethod`), que preserva o que você já tinha copiado. Durante a gravação, uma
**moldura colorida** marca a janela que vai receber o texto — se um pop-up roubar o foco, você vê
antes de colar.

## Primeiro uso

Se não houver um modelo Whisper configurado, o Matraca abre uma **tela de primeiro uso** que
baixa um pra você (large-v3-turbo, small ou base — direto do repositório do whisper.cpp no
Hugging Face) e captura sua tecla de atalho. Já tem um `.bin`? Aponte pro seu. É toda a
configuração necessária.

## Revisando o texto com IA (opcional, desligado por padrão)

Com `postProcess` ligado, o texto transcrito passa por um modelo Claude na Anthropic ou por um
endpoint OpenAI-compatible configurado por você. O modelo corrige pontuação e capitalização e
tira as muletas de fala ("é...", "tipo", "né") — sem reescrever o que você disse.

> Esse é o **único** recurso que manda algo pra fora da sua máquina, e só o texto, nunca o áudio.
> Custa uma ida à rede por ditado (por *frase* nos modos `live`/`push`, o que joga contra a baixa
> latência que esses modos buscam). Se falhar, estourar o tempo ou for recusado, você recebe a
> transcrição original — nenhum ditado se perde por causa disso.

## Primeiro uso alternativo — descobrir sua tecla

O `appsettings.json` já vem com `"hotkey": "discover"`. Rode o app:

```powershell
dotnet run --project Matraca.Windows
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
installer\build-installer.ps1 -Version 1.1.0
# saida: installer\output\matraca-setup-1.1.0.exe
```

O instalador é self-contained (não precisa de .NET instalado) e oferece duas opções:

- **Iniciar com o Windows** — atalho na pasta de inicialização.
- **Ditar em janelas elevadas (Admin)** — instala a variante com `uiAccess` e assina o exe com um
  certificado local criado na hora (necessário pro Windows honrar o uiAccess). Sem essa opção o app
  funciona normalmente, só não captura o atalho quando a janela em foco é elevada.

## Configuração

**Menu da bandeja → Configurações...** abre a tela de parametrização: tecla de atalho (clique em
*Capturar* e pressione a tecla), modo de ditado, idioma, moldura de foco, VAD, GPU etc. Salva em
`%LOCALAPPDATA%\Matraca\appsettings.json` e **aplica tudo na hora** — sem reiniciar. A única
exceção é a troca entre GPU e CPU, que é fixada por processo; só nesse caso ele pergunta se você
quer reiniciar.

O mesmo arquivo pode ser editado na mão (`appsettings.json`):

| Campo | Default | O que faz |
|---|---|---|
| `modelPath` | modelo do Vibe | Caminho do `.bin` ggml do Whisper. Aceita variáveis (`%LOCALAPPDATA%`). |
| `language` | `pt` | Idioma do áudio. `pt` lida bem com termos em inglês embutidos. |
| `hotkey` | `discover` | Tecla de atalho: `F13`–`F24`, media keys, numpad (`NumPad0`), teclas de navegação, código cru (`0xB6`) ou combo (`Ctrl+Alt+X`). Também aceita `discover` pra descobrir sua tecla. Letras, dígitos e teclas de edição só são aceitos **com** modificador — sozinhos parariam de funcionar no sistema inteiro, já que a tecla configurada é reservada pro ditado. |
| `pinHotkey` | `none` | Tecla que **fixa a janela de destino** (veja abaixo). `none` desliga. Precisa ser diferente da `hotkey`. |
| `pinDelivery` | `focus` | Como a janela fixada recebe o texto. `focus`: traz pra frente, digita e devolve o foco — funciona em qualquer app. `nofocus`: entrega em silêncio — só campos Win32 clássicos. |
| `mode` | `toggle` | `toggle` (aperta liga / aperta desliga), `hold` (segura pra falar), `live`/`push` (ver abaixo). |
| `autoEnter` | `false` | Se `true`, pressiona Enter depois de colar (envia na hora). |
| `pasteMethod` | `unicode` | Como o texto é entregue. `unicode`: digita direto via SendInput — **não encosta no seu clipboard**. `clipboard`: copia e manda `Ctrl+V`, restaurando o conteúdo anterior depois. Use `clipboard` se algum app não aceitar entrada Unicode sintética. |
| `beep` | `true` | Sons de início (subindo) / fim (descendo) de gravação. |
| `silenceMs` | `700` | (modo live) duração da pausa que finaliza uma frase. |
| `vadThreshold` | `0.012` | (modo live) energia mínima (RMS) p/ considerar que há fala. É o valor de fallback, usado quando o microfone atual não tem entrada em `micSensitivity`. |
| `micSensitivity` | `{}` | Sensibilidade por microfone (`{"Nome do mic": 0.02}`). Microfones têm níveis de saída bem diferentes, então um valor único está errado pra pelo menos um deles. Ajuste na tela de configurações: fale e arraste a marca sobre o medidor ao vivo — a barra fica verde quando o Matraca considera que é fala. |
| `idleUnloadMinutes` | `5` | Descarrega o modelo (libera ~1,5 GB de VRAM) após N min sem uso. Recarrega sozinho no próximo ditado. `0` = nunca descarrega. |
| `gpu` | `auto` | `auto` (GPU se houver, senão CPU), `gpu` (força o backend de GPU da plataforma) ou `cpu` (força CPU). Valores legados `vulkan` seguem aceitos no Windows. |
| `focusBorder` | `true` | Desenha uma moldura colorida na janela em foco enquanto grava — mostra **onde o texto vai ser colado** (útil quando um pop-up rouba o foco). A moldura segue o foco em tempo real e não interfere em cliques nem no foco. |
| `focusBorderColor` | `#E81123` | Cor da moldura (hex HTML). |
| `focusBorderThickness` | `4` | Espessura da moldura em pixels (1–40). |
| `focusBorderOpacity` | `0.9` | Opacidade da moldura (0.1–1.0). |
| `focusBorderColorBusy` | `#FFB900` | Cor da moldura enquanto transcreve. |
| `focusBorderColorPinned` | `#0078D4` | Cor da moldura quando há uma janela de destino fixada. |
| `phraseMaxSeconds` | `6` | (modo live) passando disto numa fala contínua, uma pausa curta já encerra a frase — o texto continua fluindo em vez de esperar o corte duro de 20s. |
| `inputDevice` | `""` | Nome do microfone. Vazio = padrão do Windows. Guardado por nome, então plugar/desplugar outros dispositivos não muda a escolha. |
| `vocabulary` | `[]` | Termos que o Whisper costuma errar (nomes próprios, siglas, jargão). Vão como prompt inicial do modelo. |
| `history` | `true` | Guarda as transcrições recentes em **texto puro** em `%LOCALAPPDATA%\Matraca\history.json`. Menu da bandeja → "Histórico de ditados...". |
| `historyMaxItems` | `100` | Quantas transcrições manter. |
| `postProcess` | `false` | Revisa o texto transcrito com o provedor configurado (veja abaixo). |
| `postProcessProvider` | `anthropic` | `anthropic` ou `openai-compatible`. |
| `postProcessEndpoint` | `""` | URL completa de chat completions. No provedor OpenAI-compatible, vazio usa `https://api.openai.com/v1/chat/completions`. |
| `postProcessModel` | `claude-opus-5` | Nome do modelo usado na revisão. |
| `postProcessApiKey` | `""` | Chave da Anthropic. Vazio usa `ANTHROPIC_API_KEY`. |
| `postProcessOpenAiApiKey` | `""` | Chave OpenAI-compatible. Vazio usa `OPENAI_API_KEY`; endpoints locais podem dispensá-la. Credenciais nunca são compartilhadas entre provedores. |
| `postProcessPrompt` | `""` | Instrução customizada de limpeza. Vazio = usa a padrão embutida. |
| `postProcessTimeoutMs` | `8000` | Passando disso, entrega a transcrição original sem limpar. |

### Concorrência de GPU (VRAM)

Enquanto o modelo está carregado ele ocupa **~1,5–2 GB de VRAM**. Duas formas de lidar quando você
precisa da GPU pra outra coisa:

- **`idleUnloadMinutes`** (automático): depois de ocioso, o app **libera a VRAM sozinho** e recarrega
  (~2–8s) quando você voltar a ditar. É o comportamento padrão (5 min).
- **`gpu: "cpu"`** (manual): roda **100% na CPU**, VRAM zero — porém a transcrição fica **lenta
  (~13s por frase)** com o modelo large. Bom pra quando a GPU está totalmente ocupada. Trocar entre
  `cpu`/`gpu`/`auto` exige **reiniciar o app** (o runtime nativo é fixado por processo).

> Durante a transcrição o uso de GPU é só uma **rajada de ~0,3s**; não é carga contínua.

### Fixando uma janela de destino

Defina uma tecla em `pinHotkey` e, ao apertá-la, o Matraca **fixa a janela que está em foco naquele
momento** como destino do ditado. A partir daí o texto vai sempre pra ela, não importa em qual janela
você esteja — dá pra ditar no editor enquanto lê o navegador, por exemplo. Aperte a tecla de novo pra
liberar. Enquanto fixada, a moldura marca a janela fixa e o tooltip da bandeja mostra o título dela.

A entrega tem dois modos, escolhidos em `pinDelivery`:

- **`focus`** (padrão) — traz a janela fixada pra frente, digita e **devolve o foco pra onde você
  estava**. Funciona em qualquer alvo, inclusive terminal e apps Electron. O custo é a janela
  piscar na tela por um instante.
- **`nofocus`** — posta a mensagem direto na janela, sem trazê-la pra frente. Mais discreto, mas
  só funciona em campos Win32 clássicos: **terminal, console e apps Chromium/Electron tratam a
  entrada do jeito deles e ignoram mensagens postadas**, então nesses o texto não aparece.

### Modo `live` (ditado por pausa / VAD)

Com `"mode": "live"`, aperta o atalho pra **iniciar a sessão** e aperta de novo pra **encerrar**.
Durante a sessão, o app grava contínuo e, **a cada pausa** sua (>= `silenceMs`), transcreve aquela
frase e cola — enquanto você continua falando a próxima. Dá a sensação de "ir escrevendo" conforme
você fala, frase a frase (não letra a letra — isso é proposital, fica estável e cola limpo).

Dica: o texto é colado com um espaço ao final de cada frase, então as frases se encadeiam naturalmente.
O modo `push` é igual, mas só enquanto a tecla está pressionada (push-to-talk).

## Build / publicar

```powershell
dotnet build Matraca.sln -c Release
# exe portátil (usa o .NET 8 já instalado):
dotnet publish Matraca.Windows -c Release -r win-x64 --self-contained false
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

## Privacidade

O áudio nunca sai da sua máquina e nunca é gravado em disco. Não há telemetria, analytics nem
verificação de atualização. Dois recursos escrevem em disco (o log e o histórico de ditados,
ambos em `%LOCALAPPDATA%\Matraca`, ambos com o texto transcrito), e um recurso opcional e
desligado por padrão transmite texto (o pós-processamento com IA configurado pelo usuário). Detalhes completos na
[política de privacidade](CODE_SIGNING_POLICY.md#privacy-policy).

## Code signing policy

Free code signing provided by [SignPath.io](https://signpath.io), certificate by
[SignPath Foundation](https://signpath.org).

Os releases são gerados apenas pelo GitHub Actions a partir do commit da tag, e cada release exige
aprovação manual antes de ser assinado. Os papéis do time, o processo de build e a
[política de privacidade](CODE_SIGNING_POLICY.md#privacy-policy) estão na
[Code Signing Policy](CODE_SIGNING_POLICY.md) completa.

## Licença

[MIT](LICENSE).
