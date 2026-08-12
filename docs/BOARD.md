# Quadro do projeto

Toda tarefa combinada vira um **card** com ID estável (`MT-###`).
Status: 🗂️ Backlog · 📋 A fazer · 🔄 Fazendo · ✅ Feito · ⏸️ Pausado
Tamanho: **P** (uma sessão) · **M** (poucas) · **G** (frente maior)
Classe: **importante** · **melhoria**
Épico entre [colchetes].

Cartão concluído **sai deste arquivo** — a prova de que foi feito está no diário do dia.
O desenho da frente 2.0 está em [`PLANO-2.0.md`](PLANO-2.0.md).

## 🔄 Fazendo

- **MT-002** **Fase 0 — spike de viabilidade no macOS** — as seis pernas estão **escritas,
  compilando e commitadas** em `spike/mac/` (24 arquivos), e **um só risco fechou**: o
  **risco 3 — Whisper com Metal no arm64 — passou**. O ggml elege o `MTL0` no M4 e
  transcreve 2,93 s de áudio em **160–230 ms**, mesma faixa da 4070 Ti do README, ou
  abaixo. Os riscos **1, 2, 4, 5 e 6 seguem por provar**, e aqui "por provar" quer dizer
  *escrito e compilando, nunca executado com sucesso* — não "deve funcionar". Quatro
  deles travam em permissão que só é concedida à mão, na frente da máquina. **O que fecha
  o cartão é o roteiro de seis passos** de [`spike/mac/README.md`](../spike/mac/README.md),
  que é a fonte de verdade dos vereditos e dos números — o quadro não os repete.
  Decidido na aprovação: **sem** o certificado self-signed `Matraca Dev` por enquanto,
  então o `pack.sh` assina ad-hoc, e o *designated requirement* fica ancorado no `cdhash`
  — que muda a cada compilação, revogando a Acessibilidade **sem o macOS reprompar**.
  Rodar várias vezes seguidas exige `MATRACA_SKIP_PACK=1`, ou remover e readicionar o app
  no painel. Sem `.sln` nesta fase (nasce na Fase 1). O spike é apagado no primeiro commit
  da Fase 2. · `[2.0]` · M · importante
  - ⏳ **Espera decisão do Fabricio:** o `Matraca.csproj` ganhou 12 linhas de
    `Compile/None/EmbeddedResource Remove="spike/**"`, em commit isolado (`613d4ca`),
    porque o glob padrão do SDK varre `**/*.cs` da raiz e passou a compilar os fontes e o
    `obj/` do spike — oito `CS0579`, build cruzado do Windows quebrado. Isso **contraria a
    premissa de "diff zero fora de `spike/`"** do desenho aprovado, e ainda não foi
    respondido. Revertível numa linha.

## 📋 A fazer

- **MT-003** **Fase 1 — nascimento do `Matraca.Core`** — mover o portável, extrair as
  cinco interfaces de fronteira, e criar a primeira rede de testes do projeto (hoje são
  4.710 linhas sem teste nenhum). O app Windows continua rodando sobre o Core, ainda com
  WinForms. · `[2.0]` · G · importante
- **MT-004** **Fase 2 — o Mac dita** — `Matraca.Mac` de verdade: event tap, AudioQueue,
  injeção, NSStatusItem, e o pin + moldura por `AXUIElement`. Os quatro modos. Config
  pelo JSON, sem tela. É o marco que importa. Dois requisitos que o spike (MT-002)
  descobriu e que precisam ser atendidos aqui: **(a)** carregar o modelo tem dois regimes
  — 7.232 ms na primeira vez da máquina, compilando os kernels Metal, contra 123 ms
  depois; se o modelo for carregado sob demanda, o primeiro ditado depois de instalar vai
  parecer travado, então é carregar na inicialização ou avisar. **(b)** o `AudioQueueStart`
  **bloqueia** esperando a decisão do TCC sobre o microfone, logo não pode ser chamado de
  uma thread que precise continuar respondendo. · `[2.0]` · G · importante
- **MT-005** **Fase 3 — a UI unificada** — mockup navegável primeiro (o mockup **é** o
  app, não é descartável), depois o casco e as telas: config, microfone com espectro,
  histórico, onboarding e HUD. · `[2.0]` · G · importante
- **MT-006** **Fase 4 — o Windows muda de casa** — WebView2 em HWND cru, Shell_NotifyIcon,
  e as quatro telas WinForms aposentadas. Só apaga quando a paridade estiver de
  pé. · `[2.0]` · G · importante
- **MT-007** **Fase 5 — empacotamento** — `.app` + `.dmg` no Mac, Inno seguindo no
  Windows com teste do WebView2 Runtime, e `macos-14` na matriz do CI. · `[2.0]` · M ·
  importante

## 🗂️ Backlog

- **MT-008** **Testes manuais pendentes da 1.1** — `pinDelivery: "nofocus"`, onboarding,
  pós-processamento com Claude, beep e latência do live. Nunca foram feitos porque não
  tinham dono; agora têm (`provador`). · `[2.0]` · P · importante
- **MT-009** **Assinatura no macOS** — decidir se compensa conta de desenvolvedor Apple
  para notarizar. O SignPath cobre só Windows, então é uma decisão nova, não uma
  extensão daquela. · `[distribuição]` · P · melhoria
- **MT-010** **Reputação para aplicar no SignPath** — adiado por decisão dele: o
  formulário exige reputação verificável e hoje são 2 downloads e 0 estrelas. O rascunho
  campo-a-campo já existe, é usar em vez de reescrever. · `[distribuição]` · M · melhoria
- **MT-011** **Repositório sem description nem topics no GitHub** — custa minutos e é
  pré-requisito do MT-010. · `[distribuição]` · P · melhoria
- **MT-013** **A cola por clipboard fica fora da cadeia de entrega** — o PR #4 serializou
  `PasteText` (unicode) e `DeliverWithFocus` numa cadeia única, mas `PasteViaClipboard`
  segue inline na UI thread, porque o `Clipboard` do WinForms exige STA. Hoje não dá
  bug — os `_ui.Post` são sequenciais e a cola termina antes de o Enter ser enfileirado —
  mas é a única entrega em pista separada, e o `SendCtrlV` ali viola a regra de não
  injetar na thread do hook (poucos eventos, por isso passa). Resolver quando a
  `DeliveryQueue` nascer no Core. · `[2.0]` · P · melhoria
- **MT-012** **Decidir o destino de `.claude/agents/`** — o repositório é público. São só
  prompts, sem segredo, mas é o processo dele à vista. Só importa na hora do
  push. · `[processo]` · P · melhoria
- **MT-014** **A cadeira `especialista-particular` não tem agente** —
  `.claude/agents/especialista-particular.md` não existe, embora o `PLANO-2.0.md` a liste
  como membro sob demanda e o `arquiteto.md` mande consultá-la. Sem o arquivo, quem
  precisa da memória do projeto lê o vault na mão. Criar o agente ou tirar a cadeira das
  duas referências. · `[processo]` · P · melhoria

## ⏸️ Pausado

_(nada pausado)_
