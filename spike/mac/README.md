# Spike do macOS — MT-002, Fase 0

App descartável que responde, uma perna por vez, se o Matraca 2.0 tem chão no macOS.
**É este arquivo que a Fase 2 lê, não o código.**

Máquina da apuração: Apple **M4**, macOS **26.5.1** (build 25F80), .NET SDK 10.0.100-preview.6
com o projeto em `net8.0`.

```bash
bash spike/mac/pack.sh                 # compila, monta o .app e assina
bash spike/mac/run.sh --tap            # pack + executa a perna
MATRACA_SKIP_PACK=1 bash spike/mac/run.sh --tap   # roda sem recompilar (ver risco 6)
```

Pernas: `--alive` `--tap` `--audio` `--whisper [arquivo.wav]` `--inject` `--hud` `--pipeline`.
O log vai para o stdout **e** para `~/Library/Application Support/Matraca/spike.log`.

---

## O veredito, um risco por linha

| # | risco | veredito | evidência |
|---|---|---|---|
| 1 | event tap no F13, e o religamento no timeout | **por provar** | `CGEventTapCreate devolveu NULL` — falta Acessibilidade concedida à mão. Tudo antes disso roda: frameworks carregam, `AXIsProcessTrusted = False` é lido corretamente. |
| 2 | captura de áudio por AudioQueue a 16 kHz | **por provar** | `AudioQueueNewInput`/`AllocateBuffer`/`Start` retornam sem erro; o processo então **fica parado** — consistente com o TCC esperando alguém responder ao diálogo do microfone. |
| 3 | Whisper com Metal no arm64 | **PASSOU** | `whisper_backend_init_gpu: using MTL0 backend` · `ggml_metal_init: picking default device: Apple M4` · **160–230 ms** para 2,93 s de áudio. |
| 4 | injeção por CGEvent, com a marca da fonte | **por provar** | `CGEventPost` exige a mesma Acessibilidade do risco 1; sem ela o post é engolido em silêncio. Código completo, nunca exercitado. |
| 5 | janela WKWebView transparente e não-ativável | **por provar** (com meio caminho andado) | A janela **sobe sem erro**: subclasse registrada, `WKWebView` criado, KVC aplicado, `orderFrontRegardless` chamado, `[NSApp run]` de pé por 7 s. Transparência, animação e clique-atravessa exigem olho na tela. |
| 6 | bundle `.app` com assinatura estável | **PASSOU** | Com o certificado `Matraca Dev` no chaveiro, **duas embalagens seguidas da mesma fonte deram o requisito idêntico**: `identifier "io.github.fabriciocasali.matraca" and certificate root = H"b57f05a8…"`. Ancorado no certificado, não no código. |

**Por provar** aqui quer dizer *escrito e compilando, nunca executado com sucesso* — e não
"deve funcionar". Quatro das seis pernas dependem de permissão que só é concedida à mão, na
frente da máquina. Nada nesta tabela foi arredondado para cima.

---

## Risco 3 — o único que fechou, e os números

A incerteza número 1 do desenho era se o ggml *elege* o Metal em runtime. **Elege.** As linhas
que sustentam isso, tiradas do `spike.log`:

```
whisper_init_with_params_no_state: use gpu    = 1
ggml_metal_device_init: simdgroup reduction   = true
ggml_metal_device_init: simdgroup matrix mul. = true
whisper_backend_init_gpu: device 0: MTL0 (type: 1)
whisper_backend_init_gpu: found GPU device 0: MTL0 (type: 1, cnt: 0)
whisper_backend_init_gpu: using MTL0 backend
ggml_metal_init: found device: Apple M4
ggml_metal_init: picking default device: Apple M4
whisper_backend_init: using BLAS backend
```

Repare na última linha: o BLAS **também** sobe. Não é contradição — o Metal atende a GPU e o
BLAS (via Accelerate) atende o que sobra na CPU. Quem faz o trabalho pesado é o `MTL0`.

**Latência**, `ggml-base`, áudio sintetizado pelo `say` em pt-BR:

| áudio | 1ª execução | 2ª | 3ª |
|---|---|---|---|
| 2,93 s | 230 ms | 173 ms | **160 ms** |
| 6,15 s | 195 ms (a quente) | — | — |

Comparação que o plano pediu: a 4070 Ti do README faz ~0,3 s. **O M4 com Metal está na mesma
faixa, e no melhor caso abaixo.** Não há motivo de desempenho para tratar o Mac como o lado
pobre.

**Carregar o modelo tem dois regimes, e a diferença é grande:**

| | tempo |
|---|---|
| 1ª vez na vida da máquina (compilação dos kernels Metal) | **7.232 ms** |
| depois (biblioteca de shaders em cache) | **123 ms** |

