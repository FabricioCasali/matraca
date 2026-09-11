# MT-039 — prova Windows isolada: NetSparkle + Inno Setup

O atualizador foi renumerado de MT-038 para MT-039 ao integrar com o remoto,
onde MT-038 já identificava a internacionalização. Os nomes MT038 nos probes,
fixtures e evidências anteriores são identificadores de teste preservados.

**Entrega:** `C:\Desenv\particular\ditador\tests\WindowsUpdateProbe\README.md`.

Etapa 1 somente. Prova executada em 10/09/2026: checagem e download reais pelo
NetSparkle, Ed25519 estrito, dois instaladores Inno locais, dados preservados e
reabertura headless. A extensão também comprova **`InstallUpdate` dentro do próprio
v1 instalado**, encerramento normal e instalação/reabertura pelo helper original
da biblioteca. **Não é um atualizador integrado ao Matraca.**

## Resultado observado

| Verificação | Resultado |
|---|---|
| Build .NET 8 Release | 0 erros, 0 avisos |
| Restore com lock | NetSparkle 3.1.0; Chaos.NaCl 0.9.4 |
| Appcast e assinatura `.signature` por HTTP loopback | Consultados pela biblioteca |
| Versão instalada 1.0.0 e oferta 2.0.0 | `UpdateAvailable` |
| Oferta igual a 1.0.0 | `UpdateNotAvailable` |
| Appcast alterado depois da assinatura | `CouldNotDetermine`; nenhum download de pacote |
| Pacote com um byte alterado depois da assinatura | `DownloadedFileIsCorrupt`; nenhum `DownloadFinished` |
| Cancelamento após receber bytes de resposta lenta | Parcial removido; nenhum `DownloadFinished` |
| Evento de cancelamento | **Divergência: `DownloadedFileIsCorrupt`, não `DownloadCanceled`** |
| Download sem confirmação de instalação | Nenhum instalador iniciado |
| Inno 1.0.0 → 2.0.0 no mesmo diretório exclusivo | Exit code 0 nas duas instalações |
| Versão do EXE e versão reportada por processos reais | 1.0.0.0 antes; 2.0.0.0 depois |
| Fixture de dados editada entre as versões | Igual byte a byte após instalação e reabertura |
| Privilégios no log Inno | `User privileges: None`; `Administrative install mode: No` |
| `InstallUpdate` após adulterar arquivo já baixado | `InvalidSignature`; nenhum helper/encerramento |
| Veto real por `PreparingToExit` | Nenhum helper/encerramento |
| Helper `.cmd` original, sem alteração de script/StartInfo | Instalou e reabriu v2 |
| Gate sintético de 2 segundos em `CloseApplicationAsync` | v1 vivo; log Inno v2 ainda ausente |
| Ordem cruzada dos horários | Saída real do PID v1 → abertura do log Inno → reabertura v2 |
| Três verificadores obrigatórios do pacote de design | PASS; não representam teste visual/nativo |

Ambiente observado: SDK **8.0.425**, Inno Setup **6.7.3**, Windows x64
**10.0.26200**. Não houve bloqueio de rede, NuGet ou instalação neste host.

### Evidência atual: autoatualização do próprio processo

