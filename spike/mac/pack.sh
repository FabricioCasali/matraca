#!/usr/bin/env bash
#
# Monta e assina o Matraca.app do spike.
#
# Script, e nao alvo de MSBuild, de proposito: `codesign` so' existe no macOS, e um alvo
# condicionado por SO dentro de um csproj compartilhado e' exatamente o tipo de coisa que
# quebra o build cruzado do Windows sem ninguem ver. Na Fase 5 isso pode virar alvo do
# Matraca.Mac.csproj; agora, nao.
#
# Uso:  bash spike/mac/pack.sh
# Env:  MATRACA_SIGN_IDENTITY="Nome da identidade"   forca a identidade de assinatura
#
set -euo pipefail

cd "$(dirname "$0")"

BUNDLE_ID="io.github.fabriciocasali.matraca"
APP="Matraca.app"
PROJ="Matraca.MacSpike.csproj"

# ---------------------------------------------------------------------------
# 1. Resolver a identidade de assinatura
#    ordem: variavel de ambiente > certificado "Matraca Dev" no chaveiro > ad-hoc
#
#    O caminho bom ja' esta' escrito: no dia em que o certificado "Matraca Dev" existir
#    no chaveiro login, ele e' encontrado aqui e NADA muda no script.
# ---------------------------------------------------------------------------
IDENTITY=""
AD_HOC=0

if [[ -n "${MATRACA_SIGN_IDENTITY:-}" ]]; then
  IDENTITY="$MATRACA_SIGN_IDENTITY"
  echo ">> Identidade vinda de MATRACA_SIGN_IDENTITY: $IDENTITY"
elif security find-identity -v -p codesigning 2>/dev/null | grep -q '"Matraca Dev"'; then
  IDENTITY="Matraca Dev"
  echo ">> Identidade encontrada no chaveiro: $IDENTITY"
else
  IDENTITY="-"
  AD_HOC=1
fi

if [[ "$AD_HOC" == "1" ]]; then
  cat <<'AVISO'

################################################################################
#                                                                              #
#   ATENCAO: ASSINATURA AD-HOC. LEIA, PORQUE ISSO VAI TE MORDER.               #
#                                                                              #
#   Nao existe certificado "Matraca Dev" neste chaveiro, entao o app esta'     #
#   sendo assinado com "-" (ad-hoc). O designated requirement de um binario     #
#   ad-hoc e' ancorado no cdhash — a impressao digital do codigo — e o cdhash   #
#   MUDA A CADA COMPILACAO.                                                    #
#                                                                              #
#   Consequencia pratica, e ela custa tempo:                                   #
#                                                                              #
#     * TODO `dotnet build` REVOGA a permissao de Acessibilidade ja' concedida. #
#     * E o macOS NAO VAI PERGUNTAR DE NOVO, porque a entrada obsoleta continua #
#       na lista de Ajustes do Sistema com o requisito antigo.                  #
#     * O sintoma e' AUSENCIA DE COMPORTAMENTO: o event tap simplesmente para   #
#       de ver teclas, sem erro nenhum, com o codigo certo.                     #
#                                                                              #
#   Depois de cada pack.sh, ANTES de rodar --tap ou --pipeline:                 #
#     Ajustes do Sistema -> Privacidade e Seguranca -> Acessibilidade           #
#     -> selecionar Matraca -> clicar no MENOS -> adicionar de novo.            #
#   (ou rodar: tccutil reset Accessibility io.github.fabriciocasali.matraca)    #
#                                                                              #
#   Para acabar com isso de vez, uma vez so':                                   #
#     Acesso as Chaves -> Assistente de Certificado -> Criar um Certificado     #
#       nome ................ Matraca Dev                                       #
#       tipo de identidade .. Autoassinado raiz                                 #
#       tipo de certificado . Assinatura de Codigo                              #
#       marcar "Permitir a substituicao dos padroes" e por validade 3650 dias   #
#       (o padrao de 365 significa refazer isto daqui a um ano)                 #
#       chaveiro ............ login                                             #
#   Feito isso, este script acha sozinho e para de gritar.                      #
#                                                                              #
################################################################################

AVISO
fi

# ---------------------------------------------------------------------------
# 2. Publicar
# ---------------------------------------------------------------------------
# O desenho escrevia `-o out`. Publicamos em bin/publish porque `bin/` ja' esta' no
# .gitignore da raiz: assim o `git status` do repositorio continua limpo sem precisar de
# uma terceira linha no .gitignore alem das duas combinadas.
PUB="bin/publish"

echo ">> publicando..."
rm -rf "$APP" "$PUB"
dotnet publish "$PROJ" -c Release -r osx-arm64 --self-contained false -o "$PUB"

# ---------------------------------------------------------------------------
# 3. Montar o bundle
#
#    TUDO dentro de Contents/MacOS/, ao lado do apphost. Nao e' a arrumacao canonica da
#    Apple (que quer dylibs em Contents/Frameworks/), mas e' a correta aqui: o
#    NativeLibraryLoader do Whisper.net sonda runtimes/{plataforma}-{arch}/ relativo ao
#    AppContext.BaseDirectory, que e' o diretorio do apphost. Espalhar quebra a sondagem.
#    A arrumacao canonica so' importa na notarizacao — Fase 5.
# ---------------------------------------------------------------------------
echo ">> montando $APP..."
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp Info.plist "$APP/Contents/"
cp -R "$PUB"/ "$APP/Contents/MacOS/"
chmod +x "$APP/Contents/MacOS/Matraca"

