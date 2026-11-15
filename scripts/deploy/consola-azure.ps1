#requires -Version 5.1
# Consola compacta compartida por los scripts de despliegue.
# Nunca silenciar fallos: se conservan detalles y se muestra el tramo relevante.
if (-not $env:LASTMILEUY_DETAIL_LOG) {
  $dir=Join-Path $HOME 'LastMileUY-Azure/logs'
  [void](New-Item -ItemType Directory -Force -Path $dir)
  $env:LASTMILEUY_DETAIL_LOG=Join-Path $dir ('detalle-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'.log')
}
function Set-LmStage([int]$Step,[string]$Title,[string]$Detail='') {
  $percent=[int][math]::Floor(($Step / 7.0)*100)
  Write-Progress -Id 1 -Activity 'LastMileUY | Reconstruccion Azure' -Status "$Step/7 - $Title" -PercentComplete $percent -CurrentOperation $Detail
  Write-Host ('[{0}/7] {1}' -f $Step,$Title) -ForegroundColor Cyan
  if ($Detail) { Write-Host ('      '+$Detail) -ForegroundColor DarkGray }
}
function Complete-LmStage([string]$Label) {
  Write-Host ('  [OK] '+$Label) -ForegroundColor Green
}
function Invoke-LmNative {
  [CmdletBinding()]
  param([Parameter(Mandatory=$true)][string]$FilePath,
        [string[]]$Arguments=@(),
        [Parameter(Mandatory=$true)][string]$Label,
        [switch]$ShowNotices)
  $temp=Join-Path ([IO.Path]::GetTempPath()) ('lm-'+[guid]::NewGuid().ToString('N')+'.txt')
  $prior=$ErrorActionPreference
  try {
    # PowerShell 5.1 puede convertir stderr de aplicaciones nativas en ErrorRecord.
    # Capturar sin usar Start-Process preserva los argumentos de SSH, rutas y comillas.
    $ErrorActionPreference='Continue'
    & $FilePath @Arguments 2>&1 | Out-File -LiteralPath $temp -Encoding UTF8
    $rc=$LASTEXITCODE
  } catch {
    $rc=1
    Add-Content -LiteralPath $temp -Value $_.ToString() -ErrorAction SilentlyContinue
  } finally { $ErrorActionPreference=$prior }
  try {
    Add-Content -LiteralPath $env:LASTMILEUY_DETAIL_LOG -Value "`r`n===== $(Get-Date -Format o) | $Label | exit=$rc =====" -Encoding UTF8
    if (Test-Path -LiteralPath $temp) { Get-Content -LiteralPath $temp | Add-Content -LiteralPath $env:LASTMILEUY_DETAIL_LOG -Encoding UTF8 }
    if ($ShowNotices -and (Test-Path -LiteralPath $temp)) {
      Get-Content -LiteralPath $temp | Where-Object { $_ -match '^(AVISO:|ADVERTENCIA:|WARNING:|ERROR:)' } |
        ForEach-Object { Write-Warning $_ }
    }
    if ($rc -ne 0) {
      Write-Host ("  [ERROR] $Label (codigo $rc)") -ForegroundColor Red
      if (Test-Path -LiteralPath $temp) {
        Get-Content -LiteralPath $temp -Tail 35 | ForEach-Object { Write-Host $_ -ForegroundColor Red }
      }
      throw "$Label fallo ($rc). Detalles: $env:LASTMILEUY_DETAIL_LOG"
    }
  } finally { Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue }
}
