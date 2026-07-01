<#
    build-installer.ps1 — publica o Matraca (self-contained, x64) nas duas variantes
    de manifest e compila o instalador Inno Setup.

    Requisitos: .NET 8 SDK e Inno Setup 6 (winget install JRSoftware.InnoSetup).
    Saida: installer\output\matraca-setup-<versao>.exe
#>
[CmdletBinding()]
param(
    [string]$Version = '1.0.0'
)

$ErrorActionPreference = 'Stop'
$proj = Split-Path $PSScriptRoot -Parent
$csproj = Join-Path $proj 'Matraca.csproj'

foreach ($variant in @(
    @{ Manifest = 'app.manifest';          Out = 'standard' },
    @{ Manifest = 'app.uiaccess.manifest'; Out = 'uiaccess' }
)) {
    $out = Join-Path $PSScriptRoot "publish\$($variant.Out)"
    if (Test-Path $out) { Remove-Item $out -Recurse -Force }
    Write-Host "Publicando variante $($variant.Out) ($($variant.Manifest))..." -ForegroundColor Cyan
    & dotnet publish $csproj -c Release -r win-x64 --self-contained true `
        -p:ApplicationManifest=$($variant.Manifest) -p:Version=$Version -o $out
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish ($($variant.Out)) falhou" }
}

# localizar o compilador do Inno Setup
$iscc = @(
    (Get-Command iscc.exe -ErrorAction SilentlyContinue | ForEach-Object Source),
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $iscc) {
    throw 'ISCC.exe nao encontrado. Instale com: winget install JRSoftware.InnoSetup'
}

Write-Host "Compilando instalador (ISCC)..." -ForegroundColor Cyan
& $iscc "/DMyAppVersion=$Version" (Join-Path $PSScriptRoot 'matraca.iss')
if ($LASTEXITCODE -ne 0) { throw 'ISCC falhou' }

Write-Host "OK: installer\output\matraca-setup-$Version.exe" -ForegroundColor Green
