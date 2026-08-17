#!/usr/bin/env bash
#
# pack.sh + executa a perna pedida, mostrando o log no terminal.
#
# Uso:  bash spike/mac/run.sh --tap
#       bash spike/mac/run.sh --audio | --whisper | --inject | --hud | --pipeline
#
# Roda o binario DIRETO, e nao via `open`. ATENCAO ao que isso custa:
#
#   O NSBundle deriva a identidade do caminho do executavel, entao para efeito de Info.plist
#   rodar direto e' equivalente. O TCC NAO. O TCC pergunta quem e' o *processo responsavel*
#   pela cadeia de lancamento, e rodando assim o responsavel e' o TERMINAL. O app passa a
#   viver das permissoes dele: se o iTerm2 ja' tem microfone e Acessibilidade, tudo parece
#   funcionar, nenhum dialogo aparece, e a TCC.db nao ganha uma linha sequer do Matraca.
#   Medicao feita assim prova o CODIGO e MENTE sobre a PERMISSAO.
#
# Serve, portanto, para as pernas que nao dependem de TCC: --alive, --hud, --whisper.
#
# Para qualquer perna que dependa de permissao (--tap, --audio, --inject, --pipeline), lance
# pelo launchd, que torna o app seu proprio processo responsavel:
#
#   launchctl submit -l matraca-spike -- \
#       "$PWD/Matraca.app/Contents/MacOS/Matraca" --tap
#   launchctl remove matraca-spike        # SEMPRE encerre assim
#
# DUAS ARMADILHAS, as duas custaram tempo:
#
#   * `launchctl submit` tem KeepAlive LIGADO por padrao: job que termina e' relancado em
#     segundos, para sempre. Com --inject isso vira o app digitando 225 caracteres na janela
#     em foco a cada ~10 s. Encerre com `launchctl remove`, NUNCA com `pkill` — matar no meio
#     de uma injecao corta o texto e produz falso sintoma de truncamento.
#   * `open -a ./Matraca.app --args --tap` (o plano B que estava escrito aqui) NAO FUNCIONA.
#     Testado com e sem -n, com e sem --args, caminho relativo e absoluto, dentro e fora de
#     sandbox: o processo nao chega a nascer, nem uma linha no log. `spctl -a -vv` responde
#     `rejected, origin=Matraca Dev`. O duplo clique no Finder funciona (o macOS aceita o
#     lancamento iniciado pelo usuario e recusa o programatico), mas nao passa argumento.
#
# O log fica em ~/Library/Application Support/Matraca/spike.log — e' de la' que se le' o
# resultado quando a perna nao roda neste terminal.
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

# O apphost procura o runtime .NET no DOTNET_ROOT, e esta maquina tem DOIS .NET: o do
# Homebrew (so' runtime 10) e o de /usr/local/share/dotnet (que tem o 8.0.18 que o spike,
# em net8.0, precisa). Se o perfil do shell exportar DOTNET_ROOT para o do Homebrew, o app
# morre com "You must install or update .NET to run this application" ANTES de escrever
# qualquer log — e o sintoma nao parece ter nada a ver com dotnet.
#
# Corrige so' quando o runtime pedido nao estiver onde DOTNET_ROOT aponta.
readonly TFM_RUNTIME=8
if [[ -n "${DOTNET_ROOT:-}" ]] \
   && ! compgen -G "$DOTNET_ROOT/shared/Microsoft.NETCore.App/$TFM_RUNTIME.*" > /dev/null; then
  for candidato in /usr/local/share/dotnet "$(cat /etc/dotnet/install_location_arm64 2>/dev/null)"; do
    if [[ -n "$candidato" ]] \
       && compgen -G "$candidato/shared/Microsoft.NETCore.App/$TFM_RUNTIME.*" > /dev/null; then
      echo ">> DOTNET_ROOT=$DOTNET_ROOT nao tem runtime $TFM_RUNTIME; usando $candidato"
      export DOTNET_ROOT="$candidato"
      break
    fi
  done
fi

echo ""
echo ">> executando: ./Matraca.app/Contents/MacOS/Matraca $*"
echo ""
exec ./Matraca.app/Contents/MacOS/Matraca "$@"
