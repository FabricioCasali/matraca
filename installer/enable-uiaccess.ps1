<#
    enable-uiaccess.ps1 — executado PELO INSTALADOR (ja elevado) quando o usuario
    marca a opcao "ditar em janelas elevadas".

    O Windows so honra uiAccess=true se o exe estiver assinado (Authenticode) com
    certificado confiavel E em pasta segura (Program Files). Este script:
      1. cria (ou reaproveita) um certificado self-signed de code signing em
         Cert:\LocalMachine\My;
      2. confia o certificado em LocalMachine\Root e LocalMachine\TrustedPublisher;
      3. assina o Matraca.exe instalado.

    O certificado so valida binarios assinados por ele nesta maquina — nao concede
    confianca a nada vindo de fora. Log: %TEMP%\matraca-uiaccess.log
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$ExePath,
    [string]$CertSubject = 'CN=Matraca Self-Signed (uiAccess)'
)

$ErrorActionPreference = 'Stop'
Start-Transcript -Path (Join-Path $env:TEMP 'matraca-uiaccess.log') -Append | Out-Null
try {
    if (-not (Test-Path $ExePath)) { throw "Exe nao encontrado: $ExePath" }

    $cert = Get-ChildItem Cert:\LocalMachine\My |
        Where-Object { $_.Subject -eq $CertSubject } | Select-Object -First 1
    if (-not $cert) {
        Write-Output "Criando certificado $CertSubject"
        $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $CertSubject `
            -CertStoreLocation Cert:\LocalMachine\My -NotAfter (Get-Date).AddYears(10)
    } else {
        Write-Output "Reaproveitando certificado $($cert.Thumbprint)"
    }

    $cer = Join-Path $env:TEMP 'matraca-uiaccess.cer'
    Export-Certificate -Cert $cert -FilePath $cer | Out-Null
    foreach ($store in @('Root', 'TrustedPublisher')) {
        Import-Certificate -FilePath $cer -CertStoreLocation "Cert:\LocalMachine\$store" | Out-Null
        Write-Output "Confiado em LocalMachine\$store"
    }
    Remove-Item $cer -Force -ErrorAction SilentlyContinue

    $sig = Set-AuthenticodeSignature -FilePath $ExePath -Certificate $cert -HashAlgorithm SHA256
    if ($sig.Status -ne 'Valid') { throw "Assinatura falhou: $($sig.Status) - $($sig.StatusMessage)" }
    Write-Output "Assinado: $ExePath ($($sig.Status))"
    exit 0
}
catch {
    Write-Output "ERRO: $_"
    exit 1
}
finally {
    Stop-Transcript | Out-Null
}
