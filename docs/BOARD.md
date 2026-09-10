# Quadro do projeto

Toda tarefa combinada vira um **card** com ID estável (`MT-###`).
Status: 🗂️ Backlog · 📋 A fazer · 🔄 Fazendo · ✅ Feito · ⏸️ Pausado
Tamanho: **P** (uma sessão) · **M** (poucas) · **G** (frente maior)
Classe: **importante** · **melhoria**
Épico entre [colchetes].

Cartão concluído **sai deste arquivo** — a prova de que foi feito está no diário do dia.
O desenho da frente 2.0 está em [`PLANO-2.0.md`](PLANO-2.0.md).

## 🔄 Fazendo

- **MT-038** **Internacionalizar a interface sem misturar reconhecimento** — contrato DS 1.1.0 e implementação pt-BR/en-US integrados no Core, painel Web, HUD e superfícies nativas Windows/macOS. `uiLanguage` aceita `system`, `pt-BR` e `en-US`; instalação nova usa `system`, configuração legada preserva pt-BR e `language` continua exclusivo do reconhecimento. Troca a quente preserva foco, seção, conteúdo e estados explícitos; menus, diálogos, erros seguros e recursos de permissão do macOS acompanham o locale efetivo. Provas automatizadas, navegador Chromium, builds cruzados e bundle macOS assinado estão verdes. Restam confirmação assistida em WebView2/WKWebView, permissões e acessibilidade nativas, além da execução em Windows físico. VER-001, I18N-001, CMP-002/003/004 e A11Y-001. · `[design-system]` · P · importante
- **MT-037** **Fixar navegacao e ampliar janela inicial** — usuario esclareceu que
  Aparencia caia na linha seguinte das abas e nao parecia menu; rolagem herdada era
  hipotese anterior, nao causa confirmada. Escopo: navegacao principal fora da
  rolagem do conteudo, overflow proprio em janela baixa e tamanho inicial 1200 x 820
  logico nos dois hosts, limitado pela area util. Sem redesign das abas ou temas;
  reset de rolagem anterior preservado no novo conteiner. DS 1.0.1, FND-001/003,
  CMP-003/004 e A11Y-001. Implementado e verificado por browser, contratos e build
  cruzado; resta prova assistida em WebView2/WKWebView, DPI/monitores e leitor de tela.
  Regressões em
  `tests/web/browser.cjs` (aceita `MATRACA_WEB_ROOT`), `ui.test.cjs` e
  `tests/windows-window-layout.ps1` (calculo Win32 real, sem abrir janela).
  Lacuna documental: inventário de configurações ainda declara tema/família sem
  campos nativos, mas `RawConfig`/`ConfigSnapshot` já os possuem; reconciliar no
  MT-032, sem alterar preferências pessoais. · `[design-system]` · P · importante
- **MT-032** **Adotar Design System 1.0.1** — UI, tokens, aparencia persistida, microfone,
  consumo e HUD integrados; marca 03A de `design/assets/brand/` incluida nos assets Web
  dos dois hosts. CMP-001 a CMP-010, FND-001/002/003, BRD-001, MOT-001 e A11Y-001;
  contratos em `design/docs/`. Parar live drena tambem os frames finais da captura,
  sem descartar texto. Restam provas reais de audio/entrega, foco/cliques, monitores,
  Dock/DPI, leitor de tela e animacoes no Windows/macOS. Nomes de microfone ambiguos
  atualmente impedem captura em vez de associar ajuste ao dispositivo errado; falta
  resolver identidades duplicadas pelas APIs nativas. ICO Windows e ICNS macOS 03A
  integrados; provas assistidas seguem no empacotamento MT-007. Verificacao Web em
  `tests/web/ui.test.cjs`, `tests/web/browser.cjs` e `tests/hud-ds101-exclusive.test.cjs`;
  navegador usa bridge exclusiva de teste, nao comprova o host. Tokens gerados por
  `node Matraca.Web/export-design.cjs` e conferidos com `--check`.
  Complementa MT-005, MT-006 e MT-031, sem encerrar suas provas pendentes.
  · `[design-system]` · G · importante
- **MT-004** **Fase 2 — o Mac dita** — fatias 1–7 verdes. Endurecimento implementa
  histórico/pós-processamento, clipboard com restauração integral, feedback sonoro,
  instância única, sleep/wake, troca de microfone e shutdown drenando entregas aceitas.
  Prova assistida verde para entrega Unicode/clipboard em TextEdit, Terminal, Chrome e
  VS Code, com preservação do clipboard, e para a moldura entre janelas, Spaces e
  monitores sem roubar foco ou cliques. Restam o soak e o ciclo físico de sleep/wake;
  este último está aceito provisoriamente, sem prova, porque a máquina não pôde ser
  suspensa durante a sessão. Contrato em
  [`PLANO-2.0.md`](PLANO-2.0.md). · `[2.0]` · G · importante
