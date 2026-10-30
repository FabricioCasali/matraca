<!-- cfg:base v2 inicio cria="opencode.json;docs/DOMINIO.md;.flowforge/port" -->
## Como trabalhar neste projeto

| Chave | Valor |
|---|---|
| Projeto | `ditador` |
| Domínio | `particular` |
| Dono do repositório | `próprio` |
| Escopo no Cérebro | `ditador` |
| Board | Linear (time `Particular`, prefixo `PAR`) / `docs/BOARD.md` legado, prefixo `MT` |
| FlowForge | `http://localhost:4317/?project=ditador` |
| Mapa de Domínio | `docs/DOMINIO.md` |

Este bloco não guarda fatos do projeto. Fato e memória de longo prazo estão no
ambiente (`$AMBIENTE_RAIZ`); as regras gerais vêm das instruções globais.

### Onde buscar a verdade, nesta ordem

1. **Estado** — Linear (via MCP `linear`); para os cards `MT`, `docs/BOARD.md` até migração explícita.
2. **Conhecimento** — skill `consultar-wiki`, domínio `particular`
   (`$AMBIENTE_RAIZ/05-conhecimento/particular/`).
3. **Mapa de Domínio** — `docs/DOMINIO.md`.
4. **Padrões de papel** — arquivos indicados na tabela da equipe.
5. Só então o código e as fontes externas. O que o código provar que falta
   ou diverge da wiki vira proposta.

### Onde registrar

- Resultado relevante: evidência no Cérebro, conforme a postura global.
- Memória ou compromisso a pedido explícito: escopo `ditador`, conforme a postura global.
- Fato durável: proposta em `$AMBIENTE_RAIZ/05-conhecimento/_propostas/particular/`.
- Rascunho do card: `$AMBIENTE_RAIZ/06-dados/rascunho/ditador/<CARD>/`
  ou `.../ditador/equipe/<CARD>/`; exclusivo da tarefa e expurgado no encerramento ou em 5 dias.
- Tarefas ao vivo e bancada visual: `.flowforge/` (`tasks.json` e `workspace.json`).
- Execução da equipe: NodeTerm, conforme a skill `comum-equipe`.
- Iniciativa maior que um card: `$AMBIENTE_RAIZ/04-planejamento/particular/ditador/`.

### Autorizações permanentes

- commit: por fatia aprovada na revisão

### Só o Fabricio decide

- decisão de desenho de produto
- publicação por push, PR ou release

### Recursos exclusivos

Usados por um agente de cada vez; o Arquiteto condutor organiza o uso.

- Microfone, teclado e foco da sessão desktop durante provas nativas.
<!-- cfg:base fim -->

<!-- cfg:equipe v4 inicio cria="docs/padroes/refinador.md;.opencode/launch/refinador;docs/padroes/arquiteto.md;.opencode/launch/arquiteto;docs/padroes/tester.md;.opencode/launch/tester;docs/padroes/coder.md;.opencode/launch/coder;docs/padroes/revisor.md;.opencode/launch/revisor;docs/padroes/usabilista.md;.opencode/launch/usabilista" -->
## Equipe

Equipe pela skill `comum-equipe`, no NodeTerm; processo em `referencias/fluxo.md` dela.
O terminal de entrada abre a tarefa e o Refinador; o Refinador abre o Arquiteto
condutor, que conduz o resto e fala com o Fabricio.
Tarefa e devolução vão por arquivo na pasta do card. O placar das etapas fica no
FlowForge (`http://localhost:4317/?project=ditador`, `tasks.json`).

| Papel | Ativo | Padrão do projeto | Claude | OpenCode | Esforço |
|---|---|---|---|---|---|
| refinador | sim | `docs/padroes/refinador.md` | `claude-opus-5-5` | `openai/gpt-6-astra` | high |
| arquiteto | sim | `docs/padroes/arquiteto.md` | `claude-opus-5-5` | `openai/gpt-6-astra` | high |
| tester | sim | `docs/padroes/tester.md` | `claude-opus-5-5` | `openai/gpt-6-sol` | high |
| coder | sim | `docs/padroes/coder.md` | `claude-opus-5-5` | `openai/gpt-6-sol` | high |
| revisor | sim | `docs/padroes/revisor.md` | `claude-opus-5-5` | `openai/gpt-6-astra` | high |
| usabilista | sim | `docs/padroes/usabilista.md` | `claude-sonnet-5` | `openai/gpt-6-sol` | high |

