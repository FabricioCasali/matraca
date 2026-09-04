# Matraca no macOS + tela unificada

## Contexto

O Matraca é hoje um app de bandeja Windows-only (C#/.NET 8, WinForms, 4.710 LOC) que
transcreve voz com Whisper local e entrega o texto na janela em foco. O Fabricio quer
usá-lo no Mac (M4, macOS 26.5) — e, junto, quer que a tela de configuração seja **uma
só nas duas plataformas**, e que exista uma tela bonita de verdade com histórico de
ditados e espectro do microfone, porque "tem bastante gente largando app para esse fim,
mas bem poucos são usáveis/bonitos".

Isso é maior que uma porta: é o Matraca 2.0. A metade visível do app (1.191 LOC de
WinForms) não atravessa, e as quatro fronteiras com o SO precisam de gêmeo nativo.

O que **não** muda: o formato do `appsettings.json` (o mesmo arquivo vale nos dois
sistemas), o comportamento de config a quente, e os recursos que o Fabricio chama de
diferencial — agarrar uma janela específica (`pinHotkey`) e a moldura que mostra onde o
texto vai cair. No Mac esses dois são a **API de Acessibilidade** (`AXUIElement`), que
portanto é fronteira de primeira classe, não só uma permissão a pedir.

### Decisões já tomadas nesta conversa

| Decisão | Escolha |
|---|---|
| Estrutura | Core portável + UI única + uma camada por plataforma |
| Tecnologia da UI | **Web (HTML/CSS/JS) em casco nativo escrito por nós** |
| HUD ao vivo durante o ditado | **Entra no v1** |
| AX / pin de janela / moldura | **Mantidos no v1**, nas duas plataformas |
| Telas WinForms | Aposentadas ao fim da migração |

Photino foi avaliado e **rejeitado**: não tem bandeja
([issue #171](https://github.com/tryphotino/photino.NET/issues/171)) e o `WaitForClose()`
bloqueia numa janela ([issue #52](https://github.com/tryphotino/photino.NET/issues/52)).
É window-centric; o Matraca é tray-centric e precisa de janela que não rouba foco. O
casco é nosso: `WKWebView` via runtime Objective-C no Mac, `WebView2` em HWND cru no
Windows — coerente com o fato de que já vamos P/Invocar pesado nos dois lados.

## Arquitetura

```
Matraca.Core   (net8.0, sem plataforma)
   pipeline de ditado · VAD · Whisper · Config · histórico · FFT · fila de entrega
   e as interfaces das fronteiras:
      IKeyboardHook   ITextSink   IAudioCapture   ITargetWindow   IShell
            │              │            │               │            │
Matraca.Web   (html/css/js — uma UI, os dois sistemas)
   config · histórico · espectro do mic · onboarding · HUD
            │              │            │               │            │
Matraca.Windows          Matraca.Mac
   WH_KEYBOARD_LL           CGEventTap
   SendInput                CGEventKeyboardSetUnicodeString
   NAudio WaveInEvent       AudioQueue (AudioToolbox)
   HWND + uiAccess          AXUIElement
   WebView2 em HWND         WKWebView em NSWindow
   Shell_NotifyIcon         NSStatusItem
```

Cinco projetos: `Matraca.Core`, `Matraca.Web` (conteúdo), `Matraca.Windows` (plataforma
+ executável), `Matraca.Mac` (idem) e `tests/Matraca.Core.Tests`. Um tipo por arquivo,
pasta por assunto (`Config/`, `Audio/`, `Dictation/`, `Transcription/`, `Platform/`).

### A ponte com a web

Barata, porque a config **já é** um JSON — a ponte manda e recebe esse mesmo objeto.

- Web → C#: `postMessage`, normalizado por um `bridge.js` que cobre as duas APIs
  (`window.chrome.webview` / `window.webkit.messageHandlers`).
- C# → Web: `ExecuteScriptAsync` / `evaluateJavaScript` chamando `window.matraca.onMessage`.
- Mensagens: `config.get/set`, `history.list/delete/repaste`, `mic.frame`,
  `hotkey.capture.start/detected`, `model.download.progress`, `hud.state`.
- Os arquivos web ficam **em disco ao lado do executável**, servidos por host virtual
  (`SetVirtualHostNameToFolderMapping` / `WKURLSchemeHandler`). Editar CSS e recarregar a
  janela passa a ser o loop de desenvolvimento da UI — sem recompilar.

### Três primitivas de janela, por plataforma

1. **Janela normal** (config, histórico) — no Windows, frameless com chrome próprio,
   como o padrão que você fixou no Desktop Desk. No Mac, `fullSizeContentView` com o
   título transparente, **mantendo os semáforos** — janela de Mac sem eles é hostil.
2. **Overlay** (HUD e moldura de foco) — sem borda, sempre no topo, transparente,
   **não ativável** e ignorando clique. Windows: `WS_EX_NOACTIVATE | WS_EX_TRANSPARENT
   | WS_EX_LAYERED | WS_EX_TOPMOST`. Mac: `NSWindow` borderless, `level = .floating`,
   `ignoresMouseEvents = true`, `canBecomeKeyWindow = false`.
3. **Bandeja** — `Shell_NotifyIcon` / `NSStatusItem`.

## As armadilhas conhecidas, e o que fazer com cada uma

**1. O watchdog do event tap é o mesmo problema do `LowLevelHooksTimeout`.** Você já
pagou por isso no Windows: digitar na thread que hospeda o hook trava o hook e o sistema
descarta caracteres. No macOS, callback lento faz o sistema **desabilitar o tap**
(`kCGEventTapDisabledByTimeout`) e o app fica mudo. Logo, as mesmas duas regras: injeção
sempre fora da thread do tap, e reconhecer o próprio evento pela **marca da fonte**
(`kCGEventSourceUserData`, o `dwExtraInfo` deles) — nunca por "é sintético", porque
remapeadores também injetam. Além disso, tratar explicitamente
`kCGEventTapDisabledByTimeout` religando o tap e logando.

**2. A permissão de Acessibilidade gruda na identidade assinada do binário.** Se o `.app`
for ad-hoc e mudar a cada build, o macOS revoga e você re-autoriza a cada `dotnet build`.
Isso mata o dev-loop. Por isso o bundle `.app` com bundle id fixo
(`io.github.fabriciocasali.matraca`) e assinatura estável entra na **fase 0**, não no
release.

**3. Transparência do HUD é o item de maior risco técnico.** WKWebView e WebView2
suportam fundo transparente, mas em janela sem borda e não-ativável isso é caminho menos
batido. Vira spike na fase 0. Se falhar, o plano B é HUD opaco com cantos arredondados
por máscara — perde um pouco, não bloqueia.

**4. Teclado tem numeração diferente.** Hoje o `Config` resolve `"F15"` para o VK do
Windows na hora de ler o JSON. Passa a guardar o **nome canônico** e cada fronteira
traduz para o código nativo. Mesmo JSON, dois sistemas.

**5. Caminhos.** `%LOCALAPPDATA%\Matraca` vira um `AppPaths` no Core, que resolve para
`~/Library/Application Support/Matraca` no Mac. Vale para config, log, histórico e
modelos.

## A equipe que executa

Squad própria do Matraca, em `matraca/.claude/agents/`, no padrão que o NEON já usa. O
portão continua sendo o de 01/08: o `arquiteto` desenha, o `regente` leva ao Fabricio, e
só depois de aprovado o `desenvolvedor` escreve. O portão é garantido pela **lista de
ferramentas** de cada papel, não pela boa vontade.

| papel | o que faz | escreve? |
|---|---|---|
| `regente` | mantém a visão, julga se está pronto, é a única voz que fala com o Fabricio | não — leitura + `AskUserQuestion` |
| `arquiteto` | diz o que e como; **dono do contrato** (interfaces do Core, mensagens da ponte) | não — só leitura |
| `desenvolvedor` | as mãos | sim — código |
| `provador` | prova que roda: rede de testes do Core + roteiro de teste manual | sim — só `tests/` e roteiros |
| `estilista` | o padrão visual no OpenDesign; entrega **spec + artefato**, nunca diff | sim — só a pasta de design |
| `escrivão` | BOARD, READMEs, notas de release, propostas de conhecimento | sim — só documentação |
| `especialista-particular` | a memória do projeto — **cadeira sob demanda**, não membro fixo | não — só leitura |

**Pré-requisito do `regente`:** um `CLAUDE.md` na raiz do repo com as leis do projeto,
porque não dá para julgar desvio de premissa sem premissa escrita. Elas já existem
espalhadas e é só recolher: áudio nunca sai da máquina; zero telemetria; config aplica a
quente (reiniciar é falha de design, exceto GPU/CPU); nada na UI rouba foco durante o
ditado; injeção nunca na thread do hook/tap; um só `appsettings.json` nos dois sistemas;
um tipo por arquivo; todo controle mostra o efeito do que muda; push só com pedido.

**Critério de pronto do `estilista`:** todo controle mostra o efeito do que ele muda —
a crítica do Fabricio sobre a sensibilidade do microfone virada em regra. Os tokens do
OpenDesign viram **CSS custom properties** consumidas direto pelo `Matraca.Web`, sem
etapa de tradução onde o design se perde. O MCP do OpenDesign precisa estar **conectado
na hora de subir o agente**, senão ele não enxerga a ferramenta e ninguém avisa.

**O que a mesa não cobre:** ninguém roda o Matraca no Windows. O `provador` roda no Mac
e compila os dois; o Windows depende do CI e do teste manual do Fabricio.

**Decisão em aberto:** o repo é público. `.claude/agents/` são só prompts, sem segredo,
mas é processo à vista — commitar ou `.gitignore`?

**Recorte:** uma equipe **por fase**, não uma para o plano inteiro. A receita
`executar-plano-aprovado` proíbe a equipe de decidir o que ficou em aberto e de
perguntar (roda em segundo plano); um plano de cinco fases com portão humano no meio
mataria a equipe calada na Fase 3. Cada fase abaixo já é escopo fechado com verificação
definida.

## Fases

Cada fase fecha com build verde nas duas plataformas (`dotnet build` do Mac compila as
duas: `-p:EnableWindowsTargeting=true` já foi verificado) e um commit local.

**Fase 0 — spike de viabilidade (descartável, uma sessão).**
Um app de console Mac que faz o pipeline inteiro uma vez: `NSApplication` com
`LSUIElement`, event tap no F13, `AudioQueue` grava 3s, Whisper transcreve, `CGEvent`
digita no app da frente. Mais uma janela `WKWebView` borderless transparente e
não-ativável mostrando um `<canvas>`. Isso mata os quatro riscos de uma vez. Inclui o
`.app` assinado e o download do modelo (começar com `ggml-base`, ~148 MB, que é sobre
encanamento, não qualidade).

**Fase 1 — nascimento do Core.**
Antes: **fechar o PR #4** (o fix do Enter no live), senão o refactor conflita com ele — e
a fila de entrega serializada que ele introduz é justamente política de Core, não de
plataforma; ela sobe para `DeliveryQueue`. Depois: mover para `Matraca.Core` o que já é
portável (`Config`, `Logger`, `Transcriber`, `DictationHistory`, `TextPostProcessor`,
`ModelDownloader`, o núcleo do VAD, `KeyMods`), extrair as cinco interfaces, e criar
`tests/Matraca.Core.Tests` — o projeto não tem teste nenhum hoje, e a partir daqui o Core
é o cérebro compartilhado. Testes: VAD (corte por pausa, corte suave, pré-roll), parsing
de hotkey, `AppPaths`, FFT. O app Windows continua rodando sobre o Core, ainda com
WinForms.

**Fase 2 — o Mac dita.**
`Matraca.Mac` de verdade: event tap, `AudioQueue`, injeção `CGEvent`, `NSStatusItem`, e o
pin + moldura por `AXUIElement`. Os quatro modos (`toggle`, `hold`, `live`, `push`).
Config pelo JSON, sem tela ainda. **Ao fim desta fase você já dita no Mac** — é o marco
que importa.

**Fase 3 — a UI, começando pelo desenho.**
Primeiro um mockup navegável das telas publicado como artifact, para você criticar antes
de existir código de verdade — que aqui não é desperdício, porque o mockup **é** o app.
Depois o casco (`WKWebView` primeiro, que é onde estamos rodando) e as telas:

- **Configuração** — abas (Tecla · Modelo e vocabulário · Áudio · Entrega · Revisão), todo
  campo aplicando a quente, captura de tecla via hook.
- **Microfone** — espectro FFT de 48 bandas log a ~30 fps com decaimento e peak-hold
  (suave, não nervoso), **mais** a barra de nível com a marca arrastável do limiar,
  ficando verde exatamente quando o VAD conta como fala. Essa barra é desenho seu e o
  motivo está registrado: controle que não mostra o efeito é adivinhação.
- **Histórico** — lista com busca, copiar, recolar no destino e apagar.
- **Onboarding** — download do modelo, captura da tecla e, no Mac, o pedido das duas
  permissões (Acessibilidade e Microfone) com link direto para o painel.
- **HUD** — overlay com onda ao vivo, estado e a última frase reconhecida; some sozinho.

**Fase 4 — o Windows muda de casa.**
`Matraca.Windows` passa a usar o mesmo casco e as mesmas telas (WebView2 em HWND cru,
`Shell_NotifyIcon`). `SettingsForm`, `OnboardingForm`, `HistoryForm`, `MicLevelMeter` e
`FocusBorder` saem. O WinForms só é apagado quando a paridade estiver de pé — até lá os
dois compilam.

**Fase 5 — empacotamento.**
Mac: bundle `.app` + `.dmg`, `LSUIElement`, `NSMicrophoneUsageDescription`, assinatura e
notarização (que reabre a conversa do SignPath: o cert deles é Windows; no Mac o caminho
é a conta de dev Apple, e vale decidir se compensa). Windows: o Inno Setup atual segue,
com o teste do WebView2 Runtime no instalador. CI: a matriz do GitHub Actions ganha o
runner `macos-14`.

## Plano aprovado de execução — MT-003 + MT-004

Registrado em 02/09/2026 para sobreviver à troca de sessão e compactação de contexto. O
objetivo deste recorte é produzir um `.app` local, assinado e utilizável diariamente no M4,
sem esperar a UI web, o `.dmg` ou a distribuição pública.

### Decisões fixadas

1. O projeto atual muda para `Matraca.Windows`; a raiz ganha `Matraca.sln`,
   `Matraca.Core` e `tests/Matraca.Core.Tests`.
2. As fronteiras de plataforma são exatamente cinco: `IKeyboardHook`, `IAudioCapture`,
   `ITextSink`, `ITargetWindow` e `IShell`. Enumeração de áudio entra em
   `IAudioCapture`; som e feedback entram em `IShell`.
3. A configuração guarda nomes canônicos de tecla e aceleração (`auto`, `gpu`, `cpu`).
   Valores Windows já persistidos continuam aceitos como aliases de compatibilidade.
4. O primeiro Mac utilizável será um `.app` self-contained para `osx-arm64`, assinado com
   `Matraca Dev`. `.dmg`, notarização e distribuição continuam na Fase 5.
5. Uma sessão conserva o snapshot de configuração com que começou; alterações a quente
   valem na próxima sessão. GPU↔CPU continua sendo a única troca que pode pedir reinício.
6. MT-015, MT-016 e MT-017 são gates internos da MT-004: ela não fecha com foco roubado,
   backend Metal recriado por ditado ou texto truncado.
7. Cada fatia fecha com testes, build cruzado e commit local. `git push` continua proibido
   sem pedido explícito.

### MT-003 — fatias verdes

1. **Estrutura:** criar a solution e os três projetos, mover o Windows sem alterar o
   comportamento e atualizar assets, instalador e comandos de build.
2. **Caminhos e configuração:** criar `AppPaths`, separar `Config`/`RawConfig`, fixar
   defaults e introduzir hotkeys canônicas sem VK no Core.
3. **Serviços portáveis:** mover logger, histórico, download de modelo, pós-processamento
   e transcrição, eliminando tipos aninhados quando os arquivos forem tocados.
4. **Áudio puro:** separar NAudio de RMS/VAD/FFT e caracterizar por teste o pré-roll, os
   cortes por pausa, o corte suave, o limite duro e o flush final.
5. **Fronteiras Windows:** implementar as cinco portas, sem `HWND`, WinForms, NAudio ou
   P/Invoke vazando para o Core e sem callback público na thread do hook.
6. **Pipeline:** mover os quatro modos, pin/fallback, fila única de entrega, histórico,
   pós-processamento, `autoEnter`, lifecycle do modelo e hot reload para o Core.

A MT-003 fecha somente com testes reais descobertos por `dotnet test`, Core sem dependência
de plataforma e Windows compilando sobre ele. O teste manual Windows continua sendo do
Fabricio; o Mac não finge essa cobertura.

### MT-004 — fatias verdes

1. **Casco e foco:** `Matraca.Mac`, política Accessory no bootstrap, `NSStatusItem`, bundle
   assinado e nenhum roubo de foco (MT-015).
2. **Config, TCC e teclado:** watcher robusto do JSON, permissões visíveis, event tap com
   key-down/up, modificadores, auto-repeat, marca própria e religamento.
3. **Entrega:** writer CGEvent Unicode medido em TextEdit, terminal, navegador e Electron;
   texto e Enter serializados e comparação literal no alvo (MT-017).
4. **Áudio:** AudioQueue contínuo em worker próprio, frames de 16 kHz somente em memória,
   cancelamento, flush e detecção de permissão negada/fluxo de zeros.
5. **Modos:** conectar `toggle`, `hold`, `live` e `push` do Core ao event tap e AudioQueue.
6. **Whisper:** carregar Metal em background e manter processor/backend vivos entre
   ditados, com reload atômico e estado visível (MT-016).
7. **Pin e moldura:** `AXUIElement`, entrega com foco/restauração, `nofocus` somente quando
   confirmado, overlay click-through e tratamento de alvo morto/Space/monitores.
8. **Endurecimento:** histórico, pós-processamento, clipboard, beep, instância única,
   sleep/wake, troca de microfone, shutdown drenando filas e soak.

O fim da fatia 6 é o **marco de uso pessoal**: o Mac já dita pelos quatro modos, por JSON,
com entrega confiável. A MT-004 só fecha após pin, moldura e endurecimento.

### Gates de liberação

```bash
dotnet test Matraca.sln -c Release
dotnet build Matraca.sln -c Release -p:EnableWindowsTargeting=true
dotnet build Matraca.Mac/Matraca.Mac.csproj -c Release
codesign --verify --deep --strict Matraca.app
```

O roteiro manual final cobre permissões negadas/concedidas, quatro modos, TextEdit,
terminal, navegador e Electron, Unicode e texto longo, ditado de 60–120 s, pin/foco,
config a quente, uma única inicialização Metal, ausência de áudio em disco/rede e vinte
rodadas de latência quente. Log que diz “digitado” sem igualdade literal no alvo não é
evidência.

## Arquivos que mudam

O grosso é movimentação, não reescrita. Os que **atravessam sem mudar**:
`Transcriber.cs`, `DictationHistory.cs`, `TextPostProcessor.cs`, `ModelDownloader.cs`,
`Logger.cs`. Os que **atravessam com ajuste**: `Config.cs` (nome canônico de tecla +
`AppPaths`), `LiveDictation.cs` (o VAD sai de dentro do NAudio). Os que **viram
interface + duas implementações**: `HotkeyListener.cs`, `TextInjector.cs`,
`AudioRecorder.cs`, `AudioDevices.cs`, `Beeper.cs`, `FocusBorder.cs`. Os que **morrem**:
`SettingsForm.cs`, `OnboardingForm.cs`, `HistoryForm.cs`, `MicLevelMeter.cs`, e a parte
de bandeja do `Program.cs`.

## Verificação

- **Core**: `dotnet test` — VAD, parsing de hotkey, FFT, `AppPaths`. O VAD tem um caminho
  offline pronto (`FeedForTest`) que aceita um WAV; usar isso para provar que o
  comportamento não mudou na mudança de casa.
- **Compilação cruzada**: `dotnet build -p:EnableWindowsTargeting=true` a cada fase — o
  Mac compila os dois lados, então nenhuma fase quebra o Windows sem eu ver.
- **Mac, ponta a ponta**: ditar num terminal, no navegador e num app Electron; conferir
  que o texto sai inteiro (o sintoma de regressão é texto sem espaços ou cortado);
  agarrar uma janela e ditar nela de outra; conferir que o tap sobrevive a um ditado
  longo (o watchdog).
- **Windows**: build verde no CI + teste manual seu, já que aqui não roda.
- **Latência**: medir no Mac o tempo entre soltar a tecla e o texto aparecer, e comparar
  com os ~0,3 s da 4070 Ti que estão no README.

## Rastro

Trabalho do `escrivão`, com a `casa-limpa` como lei. `docs/BOARD.md` com prefixo `MT-`,
um cartão por fase mais os que sobrarem soltos (o PR #4 pendente, os testes manuais da
1.1 que nunca foram feitos, a decisão de assinatura no Mac). Cartão fechado **sai** do
arquivo; o que fica é estado, não história. O que virar conhecimento durável — as duas
armadilhas do event tap, o comportamento do TCC com binário que muda — vai como proposta
para `05-conhecimento/_propostas/`, para o `curador` aprovar; a equipe não escreve no
`05-conhecimento` direto.

## Primeiro passo, se aprovado

1. `CLAUDE.md` com as leis + os seis agentes em `.claude/agents/` + `docs/BOARD.md`.
2. Fechar o PR #4, que hoje conflita com o nascimento do Core.
3. Fase 0 — o spike que mata os quatro riscos de uma vez.
