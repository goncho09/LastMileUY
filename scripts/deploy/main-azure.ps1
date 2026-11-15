#requires -Version 5.1
[CmdletBinding()]
param(
  [switch]$Destroy,
  [string]$ResumeBackupDir,
  [string]$BackupRoot = (Join-Path $HOME 'LastMileUY-Azure/backups'),
  [string]$SshKey = (Join-Path $HOME 'LastMileUY-Azure/ssh/lastmileuy_azure_nueva'),
  [string]$EnvFile = (Join-Path $HOME 'LastMileUY-Azure/config/.env.azure')
)

. (Join-Path $PSScriptRoot 'consola-azure.ps1')

# Validaciones locales previas al despliegue
if ($Destroy) {
  Write-Host ""
  Write-Host "Ejecutando preflight antes del destroy..." -ForegroundColor Cyan

  $preflight = Join-Path $PSScriptRoot "validar-preflight-local.ps1"

  if (-not (Test-Path $preflight)) {
    throw "No se encontró el preflight: $preflight"
  }

  & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $preflight

  if ($LASTEXITCODE -ne 0) {
    throw "PREFLIGHT FALLIDO. Destroy cancelado."
  }

  Write-Host "Preflight aprobado. Continuando..." -ForegroundColor Green
}

$entry=Join-Path $PSScriptRoot 'recrear-azure.ps1'

if (-not (Test-Path -LiteralPath $entry)) { throw "Falta $entry" }

$params=@{ BackupRoot=$BackupRoot; SshKey=$SshKey; EnvFile=$EnvFile }

if ($Destroy) { $params.Destroy=$true }

if ($ResumeBackupDir) { $params.ResumeBackupDir=$ResumeBackupDir }

$logDir=Join-Path $HOME 'LastMileUY-Azure/logs'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$logFile=Join-Path $logDir ("reconstruccion-{0}.log" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))

Write-Host 'LastMileUY | Despliegue Azure' -ForegroundColor Cyan
Write-Host "Logs: $logFile" -ForegroundColor DarkGray

Start-Transcript -LiteralPath $logFile -ErrorAction Stop | Out-Null

try {
  & $entry @params
  if (-not $?) { throw 'El proceso principal no termino correctamente' }
} finally {
  Write-Progress -Id 1 -Activity 'LastMileUY | Reconstruccion Azure' -Completed
  Stop-Transcript | Out-Null
  Write-Host "Resumen: $logFile" -ForegroundColor DarkGray
  Write-Host "Detalles tecnicos: $env:LASTMILEUY_DETAIL_LOG" -ForegroundColor DarkGray
}
