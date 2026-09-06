[English](README.md) | **Português (Brasil)**

# Matraca

Ditado por voz local para qualquer lugar com cursor de texto. O Matraca fica na bandeja,
transcreve com Whisper e entrega o texto na janela escolhida. Nasceu para ditar prompts em
terminais, mas funciona também em editores, navegadores e chats.

O áudio permanece na memória da máquina e nunca é enviado ou gravado em disco.

> "Matraca" é como chamamos, em português brasileiro, alguém que não para de falar.

## Disponibilidade

| Canal | Plataforma | Estado |
|---|---|---|
| [v1.1.0](https://github.com/FabricioCasali/matraca/releases/tag/v1.1.0) | Windows x64 | Release pública mais recente, com a interface nativa anterior. |
| `main` / 2.0 | Windows x64 e macOS Apple Silicon | Em desenvolvimento. A arquitetura compartilhada está funcionando, mas ainda faltam validações físicas e o empacotamento final. |

Para instalar a versão pública no Windows, baixe
[`matraca-setup-1.1.0.exe`](https://github.com/FabricioCasali/matraca/releases/download/v1.1.0/matraca-setup-1.1.0.exe).
O instalador atual não possui assinatura digital. O Windows pode exibir um aviso antes de
executá-lo.

As seções abaixo descrevem o código atual da `main`, que será a versão 2.0. Não há `.dmg` nem
release pública para macOS ainda.

## O que existe na 2.0

- Whisper local com Vulkan no Windows e Metal no macOS, com fallback para CPU.
- Uma interface compartilhada para Windows e Mac, com tema claro e escuro.
- Quatro modos de ditado: `toggle`, `hold`, `live` e `push`.
- HUD de gravação, medidor de microfone e limiar visual para detecção de voz.
- Entrega Unicode direta, sem alterar o clipboard, ou colagem com restauração do conteúdo anterior.
- Moldura que mostra qual janela receberá o texto e atalho para fixar um destino.
- Histórico local com busca, cópia, nova entrega e exclusão.
- Vocabulário de contexto para nomes próprios, siglas e termos técnicos.
- Configuração aplicada a quente. Apenas a troca entre GPU e CPU exige reinício.
- Revisão opcional por Anthropic, DeepSeek ou um endpoint OpenAI-compatible.
- Contagem local de tokens e custo estimado da DeepSeek, além de consulta manual de saldo.

## Como usar

Escolha uma tecla global, deixe o cursor no destino e dite. O comportamento depende do modo:

| Modo | Comportamento |
|---|---|
| `toggle` | Pressione uma vez para começar e outra para parar. O trecho inteiro é entregue ao final. |
| `hold` | Mantenha a tecla pressionada enquanto fala. O trecho inteiro é entregue ao soltar. |
| `live` | Pressione para abrir uma sessão. Cada pausa encerra e entrega uma frase; pressione novamente para fechar. |
| `push` | Igual ao `live`, mas a sessão existe apenas enquanto a tecla permanece pressionada. |

Por padrão, o Matraca usa `live`, a tecla `F15`, entrega Unicode e não pressiona Enter. A troca de
modo vale para a próxima sessão de ditado.

No primeiro uso, a interface permite escolher um modelo Whisper, baixá-lo do repositório do
whisper.cpp no Hugging Face ou apontar para um arquivo `.bin` existente. Ela também captura o
atalho e, no Mac, orienta a concessão das permissões de Microfone e Acessibilidade.

## Janela de destino

Durante a gravação, uma moldura não ativável acompanha a janela que receberá o texto. Ela não
aceita clique nem rouba o foco.

Configure `pinHotkey` para fixar a janela em foco. Os ditados seguintes continuam indo para esse
destino até o atalho ser pressionado novamente. Com `pinDelivery: "focus"`, o Matraca traz o
destino para frente, entrega o texto e restaura o foco anterior. O modo `nofocus` depende do tipo
de campo e da plataforma; terminais e aplicativos Chromium/Electron costumam rejeitar esse tipo
de entrega silenciosa.

## Configuração

A interface salva o arquivo `appsettings.json` nestes diretórios:

| Plataforma | Diretório de dados |
|---|---|
| Windows | `%LOCALAPPDATA%\Matraca` |
| macOS | `~/Library/Application Support/Matraca` |

O token `%MATRACA_DATA%` pode ser usado nos caminhos do arquivo e resolve para o diretório da
plataforma atual. Os mesmos nomes de tecla e campos de configuração servem nos dois sistemas.

### Ditado e áudio

| Campo | Padrão | Efeito |
|---|---|---|
| `modelPath` | `%MATRACA_DATA%\models\ggml-large-v3-turbo.bin` | Arquivo ggml do Whisper. |
| `language` | `pt` | Idioma do áudio. |
| `hotkey` | `F15` | Atalho global. Aceita teclas como `F13` a `F24`, mídia, numpad e combinações como `Ctrl+Alt+X`. |
| `mode` | `live` | `toggle`, `hold`, `live` ou `push`. |
| `inputDevice` | vazio | Nome do microfone. Vazio usa o dispositivo padrão do sistema. |
| `beep` / `beepVolume` | `true` / `0.8` | Sons de início e fim e seu volume. |
| `startSound` / `stopSound` | vazio | Arquivos de som locais opcionais. |
| `silenceMs` | `450` | Pausa que encerra uma frase nos modos contínuos. |
| `phraseMaxSeconds` | `6` | Após esse tempo de fala contínua, uma pausa curta já fecha a frase. |
| `vadThreshold` | `0.012` | Limiar geral da detecção de voz. |
| `micSensitivity` | `{}` | Limiares por nome de microfone. A tela de áudio permite ajustá-los sobre o medidor ao vivo. |
| `vocabulary` | `[]` | Termos enviados como contexto inicial ao Whisper. |
| `gpu` | `auto` | `auto`, `gpu` ou `cpu`. O alias legado `vulkan` ainda funciona no Windows. |
| `idleUnloadMinutes` | `5` | Libera o modelo após esse período ocioso. `0` mantém o modelo carregado. |

### Entrega e armazenamento

| Campo | Padrão | Efeito |
|---|---|---|
| `autoEnter` | `false` | Pressiona Enter após entregar o texto. |
| `pasteMethod` | `unicode` | `unicode` digita diretamente; `clipboard` cola e restaura o conteúdo anterior. |
| `pinHotkey` | `none` | Atalho para fixar ou liberar a janela de destino. |
| `pinDelivery` | `focus` | `focus` entrega com restauração de foco; `nofocus` tenta entregar sem ativar o destino. |
| `focusBorder` | `true` | Mostra a moldura do destino. |
| `focusBorderColor*` | cores por estado | Cores normal, ocupada e fixada. |
| `focusBorderThickness` / `focusBorderOpacity` | `4` / `0.9` | Espessura e opacidade da moldura. |
| `history` | `true` | Mantém transcrições recentes em texto puro no computador. |
| `historyMaxItems` | `100` | Limite de itens do histórico. |

## Revisão por IA

A revisão é opcional e vem desligada. Quando ligada, o Matraca envia o texto transcrito, junto da
instrução de limpeza, ao provedor escolhido. O áudio nunca faz parte da requisição. A instrução
padrão corrige pontuação, acentuação e capitalização e remove hesitações sem resumir, traduzir ou
reescrever o ditado.

Se a chamada falhar, exceder o tempo limite, for recusada ou devolver texto vazio, o Matraca
entrega a transcrição original.

| Provedor | Configuração |
|---|---|
| Anthropic | `postProcessProvider: "anthropic"`, modelo e `postProcessApiKey` ou `ANTHROPIC_API_KEY`. |
| DeepSeek | `postProcessProvider: "deepseek"`, modelo, nível de raciocínio e `postProcessDeepSeekApiKey` ou `DEEPSEEK_API_KEY`. |
| OpenAI-compatible | `postProcessProvider: "openai-compatible"`, modelo, endpoint e `postProcessOpenAiApiKey` ou `OPENAI_API_KEY`. Endpoint vazio usa a API da OpenAI. HTTP só é aceito em endereço local; endpoints remotos exigem HTTPS. |

Outros campos disponíveis: `postProcessPrompt`, `postProcessReasoning` (`off`, `low`, `high` ou
`max`) e `postProcessTimeoutMs`, cujo padrão é `8000`.

Para respostas da DeepSeek, o histórico registra tokens da chamada e custo estimado. O arquivo
`ai-usage.json` mantém somente totais diários por provedor e modelo, sem o texto do ditado. A
consulta de saldo chama `https://api.deepseek.com/user/balance` apenas quando o usuário aperta o
botão correspondente e reutiliza o resultado por 30 segundos.

## Privacidade e rede

- O áudio existe somente em memória durante captura e transcrição.
- Não há telemetria, analytics, relatório automático de falhas ou verificação de atualização.
- Log e histórico podem conter o texto transcrito e ficam somente no diretório local do Matraca.
- A revisão por IA transmite texto apenas quando o usuário a habilita.
- O download de modelo acessa `huggingface.co` quando solicitado.
- A consulta de saldo acessa a DeepSeek apenas por ação manual e não envia texto de ditado.

As chaves de API informadas na interface são gravadas no `appsettings.json` local. Para evitar
armazená-las no arquivo, use as variáveis de ambiente descritas acima. Consulte a
[política completa](CODE_SIGNING_POLICY.md#privacy-policy).

## Compilar e testar

Requer o SDK do .NET 8. A solução inteira deve compilar mesmo quando o comando roda fora do
Windows:

```bash
dotnet build Matraca.sln -c Release -p:EnableWindowsTargeting=true
dotnet test Matraca.sln -c Release
```

### Windows

Para gerar o instalador self-contained x64, instale também o
[Inno Setup 6](https://jrsoftware.org/isinfo.php):

```powershell
.\installer\build-installer.ps1 -Version 0.0.0
# saída: installer\output\matraca-setup-0.0.0.exe
```

O instalador pode criar um atalho de inicialização e, opcionalmente, instalar a variante
`uiAccess` para ditar em janelas elevadas. Essa opção cria e confia um certificado local para
assinar o executável instalado.

### macOS

O projeto atual exige Apple Silicon, macOS 15 ou posterior, Xcode Command Line Tools e uma
identidade local de assinatura chamada `Matraca Dev`. Outra identidade pode ser passada em
`MATRACA_SIGN_IDENTITY`:

```bash
bash Matraca.Mac/pack.sh
# saída: Matraca.Mac/bin/Matraca.app
```

Esse pacote é para desenvolvimento. Ele não é notarizado e ainda não existe `.dmg` público.

## Arquitetura

```text
Matraca.Core      pipeline, configuração, Whisper, VAD, histórico e fila de entrega
Matraca.Web       HTML, CSS e JavaScript compartilhados pela interface e pelo HUD
Matraca.Windows   hotkey, áudio, entrega, bandeja e WebView2 no Windows
Matraca.Mac       event tap, AudioQueue, Acessibilidade, bandeja e WKWebView no macOS
tests             contratos e testes do Core e das fronteiras
```

## Diagnóstico

- Windows: `%LOCALAPPDATA%\Matraca\matraca.log`
- macOS: `~/Library/Application Support/Matraca/matraca.log`
- O menu da bandeja abre o log.
- O Windows pode testar um WAV mono de 16 kHz sem usar o microfone:

```powershell
Matraca.exe --transcribe caminho\audio.wav
```

O hook global de teclado pode provocar alerta heurístico de antivírus. Ele existe para capturar o
atalho em qualquer aplicação. Para capturar o atalho em janelas executadas como administrador, use
a opção `uiAccess` do instalador.

## Assinatura de código

O workflow gera releases Windows a partir do commit da tag. Se a integração com o SignPath estiver
configurada, o instalador passa por aprovação manual antes da assinatura. Sem essa configuração, o
workflow publica o instalador sem assinatura, como ocorreu na `v1.1.0`.

O pacote de desenvolvimento do Mac usa uma identidade local. Distribuição para terceiros ainda
depende de assinatura e notarização Apple. Leia a [política de assinatura](CODE_SIGNING_POLICY.md).

## Licença

[MIT](LICENSE).