- **MT-005** **Fase 3 — a UI unificada** — Spectrum Glass aprovado e extraído para o
  projeto compartilhado `Matraca.Web`. Primeira fatia técnica verde no Mac: casco
  `WKWebView` aberto apenas pelo menu, ponte versionada com placeholders, assets locais
  empacotados e navegação externa bloqueada. O arraste pela barra superior foi validado
  manualmente; resta confirmar que Dock/Command-Tab aparecem apenas com a janela aberta.
  Ponte real já liga estado,
  histórico, todos os parâmetros, captura guiada das teclas, download do modelo e monitor
  de microfone com FFT/VAD ao Core. HUD WebKit acompanha escrita/resultado sem ativar ou
  aceitar clique e segue o monitor do alvo. A fatia Windows já hospeda o mesmo painel em
  WebView2, com bridge v1, captura guiada, monitor de microfone, download e alvo preservado
  para repaste. Resta validação manual do conjunto nos dois sistemas. · `[2.0]` · G · importante
- **MT-006** **Fase 4 — o Windows muda de casa** — casco, painel, HUD, moldura, bandeja,
  lifecycle e clipboard já usam Win32/WebView2 nativos; a paridade funcional foi reposta e
  o WinForms saiu do bootstrap, do código e do publish. Resize e moldura nativa aceitos
  pelo usuario apos compilar e reabrir o app local; entrega na main autorizada, sem nova
  tag, release ou deploy. Esse aceite nao cobre as provas especificas abaixo.
  Restam os checkpoints físicos no
  Windows para painel, bandeja/overlays, áudio, hotkeys, ditado completo e sleep/wake.
  No painel, seguem arraste real, DPI entre monitores, Windows 10, alto contraste
  e acessibilidade assistidos; limites do probe em `tests/WindowsResizeProbe/README.md`.
  Contrato em [`PLANO-2.0.md`](PLANO-2.0.md). · `[2.0]` · G · importante
- **MT-019** **Ditado grava, mas não entrega texto no Mac** — correção aplicada ao par
  Unicode `keyDown`/`keyUp`. Resta a prova assistida de entrega em aplicativos reais. ·
  `[2.0]` · P · importante
- **MT-020** **Onboarding mostra modelos como `undefined`** — o contrato das bridges cobre
  rótulo e tamanho do catálogo. Resta confirmar o seletor na UI real. · `[2.0]` · P · importante
- **MT-021** **Título duplicado na barra superior do Mac** — o título nativo continua como
  metadado, mas fica oculto por `titleVisibility`. Resta a confirmação visual. · `[2.0]` · P ·
  melhoria
- **MT-022** **Permitir outros modelos de IA na revisão** — Anthropic e OpenAI-compatible
  estão configuráveis a quente, com credenciais isoladas, HTTPS remoto obrigatório e
  fallback para a transcrição original. Resta a prova assistida da configuração na UI. ·
  `[2.0]` · M · melhoria
- **MT-023** **Aumentar contraste do tema claro** — tokens ajustados e protegidos por razões
  mínimas de contraste. Resta a confirmação visual nas telas da UI. · `[2.0]` · M · importante
- **MT-024** **Aumentar a tipografia de toda a aplicação** — escala mínima aplicada à UI
  compartilhada e ao HUD, com contratos automatizados verdes. Resta a confirmação visual
  nos dois sistemas. · `[2.0]` · M · melhoria
- **MT-025** **Corrigir as cores do seletor de idioma** — selects e opções agora usam cores
  explícitas e contrastantes nos temas claro e escuro. Resta a confirmação visual. · `[2.0]`
  · P · importante
- **MT-027** **Tornar funcionais os controles da tela principal** — núcleo visual inicia e
  encerra o modo toggle com alvo explícito; `FLUXO` troca o modo a quente. Resta a prova na
  UI real. · `[2.0]` · M · melhoria
- **MT-028** **Corrigir o cartão de última frase** — cada entrega, inclusive trecho live,
  atualiza texto e horário; o menu `•••` copia, recola ou exclui com confirmação. Resta a
  prova na UI real. · `[2.0]` · P · importante
- **MT-031** **Medir consumo e saldo da revisão por IA** — cada resposta DeepSeek captura
  entrada, cache hit/miss, saída, raciocínio e total; o histórico mostra a chamada e o
  custo estimado pela tabela oficial versionada, enquanto o ledger local mantém agregados
  diários sem texto. A tela Revisão consolida o consumo e consulta `/user/balance` somente
  por ação do usuário, com cache de 30 segundos. Restam a prova de um ditado real e a
  confirmação visual do saldo. · `[2.0]` · M · melhoria