Os 7 s aparecem como dezenas de linhas `ggml_metal_library_compile_pipeline: compiling
pipeline: ...`. **A Fase 2 precisa saber disso:** o primeiro ditado depois de instalar vai
parecer travado se o modelo for carregado sob demanda. Carregar na inicialização, ou avisar.

Download do `ggml-base.bin`: 141 MB em 68,6 s, para
`~/Library/Application Support/Matraca/models/` — que é a decisão de `AppPaths` que a Fase 1
formaliza, agora exercitada.

---

## Onde a realidade divergiu do desenho

São quatro, e a primeira é a que mais importa.

### 1. "Diff zero fora de `spike/`" não se sustenta — e quebra o build do Windows

O desenho chamava o diff zero de "garantia forte de que o lado Windows não pode quebrar". É o
contrário: **é justamente o diff zero que quebra o Windows.** O `Matraca.csproj` mora na raiz
e o glob padrão do SDK varre `**/*.cs` a partir dali, então ele passa a compilar os fontes
**e o `obj/`** do spike:

```
obj/Debug/net8.0-windows/Matraca.AssemblyInfo.cs(20,12): error CS0579:
    Duplicar atributo "System.Reflection.AssemblyTitleAttribute"
```

Oito erros desses. Os globs padrão só excluem o `bin/` e o `obj/` **do próprio projeto**,
nunca os de um projeto aninhado. Não há como corrigir de dentro de `spike/`: MSBuild não
deixa um subdiretório influenciar o glob do projeto de cima.

**O que foi feito:** um `ItemGroup` com `Compile/None/EmbeddedResource Remove="spike/**"` no
`Matraca.csproj`, em **commit separado** (`MT-002 Excluir spike/** dos globs`), para ser
revertível numa linha. **Isso contraria a instrução explícita do desenho de não tocar no
`Matraca.csproj`** e precisa do aval do Fabricio. A alternativa era commitar o lado Windows
quebrado.

### 2. `codesign --verify --strict` exige assinatura em **tudo** dentro de `Contents/MacOS`

O desenho mandava assinar `-name '*.dylib'`. Não basta. O `codesign` trata todo arquivo em
`Contents/MacOS` como código aninhado — `.dll` gerenciada (que é PE), `.json`, `.pdb`, e até o
shader `ggml-metal.metal`. Cada um reprova, um de cada vez:

```
Matraca.app: code object is not signed at all
In subcomponent: .../Contents/MacOS/ggml-metal.metal
```

O `pack.sh` agora assina **todo** arquivo de `Contents/MacOS` menos o apphost (esse é coberto
pela assinatura do bundle). São 17 arquivos.

**Detalhe para a Fase 5:** em arquivo que não é Mach-O o `codesign` guarda a assinatura num
**atributo estendido** (`com.apple.cs.CodeDirectory` e irmãos). Funciona, mas xattr é frágil —
some num `zip` sem `-X`, num `cp` sem `-p`, numa transferência por rede. Quem for montar o
`.dmg` precisa saber disso antes, não depois.

### 3. Não são só os `runtimes/linux-*` "de brinde"

O desenho previa Linux como ruído inofensivo. Vêm **sete** RIDs alheios — `linux-arm`,
`linux-arm64`, `linux-x64`, `macos-x64`, `win-arm64`, `win-x64`, `win-x86` — e eles **não** são
inofensivos: os `.so` de Linux reprovam o `--strict` pelo mesmo motivo do item 2, e assinar um
ELF de Linux dentro de um `.app` de macOS é absurdo. O `pack.sh` poda tudo que não é
`macos-arm64` **ao montar o bundle**; a pasta de publish continua intacta. São ~13 MB a menos.

### 4. O publish saiu de `out/` para `bin/publish`

O `-o out` do desenho deixaria uma pasta não ignorada suja no `git status`. Publicar em
`bin/publish` reaproveita o `bin/` que já está no `.gitignore` da raiz, e assim o `.gitignore`
ficou com **exatamente as duas linhas** combinadas.

---

## Coisas que valem para a Fase 2

**O `AudioQueueStart` bloqueia esperando a decisão do microfone.** Foi o que aconteceu aqui: o
log para em `gravando 3,0 s...` e o processo não volta. Não é travamento — é o TCC esperando
alguém responder. A Fase 2 não pode chamar isso de uma thread que precise continuar
respondendo.

**A permissão não vale para o processo que já está rodando.** Conceder Acessibilidade com o
app aberto não ressuscita o tap: tem de rodar de novo. O spike avisa isso no log.

