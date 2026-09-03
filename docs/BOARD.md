# Quadro do projeto

Toda tarefa combinada vira um **card** com ID estável (`MT-###`).
Status: 🗂️ Backlog · 📋 A fazer · 🔄 Fazendo · ✅ Feito · ⏸️ Pausado
Tamanho: **P** (uma sessão) · **M** (poucas) · **G** (frente maior)
Classe: **importante** · **melhoria**
Épico entre [colchetes].

Cartão concluído **sai deste arquivo** — a prova de que foi feito está no diário do dia.
O desenho da frente 2.0 está em [`PLANO-2.0.md`](PLANO-2.0.md).

## 🔄 Fazendo

- **MT-004** **Fase 2 — o Mac dita** — fatias 1–6 verdes e marco de uso pessoal
  entregue. Fatia 7 em execução: contrato seguro de fallback, registro AX, entrega
  pinada com e sem foco por capacidade, moldura click-through, Spaces e monitores.
  Depois falta o endurecimento e a prova assistida final. Contrato em
  [`PLANO-2.0.md`](PLANO-2.0.md). · `[2.0]` · G · importante

## 📋 A fazer

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

_(nada pausado)_
