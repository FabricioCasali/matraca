<#
    setup-uiaccess.ps1 — instala o Matraca com uiAccess habilitado.

    O que faz (rode UMA vez, como Administrador):
      1. Cria (ou reaproveita) um certificado de assinatura de codigo auto-assinado
         e o instala como confiavel na maquina (Trusted Root + Trusted Publisher).
      2. Publica o Matraca com o manifest uiAccess (app.uiaccess.manifest).
      3. Copia para C:\Program Files\Matraca (pasta "segura" exigida pelo uiAccess).
      4. Assina o Matraca.exe instalado.
      5. Copia um appsettings.json editavel para %LOCALAPPDATA%\Matraca.
      6. Cria atalho no Menu Iniciar.

    Por que cada passo: uiAccess (furar o UIPI p/ hook + colar em janela elevada, ex.:
    terminal Admin) so e' concedido pelo Windows se o exe estiver ASSINADO e numa pasta
    SEGURA. O app roda em integridade MEDIA -> o microfone funciona (diferente de elevar,
    que mata o mic).

    SEGURANCA: instala um certificado auto-assinado na raiz confiavel da maquina. Ele so
    valida o que VOCE assinar com ele. Revise antes de rodar.
#>

[CmdletBinding()]
param(
    [string]$InstallDir = "$env:ProgramFiles\Matraca",
    [string]$CertSubject = "CN=Matraca Self-Signed (uiAccess)",
    [switch]$AddToStartup
)

$ErrorActionPreference = 'Stop'

# --- auto-elevacao ---
$id = [Security.Principal.WindowsIdentity]::GetCurrent()
$isAdmin = (New-Object Security.Principal.WindowsPrincipal($id)).IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "Reabrindo como Administrador..." -ForegroundColor Yellow
    $argList = @('-NoProfile','-ExecutionPolicy','Bypass','-File',"`"$PSCommandPath`"")
    if ($AddToStartup) { $argList += '-AddToStartup' }
    Start-Process powershell -Verb RunAs -ArgumentList $argList
    return
}

$proj = $PSScriptRoot
Write-Host "Projeto: $proj" -ForegroundColor Cyan

# --- 0. parar instancias em execucao (evita lock do exe) ---
Get-Process Matraca -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500

# --- 1. certificado de assinatura de codigo ---
$cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $CertSubject } | Select-Object -First 1
if (-not $cert) {
    Write-Host "Criando certificado auto-assinado..." -ForegroundColor Cyan
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $CertSubject `
        -CertStoreLocation Cert:\CurrentUser\My -KeyExportPolicy Exportable `
        -NotAfter (Get-Date).AddYears(10)
} else {
    Write-Host "Reaproveitando certificado existente: $($cert.Thumbprint)" -ForegroundColor Green
}

# confiar no certificado na maquina (Root + TrustedPublisher) para o Authenticode validar
$pubKeyOnly = "$env:TEMP\matraca-cert.cer"
Export-Certificate -Cert $cert -FilePath $pubKeyOnly | Out-Null
foreach ($store in @('Root','TrustedPublisher')) {
    Import-Certificate -FilePath $pubKeyOnly -CertStoreLocation "Cert:\LocalMachine\$store" | Out-Null
    Write-Host "Certificado confiado em LocalMachine\$store" -ForegroundColor Green
}
Remove-Item $pubKeyOnly -Force -ErrorAction SilentlyContinue

# --- 2. publicar com o manifest uiAccess ---
$staging = Join-Path $env:TEMP "matraca-publish"
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
Write-Host "Publicando (Release, manifest uiAccess)..." -ForegroundColor Cyan
& dotnet publish (Join-Path $proj 'Matraca.csproj') -c Release `
    -p:ApplicationManifest=app.uiaccess.manifest -o $staging
if ($LASTEXITCODE -ne 0) { throw "dotnet publish falhou (exit $LASTEXITCODE)" }

# --- 3. copiar para Program Files ---
Write-Host "Instalando em $InstallDir" -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Copy-Item "$staging\*" $InstallDir -Recurse -Force

# --- 4. assinar o exe instalado ---
$exe = Join-Path $InstallDir 'Matraca.exe'
Write-Host "Assinando $exe" -ForegroundColor Cyan
$sig = Set-AuthenticodeSignature -FilePath $exe -Certificate $cert `
    -TimestampServer 'http://timestamp.digicert.com' -HashAlgorithm SHA256
if ($sig.Status -ne 'Valid') { throw "Assinatura falhou: $($sig.Status) - $($sig.StatusMessage)" }
Write-Host "Assinatura: $($sig.Status)" -ForegroundColor Green

# --- 5. appsettings.json editavel em %LOCALAPPDATA%\Matraca ---
$dataDir = Join-Path $env:LOCALAPPDATA 'Matraca'
New-Item -ItemType Directory -Force -Path $dataDir | Out-Null
$userSettings = Join-Path $dataDir 'appsettings.json'
if (-not (Test-Path $userSettings)) {
    Copy-Item (Join-Path $InstallDir 'appsettings.json') $userSettings -Force
    Write-Host "appsettings.json criado em $userSettings (edite aqui, sem admin)" -ForegroundColor Green
} else {
    Write-Host "Mantendo seu appsettings.json em $userSettings" -ForegroundColor Green
}

# --- 6. atalho no Menu Iniciar (e opcionalmente startup) ---
$ws = New-Object -ComObject WScript.Shell
$startMenu = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\Matraca.lnk'
$lnk = $ws.CreateShortcut($startMenu)
$lnk.TargetPath = $exe
$lnk.WorkingDirectory = $InstallDir
$lnk.Save()
Write-Host "Atalho criado: $startMenu" -ForegroundColor Green

if ($AddToStartup) {
    $startup = [Environment]::GetFolderPath('Startup')
    $lnk2 = $ws.CreateShortcut((Join-Path $startup 'Matraca.lnk'))
    $lnk2.TargetPath = $exe
    $lnk2.WorkingDirectory = $InstallDir
    $lnk2.Save()
    Write-Host "Adicionado ao startup do usuario." -ForegroundColor Green
}

Write-Host ""
Write-Host "=== Pronto! ===" -ForegroundColor Cyan
Write-Host "Inicie pelo Menu Iniciar (Matraca) — SEM 'executar como admin'." -ForegroundColor Yellow
Write-Host "Confira o log em: $dataDir\matraca.log" -ForegroundColor Yellow
Write-Host "Para validar o uiAccess, abra o terminal como Admin e teste o F15 la." -ForegroundColor Yellow

