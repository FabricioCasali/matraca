# Matraca: site publico

Pagina de entrada: https://fabriciocasali.github.io/matraca/

`index.html` serve portugues e `en/index.html` serve ingles, ambos com marca 03A e demonstracao sem captura de audio. `favicon.svg` deriva do mesmo asset oficial. O site nao faz requisicoes em tempo de execucao, nao usa fontes externas nem analytics. O design aprovado foi trazido do OpenDesign; este diretorio e a fonte versionada para futuras publicacoes.

## Publicacao

O workflow `.github/workflows/pages.yml` roda os testes antes de publicar. Push em `main` que altera `site/**` ou o workflow publica automaticamente; pull requests somente validam. O `release.yml` tambem dispara `pages.yml` explicitamente depois de criar uma release por tag, porque eventos criados pelo `GITHUB_TOKEN` nao iniciam outro workflow automaticamente. Tambem e possivel disparar manualmente pelo GitHub Actions.

Settings > Pages deve usar **GitHub Actions** (`build_type: workflow`). O ambiente de deploy e `github-pages`, sem dominio customizado.

`index.html`, `en/index.html`, `favicon.svg` e `.nojekyll` entram no artefato publico. Durante o workflow, `build-site.cjs` consulta a API do GitHub, em tempo de build, para resolver a ultima release estavel do Windows e o preview macOS mais recente; os links sao gravados no HTML estatico. Testes, este README, gerador e arquivos de design nao sao publicados. A publicacao do site nao cria uma nova release nem instala o aplicativo. O CI geral do repositorio tambem pode rodar em pushes de main, conforme sua configuracao existente.

## Testar localmente

```sh
node site/site.test.cjs
node site/generate-english.cjs --check
```

Requer Node.js 18 ou superior; o workflow usa Node 22. `test-dom.cjs` verifica traducoes e interacoes numa arvore derivada do HTML. Nao substitui teste de layout em navegador.

## Idiomas

Portugues e a fonte em `index.html`; `STATIC_COPY` contem as traducoes inglesas, `DEMO_COPY` os estados e `PAGE_META` os metadados. `generate-english.cjs` aplica esse contrato para gerar a pagina inglesa estatica. O seletor navega entre as URLs canonicas sem depender de preferencia local.

As duas paginas possuem metadados, canonical e `hreflang` estaticos. Crawlers sem JavaScript recebem o idioma correto.

## Releases e disponibilidade

O site nao fixa uma versao no codigo publicado. Em cada push na `main` e em cada release publicada, o workflow resolve a release estavel do Windows e o preview macOS existente. O preview macOS pode ser assinado apenas com a identidade local de desenvolvimento e nao deve ser apresentado como notarizado pela Apple.

Quando o pacote Mac mudar de preview para distribuicao, revisar os textos PT/EN e o link em conjunto. Ao mudar a versao Windows, revisar URL, assinatura e avisos nos dois idiomas. Nao reutilizar um nome de asset sem conferir sua existencia. Para publicar uma versao Windows, basta criar e enviar a tag `vX.Y.Z`; o `release.yml` publica a release e dispara a atualizacao do Pages.

## Reverter

Reverter o commit do site por um novo commit e publicar novamente via push autorizado. Nao usar force-push, nao reverter outras alteracoes da aplicacao e nao mudar o workflow de release para corrigir o site.
