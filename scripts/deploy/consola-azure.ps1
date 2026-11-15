#requires -Version 5.1
# Salida por etapas sin Write-Progress. Los detalles completos quedan en un archivo local.
if (-not $env:LASTMILEUY_DETAIL_LOG) {
  $dir=Join-Path $HOME 'LastMileUY-Azure/logs'
  [void](New-Item -ItemType Directory -Force -Path $dir)
  $env:LASTMILEUY_DETAIL_LOG=Join-Path $dir ('detalle-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'.log')
}
function Set-LmStage([int]$Step,[string]$Title,[string]$Detail='') {
  Write-Host ("`n[{0}/7] {1}" -f $Step,$Title) -ForegroundColor Cyan
  if ($Detail) { Write-LmDetail $Detail }
}
function Write-LmDetail([string]$Message) {
  Write-Host ('      - '+$Message) -ForegroundColor DarkGray
}
function Complete-LmStage([string]$Label) {
  Write-Host ('      [OK] '+$Label) -ForegroundColor Green
}
function Invoke-LmNative {
  [CmdletBinding()]
  param([Parameter(Mandatory=$true)][string]$FilePath,
        [string[]]$Arguments=@(),
        [Parameter(Mandatory=$true)][string]$Label,
        [switch]$ShowNotices,
        [switch]$LiveStatus)
  $temp=Join-Path ([IO.Path]::GetTempPath()) ('lm-'+[guid]::NewGuid().ToString('N')+'.txt')
  $prior=$ErrorActionPreference
  $rc=1
  $lastDetail=''
  $lastDetailAt=[datetime]::MinValue
  try {
    # En PS 5.1 stderr de una aplicacion nativa puede convertirse en ErrorRecord.
    $ErrorActionPreference='Continue'
    & $FilePath @Arguments 2>&1 | ForEach-Object {
      $line=[string]$_
      Add-Content -LiteralPath $temp -Value $line -Encoding UTF8
      if ($LiveStatus) {
        if ($line -match '^PASO:\s*(.+)$') {
          Write-LmDetail $Matches[1]
        } elseif ($line -match '^((?:azurerm_[\w.]+|module\.[\w.]+):\s*(?:Creating|Destroying|Creation complete|Destruction complete|Still creating|Still destroying).*)$') {
          $now=Get-Date
          if ($line -ne $lastDetail -and ($now-$lastDetailAt).TotalSeconds -ge 4) {
            Write-LmDetail $line
            $lastDetail=$line; $lastDetailAt=$now
          }
        }
      }
    }
    $rc=$LASTEXITCODE
  } catch {
    $rc=1
    Add-Content -LiteralPath $temp -Value $_.ToString() -ErrorAction SilentlyContinue
  } finally { $ErrorActionPreference=$prior }
  try {
    Add-Content -LiteralPath $env:LASTMILEUY_DETAIL_LOG -Value "`r`n===== $(Get-Date -Format o) | $Label | exit=$rc =====" -Encoding UTF8
    if (Test-Path -LiteralPath $temp) {
      Get-Content -LiteralPath $temp | Add-Content -LiteralPath $env:LASTMILEUY_DETAIL_LOG -Encoding UTF8
    }
    if ($ShowNotices -and (Test-Path -LiteralPath $temp)) {
      Get-Content -LiteralPath $temp | Where-Object { $_ -match '^(AVISO:|ADVERTENCIA:|WARNING:|ERROR:)' } |
        ForEach-Object { Write-Warning $_ }
    }
    if ($rc -ne 0) {
      Write-Host ("      [ERROR] $Label (codigo $rc)") -ForegroundColor Red
      if (Test-Path -LiteralPath $temp) {
        Get-Content -LiteralPath $temp -Tail 35 | ForEach-Object { Write-Host $_ -ForegroundColor Red }
      }
      throw "$Label fallo ($rc). Detalles: $env:LASTMILEUY_DETAIL_LOG"
    }
  } finally { Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue }
}
