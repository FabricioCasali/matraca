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

> **Leia a seção [Como lançar o `.app`](#como-lançar-o-app-e-por-que-isso-decide-tudo) antes de
> rodar qualquer perna que dependa de permissão.** O `run.sh` executa o binário direto, e por
> isso o TCC atribui microfone e Acessibilidade ao **terminal**, não ao Matraca. Medições
> feitas assim provam o código e **não** provam a permissão — foi o erro que quase deu a
> Fase 0 por encerrada cedo demais.

---

## O veredito, um risco por linha

| # | risco | veredito | evidência |
|---|---|---|---|
| 1 | event tap no F13, e o religamento no timeout | **PASSOU** | Com a Acessibilidade **do próprio `.app`**: `AXIsProcessTrusted = True` · `event tap ativo (mascara 0xC00)` · **F13 é o keycode 105 (0x69) nesta máquina**, igual à constante · o F13 foi engolido e não chegou ao TextEdit · o disparo rodou na thread `t4`, não na `t1` do tap (lei 5) · o F14 forçou o timeout e o religamento #1 veio **5,6 s** depois, com as teclas seguintes de volta no log. |
| 2 | captura de áudio por AudioQueue a 16 kHz | **PASSOU** | Com o microfone concedido **ao Matraca**, e não ao terminal: `capturadas 49664 amostras (3,10 s)`, RMS 0,01383 — taxa exata, três buffers de 100 ms sem estouro, e nada do fluxo de zeros que o macOS devolve quando nega. A linha nova na `TCC.db` é `kTCCServiceMicrophone \| io.github.fabriciocasali.matraca \| 0 \| 2`. |
| 3 | Whisper com Metal no arm64 | **PASSOU** | `whisper_backend_init_gpu: using MTL0 backend` · `ggml_metal_init: picking default device: Apple M4` · **160–230 ms** para 2,93 s de áudio. |
| 4 | injeção por CGEvent, com a marca da fonte | **PASSOU** | Sete ditados seguidos pelo `--pipeline`, e o contador de auto-reconhecimento subiu monotonicamente: **2 → 6 → 10 → 12 → 16 → 18 → 22**. A marca `0x4D545243` funciona: o tap vê o que o app injeta e ignora, sem reentrância. A injeção rodou na `t4`, o tap na `t1`. |
| 5 | janela WKWebView transparente e não-ativável | **PASSOU** | Conferido na tela, as quatro perguntas do roteiro: dá para ver o que está atrás, o clique atravessa para o app de baixo, a barra do canvas **anima**, e o HUD sobrevive à tela cheia. O `drawsBackground=NO` por KVC — que não é API pública e era o primeiro suspeito — funciona. |
| 6 | bundle `.app` com assinatura estável | **PASSOU** | Com o certificado `Matraca Dev` no chaveiro, **duas embalagens seguidas da mesma fonte deram o requisito idêntico**: `identifier "io.github.fabriciocasali.matraca" and certificate root = H"b57f05a8…"`. Ancorado no certificado, não no código. |

**Os seis fecharam. A Fase 0 acabou.** Nada nesta tabela foi arredondado para cima: cada
veredito tem número ou observação direta atrás dele.

Vale registrar como a tabela chegou aqui, porque o erro é fácil de repetir. Os riscos 1, 2 e 4
já tinham sido dados como passados uma vez, medindo com o `run.sh` — que executa o binário
direto. O TCC atribuiu tudo ao **iTerm2**, que já tinha microfone e Acessibilidade. A `TCC.db`
não tinha linha nenhuma do Matraca, e mesmo assim o áudio chegava e o tap subia. Só quando o
app foi lançado com identidade própria é que a verdade apareceu: `AXIsProcessTrusted = False`.
**Permissão emprestada mede o código e mente sobre a permissão.**

---

## Como lançar o `.app`, e por que isso decide tudo

O TCC não pergunta "quem é este processo?". Ele pergunta "quem é o **processo responsável**
por este processo?" — e a resposta é o pai da cadeia de lançamento. Rodar o binário a partir
de um terminal faz do terminal o responsável, e o app passa a viver das permissões dele.

As quatro formas, todas testadas nesta máquina:

| forma | sobe? | identidade | serve para quê |
|---|---|---|---|
| `./Matraca.app/Contents/MacOS/Matraca --perna` (o que o `run.sh` faz) | sim | **do terminal** | pernas que não dependem de TCC: `--alive`, `--hud`, `--whisper` |
| `open [-n] Matraca.app --args --perna`, em qualquer variante | **não** | — | nada; o processo não chega a nascer |
| duplo clique no Finder | sim | **própria** | só `--alive` — o Finder não passa argumento |
| **`launchctl submit -l <rótulo> -- <exe> --perna`** | sim | **própria** | **tudo que depende de permissão** |

O `open` foi testado com e sem `-n`, com e sem `--args`, com caminho relativo e absoluto, dentro
e fora de sandbox: em nenhuma delas o processo nasce — nem uma linha no log, nada em `pgrep`.
O `spctl -a -vv` responde `rejected, origin=Matraca Dev`. O duplo clique no Finder, por outro
lado, **funciona** — o macOS aceita o lançamento iniciado pelo usuário e recusa o programático.
Isso invalida o plano B que estava escrito no `run.sh` (`open -a ./Matraca.app --args --tap`).

**Não é preciso conta de desenvolvedor Apple para nada disso.** Chegou-se a suspeitar que sim;
o duplo clique no Finder desmentiu. A MT-009 segue sendo melhoria, não pré-requisito.

### A armadilha do `launchctl submit`

Ele tem **`KeepAlive` ligado por padrão**. Um job que termina é relançado em segundos, para
sempre. Com o `--inject` isso significa o app digitando 225 caracteres na janela em foco a cada
~10 segundos. Encerre **sempre** com `launchctl remove <rótulo>`, nunca com `pkill`: matar o
processo no meio de uma injeção corta o texto e produz um falso sintoma de truncamento — o que
aconteceu aqui e custou uma rodada de diagnóstico errada.

---

## Risco 3 — o único que fechou de primeira, e os números

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

**Carregar o modelo é lento, e o cache de shaders NÃO é confiável:**

| | tempo |
|---|---|
| 1ª vez na vida da máquina (compilação dos kernels Metal) | **7.232 ms** |
| uma medição seguinte, com a biblioteca em cache | **123 ms** |
| **o `--pipeline`, muito depois, na mesma máquina** | **6.500 ms** |

A terceira linha é a que importa, e ela corrige a conclusão anterior. A leitura de 123 ms
sugeria "só a primeira instalação é lenta"; a de 6.500 ms — `ggml_metal_library_init: loaded in
6.283 sec`, com os mesmos `compile_pipeline` de sempre — mostra que **o cache pode não estar
lá quando o app subir**. A Fase 2 tem de tratar 6–7 s como o custo possível de *toda*
inicialização, não como pedágio único: carregar o modelo na inicialização e nunca sob demanda,
e ter algo na tela enquanto isso acontece.

Pior: o backend Metal é **criado e destruído a cada ditado**. O log do `--pipeline` mostra
`whisper_backend_init_gpu` → `ggml_metal_init: allocating` → `ggml_metal_free: deallocating` em
todas as sete rodadas, e em algumas ele ainda recompila pipelines no meio. É o que explica a
transcrição variar de **175 ms a 1.098 ms** para áudios do mesmo tamanho. Ver a MT-016.

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
| `kCGSessionEventTap` | 1 | **sim** — o tap subiu e viu as teclas |
| `kCGHeadInsertEventTap` / `kCGEventTapOptionDefault` | 0 / 0 | **sim** — idem, e o F13 foi engolido antes de chegar ao app da frente |
| máscara keyDown\|keyUp | `0xC00` | **sim** |
| `kCGEventTapDisabledByTimeout` | `0xFFFFFFFE` | **sim** — o F14 forçou, o callback reconheceu e religou |
| `kCGEventTapDisabledByUserInput` | `0xFFFFFFFF` | por provar (nunca ocorreu) |
| `kCGEventSourceUserData` | 42 | **sim** — o contador de auto-reconhecimento do `--pipeline` chegou a 22 |
| `kVK_F13` / `kVK_F14` | `0x69` / `0x6B` | **sim, confirmadas no teclado dele**: `keycode=105 (0x69)` e `keycode=107 (0x6B)` |
| `NSWindowStyleMaskBorderless` / `NSBackingStoreBuffered` / `NSFloatingWindowLevel` | 0 / 2 / 3 | **sim** — a janela subiu |
| `NSApplicationActivationPolicyAccessory` | 1 | **sim** |
| collectionBehavior (allSpaces\|stationary\|fullScreenAuxiliary) | `1\|16\|256` = 273 | **sim** — o HUD continuou visível em tela cheia |
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

## O roteiro, e o que ele devolveu

Os seis passos foram executados. **Não é preciso mexer na Acessibilidade entre as rodadas** —
o certificado fechou o risco 6, e o requisito não muda mais quando se recompila. Conceder uma
vez basta, e a concessão sobrevive a todo `pack.sh`. (Se algum dia o certificado for embora do
chaveiro, o requisito muda junto e o sintoma volta: `tccutil reset Accessibility
io.github.fabriciocasali.matraca` e conceder de novo.)

Concessões dadas ao `.app`, uma vez cada: **microfone** pelo diálogo do sistema, durante uma
rodada de `--audio` lançada por `launchctl submit`; **Acessibilidade** à mão em Ajustes →
Privacidade e Segurança.

| passo | resultado |
|---|---|
| 1. `--tap`, keycode do F13 e engolir o evento | `keycode=105 (0x69)`, engolido, handler na `t4` |
| 2. `--tap`, F14 força o timeout | religamento #1 em 5,6 s, teclas de volta |
| 3. `--audio`, RMS > 0 | 49664 amostras, 3,10 s, RMS 0,01383 |
| 4. `--hud`, as quatro perguntas na tela | quatro sins |
| 5. `--inject`, contador acima de zero | **falhou por defeito do spike** — ver abaixo |
| 6. `--pipeline`, o critério de aceite | ditou e injetou sete vezes; contador chegou a 22 |

### O passo 5 mente, e o defeito é nosso

A perna `--inject` sempre reporta `o tap viu 0 eventos com a NOSSA marca`, e a mensagem de erro
manda desconfiar da marca da fonte. **A marca está certa; a perna é que está errada.**

O tap é anexado à run loop **principal** (`Tap/KeyTap.cs:84`, `CFRunLoopGetMain()`). Mas o
`RunInject` nunca entra numa run loop: faz `Thread.Sleep(5000)`, injeta e lê o contador, tudo
na thread principal. O callback do tap não tem como disparar — o zero é garantido por
construção e não diz nada sobre a marca. Compare com `RunTap` e `RunPipeline`, que terminam em
`CFRunLoopRun()`, e com `RunHud`, que termina em `HudWindow.RunApp()`: nessas, o contador sobe.

Quem for validar a marca da fonte, use o `--pipeline`. Se o `--inject` sobreviver à Fase 2,
consertar é dar a ele uma run loop de verdade.

---

## O que a Fase 2 herda como trabalho

Quatro coisas que o `--pipeline` expôs. As três primeiras têm causa identificada.

**1. O app rouba foco, e isso viola a lei 4.** `setActivationPolicy:` com
`NSApplicationActivationPolicyAccessory` é chamado **só** em `Hud/HudWindow.cs:54`. Qualquer
perna que não passe pelo HUD sobe com a política padrão e se comporta como app comum: ao ditar,
a janela de destino perde o foco e o texto se perde. É defeito do spike, não do macOS — mas
vira requisito explícito: a política de ativação é do **app**, não da janela. Ver MT-015.

**2. O backend Metal nasce e morre a cada ditado.** Ver a seção do risco 3. Ver MT-016.

**3. Não há detecção de fala.** `RecordSeconds = 3.0`, fixo, sem VAD: a gravação começa no
instante do atalho e termina 3 s depois, cortando a frase no meio se o usuário falar mais. Não
é bug do spike, é o spike sendo spike — mas confirma que os quatro modos e o VAD da Fase 2 não
são luxo.

**4. A lacuna entre o que o app entrega e o que o alvo recebe.** O log diz `digitados 39
caracteres em 4 eventos, 10 ms`; na tela, o texto chega lento e truncado. Como a injeção roda
na `t4`, fora da thread do tap, **não é** a violação da lei 5. A suspeita é o ritmo — blocos de
20 unidades UTF-16 com pausa de 2 ms, rápido demais para o alvo digerir — mas isso é hipótese,
não medição. Precisa de investigação própria. Ver MT-017.

### Coisas menores que o dia deixou

**O `run.sh` não sobe nesta máquina sem ajuda.** O perfil do shell exporta `DOTNET_ROOT`
apontando para o .NET do Homebrew, que só tem o runtime 10; o spike é `net8.0` e o 8.0.18 mora
em `/usr/local/share/dotnet`. Sem o ajuste, o apphost morre com
`You must install or update .NET to run this application` antes de escrever qualquer log.

**A transcrição do `ggml-base` erra muito em pt-BR** neste teste: três segundos de fala viraram
`"[MÚSICA DE FUNDO]"` duas vezes, e `"Fazendo um teste de gravação de álcool."` uma. Não é
risco de viabilidade — o caminho funciona —, mas é o tamanho de modelo em jogo quando a Fase 2
for escolher o padrão.

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