**Os `.metal` e o `runtimes/macos-arm64/` precisam estar ao lado do apphost.** O
`NativeLibraryLoader` do Whisper.net sonda relativo ao `AppContext.BaseDirectory`. Espalhar
para `Contents/Frameworks/` (a arrumação canônica da Apple) quebra a sondagem. Isso só vira
problema na notarização — Fase 5.

**`MATRACA_SKIP_PACK=1` deixou de ser necessário**, mas a razão de ele existir vale para a
Fase 2 e fica registrada. Assinatura ad-hoc ancora o *designated requirement* no `cdhash` —
a impressão digital do próprio código —, e recompilar muda esse número. Duas embalagens **da
mesma fonte, sem nenhuma alteração**, produziram requisitos diferentes:

```
# designated => cdhash H"0d0080676c07389cb0e448ad17b5c4817f5abc40"
# designated => cdhash H"b350693fe72afbd2a6aba4e0c7904aaf39825614"
```

Toda vez que esse número muda, a concessão de Acessibilidade guardada pelo TCC deixa de casar
com o app — **e o macOS não reprompta**, porque a entrada obsoleta continua na lista. O
sintoma é ausência de comportamento: o tap simplesmente para de ver teclas, com o código
certo. Com o `Matraca Dev` o requisito passou a ser ancorado no certificado e parou de mudar:

```
# designated => identifier "io.github.fabriciocasali.matraca" and
#               certificate root = H"b57f05a8a36495042b6da909802ad5896192cae7"
```

O mesmo mecanismo vale para o **microfone** — o TCC guarda requisito de código para todo
serviço, não só para a Acessibilidade.

### Constantes: todas bateram

Nenhuma das constantes do desenho precisou de correção. As que ainda **não** foram exercidas
contra a realidade estão marcadas:

| constante | valor | conferida? |
|---|---|---|
| `kCGSessionEventTap` | 1 | por provar |
| `kCGHeadInsertEventTap` / `kCGEventTapOptionDefault` | 0 / 0 | por provar |
| máscara keyDown\|keyUp | `0xC00` | por provar |
| `kCGEventTapDisabledByTimeout` / `ByUserInput` | `0xFFFFFFFE` / `0xFFFFFFFF` | por provar |
| `kCGEventSourceUserData` | 42 | por provar |
| `kVK_F13` / `kVK_F14` | `0x69` / `0x6B` | **por confirmar no teclado dele** — o spike loga o keycode de toda tecla justamente para isso |
| `NSWindowStyleMaskBorderless` / `NSBackingStoreBuffered` / `NSFloatingWindowLevel` | 0 / 2 / 3 | **sim** — a janela subiu |
| `NSApplicationActivationPolicyAccessory` | 1 | **sim** |
| collectionBehavior (allSpaces\|stationary\|fullScreenAuxiliary) | `1\|16\|256` = 273 | **sim** (aceito; o efeito é por ver) |
| `kAudioFormatLinearPCM` / flags PCM16 | `0x6C70636D` / `0xC` | **sim** — o AudioQueue aceitou o formato |
| marca da fonte (`InjectionTag`) | `0x4D545243` "MTRC" | igual à do Windows |

### `_stret` e `_fpret` no arm64

Não existem, e o código **não** tem condicional de arquitetura para isso. Há um único
`objc_msgSend`: a ABI já devolve struct por `x8` e float por `v0`. Se o x86_64 voltar à mesa um
dia, aí sim: em x86_64 uma `CGRect` de retorno exige `objc_msgSend_stret`, e a
`ObjC.SendRect` de hoje passaria lixo.

### A string de tipos `"B@:"`

O `canBecomeKeyWindow` foi registrado com `"B@:"` e a classe registra sem erro. Se o retorno
booleano se comportar de forma estranha quando o Fabricio testar, trocar para `"c@:"` é
experimento de cinco minutos — **não trate como bloqueio, anote o resultado aqui.**

---

## O roteiro do Fabricio (o que falta, e é o que fecha o cartão)

**Não é mais preciso mexer na Acessibilidade entre as rodadas** — o certificado fechou o
risco 6, e o requisito não muda mais quando se recompila. Conceder uma vez basta, e a
concessão sobrevive a todo `pack.sh`. (Se algum dia o certificado for embora do chaveiro, o
requisito muda junto e o sintoma volta: `tccutil reset Accessibility
io.github.fabriciocasali.matraca` e conceder de novo.)

1. `bash spike/mac/run.sh --tap` → conceder Acessibilidade, **rodar de novo**. Apertar F13 e
   outras teclas: conferir no log **o keycode do F13 nesta máquina**. Com o TextEdit na
   frente, o F13 não pode digitar nada lá.
2. Ainda em `--tap`: apertar **F14** e depois qualquer tecla. Tem de aparecer o WARN de
   timeout, o religamento, e a tecla seguinte de volta no log.
