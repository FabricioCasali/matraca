# MT-039 — probe isolado de atualização Mac

O atualizador foi renumerado de MT-038 para MT-039 ao integrar com o remoto,
onde MT-038 já identificava a internacionalização. Os nomes MT038 nos probes,
fixtures e evidências anteriores são identificadores de teste preservados.

**Entrega de fontes e roteiro. Prova nativa ainda pendente.** Preparado em Windows;
nenhuma atualização Mac, assinatura local, execução AppKit ou relaunch foi executada
nesta entrega. Não integrar em produção antes do gate de atualização real **nos dois
hosts** (Mac e Windows).

## O que este probe exercita

`apphost .NET 8 → P/Invoke/delegate C → shim Objective-C → AppKit + Sparkle 2.8.0`.
O executável principal continua sendo .NET, self-contained, sem MAUI/Xamarin e sem
alterar frameworks do Matraca. O shim executa `NSApplication.run` na thread principal;
o callback volta ao .NET para controlar uma carga **simulada** e autorizar encerramento.
As versões 1.0 e 2.0 têm assemblies gerenciados distintos e o log registra
`managed-major=1` / `managed-major=2` retornado pelo callback.

O build produz dois ZIPs e uma instalação v1 com o mesmo identificador exclusivo por
execução: `io.github.fabriciocasali.matraca.updateprobe.r<uuid>`.
Esse identificador é diferente do Matraca real. Cada novo build gera outro UUID e
outra chave de teste. Os dois bundles de uma execução precisam compartilhar o ID e
a chave pública para formar uma atualização, não dois aplicativos independentes.

Tudo gerado fica em `.work/` desta pasta. O runtime recusa iniciar fora do caminho
`<RUN>/installed/MacUpdateProbe.app`. Não copiar para `/Applications`, não lançar os
bundles de staging `v1.0/` e `v2.0/`. O servidor expõe apenas `good/` ou `invalid/`,
em **127.0.0.1**, nunca a raiz do projeto. Preferências/cache do próprio Sparkle podem
ser gravados pelo macOS sob o ID exclusivo do probe; não são dados do Matraca.

### Decisão de download e confirmação

Na API consultada, `SUAutomaticallyUpdate=YES` não é “só baixar”: pode preparar a
instalação silenciosa e instalar ao encerrar. `willInstallUpdateOnQuit` e segurar o
relaunch não eliminam esse comportamento. Além disso, responder `Dismiss` no estágio
`Installing` também pode manter a instalação ao sair.

Por isso, neste probe:

1. `SUEnableAutomaticChecks=YES`, `SUAutomaticallyUpdate=NO`,
   `SUAllowsAutomaticUpdates=NO`, profiling desligado. Há uma consulta imediata no
   lançamento, pela exceção documentada, e depois o agendador normal do Sparkle.
2. `SPUUpdater` com `SPUUserDriver` próprio retém a resposta da oferta no estágio
   `NotDownloaded`. O download antecipado usa `NSURLSessionDownloadTask`, sem
   extração, sem validar/instalar código, e sem mostrar diálogo automático.
3. O menu informa **baixado, ainda não validado**. “Confirmar instalação…” abre um
   `NSAlert` somente por ação do operador e somente sem carga pendente. “Cancelar”
   é a ação padrão do diálogo. Rejeição descarta o download e responde `Dismiss`
   ainda em `NotDownloaded`. Um estado inesperado é cancelado com `Skip`.
4. Após a confirmação, o gate .NET é revalidado e adquirido atomicamente: nenhuma
   nova captura simulada entra. Só então é respondido `Install` ao Sparkle.
5. **Sparkle baixa novamente**, valida Ed25519 antes da extração
   (`SUVerifyUpdateBeforeExtraction=YES`) e executa instalação/relaunch. A autorização
   vale para esse ciclo, incluindo instalação ao sair após confirmar.

Não foi encontrada na API pública 2.8.0 consultada uma operação para entregar esse
arquivo já baixado ao instalador. O download duplicado é uma limitação deliberada
**deste ensaio**, não uma solução de cache aprovada para produção. Não usamos APIs
privadas nem fingimos que o primeiro download é um download validado pelo Sparkle.

## Pré-requisitos no Mac

- macOS **15+ arm64**, alinhado ao shim/empacotamento atual do Matraca.
- SDK .NET capaz de compilar `net8.0` e publicar `osx-arm64` (preferir SDK 8).
- Xcode Command Line Tools já disponíveis: `xcrun clang`, `swiftc`, SDK AppKit/CryptoKit.
- Python **3.9+** e ferramentas nativas `codesign`, `security`, `ditto`, `tar`, `file`,
  `otool`, `plutil`.