# --- DIVERGENCIA DO DESENHO, registrada aqui e no README -------------------
# O desenho previa que o TFM sem traco traria "runtimes/linux-*/ de brinde" e mandava
# NAO limpar, por ser ruido inofensivo. Ele nao e' inofensivo, e sao mais RIDs do que o
# previsto: a saida vem com linux-arm, linux-arm64, linux-x64, macos-x64, win-arm64,
# win-x64 e win-x86 alem do macos-arm64 que interessa.
#
# O `codesign --verify --strict` REPROVA o bundle por causa deles:
#     Matraca.app: code object is not signed at all
#     In subcomponent: .../Contents/MacOS/runtimes/linux-arm/libwhisper.so
# O codesign reconhece ELF e PE como codigo aninhado ao selar o bundle, e codigo
# aninhado sem assinatura invalida o selo. Assinar um .so de Linux nao e' possivel.
#
# Por isso o bundle leva SO' o runtimes/macos-arm64. A pasta out/ continua intacta com
# tudo (o desenho pediu p/ nao mexer no build), e o Whisper.net so' sonda o RID atual —
# nada aqui muda o que ele carrega. Sao ~13 MB a menos no .app, de brinde.
echo ">> podando runtimes que nao sao macos-arm64 (senao o --strict reprova)..."
find "$APP/Contents/MacOS/runtimes" -mindepth 1 -maxdepth 1 -type d \
     ! -name 'macos-arm64' -exec rm -rf {} +
ls "$APP/Contents/MacOS/runtimes"

# ---------------------------------------------------------------------------
# 4. Assinar DE DENTRO PARA FORA — dylibs primeiro, bundle por ultimo.
#
#    A inversao que explode: assinar o bundle antes das dylibs. O selo do bundle cobre os
#    recursos internos; assinar uma dylib DEPOIS invalida esse selo, e o `codesign
#    --verify` acusa "a sealed resource is missing or invalid". Como o app AS VEZES ainda
#    roda nesse estado, o erro passa despercebido ate o TCC comecar a se comportar de
#    forma erratica.
#
#    E no Apple Silicon isto nao e' higiene: no arm64 TODO codigo executavel precisa de
#    assinatura valida p/ o dyld carregar, dylib de terceiro inclusa. Sem o passo 4a o
#    app nao abre.
# ---------------------------------------------------------------------------
#    SEGUNDA DIVERGENCIA DO DESENHO: nao basta assinar as *.dylib.
#    O desenho mandava assinar so' `-name '*.dylib'`. Na pratica o codesign trata TODO
#    arquivo dentro de Contents/MacOS como codigo aninhado — .dll gerenciada (que e' PE),
#    .json, .pdb e ate' o shader ggml-metal.metal. Com qualquer um deles sem assinatura o
#    --strict reprova, um de cada vez, na cara-de-pau:
#        Matraca.app: code object is not signed at all
#        In subcomponent: .../Contents/MacOS/ggml-metal.metal
#    Entao a regra e': assina TUDO que esta' em Contents/MacOS, menos o apphost — esse e'
#    o executavel principal, e quem o assina e' a assinatura do bundle, no passo seguinte.
#    (Em arquivo que nao e' Mach-O o codesign guarda a assinatura em atributo estendido,
#    com.apple.cs.*. Funciona, mas xattr e' fragil: some num zip sem -X, num cp -R sem -p,
#    numa transferencia por rede. Anotado p/ a Fase 5, que empacota .dmg.)
echo ">> assinando o conteudo de Contents/MacOS (de dentro para fora)..."
SIGNED=0
while IFS= read -r -d '' f; do
  codesign -f -s "$IDENTITY" --timestamp=none "$f" >/dev/null 2>&1 \
    || { echo "!! falhou ao assinar: $f"; exit 1; }
  SIGNED=$((SIGNED + 1))
done < <(find "$APP/Contents/MacOS" -type f ! -name 'Matraca' -print0)
echo ">> $SIGNED arquivos assinados"

echo ">> assinando o bundle..."
codesign -f -s "$IDENTITY" --timestamp=none --identifier "$BUNDLE_ID" "$APP"

# ---------------------------------------------------------------------------
# 5. A prova
# ---------------------------------------------------------------------------
echo ""
echo ">> codesign --verify --strict --verbose=2"
codesign --verify --strict --verbose=2 "$APP"

echo ""
echo ">> codesign -d -r-   (o veredito do risco 6 sai desta linha)"
codesign -d -r- "$APP" 2>&1 || true

echo ""
if [[ "$AD_HOC" == "1" ]]; then
  echo ">> PRONTO — assinado AD-HOC. Reveja a permissao de Acessibilidade antes de rodar."
else
  echo ">> PRONTO — assinado com '$IDENTITY'."
fi
