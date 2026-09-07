# Matraca Design System

Esta pasta e a fonte versionada de especificacao, tokens, mocks e contratos do Matraca. O material foi trazido do OpenDesign; novas alteracoes devem ser reconciliadas aqui, sem manter duas fontes oficiais concorrentes.

## Comece aqui

- [Especificacao 1.0.1](docs/design-system.md): regras normativas para o refactor.
- [Tokens](docs/design-system-tokens.json): valores compartilhados dos dez temas.
- [Guia visual](matraca-design-system.html): exemplos de componentes.
- [Historico de versoes](docs/design-system-changelog.md).
- [Manifesto de integridade](docs/design-system-manifest.json).

As instrucoes para agentes estao no `AGENTS.md` da raiz do repositorio.

## Mocks

| Assunto | Entrada |
| --- | --- |
| Primeiro uso | [Experiencia](matraca-experiencia.html) |
| Ditado | [Ditado](matraca-ditado.html) |
| Microfone | [Microfone](matraca-microfone.html) |
| Configuracoes | [Configuracoes](matraca-configuracoes.html) |
| Historico e consumo | [Historico](matraca-historico.html) |
| Frame e animacao | [Gravacao](matraca-gravacao.html) |
| Marca | [Identidade](matraca-identidade.html) |

As entradas pequenas carregam `matraca-experiencia.html` por iframe. Preserve a estrutura da pasta. Os HTMLs podem ser servidos por um servidor estatico local; nao dependem do OpenDesign para renderizar. Os estudos desatualizados de `mt-005/` foram removidos; continuam recuperaveis pelo historico Git.

## Validacao

Da raiz do repositorio:

```sh
node design/tests/matraca-design-system.test.cjs
node design/tests/matraca-prototype.test.cjs
node design/tests/matraca-brand.test.cjs
```

Requer Node.js 18 ou superior, sem instalar dependencias. O teste do pacote compara tokens, referencias e hashes SHA-256. A especificacao tem versao propria, independente da versao do aplicativo. `.gitattributes` mantem LF nos arquivos deste pacote para que as assinaturas sejam iguais depois de checkout no Windows ou no Mac, sem alterar os atributos do estudo antigo.

Mudancas intencionais devem atualizar o changelog e o manifesto, apos revisao: `node design/tests/matraca-design-system.test.cjs --seal`.

Screenshots antigos nao foram importados: sao derivados potencialmente desatualizados. A fonte e o HTML acompanhado da especificacao. A marca confirmada e **03A**; os [24 SVGs estaticos e animados](assets/brand/README.md) ficam em `assets/brand/`. Confira a exportacao com `node design/tools/export-brand-svg.cjs --check` a partir da raiz do repositorio. Pacote ICO/ICNS ainda pendente.

## Preservacao

Git fornece historico local. Somente um push autorizado ou backup externo protege contra perda da maquina. Esta entrega nao publica nada automaticamente. A copia no OpenDesign permanece disponivel como origem da entrega, mas nao sincroniza sozinha com este repositorio.
