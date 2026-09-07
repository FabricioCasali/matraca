# Matraca Design System

**Versão da especificação: 1.0.1**

**Estado: referência consolidada para implementação; conformidade dos mocks ainda parcial.**

## DS-001 — Autoridade e escopo

Este documento define o padrão visual e de interação do Matraca. Os valores reutilizáveis estão em [design-system-tokens.json](design-system-tokens.json), na mesma versão. Eles formam juntos a especificação normativa.

Ordem para implementar: **especificação e tokens → contrato funcional → mock da tela → código existente**. O contrato funcional governa dados e comportamento de negócio, não redefine aparência. Uma incompatibilidade entre contrato e padrão deve ser explicitada; não resolver silenciosamente removendo recursos.

O [guia visual](../matraca-design-system.html) e os demais mocks exemplificam a especificação. Eles não a alteram implicitamente. O CSS histórico dos protótipos não é uma biblioteca pronta para copiar em produção.

Escopo: aplicativo desktop Windows e macOS, primeiro uso, ditado, microfone, configurações, histórico, consumo, marca e indicador flutuante. Linux, site público e instaladores não são novos escopos aprovados nesta versão.

O pacote oficial fica em `design/` no repositório do Matraca. Esta entrega registra o design; não realiza integração nativa nem publicação remota. Consulte o README desta pasta e o AGENTS.md da raiz do projeto.

## DS-002 — Princípios

- A voz é a tarefa principal; tecnologia aparece quando ajuda a configurar ou recuperar algo.
- Identidade discreta, com cor nos destaques e superfícies predominantemente neutras.
- Uma ação principal por tarefa, sem botões sólidos concorrentes para a mesma função.
- Estados sempre têm texto; cor e movimento não podem ser a única explicação.
- Falhas não descartam texto nem somem antes de oferecer recuperação.
- Simplificar organização e linguagem não significa remover opções existentes.

## FND-001 — Temas e cores

Cinco famílias, cada uma clara e escura: Oliva/Lima, Açafrão/Amarelo, Terracota/Coral, Amora/Orquídea e Jade/Menta. As duas primeiras opções de exibição são claro e escuro; a terceira acompanha o sistema. A família permanece ao mudar o modo.

| Token CSS | Função | Par obrigatório |
| --- | --- | --- |
| `--bg` | Fundo da aplicação | `--fg`, `--subtle` |
| `--surface` | Painéis, campos e menus | `--fg`, `--subtle` |
| `--soft` | Agrupamento discreto | `--fg`, `--subtle` |
| `--fg` | Texto principal | Fundo/superfície |
| `--subtle` | Texto secundário legível | Fundo/superfície |
| `--border` | Separador decorativo | Não substitui foco ou limite necessário de controle |
| `--control-border` | Limite identificável de controle | Superfície do controle |
| `--action-bg` | Ação principal | `--on-accent` |
| `--accent-strong` | Hover da ação principal | `--on-accent` |
| `--brand-ink` | Texto ou ícone de destaque | `--brand-soft` |
| `--success`, `--success-bg` | Confirmação | Usar em conjunto |
| `--danger`, `--danger-bg` | Erro ou destruição | Usar em conjunto |
| `--hud-bg`, `--hud-fg`, `--hud-muted` | Frame flutuante | Conjunto próprio, legível sobre outras janelas |

Os valores exatos e fórmulas estão no JSON. Não escolher cores por tela. `--muted` permanece catalogado por compatibilidade, mas texto de apoio do produto usa `--subtle`. O estudo de marca possui aliases locais que ainda precisam ser unificados.

No escuro, botões vibrantes usam texto escuro. No claro, botões mais escuros usam texto claro. Hover não reduz contraste. Erro e sucesso mantêm seus significados em qualquer família. As cores configuráveis da moldura são dados do usuário, não uma paleta alternativa da interface.

## FND-002 — Tipografia