- Identidade local de code signing **Matraca Dev**, já instalada e utilizável.

O harness não instala dependências nem cria certificado. Falta de ferramenta ou
identidade interrompe o build. Não usa assinatura automática Xcode, Apple Development,
Developer ID, notarização ou conta Apple paga. `codesign` usa `Matraca Dev`,
`--timestamp=none`, flags `0` (sem Hardened Runtime/Library Validation), assina de
dentro para fora e verifica com `--deep --strict`. Os helpers Sparkle também são
reassinados. Essa combinação local ainda precisa ser exercitada no Mac.

### Integridade e chaves

Distribuição fixa:

```text
https://github.com/sparkle-project/Sparkle/releases/download/2.8.0/Sparkle-2.8.0.tar.xz
SHA-256: fd5681ee92bf238aaac2d08214ceaf0cc8976e452d7f882d80bac1e61581f3b1
Asset oficial: 293691465
```

Hash obtido no metadata oficial da release e conferido sobre o arquivo baixado nesta
sessão. O harness verifica inclusive downloads em cache **antes de extrair/executar**.
Uma divergência falha; não há opção para ignorar o pin. Framework/helpers são copiados
por `ditto` com os symlinks; a licença permanece na distribuição baixada. Não há
dependência de `latest`, SPM, CocoaPods ou instalação global do Sparkle.

`EphemeralSigner.swift` cria um seed Ed25519 de TESTE em memória com CryptoKit. Envia-o
por pipe anônimo ao `sign_update --ed-key-file - -p`, da distribuição verificada,
e confere a assinatura resultante com a chave pública CryptoKit. Apenas chave pública
e assinaturas saem no protocolo stdout. Não usa `generate_keys` (que persistiria no
Keychain), exportação de privada, argumento de comando ou variável de ambiente com
segredo. O processo termina após assinar os dois ZIPs; não há arquivo de chave privada.
Não anexar debugger, habilitar dumps ou instrumentar o pipe privado.

As chaves são descartáveis: um novo build não atualiza um RUN antigo. Chaves duráveis
do produto continuam previstas no **1Password**, via referência `op://` a definir na
integração; nenhum item/referência fictícia ou segredo de produção foi criado aqui.

## Executar no Mac

Partindo da raiz do clone Mac (substitua os caminhos pelos caminhos absolutos locais):

```bash
python3 -B tests/MacUpdateProbe/probe.py build
```

O build imprime `RUN=/caminho/absoluto/.../.work/runs/<id>`. Guarde esse caminho.
O restore/publicação usa cache NuGet e CLI home locais em `.work/`; não publica,
instala ou abre aplicativos. Permita o uso da identidade local se o Keychain pedir.
`--port 18739` pode ser definido **no build** se a porta padrão estiver ocupada.

```bash
RUN='/caminho/absoluto/impresso/pelo/build'
python3 -B tests/MacUpdateProbe/probe.py verify "$RUN" --version 1.0
python3 -B tests/MacUpdateProbe/probe.py serve "$RUN" --invalid
```

Mantenha o servidor nesse terminal. Em outro terminal ou Finder, localize a instalação:

```bash
open -R "$RUN/installed/MacUpdateProbe.app"
```

Isso revela o bundle no Finder, não lança o executável. Abra o bundle pelo Finder e
registre o resultado. O projeto já registra restrição de `open` para sua assinatura
local; não presumir que `open -a` ou o relaunch do Sparkle terá o mesmo resultado de
duplo clique. Não remover quarentena ou desabilitar Gatekeeper para produzir um PASS.
Se a política do sistema impedir lançamento, registrar o bloqueio e o caminho usado.

### Roteiro obrigatório

Use o menu **MT038 v1.0**. O item superior e `<RUN>/events.log` mostram o estado.
Nenhum microfone ou texto real é capturado/injetado por este probe.

