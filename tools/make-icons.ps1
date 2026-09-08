# Rasteriza SOMENTE os SVGs oficiais 03A. Requer Node 18+ e npm ci --prefix tools/icons.
param([switch]$Check)
$ErrorActionPreference = 'Stop'
$script = Join-Path $PSScriptRoot 'icons/make-icons.cjs'
if ($Check) { & node $script --check } else { & node $script }
if ($LASTEXITCODE -ne 0) { throw 'Falha ao gerar/conferir os icones oficiais.' }