Pilhas de display e leitura seguem os tokens. Não exigir fontes remotas para o aplicativo funcionar. Uma família utilitária é adequada a esta interface; a fonte final do nome da marca não está convertida em curvas.

| Papel | Tamanho-base | Uso |
| --- | --- | --- |
| Legenda | 12 px | Metadados, ajuda curta, rótulos auxiliares |
| Corpo/controle | 15 px | Texto de leitura e ações |
| Subtítulo | 18 px | Agrupamentos e conteúdo selecionado |
| Seção | 23 px | Seção de conteúdo |
| Métrica | 24 px | Custos e contadores em destaque |
| Título de página | 30 px | Navegação principal |
| Destaque | 38 px | Mensagem principal de uma tela |
| Abertura | Até 60 px | Primeiro uso, reduzido em janelas estreitas |

Pesos: leitura 400, ênfase 500, anúncio 600. Texto corrido: entrelinha 1.5–1.6 e largura preferencial até 65 caracteres. Legendas: 1.5; títulos: 1.1–1.25. Maiúsculas: espaçamento entre letras 0.06–0.1em; controles 0.02em; títulos grandes -0.01 a -0.03em. Não justificar parágrafos.

Exceção de perfil: frame compacto usa título de 14 px e detalhe de 12 px, com truncamento explícito segundo CMP-010. Não aplicar essa densidade aos formulários.

## FND-003 — Geometria e composição

Espaços base: 4, 8, 12, 16, 24 e 32 px. Controle principal: altura 46 px; alvo interativo nunca menor que 44 px. Raio de controle: 10 px; painel: 14 px; foco: 3 px com afastamento 4 px. Valores literais remanescentes dos mocks serão migrados, não transformados em novos tokens automaticamente.

Título → explicação curta → tarefa → ajuda/recuperação. Formulários usam rótulo e ajuda próximos ao controle. Grupos são separados por função, não por um cartão para cada frase. Barras laterais e colunas devem ceder espaço quando a janela estreita, sem rolagem horizontal da página.

Referências atuais de adaptação: 760 px para reorganização principal; componentes compostos podem empilhar antes, em 960/1000 px. Confirmar em janelas de 360, 760 e 1280 px e em escala de interface de 100%, 150% e 200%. Esses são critérios de verificação, não resultados já comprovados.

## BRD-001 — Marca e ícones

A família do monograma “m” é a direção mantida por ora. [O estudo de identidade](../matraca-identidade.html) conserva o original e 03A/03B/03C; o default técnico do arquivo é 03A, **não uma prova da seleção que estava aberta no navegador quando houve a aprovação**. No empacotamento, identificar explicitamente o SVG escolhido antes de substituir todas as marcas. Não reabrir conceitos de balão ou aspas por iniciativa do implementador.

Ícones de função são vetoriais, de linguagem uniforme. Botão apenas com ícone exige nome acessível. Bandeja/barra de menus deve ter versão monocromática e indicação de estado além da cor. Pacote ICO/ICNS e nome tipográfico final continuam pendentes; exports SVG dos estudos não são esse pacote.

## Componentes normativos

### CMP-001 — Ações

Anatomia: rótulo de verbo, ícone opcional, superfície e foco. Variantes: principal, secundária, discreta e destrutiva. Estados: normal, hover, foco, pressionado, desabilitado e ocupado. Estado ocupado impede submissão duplicada e mantém nome compreensível; texto não desaparece em troca de spinner sem descrição.

O mock usa `button()`/`.btn` e `textButton()`. Não acoplar lógica de negócio a uma cor ou posição do botão.

### CMP-002 — Campo de configuração

Anatomia: rótulo persistente, ajuda, controle, valor e erro associado. Usar `aria-describedby`; erro adiciona `aria-invalid`. Edição inválida permanece visível para correção, sem sobrescrever o último valor aceito. Dependências não apagam silenciosamente o conteúdo.

