# Matraca 03A: arquivos vetoriais

Variante confirmada pelo usuario: **03A, m que fala** (`monogram-speech`). A terminacao diagonal pertence ao simbolo; o prolongamento extra de saida da animacao foi retirado.

## Arquivos

- `matraca-03a-symbol-light.svg`: simbolo escuro, transparente, para fundo claro.
- `matraca-03a-symbol-dark.svg`: simbolo claro, transparente, para fundo escuro.
- `matraca-03a-app-{familia}-{tema}.svg`: dez icones coloridos, cinco familias nos dois temas.
- `matraca-03a-tray-{recording|busy|error}-{light|dark}.svg`: seis estados de bandeja, conforme o estudo. Para pronto, use o simbolo.
- `matraca-03a-{listening|loading|done}-{light|dark}.svg`: seis animacoes independentes, sem JavaScript ou recurso externo.

`light` e `dark` indicam o fundo de destino. As familias sao `olive`, `ochre`, `terracotta`, `plum` e `teal`. Arquivos estaticos usam sRGB convertido dos tokens aprovados.

## Animacoes

- Ouvindo: traco continuo de 11 unidades, sem lapis ou extensao extra; ciclo de 1400 ms.
- Carregando: formacao em ciclo de 2400 ms.
- Concluido: formacao unica de 1100 ms.
- Movimento reduzido: simbolo completo e parado.

O SVG de escuta tem viewBox ampliado para acomodar o traco de entrada sem corte. As animacoes usam CSS, inclusive quando exibidas por `<img>` em navegadores compativeis; a bandeja nativa nao executa animacao SVG. Para controlar pausa, integrar o componente no DOM e gerenciar seu ciclo de vida.

Os estados de bandeja reproduzem os marcadores do estudo, com disco de recorte na cor da superficie prevista. Validar a aparencia sobre a superficie real do sistema antes de gerar ICO/ICNS; estes arquivos nao sao imagens template do macOS prontas para qualquer fundo.

O pacote nao inclui o nome Matraca convertido em curvas, ICO ou ICNS. Nao sao exportacoes de logo tipografico final.

## Reproduzir e conferir

Da raiz do repositorio:

```sh
node design/tools/export-brand-svg.cjs
node design/tools/export-brand-svg.cjs --check
```

O gerador le a geometria do estudo versionado e fixa a escolha 03A. Revise o diff apos regenerar; depois atualize o manifesto de integridade do design.