- **MT-007** **Fase 5 — empacotamento** — instalador Windows publico para testes:
  [v2.0.1](https://github.com/FabricioCasali/matraca/releases/tag/v2.0.1), sem assinatura,
  com variantes standard e uiAccess e SHA-256 conferido; sem instalacao local.
  O manifest nao se propaga para o Core. Icones Windows
  derivados exclusivamente dos SVGs oficiais 03A (DS 1.0.1, BRD-001/FND-001/A11Y-001):
  EXE/instalador, janela por tema/familia, bandeja monocromatica com quatro estados e
  ICNS olive-light do bundle macOS, com representacoes de 16 a 1024 px.
  Gerador e contrato em `tools/icons/README.md`; testes em `tests/windows-icons*`.
  Restam prova assistida do instalador e na bandeja/taskbar/Dock, tema/DPI/alto contraste,
  `.app` + `.dmg` no Mac e `macos-14` na matriz do CI. Prova local deve usar
  build isolado, sem substituir a instalacao nem interromper o aplicativo em uso.
  · `[2.0]`
  · M · importante

## 📋 A fazer

_(nada a fazer)_

## 🗂️ Backlog

- **MT-008** **Testes manuais pendentes da 1.1** — `pinDelivery: "nofocus"`, onboarding,
  pós-processamento com Claude, beep e latência do live. Nunca foram feitos porque não
  tinham dono; agora têm (`provador`). · `[2.0]` · P · importante
- **MT-009** **Assinatura no macOS** — decidir se compensa conta de desenvolvedor Apple
  para notarizar. O SignPath cobre só Windows, então é uma decisão nova, não uma
  extensão daquela. **Confirmado na MT-002 que não é pré-requisito da Fase 2:** o `.app`
  autoassinado com o certificado `Matraca Dev` é lançável e obtém permissões próprias de
  microfone e Acessibilidade. O que ele *não* faz é ser lançado programaticamente por
  `open` — só por duplo clique no Finder ou por `launchctl`. Segue melhoria, e o que a
  conta compra é distribuição sem atrito para terceiros. · `[distribuição]` · P · melhoria
- **MT-010** **Reputação para aplicar no SignPath** — adiado por decisão dele: o
  formulário exige reputação verificável e hoje são 2 downloads e 0 estrelas. O rascunho
  campo-a-campo já existe, é usar em vez de reescrever. · `[distribuição]` · M · melhoria
- **MT-011** **Repositório sem description nem topics no GitHub** — custa minutos e é
  pré-requisito do MT-010. · `[distribuição]` · P · melhoria
- **MT-012** **Decidir o destino de `.claude/agents/`** — o repositório é público. São só
  prompts, sem segredo, mas é o processo dele à vista. Só importa na hora do
  push. · `[processo]` · P · melhoria
- **MT-014** **A cadeira `especialista-particular` não tem agente** —
  `.claude/agents/especialista-particular.md` não existe, embora o `PLANO-2.0.md` a liste
  como membro sob demanda e o `arquiteto.md` mande consultá-la. Sem o arquivo, quem
  precisa da memória do projeto lê o vault na mão. Criar o agente ou tirar a cadeira das
  duas referências. · `[processo]` · P · melhoria
- **MT-018** **Configurar entrega para alvo em outro Space** — a política inicial no Mac
  é ir ao Space do alvo, confirmar, entregar e restaurar. Expor em configuração a quente
  as alternativas de recusar ou usar `nofocus` quando a capacidade estiver comprovada. ·
  `[2.0]` · P · melhoria

## ⏸️ Pausado

- **MT-036** **Retirar credenciais de IA do JSON em texto claro** — bloqueado por
  decisao de dependencia e migracao. `Config.SaveRaw` serializa `RawConfig` completo
  no temporario e no JSON final; `ConfigSnapshot` protege somente o snapshot Web.
  Revisao e saldo consomem chave literal/configurada ou variavel de ambiente, sem
  resolver `op://`. Padrao do ambiente exige 1Password e referencia validada, mas
  falta definir se o produto distribuido exigira 1Password/CLI ou usara um lancador
  do ambiente, como autenticar sem interromper ditado e como migrar chaves existentes
  com confirmacao de funcionamento antes de remover o legado. Nao basta ocultar
  campos ou gravar `op://` literal: isso quebra autenticacao. Nenhuma configuracao
  pessoal/segredo lido, referencia inventada, credencial alterada ou exposicao
  externa investigada. · `[seguranca]` · M · importante