| Caso | Ação e resultado que precisa ser observado |
|---|---|
| Carga .NET real | v1 abre pelo apphost; log tem `launch` e `managed-major=1`; menu responde às ações que voltam ao gerenciado. |
| Consulta/download automático | Sem clicar em consultar, servidor recebe GET de `appcast.xml` e `update.zip`; menu informa download antecipado concluído; nenhum reinício/diálogo automático. |
| Captura ativa | “1. Simular captura”, tentar confirmar e sair: ambos bloqueados, sem diálogo de confirmação. |
| Drenagem | “2. Parar captura”: confirmar e sair continuam bloqueados, inclusive após esperar mais de cinco segundos. Só “3. Concluir entrega” libera. |
| ZIP inválido | Servidor `--invalid` fornece ZIP com um byte alterado, mesmo comprimento e assinatura do original. Após drenar, confirmar. Deve ocorrer `update-error`/`abort` de assinatura no Sparkle, **antes de extração**; v1 continua utilizável. Ausência de atualização ou erro HTTP não contam como prova criptográfica. |
| Recuperação | Após erro, iniciar/parar/drenar nova simulação. Fechar o probe. `verify --version 1.0` deve passar. |
| Rejeição | Encerrar servidor inválido com Ctrl+C; iniciar `serve "$RUN"` sem `--invalid`. Reabrir v1, aguardar download, rejeitar a oferta, sair. `verify --version 1.0` deve passar; nada instalado ao sair. |
| Cancelamento da confirmação | Reabrir, aguardar, clicar confirmar e escolher Cancelar. Sair; versão continua 1.0. Uma consulta manual também pode ser usada para repetir a oferta sem reiniciar. |
| Confirmação positiva | Reabrir com servidor válido, aguardar download, confirmar sem carga. Deve haver segundo GET do ZIP feito pelo Sparkle, validação, troca do bundle e **reabertura automática** em v2.0. Não lançar v2 manualmente para completar esse caso. |
| Nova versão | `events.log` registra `install-confirmed`, término de v1 e novo `launch` com outro PID, `v=2.0` e `managed-major=2`. Menu mostra v2.0, carga simulada responde e `verify --version 2.0` passa. |
| Dados | `verify` compara nomes/hashes das duas fixtures externas (`settings.json` e `history.txt`, com Unicode) com o baseline em `run.json`, após rejeição, erro e atualização. Não é prova dos caminhos/configuração/histórico reais do Core. |

No caso inválido, examine a causa encadeada `error:` em `events.log` e, se necessário,
o Console.app para os processos do probe/Sparkle. A notificação de interface “etapa
de validação/extração iniciada” não prova que a assinatura passou ou que o arquivo
foi extraído. O motivo criptográfico real e a manutenção da v1 são os critérios.

Servidor válido:

```bash
python3 -B tests/MacUpdateProbe/probe.py serve "$RUN"
# Após observar o relaunch, em outro terminal:
python3 -B tests/MacUpdateProbe/probe.py verify "$RUN" --version 2.0
```

`verify` prova apenas ID/versão no disco, verificação codesign e hashes das fixtures.
O sucesso desse comando sozinho **não** comprova relaunch automático. Conserve log do
servidor, `events.log`, saídas de build/verify e observação assistida no diretório do
RUN; não inclua `.work/` em commit. Se o instalador falhar ou só trocar o bundle sem
reabrir, o caso positivo continua reprovado/pendente.

### Assinatura, Gatekeeper e TCC: provas pendentes

No Mac, comparar assinatura/requirement antes e depois e registrar a avaliação real:

```bash
codesign -dv --verbose=4 "$RUN/installed/MacUpdateProbe.app"
codesign -d -r- "$RUN/installed/MacUpdateProbe.app"
spctl --assess --type execute --verbose=4 "$RUN/installed/MacUpdateProbe.app"
otool -L "$RUN/installed/MacUpdateProbe.app/Contents/MacOS/libMacUpdateProbe.dylib"
otool -l "$RUN/installed/MacUpdateProbe.app/Contents/MacOS/libMacUpdateProbe.dylib"
```

`spctl` pode rejeitar assinatura local: registrar saída/status, não tratar como falha
do hash/Ed25519 nem como algo automaticamente resolvido por codesign. Comparar o
lançamento Finder com o relaunch efetivo do Sparkle. Incluir posteriormente cenário
com quarentena obtida por download real em navegador: pacote montado localmente não
reproduz essa distribuição. Não há notarização neste ensaio.

**TCC (microfone/Acessibilidade): pendente e não coberto por este probe.** Ele não
solicita essas permissões, não altera o banco TCC e não testa áudio/event tap. Mesmo
ID/certificado e requirement estável não são prova de preservação das permissões.
Isso exige ensaio Mac separado com permissões do bundle de teste e operação real antes
e depois; permissões do Matraca instalado nunca devem ser usadas como atalho.
Da mesma forma, o gate simulado não comprova drenagem/entrega real nem ausência de
roubo de foco durante ditado de produção.

Para repetir do zero, feche somente o probe e seu servidor e gere outro RUN. Para
limpeza, remova pelo Finder o RUN específico depois de guardar as evidências; pode
remover **somente** o domínio de preferências cujo ID está em `run.json`, se necessário.
Não há comando de limpeza genérico, reset TCC, processo morto por nome ou manipulação
da instalação real neste harness.

## Arquitetura existente consultada e limites de integração

- `Matraca.Mac/Matraca.Mac.csproj`: `net8.0`, `osx-arm64`, self-contained;
  shim C compilado com mínimo macOS 15.
