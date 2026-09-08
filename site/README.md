# Matraca: site publico

Pagina de entrada: https://fabriciocasali.github.io/matraca/

O `index.html` e autocontido, com marca 03A, portugues/ingles e demonstracao sem captura de audio. Sem build de frontend, fontes externas ou analytics. O design aprovado foi trazido do OpenDesign; este diretorio e a fonte versionada para futuras publicacoes.

## Publicacao

O workflow `.github/workflows/pages.yml` roda os testes antes de publicar. Push em `main` que altera `site/**` ou o workflow publica automaticamente; pull requests somente validam. Tambem e possivel disparar manualmente pelo GitHub Actions.

Settings > Pages deve usar **GitHub Actions** (`build_type: workflow`). O ambiente de deploy e `github-pages`, sem dominio customizado.

Apenas `index.html` e `.nojekyll` entram no artefato publico. Testes, este README, previews e arquivos de design nao sao publicados. A publicacao do site nao cria uma nova release nem instala o aplicativo. O CI geral do repositorio tambem pode rodar em pushes de main, conforme sua configuracao existente.

## Testar localmente

```sh
node site/site.test.cjs
```

Requer Node.js 18 ou superior; o workflow usa Node 22. `test-dom.cjs` verifica traducoes e interacoes numa arvore derivada do HTML. Nao substitui teste de layout em navegador.

## Idiomas

Portugues e a base HTML; `STATIC_COPY` contem as traducoes inglesas, `DEMO_COPY` os estados e `PAGE_META` os metadados. O seletor respeita a preferencia salva, com fallback para o idioma do navegador. Mudar idioma nao recarrega a pagina e reinicia o exemplo sem misturar textos.

Metadados traduzidos dependem de JavaScript. Crawlers sem JavaScript recebem portugues; paginas estaticas independentes por idioma nao foram criadas.

## Releases e disponibilidade

Na verificacao de publicacao, a ultima release publica era `v2.0.0`, com apenas `matraca-setup-2.0.0.exe` para Windows x64. O instalador nao esta assinado. Como o pacote Mac ainda nao apareceu nas releases, o site informa que esta em preparacao e aponta para a pagina de releases, sem inventar um DMG.

Quando o pacote Mac estiver publico, atualizar os textos PT/EN e o link em conjunto. Ao mudar a versao Windows, revisar URL, assinatura e avisos nos dois idiomas. Nao reutilizar um nome de asset sem conferir sua existencia.

## Reverter

Reverter o commit do site por um novo commit e publicar novamente via push autorizado. Nao usar force-push, nao reverter outras alteracoes da aplicacao e nao mudar o workflow de release para corrigir o site.