3. `MATRACA_SKIP_PACK=1 bash spike/mac/run.sh --audio` → conceder o microfone, falar 3 s.
   RMS tem de ser > 0.
4. `--hud` → dá para ver o que está atrás? o clique atravessa? **a barra do canvas se mexe?**
   entra em tela cheia e o HUD continua lá?
5. `--inject` → o texto chega inteiro e **com os espaços** no TextEdit, no Terminal e num app
   Electron. E o log tem de dizer que o tap viu **mais de zero** eventos com a nossa marca —
   se for zero, o auto-reconhecimento nunca foi exercitado.
6. `--pipeline` → o critério de aceite: ditar nos três alvos, texto inteiro e com espaços.

### O certificado `Matraca Dev` — feito em 17/08/2026

Gerado pela linha de comando em vez do Assistente de Certificado, para ficar reproduzível.
Impressão digital `B57F05A8A36495042B6DA909802AD5896192CAE7`, válido até **14/08/2036**.
O `pack.sh` o encontrou sozinho na primeira tentativa — **nada precisou mudar no script**,
como o comentário dele em `pack.sh:23-26` previa.

```bash
cat > matraca-dev.cnf <<'EOF'
[ req ]
distinguished_name = dn
x509_extensions    = v3
prompt             = no
default_md         = sha256
[ dn ]
CN = Matraca Dev
O  = Matraca
C  = BR
[ v3 ]
basicConstraints     = critical, CA:true
keyUsage             = critical, digitalSignature, keyCertSign
extendedKeyUsage     = critical, codeSigning
subjectKeyIdentifier = hash
EOF

openssl req -x509 -newkey rsa:2048 -nodes -sha256 -days 3650 \
  -config matraca-dev.cnf -keyout matraca-dev.key.pem -out matraca-dev.cert.pem
openssl pkcs12 -export -legacy -inkey matraca-dev.key.pem -in matraca-dev.cert.pem \
  -name "Matraca Dev" -out matraca-dev.p12 -passout pass:matraca
security import matraca-dev.p12 -k "$HOME/Library/Keychains/login.keychain-db" \
  -P matraca -T /usr/bin/codesign -T /usr/bin/security
security add-trusted-cert -r trustRoot -p codeSign matraca-dev.cert.pem   # pede a senha
```

**Duas armadilhas, as duas encontradas na prática:**

1. **Importar não basta.** Sem o `add-trusted-cert`, o `security find-identity -v -p
   codesigning` devolve `0 valid identities found`, e sem o `-v` explica por quê:
   `CSSMERR_TP_NOT_TRUSTED`. Certificado autoassinado nasce sem confiança para assinar
   código. Pela GUI o efeito é o mesmo: dois cliques no certificado → **Confiar** →
   *Assinatura de Código: Sempre Confiar*.
2. **O `openssl` do macOS é LibreSSL**, e não tem `-ext` no `x509` nem, dependendo da
   versão, o `-legacy` no `pkcs12`. Conferir extensões é `openssl x509 -text | grep`.

Na primeira assinatura o macOS pergunta se o `codesign` pode usar a chave — **"Sempre
Permitir"**, senão ele repete a pergunta nos 18 arquivos.

**Se a chave for perdida** (chaveiro apagado, máquina nova), gerar de novo produz um
certificado com hash **diferente**, logo um requisito diferente, logo a concessão de
Acessibilidade quebra de novo. É reset de TCC e conceder outra vez — dois minutos, mas vale
guardar o `.p12` se quiser evitar até isso.

---

## O que morre com o spike, e o que sobrevive

| sobrevive → `Matraca.Mac/Platform/Interop/` na Fase 2 | morre |
|---|---|
| `Interop/` inteiro: o padrão de uma `DllImport` por assinatura, classes/seletores cacheados, `ObjCClassBuilder`, `MainThread`, os structs de geometria, as tabelas de constantes | `Program.cs` e o despacho por argumento |
| `Info.plist` | os 3 s fixos, o F13 no código |
| `pack.sh` (com as duas correções de assinatura) | `SpikeTranscriber`, `SpikeModel`, `SpikeLog`, `WavFile` |
| este `README.md`, com os números | `hud.html` |

**O spike não tem** interface nem abstração, VAD, modos, `appsettings.json`, `NSStatusItem`,
`AXUIElement`, ponte com a web, pós-processamento, histórico, `.dmg` ou projeto de teste. Isso
é a Fase 1 em diante, por desenho.

`dotnet test` continua **não existindo** neste repositório — hoje ele encontra o
`Matraca.csproj` e falha com `NETSDK1100` (falta `EnableWindowsTargeting`). Esperado até a
MT-003.