- **Arquiteto na rota simples:** `claude-sonnet-5 · medium` (OpenCode: `openai/gpt-6-sol`).
- **Problema apontado pelo QA ou pelo Tester:** o Arquiteto confere antes de voltar
  ao Coder; divergência persistente vai ao Fabricio com as duas posições.
- **Times extras:** worktree e branch próprios; mesma composição; CLI escolhida
  ao abrir (padrão: a do terminal de entrada).

### Portões obrigatórios de uma ENTREGA

O contrato da tarefa define as provas aplicáveis; comandos exatos são consultados na wiki.

- Testes do que mudou passam.
- Build cruzado preserva os dois hosts.
- Mudança visual passa nos contratos do Design System e na conferência de tema, teclado e acessibilidade.
- Mudança nativa exige prova no sistema afetado; build e mock não substituem execução real.

### Papéis próprios deste projeto

- nenhum
<!-- cfg:equipe fim -->

# Matraca: instrucoes para agentes

## Antes de alterar a aplicacao

Leia `CLAUDE.md` para as regras de arquitetura e execucao e `docs/BOARD.md` para o estado do projeto. Este arquivo acrescenta o contrato de design; nao substitui as leis existentes nem autoriza refactors fora do pedido aprovado.

## Design System obrigatorio

A fonte versionada do design fica em `design/`. Comece por `design/README.md`.

1. Leia `design/docs/design-system.md` e `design/docs/design-system-tokens.json`.
2. Cite na tarefa a versao e os IDs de regras aplicaveis, por exemplo CMP-006, CMP-010, MOT-001 e A11Y-001.
3. Consulte o mock correspondente como exemplo de composicao, nao como autoridade superior a especificacao.
4. Leia o contrato funcional aplicavel em `design/docs/configuracoes-inventario.md`, `design/docs/consumo-revisao.md` ou `design/docs/movimento-e-gravacao.md`.
5. Registre conflitos e lacunas; nao elimine opcoes existentes nem invente capacidades de Windows/macOS para fazer o mock caber.

## Regras de implementacao

- Reutilize tokens e componentes. Nao escolha cores, medidas ou animacoes por tela.
- Preserve as cinco familias de cor, os modos claro/escuro/sistema e as preferencias do usuario.
- Truncamento e apenas visual: insercao, historico e copia conservam o texto completo.
- Ajuste de microfone pertence ao dispositivo real, sem controle generico na interface.
- Custo desconhecido nao e zero; apagar ditados nao apaga consumo de IA.
- Frame e moldura nativos nao recebem foco nem cliques. Preserve entrega continua e invalide timers antigos.
- Respeite movimento reduzido, navegacao por teclado e contrastes do sistema.
- Mocks possuem fixtures e simulacoes: nao portar essas rotinas para producao nem armazenar credenciais no navegador.
- A variante de marca confirmada e a 03A, `monogram-speech`. Use os SVGs de `design/assets/brand/`; outras variantes do estudo sao apenas historicas. Nao confunda a terminacao diagonal da marca com o prolongamento extra que foi removido da animacao.
- Preserve a arquitetura atual. Mudanca de framework exige necessidade concreta e aprovacao.

## Verificacao e versionamento

Da raiz do repositorio, execute com Node.js 18 ou superior:

```sh
node design/tests/matraca-design-system.test.cjs
node design/tests/matraca-prototype.test.cjs
node design/tests/matraca-brand.test.cjs
```

Esses testes verificam o pacote e a logica dos mocks; nao substituem build, testes .NET, verificacao visual ou prova nativa exigidos pelo projeto.

Ao mudar o design intencionalmente, atualize especificacao, tokens, mocks afetados, testes e `design/docs/design-system-changelog.md` juntos. Revise o diff antes de executar `node design/tests/matraca-design-system.test.cjs --seal`; depois execute a verificacao normal novamente. Nunca regenere o manifesto so para esconder uma divergencia inesperada.

Estado executavel continua em `docs/BOARD.md` da raiz. O quadro importado em `design/docs/BOARD.md` e referencia de adocao, nao um segundo backlog operacional.

Nao sobrescreva mudancas de outros agentes. Nao inclua `.lh/`, `.nodeterm/`, dados locais ou segredos em commits. Os estudos antigos de `design/mt-005/` foram removidos por pedido do usuario; consulte o historico Git se necessario. Push somente mediante pedido explicito do usuario.
