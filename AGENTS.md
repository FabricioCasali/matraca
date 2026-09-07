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
- O estudo de marca contem variantes; confirme o SVG de referencia no empacotamento, nao trate o default de um seletor como nova aprovacao.
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

Nao sobrescreva mudancas de outros agentes. Nao inclua `.lh/`, `.nodeterm/`, dados locais ou segredos em commits. Preserve estudos historicos em `design/mt-005/`. Push somente mediante pedido explicito do usuario.
