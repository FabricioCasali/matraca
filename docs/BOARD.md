# Quadro do projeto

Toda tarefa combinada vira um **card** com ID estável (`MT-###`).
Status: 🗂️ Backlog · 📋 A fazer · 🔄 Fazendo · ✅ Feito · ⏸️ Pausado
Tamanho: **P** (uma sessão) · **M** (poucas) · **G** (frente maior)
Classe: **importante** · **melhoria**
Épico entre [colchetes].

Cartão concluído **sai deste arquivo** — a prova de que foi feito está no diário do dia.
O desenho da frente 2.0 está em [`PLANO-2.0.md`](PLANO-2.0.md).

## 🔄 Fazendo

- **MT-032** **Adotar Design System 1.0.1** — UI, tokens, aparencia persistida, microfone,
  consumo e HUD integrados; marca 03A de `design/assets/brand/` incluida nos assets Web
  dos dois hosts. CMP-001 a CMP-010, FND-001/002/003, BRD-001, MOT-001 e A11Y-001;
  contratos em `design/docs/`. Parar live drena tambem os frames finais da captura,
  sem descartar texto. Restam provas reais de audio/entrega, foco/cliques, monitores,
  Dock/DPI, leitor de tela e animacoes no Windows/macOS. Nomes de microfone ambiguos
  atualmente impedem captura em vez de associar ajuste ao dispositivo errado; falta
  resolver identidades duplicadas pelas APIs nativas. ICO/ICNS e troca dos icones
  nativos de bandeja seguem no empacotamento MT-007. Verificacao Web em
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
  o WinForms saiu do bootstrap, do código e do publish. Restam os checkpoints físicos no
  Windows para painel, bandeja/overlays, áudio, hotkeys, ditado completo e sleep/wake.
  Contrato em [`PLANO-2.0.md`](PLANO-2.0.md). · `[2.0]` · G · importante
- **MT-019** **Ditado grava, mas não entrega texto no Mac** — correção aplicada ao par
  Unicode `keyDown`/`keyUp`, com smoke passando pela fila real e pelo event tap. Resta a
  prova assistida de entrega em aplicativos reais. · `[2.0]` · P · importante
- **MT-020** **Onboarding mostra modelos como `undefined`** — o contrato das bridges e o
  smoke cobrem rótulo e tamanho do catálogo. Resta confirmar o seletor na UI real. · `[2.0]`
  · P · importante
- **MT-021** **Título duplicado na barra superior do Mac** — o título nativo continua como
  metadado, mas fica oculto por `titleVisibility`; o smoke cobre a propriedade. Resta a
  confirmação visual. · `[2.0]` · P · melhoria
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
- **MT-007** **Fase 5 — empacotamento** — o instalador Windows voltou a publicar as
  variantes standard e uiAccess sem propagar o manifest para o Core. Restam `.app` +
  `.dmg` no Mac, `macos-14` na matriz do CI e a validação dos artefatos finais. · `[2.0]`
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