`C:\Desenv\particular\ditador\tests\WindowsUpdateProbe\artifacts\d821aa2f0b8a4efd8723d2edd6be280e\`

- `result.json`: `selfUpdateHelperVerified: true`; demais asserções de download
  mantidas, incluindo a divergência conhecida do evento de cancelamento.
- `self-update-observation.json`: observação do controlador, sem executar v2.
- `self-download-validated.json`: download e Ed25519 realizados pelo v1 instalado.
- `install-signature-recheck.json`: arquivo adulterado após download rejeitado pelo
  próprio `InstallUpdate`. `install-update-failed.json` registra essa **falha esperada**.
- `close-veto.json`: tentativa válida vetada antes de criar/iniciar helper.
- `helper-original.cmd`: cópia exata do script produzido pela biblioteca.
- `helper-start-info.json`: parâmetros originais de lançamento; apenas observados.
- `close-requested.json`, `close-gate.json`, `self-exiting.json`: callback, gate e
  retorno normal do v1. Nenhuma chamada `Kill`/`Environment.Exit`.
- `install-v2.log`: pacote executado de `self-download/package.exe` pelo helper.
- `reopened.json`: v2 reaberto pelo helper, PID novo, versão e fixture preservada.

| Evento real | PID | Horário UTC |
|---|---:|---|
| v1 instalado, saída observada pelo Windows (`Process.ExitTime`) | 56928 | 18:49:59.9873708 |
| Helper original, PID informado no callback após `Start` | 57136 | registrado em `close-requested.json` |
| Inno v2 abre seu log | — | 18:50:01.003 |
| v2 reaberto, versão 2.0.0.0 | 55748 | 18:50:01.2472155 |

O controlador instala **somente a baseline v1** e a inicia com consentimento CLI.
Depois entrega URL de appcast e chave **pública** por stdin, mantém o servidor de
fixture vivo e observa os resultados. A chave privada nunca sai de sua memória.
O v1 faz outra consulta/download, verifica, veta uma tentativa de encerramento e,
na tentativa liberada, chama `InstallUpdate`. A biblioteca inicia seu helper e
chama `CloseApplicationAsync`; o v1 retorna normalmente de `Main`. O helper espera
o PID, executa Inno e reabre v2 com `RelaunchAfterUpdate = true` / `--reopened`.

### Evidências anteriores e regressões

Prova original de composição (controlador instalava também v2):

`C:\Desenv\particular\ditador\tests\WindowsUpdateProbe\artifacts\2297d5da09e245bd8b7852104756d8bf\`

- `result.json`: resultado gerado após as asserções; registra explicitamente
  `cancellationEventContractSatisfied: false` e `selfUpdateHelperVerified: false`.
- `install-v1.log`, `install-v2.log`: logs Inno; v2 executado de `downloads/valid/package.exe`.
- `data/launches.txt`, `data/last-launch.json`, `data/sentinel.txt`: versões e fixture preservada.
- `fixtures/`: appcasts, assinaturas, pacotes e **somente a chave pública** do teste.
- `packages/`: dois instaladores; `install/`: payload final instalado.

Regressões executadas após adicionar o helper, com os mesmos casos de assinatura e cancelamento:

- Sem autorização: `C:\Desenv\particular\ditador\tests\WindowsUpdateProbe\artifacts\4568aece3fb14239a97a2d098fee45c3\result.json`.
- Composição anterior: `C:\Desenv\particular\ditador\tests\WindowsUpdateProbe\artifacts\5e17966e52a848718bc42c6e28bbd5c7\result.json`.

Esses artefatos são ignorados pelo Git. Nova execução gera outro GUID e outra chave;
o appcast preservado contém a porta efêmera daquela execução, não um serviço permanente.

## Reproduzir

Pré-requisitos já instalados: Windows x64, SDK .NET 8 (inclui o runtime ASP.NET Core
usado pelo servidor de fixture) e, somente para instalar, Inno Setup 6 em
`%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe`. O harness falha se não encontrar
esse compilador; não baixa nem instala ferramentas globais.

Da raiz `C:\Desenv\particular\ditador`:

```powershell
dotnet restore tests/WindowsUpdateProbe/WindowsUpdateProbe.csproj --locked-mode --source https://api.nuget.org/v3/index.json
dotnet build tests/WindowsUpdateProbe/WindowsUpdateProbe.csproj -c Release --no-restore
dotnet restore tests/WindowsUpdateProbe/Payload/Payload.csproj --locked-mode -r win-x64 --source https://api.nuget.org/v3/index.json

# Checa/baixa fixtures automaticamente; nao instala nem compila instaladores.
dotnet run --project tests/WindowsUpdateProbe/WindowsUpdateProbe.csproj -c Release --no-build