`CONFIG_FIELDS`/`configField()` exemplificam o padrão. Não copiar credenciais fictícias, fixtures ou defaults demonstrativos para configurações reais. Metadados do campo e validação devem usar uma definição compartilhada na implementação.

### CMP-003 — Seleção e preferência

Usar select para escolhas finitas; controles de tema mantêm modo e família independentes. Seleção deve ter nome e indicação explícita. Switch representa estado ligado/desligado, usa `role="switch"` e `aria-checked`. Alterações sem necessidade de reinício aplicam-se sem mudar rota, perder foco ou reiniciar uma gravação.

### CMP-004 — Painel e navegação

`.panel` agrupa uma tarefa relacionada. Navegação principal usa estado atual identificável; subseções de configuração não substituem a navegação de página. Menus fecham com Escape e clique fora; ao fechar, o foco volta ao acionador quando ainda existir. Nenhum menu deve ficar inacessível fora da área útil em janelas pequenas.

### CMP-005 — Estado e aviso

`statusBadge()` identifica estado, não é uma ação. `feedback()` explica o evento, consequência e recuperação. Variantes neutra, ativa, sucesso e erro. Avisos persistentes para falha importante; toast apenas para confirmação breve que não exija ação. Nunca usar “pronto” antes da validação correspondente.

### CMP-006 — Microfone

Um único `microphonePanel()` em Microfone e Configurações/Áudio. Espectro de 48 bandas com preenchimento, indicação textual e barra de ajuste abaixo. Usuário escolhe dispositivo; a aplicação associa e recupera o ajuste daquele dispositivo automaticamente.

Não expor “limiar geral” nem editor textual do mapa. O intervalo do backend é 0.001–0.5; a interface explica “Capta voz mais baixa” e “Ignora mais ruído”. Dispositivo desconhecido ganha registro próprio; valor inicial não é calibração por áudio. Resolver identidade real da entrada, inclusive ao usar o padrão do sistema. Redefinir somente o ativo.

Estados: indisponível, disponível, testando, encerrado e falha. Durante o teste, não trocar de entrada sem encerrar a captura. Sair da página cancela o teste. Mudança de tema não cancela. Ausência de dados não é desenhada como áudio real.

### CMP-007 — Histórico e recuperação

Lista e detalhe sincronizados com seleção e filtros. Conservar texto integral, quebras de linha e Unicode. A ação “Copiar mensagem” é visível no detalhe e confirma resultado; se a cópia falhar, oferecer texto selecionável. Reinserção não é nova chamada de IA.

Vazio, busca sem resultados, seleção, falha de cópia e confirmação de exclusão devem existir. Apagar textos não reduz o acumulado de consumo. Não atribuir modo/idioma antigos com base na configuração atual.

### CMP-008 — Consumo de revisão

Mostrar provedor/modelo e tokens quando recebidos; custo nulo não vira zero. Raciocínio já integra a saída. Identificar moeda USD, preço estimado e versão da tabela. Resumo incompleto chama-se subtotal conhecido, não gasto total. Filtro de provedor afeta resumo e lista; busca textual afeta só textos.

Não inferir quantidade de chamadas sem preço a partir do histórico restante. Exige suporte do acumulador. Saldo oficial de conta não equivale a consumo local e não pode ser inventado. Contrato e lacunas em [consumo-revisao.md](consumo-revisao.md).

### CMP-009 — Revisão e reconhecimento

Reconhecimento local e revisão externa são etapas diferentes. Campos de revisão mudam por provedor, com indicação sobre envio de texto. Limite de espera/falha preserva original. Trocar provedor tem efeitos colaterais no código atual; explicá-los, não remover credencial sem informar.

Download mostra progresso somente quando medido e permite cancelar; aceleração pode exigir reinício, sem confundir preferência salva com hardware ativo. Recursos não implementados no Mac são tratados por capacidades reais. Inventário em [configuracoes-inventario.md](configuracoes-inventario.md).

### CMP-010 — Frame e moldura

