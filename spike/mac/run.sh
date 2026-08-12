#!/usr/bin/env bash
#
# pack.sh + executa a perna pedida, mostrando o log no terminal.
#
# Uso:  bash spike/mac/run.sh --tap
#       bash spike/mac/run.sh --audio | --whisper | --inject | --hud | --pipeline
#
# Roda o binario DIRETO, e nao via `open`: o `open` engole o stdout, e rodar direto ainda
# da' identidade de bundle completa, porque tanto o NSBundle quanto o TCC derivam o bundle
# do caminho do executavel. Se a concessao de Acessibilidade nao colar rodando assim, o
# plano B e' `open -a ./Matraca.app --args --tap` e ler o arquivo de log em
# ~/Library/Application Support/Matraca/spike.log.
#
# ESCAPE HATCH (vai ser preciso, com assinatura ad-hoc o cdhash muda a cada build):
#   tccutil reset Accessibility io.github.fabriciocasali.matraca
#   tccutil reset Microphone    io.github.fabriciocasali.matraca
#
# Para pular a recompilacao e so' rodar o que ja' esta' empacotado:
#   MATRACA_SKIP_PACK=1 bash spike/mac/run.sh --tap
# (util justamente porque recompilar revoga a Acessibilidade no modo ad-hoc)
#
set -euo pipefail

cd "$(dirname "$0")"

if [[ "${MATRACA_SKIP_PACK:-0}" != "1" ]]; then
  bash ./pack.sh
else
  echo ">> MATRACA_SKIP_PACK=1 — usando o Matraca.app que ja' esta' montado"
fi

echo ""
echo ">> executando: ./Matraca.app/Contents/MacOS/Matraca $*"
echo ""
exec ./Matraca.app/Contents/MacOS/Matraca "$@"
