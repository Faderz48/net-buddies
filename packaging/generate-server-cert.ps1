param(
    [string]$DnsName = "netbuddies.local",
    [string]$Password = "change-me",
    [string]$OutputPath = ".\publish\netbuddies-server.pfx"
)

$ErrorActionPreference = "Stop"
$resolvedOutput = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputPath)
$outputDirectory = Split-Path -Parent $resolvedOutput
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null

$cert = New-SelfSignedCertificate `
    -DnsName $DnsName `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -KeyAlgorithm RSA `
    -KeyLength 3072 `
    -HashAlgorithm SHA256 `
    -NotAfter (Get-Date).AddYears(2) `
    -KeyExportPolicy Exportable

$securePassword = ConvertTo-SecureString $Password -AsPlainText -Force
Export-PfxCertificate `
    -Cert $cert `
    -FilePath $resolvedOutput `
    -Password $securePassword | Out-Null

Write-Host "Created $resolvedOutput"
Write-Host "Use TLS with certificate password: $Password"