# Confirmacao explicita para instalar baseline e upgrade, exclusivamente do probe.
dotnet run --project tests/WindowsUpdateProbe/WindowsUpdateProbe.csproj -c Release --no-build -- --confirm-install

# Extensao: v1 instalado faz InstallUpdate e encerra; helper instala/reabre v2.
dotnet run --project tests/WindowsUpdateProbe/WindowsUpdateProbe.csproj -c Release --no-build -- --confirm-self-update

dotnet list tests/WindowsUpdateProbe/WindowsUpdateProbe.csproj package --include-transitive
node design/tests/matraca-design-system.test.cjs
node design/tests/matraca-prototype.test.cjs
node design/tests/matraca-brand.test.cjs
git diff --check
```

Esses comandos foram executados; o restore inicial usou os mesmos argumentos sem
`--locked-mode`, para gerar `packages.lock.json`. Os testes locais saem com código 1
em falha e só escrevem `result.json` após concluir as asserções. PASS na suíte de
segurança não equivale a conformidade do evento de cancelamento: veja o campo
específico no relatório. No modo self-update, falhas observadas do helper geram
`self-update-observation.json` e `result.json` com `selfUpdateHelperVerified: false`,
seguidas de exit code 1, sem substituição pelo instalador externo. No payload,
exceções geram `self-update-error.json` com tipo, HResult e eventual código Win32.

Ambos os modos com consentimento compilam as versões `1.0.0` e `2.0.0`. No modo
`--confirm-install`, o próprio harness também executa os dois pacotes e payloads:

```text
dotnet publish <root>\Payload\Payload.csproj -c Release -r win-x64 --self-contained false -p:Version=<versao> -o <run>\payload-<versao> --source https://api.nuget.org/v3/index.json
<Inno-local>\ISCC.exe /Q /DProbeVersion=<versao> /DRunId=<guid> /DInstallDir=<run>\install /DPackageDir=<run>\packages /DPayloadDir=<run>\payload-<versao> <root>\Probe.iss
<pacote> /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /LOG=<run>\install-v<1-ou-2>.log
<run>\install\MT038.Isolated.Payload.exe
```

Os comandos do controlador usam `ProcessStartInfo.ArgumentList`. No self-update,
os argumentos de Inno e relançamento são configurados nas propriedades reais da
biblioteca, que os escreve em seu `.cmd`; o probe não reescreve esse script.
Os processos de build/composição têm timeout de dois minutos; o observador espera
até 60 segundos pelo v1 e 100 segundos pelo relatório de reabertura. O timeout falha a execução, sem
matar processos; se ocorrer, confira o processo/artefato do probe antes de remover
seu diretório. Em conclusão normal, payloads terminam sozinhos e o servidor para.

## API real usada e responsabilidades

- Referência fixa **`NetSparkleUpdater.SparkleUpdater [3.1.0]`** no controlador e no
  payload, com locks de
  dependências. Única dependência NuGet transitiva: **`NetSparkleUpdater.Chaos.NaCl
  0.9.4`**. Nenhum WinForms, WPF ou Avalonia.
- `SparkleUpdater`, `UIFactory = null`, `UserInteractionMode.DownloadNoInstall`,
  `CheckForUpdatesQuietly()` e `InitAndBeginDownload(AppCastItem)` fazem a
  consulta, seleção, download e validação reais. Não substituímos downloader,
  parser ou verificador por mocks.
- `Ed25519Checker(SecurityMode.Strict, publicKey, publicKeyFile: null)` exige
  assinatura do appcast e do pacote. A existência do arquivo não autoriza instalação;
  somente `DownloadFinished` libera o caminho, revalidado imediatamente antes de
  executar o instalador com a mesma API da biblioteca.
- `DefaultConfiguration` com `AssemblyDiagnosticsAccessor` mantém a configuração
  apenas em memória. Não usa Registro ou configuração pessoal do Matraca.
- Fixtures assinam usando `Chaos.NaCl.Ed25519.KeyPairFromSeed` e `Ed25519.Sign`,
  da dependência do próprio NetSparkle. Seed aleatório em bytes via .NET; seed e
  chave expandida são zerados ao final. Nenhum algoritmo criptográfico próprio,
  chave privada em arquivo/string de ambiente/argumento/log ou segredo de produção.
- Kestrel em `127.0.0.1:0`, configuração vazia, sem carregar `appsettings`,
  user-secrets ou configuração da aplicação. A resposta lenta de 2 MiB permite
  cancelar após progresso real, não antes de começar uma transferência fictícia.
- No modo composição, o controlador chama Inno e reabre o payload por `Process.Start`.
  No modo self-update, **`Payload/SelfUpdate.cs` chama `SparkleUpdater.InstallUpdate`**;
  `ShouldKillParentProcessWhenStartingInstaller = true` ativa espera/encerramento e
  `ProcessIDToKillBeforeInstallerRuns` recebe somente `Environment.ProcessId` do v1.
  Apesar do nome da propriedade, o script Windows observado usa `tasklist/findstr`
  para esperar, não `taskkill`. `InstallerProcessAboutToStart` apenas registra/copia
  evidência e retorna `true`; não altera o processo nem seu script.
- `PreparingToExit` exerce um veto explícito e depois permite prosseguir.
  `CloseApplicationAsync` registra o PID do helper e mantém o v1 vivo por dois
  segundos antes do retorno normal de `Main`; o callback não captura áudio/trabalho.

## Isolamento e limites

- Todo código novo e artefato explícito está em `tests/WindowsUpdateProbe/`.
  Compilador, .NET e Inno podem usar seus caches/temporários usuais. No self-update,
  TEMP/TMP do **processo filho somente** apontam a `artifacts/<guid>/helper-temp`;
  a biblioteca grava ali seu `.cmd` original e Inno usa temporários desse ensaio.
- AppId `MT038-Isolated-Probe-<guid>`, AppName próprio, destino
  `artifacts/<guid>/install`, dados sintéticos em `artifacts/<guid>/data`.
  O `.iss` rejeita mudança do destino compilado por `/DIR`.
- `PrivilegesRequired=lowest`, sem certificado, atalhos, startup, desinstalador,
  registro de desinstalação ou reinício do Windows. `CloseApplications=no` e
  `RestartApplications=no`; não enumera nem encerra o Matraca.
- Payload `WinExe` sem janela, UI ou áudio; controlador e filhos usam
  `CreateNoWindow`. Não é um teste físico de foco do sistema.
- No modo composição, v1 termina antes da instalação coordenada externamente.
  No modo self-update, o v1 ainda está em execução quando chama `InstallUpdate`;
  seu callback termina e o helper espera sua saída antes de substituir os arquivos
  e reabrir v2. Isso comprova autoatualização **deste payload** no host testado.
- Prova **user-local em diretório gravável**, não em Program Files. Não comprova
  UAC, uiAccess, startup real, certificado/Authenticode, SmartScreen, drenagem de
  áudio/entregas, permissões do macOS, rollback ou recuperação de energia/processo.
- HTTP é exclusivamente loopback para fixtures. GitHub Releases, HTTPS remoto,
  redirecionamentos/CDN, publicação, rotação de chave e agendamento periódico ainda
  não foram exercitados. Cada execução faz uma rodada automática de checar/baixar;
  não integra o loop de atualização à aplicação.
- A flag de CLI confirma a instalação para a prova; não representa a futura UI
  de consentimento nem a coordenação com ditado/entrega.
- Payload depende do runtime .NET 8 instalado. Não é prova de instalação de
  pré-requisitos em uma máquina limpa.
- Artefatos podem ser removidos manualmente após terminar a execução; não há
  serviço, startup ou entrada de desinstalação persistente para limpar.

### Achados que devem orientar a próxima etapa

1. **Cancelamento:** no 3.1.0, `CancelFileDownload()` durante progresso real removeu
   o parcial, mas produziu `DownloadedFileIsCorrupt`. A suíte preserva esse resultado
   bruto e verifica a propriedade importante de segurança: não produzir
   `DownloadFinished` nem deixar pacote parcial elegível. O contrato de evento
   continua divergente; não foi corrigido com um downloader próprio.
2. **Versão com metadata Git:** `AssemblyDiagnosticsAccessor` inicialmente leu
   `1.0.0+<hash>`; a oferta 1.0.0 apareceu como disponível neste ensaio. A fixture fixa
   `IncludeSourceRevisionInInformationalVersion=false`, tornando explícita a versão
   comparada. Não é uma correção de comparação no produto; seu versionamento precisa
   de validação própria antes da integração.
3. **Helper real:** comprovados `InstallUpdate`, veto, revalidação, encerramento,
   espera e relançamento do próprio payload. O script gerado **não verifica o exit
   code de Inno antes de relançar** (sequência direta `:install` → `:afterinstall`).
   Isso foi observado no script, não em uma instalação deliberadamente falha;
   o ensaio cobriu sucesso. Não foi implementado um helper alternativo.
4. **Contrato do gate:** inspeção da tag 3.1.0 mostra que `AskApplicationToSafelyCloseUp`
   captura exceções do callback e termina retornando `true`. Portanto, exceção no
   gate não equivale a veto. Nosso gate usa `Cancel = true` explicitamente e foi
   exercitado; não injeta exceção nem implementa coordenação de produto.
5. **Portão da etapa 1:** drenagem real, UAC/uiAccess/startup e validações Mac
   permanecem fora desta comprovação. Nenhuma conclusão aqui autoriza habilitar
   atualização no produto.

## Regras consultadas e fontes

Lidos `AGENTS.md`, `CLAUDE.md` e `docs/BOARD.md`. Não havia regra adicional em
`tests/`. A aprovação MT-038 permite esta investigação isolada; a lei 2 foi
conciliada com a preferência e a confirmação aprovadas. Este probe não habilita
consultas no produto. Usa somente payload de teste isolado.

Design System **1.0.1** é a versão indicada pelo MT-038; não há componentes de UI
nesta fatia, portanto CMP/MOT/A11Y não têm implementação visual a avaliar aqui.
As verificações do pacote de design passaram, sem alteração do design.

Pesquisa em documentação pública e APIs do pacote restaurado; as primeiras URLs
tentadas para nomes antigos de arquivos retornaram 404, resolvidos pela árvore da
tag. Referências fixadas à tag **3.1.0**, árvore
`b3df04ab2d06230a28e12cfd7c613d25e27d2672`:

- [Pacote NuGet 3.1.0](https://www.nuget.org/packages/NetSparkleUpdater.SparkleUpdater/3.1.0)
- [Documentação oficial](https://github.com/NetSparkleUpdater/NetSparkle/blob/3.1.0/README.md)
- [SparkleUpdater](https://github.com/NetSparkleUpdater/NetSparkle/blob/3.1.0/src/NetSparkle/SparkleUpdater.cs)
- [Eventos](https://github.com/NetSparkleUpdater/NetSparkle/blob/3.1.0/src/NetSparkle/SparkleUpdaterEvents.cs)
- [Ed25519Checker](https://github.com/NetSparkleUpdater/NetSparkle/blob/3.1.0/src/NetSparkle/SignatureVerifiers/Ed25519Checker.cs)
- [WebFileDownloader e cancelamento](https://github.com/NetSparkleUpdater/NetSparkle/blob/3.1.0/src/NetSparkle/Downloaders/WebFileDownloader.cs)
- [Inno: PrivilegesRequired](https://jrsoftware.org/ishelp/topic_setup_privilegesrequired.htm)

As assinaturas das APIs foram também conferidas nos XMLs NuGet de `net8.0` e pela
compilação/execução real. `Ed25519.GeneratePrivateKeySeed()` retorna string nessa
versão; por isso a fixture usa `RandomNumberGenerator.GetBytes` e a API de derivação
da biblioteca para manter o material privado apenas em buffers apagáveis.