Frame informa operação; moldura identifica destino. Frame nativo não recebe cliques e não toma foco. Botões de simulação ficam fora dele. Estados de origem: ready, listening, thinking, writing, done, error. Ready geralmente oculta o frame; destino fixado pode conservar a moldura.

Referência: envelope 430 × 92 e cartão mínimo de 72 px, sujeitos à verificação de DPI/fonte no host. Título em uma linha e detalhe em até duas, truncados apenas visualmente. Inserção, histórico e cópia mantêm texto completo.

Contínuo entrega por trechos confirmados e permanece ouvindo; não promete streaming palavra a palavra. Parar invalida eventos pendentes e conserva trechos entregues. Erro permanece até recuperação/encerramento explícito, conforme proposta aprovada. Integração dos timers deve preservar essa regra nas duas plataformas.

## MOT-001 — Movimento funcional

Tokens: controle 120 ms, entrada 180 ms, estado 160 ms, saída 140 ms. Curva principal `cubic-bezier(.2,.8,.2,1)`. Resposta ao comando não espera uma animação terminar. Não piscar janelas, sacudir erros nem reiniciar entrada do frame a cada renderização sem mudança de estado.

Marca em repouso: parada. **Ouvindo:** formação em um traço, entrada de mesma espessura das hastes (11 unidades do desenho), sem ponta de lápis ou prolongamento extra de saída; ciclo de 1400 ms. **Carregando:** formação aprovada em 2400 ms, sem arco externo. **Concluído:** formação única em 1100 ms, mantendo a letra inteira.

Marca e frame são componentes diferentes: confirmação do frame pode ser mais curta que formação do símbolo. Preservar `prefers-reduced-motion`: mostrar informação estática completa, sem movimento substituto. Pausa da demonstração congela a animação, não muda o estado de negócio.

Eventos e condições de host estão em [movimento-e-gravacao.md](movimento-e-gravacao.md). Descrições antigas de pulso/lápis/arco nos estudos anteriores não anulam esta versão consolidada.

## A11Y-001 — Critérios de aceite

- Texto normal ≥4.5:1; ícones informativos e texto grande ≥3:1; validar pares efetivos de cada tema.
- Estados hover/foco/pressionado não diminuem o contraste do texto. Disabled é exceção.
- Alvos ≥44 px, foco visível, rótulos acessíveis, ordem de tabulação coerente e modais com foco contido.
- Erros são associados ao campo e anunciados sem repetição contínua. Contadores rápidos não inundam leitores de tela.
- Sem sobreposição ou rolagem horizontal da página em larguras previstas. Texto integral é recuperável quando há truncamento autorizado no frame.
- Movimento reduzido funciona sem depender de uma configuração local sobrescrever o sistema.
- Confirmar com teclado e leitor de tela no Windows/macOS. Teste de DOM simulado não encerra esses critérios.

## REF-001 — Mocks e exemplos

| Referência | Arquivo de entrada | Regras principais |
| --- | --- | --- |
| Primeiro uso | [matraca-experiencia.html](../matraca-experiencia.html) | CMP-001/002/005/009 |
| Ditado | [matraca-ditado.html](../matraca-ditado.html) | CMP-001/005/007/010 |
| Microfone | [matraca-microfone.html](../matraca-microfone.html) | CMP-006 |
| Configurações | [matraca-configuracoes.html](../matraca-configuracoes.html) | CMP-002/003/006/009 |
| Histórico e consumo | [matraca-historico.html](../matraca-historico.html) | CMP-007/008 |
| Frame e eventos | [matraca-gravacao.html](../matraca-gravacao.html) | CMP-010/MOT-001 |
| Marca e sua animação | [matraca-identidade.html](../matraca-identidade.html) | BRD-001/MOT-001 |
| Catálogo visual | [matraca-design-system.html](../matraca-design-system.html) | Exemplos, não autoridade normativa |

