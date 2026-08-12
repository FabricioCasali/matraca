# Quadro do projeto

Toda tarefa combinada vira um **card** com ID estável (`MT-###`).
Status: 🗂️ Backlog · 📋 A fazer · 🔄 Fazendo · ✅ Feito · ⏸️ Pausado
Tamanho: **P** (uma sessão) · **M** (poucas) · **G** (frente maior)
Classe: **importante** · **melhoria**
Épico entre [colchetes].

Cartão concluído **sai deste arquivo** — a prova de que foi feito está no diário do dia.
O desenho da frente 2.0 está em [`PLANO-2.0.md`](PLANO-2.0.md).

## 🔄 Fazendo

- **MT-001** **Fechar o PR #4** — Enter no fim da sessão no modo `live` e entregas
  serializadas. Vem antes do Core: a fila de entrega que ele introduz é política de Core,
  e deixá-lo aberto garante conflito. Revisado em 12/08: o Enter final saía mesmo em
  sessão sem fala — no modo `push` um toque acidental submetia o que estivesse digitado
  na janela em foco. Corrigido em `8f7c183`, build cruzado verde, branch publicada.
  **Parado no teste manual do Fabricio no Windows** (roteiro de 6 passos; o passo 3 é o
  toque sem fala). Sai do draft só depois disso. · `[2.0]` · P · importante

## 📋 A fazer
- **MT-002** **Fase 0 — spike de viabilidade no macOS** — event tap, AudioQueue, Whisper
  com Metal, injeção CGEvent e uma janela WKWebView transparente e não-ativável, tudo
  num app descartável. Inclui o bundle `.app` com id e assinatura estáveis, que é o que
  impede o macOS de revogar a permissão de Acessibilidade a cada build. Mata os quatro
  riscos técnicos de uma vez. · `[2.0]` · M · importante
- **MT-003** **Fase 1 — nascimento do `Matraca.Core`** — mover o portável, extrair as
  cinco interfaces de fronteira, e criar a primeira rede de testes do projeto (hoje são
  4.710 linhas sem teste nenhum). O app Windows continua rodando sobre o Core, ainda com
  WinForms. · `[2.0]` · G · importante
- **MT-004** **Fase 2 — o Mac dita** — `Matraca.Mac` de verdade: event tap, AudioQueue,
  injeção, NSStatusItem, e o pin + moldura por `AXUIElement`. Os quatro modos. Config
  pelo JSON, sem tela. É o marco que importa. · `[2.0]` · G · importante
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

## ⏸️ Pausado

_(nada pausado)_
