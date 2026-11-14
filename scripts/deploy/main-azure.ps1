#requires -Version 5.1
[CmdletBinding()]
param(
  [switch]$Destroy,
  [switch]$PermitirSkuConCosto,
  [string]$SkuAzureFreeVerificada,
  [string]$BackupRoot = (Join-Path $HOME 'LastMileUY-Azure/backups'),
  [string]$SshKey = (Join-Path $HOME 'LastMileUY-Azure/ssh/lastmileuy_azure_nueva'),
  [string]$EnvFile = (Join-Path $HOME 'LastMileUY-Azure/config/.env.azure')
)
$entry=Join-Path $PSScriptRoot 'recrear-azure.ps1'
if (-not (Test-Path -LiteralPath $entry)) { throw "Falta $entry" }
$params=@{ BackupRoot=$BackupRoot; SshKey=$SshKey; EnvFile=$EnvFile }
if ($Destroy) { $params.Destroy=$true }
if ($PermitirSkuConCosto) { $params.PermitirSkuConCosto=$true }
if ($SkuAzureFreeVerificada) { $params.SkuAzureFreeVerificada=$SkuAzureFreeVerificada }
& $entry @params