- `Matraca.Mac/Program.cs` e `Platform/MacApplication.cs`: AppKit via interop,
  política Accessory, main thread e loop nativo.
- `Matraca.Mac/pack.sh`: identidade `Matraca Dev`, sem fluxo Xcode que exigiria
  Apple Developer pago. O probe preserva esse tipo de assinatura, com assinatura
  explícita dos componentes internos Sparkle.
- `Platform/MacTerminationHandshake.cs`: prazo de cinco segundos e término permitido
  mesmo em erro/timeout. Não foi reutilizado: não garante drenagem para atualização.
- `CLAUDE.md` e `docs/BOARD.md`: a lei 2 foi conciliada com o plano aprovado MT-038.
  O anúncio anterior à primeira consulta em instalações existentes ainda precisa
  ser implementado no produto; o probe usa apenas uma instalação de teste.

DS **1.0.1** consultado: DS-001, CMP-001 (ações explícitas/duplicidade), MOT-001
(sem animações próprias), A11Y-001 (controles AppKit e rótulos textuais). O menu é
instrumentação de teste, não uma tela do produto nem alegação de conformidade visual;
teclado/VoiceOver/foco ainda dependem do Mac. O contrato de movimento/gravação mantém
o ditado como prioridade; não foram portadas simulações dos mocks para produção.

## Fontes oficiais consultadas

- [Setup programático e requisitos de thread/runpath](https://sparkle-project.org/documentation/programmatic-setup/).
- [Custom user interfaces / SPUUserDriver](https://sparkle-project.org/documentation/custom-user-interfaces/).
- [Customização: automático, manual e verificação antes da extração](https://sparkle-project.org/documentation/customization/).
- [Setup, Ed25519 e distribuição](https://sparkle-project.org/documentation/).
- [Assinatura manual dos helpers](https://sparkle-project.org/documentation/sandboxing/#code-signing).
- [Release 2.8.0 e digest do artefato](https://api.github.com/repos/sparkle-project/Sparkle/releases/tags/2.8.0).
- APIs conferidas na tag **2.8.0**, não nas assinaturas novas de 2.9:
  [SPUUpdater.h](https://github.com/sparkle-project/Sparkle/blob/2.8.0/Sparkle/SPUUpdater.h),
  [SPUUserDriver.h](https://github.com/sparkle-project/Sparkle/blob/2.8.0/Sparkle/SPUUserDriver.h),
  [SPUUserUpdateState.h](https://github.com/sparkle-project/Sparkle/blob/2.8.0/Sparkle/SPUUserUpdateState.h),
  [SPUUpdaterDelegate.h](https://github.com/sparkle-project/Sparkle/blob/2.8.0/Sparkle/SPUUpdaterDelegate.h),
  [SPUAutomaticUpdateDriver.m](https://github.com/sparkle-project/Sparkle/blob/2.8.0/Sparkle/SPUAutomaticUpdateDriver.m),
  [SPUUIBasedUpdateDriver.m](https://github.com/sparkle-project/Sparkle/blob/2.8.0/Sparkle/SPUUIBasedUpdateDriver.m),
  [sign_update](https://github.com/sparkle-project/Sparkle/blob/2.8.0/sign_update/main.swift),
  [formato do seed](https://github.com/sparkle-project/Sparkle/blob/2.8.0/common_cli/secret.swift).
- Apple: [CryptoKit Curve25519.Signing.PrivateKey](https://developer.apple.com/documentation/cryptokit/curve25519/signing/privatekey)
  e [TN2206 — assinatura, identidade própria e políticas de confiança](https://developer.apple.com/library/archive/technotes/tn2206/_index.html).

## Verificações portáteis

Da raiz do clone, também no Windows (`python3` pode ser `python`):

```bash
python3 -B tests/MacUpdateProbe/probe.py fetch
python3 -B -m unittest discover -s tests/MacUpdateProbe -p test_contracts.py -v
dotnet run --project tests/MacUpdateProbe/MacUpdateProbe.csproj -- --gate-test
```

Os contratos conferem plist/feed, isolamento do RUN, hash/layout do arquivo oficial
e presença dos seletores obrigatórios do driver contra o header baixado. Não fazem
type checking de Objective-C/Swift. O teste .NET cobre captura, entrega pendente,
aquisição exclusiva, recusa de captura após confirmar e liberação após falha.
Resultados locais desta entrega: download/hash OK; 4 testes Python OK; gate .NET OK;
os três testes obrigatórios do pacote design também passaram sem alterações nele.
Compilação nativa, assinatura, instalação, Gatekeeper, relaunch e TCC: **não executados**.