Os arquivos de entrada, salvo o estudo de identidade, dependem de `matraca-experiencia.html`. Não copiar apenas os pequenos HTMLs de entrada. Screenshots são derivados e podem anteceder ajustes recentes; não são referência para valores ou comportamento.

## GAP-001 — Lacunas declaradas

| Lacuna | Tratamento antes de declarar conformidade |
| --- | --- |
| Marca provisória no app, monogramas no estudo | Identificar SVG selecionado e propagar; gerar pacote nativo posteriormente. |
| Tokens/literais repetidos nos HTMLs | Adotar fonte compartilhada no refactor e verificar valores; não tratar duplicação como padrão. |
| Configuração mock independente da execução mock | Integrar ao estado real; modos e atalho completos não estão executados pelo protótipo. |
| Microfone e espectro sintéticos | Conectar métricas e identidade real do dispositivo; implementar persistência por microfone. |
| Contador nativo perde custo nulo | Ampliar contrato antes de reproduzir cobertura parcial. |
| Controles de teste dentro do HUD comum | Usar variante nativa não interativa; manter comandos fora do overlay. |
| Exceções incompletas | Validar permissão negada, desconexão de microfone, falha de modelo/download e retorno de revisão. |
| Animação versus janela nativa | Manter janela durante saída; cancelar timers antigos; revisar área útil/Dock/DPI. |
| Testes atuais não são um navegador | Executar verificação visual, teclado e acessibilidade na aplicação. |

Estas lacunas não reabrem a direção visual. São trabalho de conformidade e integração.

## VER-001 — Versão, integridade e preservação

Versão 1.0.1 identifica esta especificação, não a versão do aplicativo. PATCH corrige documentação sem mudar contrato; MINOR acrescenta componente/regra compatível; MAJOR muda significado, estrutura ou contrato de componente. Toda alteração registra motivo e impacto no [histórico de versões](design-system-changelog.md).

O [manifesto](design-system-manifest.json) lista arquivos-fonte relativos e SHA-256. O teste de pacote valida presença, referências, versão e assinaturas. Não inclui screenshots nem a si próprio na assinatura.

**Manifesto não é backup nem versionamento Git.** O destino definido pelo usuário é `design/` no próprio repositório. As mudanças são versionadas junto do projeto; publicação remota continua dependente de pedido explícito.

Manter especificação, tokens, marca, mocks, contratos e testes no mesmo pacote versionado; usar referências relativas; registrar cada mudança com diff e motivo. Sincronizar com remoto ou backup externo quando autorizado e testar a recuperação de uma revisão. Não considerar “há uma pasta .git” como proteção contra perda da máquina.

O procedimento `--seal` atualiza intencionalmente o manifesto depois de revisar mudanças. A verificação normal é somente leitura e deve falhar se o pacote divergir. Não reexecutar o selo para esconder uma divergência inesperada.

Verificação com Node.js 18 ou superior, a partir da raiz deste pacote, sem instalação de dependências:

```sh
node tests/matraca-design-system.test.cjs
node tests/matraca-prototype.test.cjs
node tests/matraca-brand.test.cjs
```

Somente após revisar e registrar uma alteração intencional: `node tests/matraca-design-system.test.cjs --seal`. Todos os caminhos do manifesto são relativos à raiz; mover a pasta completa não exige alterar os hashes.

## ADOPT-001 — Como uma tarefa deve citar o sistema

Exemplo: “Refatorar Microfone conforme DS 1.0.1, CMP-006, FND-001/003 e A11Y-001; usar o mock Microfone como composição e o inventário como contrato de dados. Aceite: troca restaura somente o dispositivo correto; tema não interrompe teste; desconexão recuperável; sem ajuste genérico.”

A tarefa também informa campos/eventos alterados, plataformas, regressões protegidas e testes. “Seguir o design system” sozinho não especifica mudança de lógica. O plano de refactor será aprovado antes de alterar a aplicação neste repositório.
