[CmdletBinding()]
param(
    [string]$ExecutablePath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $ExecutablePath) {
    $ExecutablePath = Join-Path $projectRoot 'Builds\Windows\Riverworks.exe'
}
$ExecutablePath = [System.IO.Path]::GetFullPath($ExecutablePath)

if (-not (Test-Path -LiteralPath $ExecutablePath -PathType Leaf)) {
    throw "게임 실행 파일이 없습니다. 먼저 Tools\Build.ps1을 실행하세요: $ExecutablePath"
}

# 이 도구는 사용자가 직접 플레이하는 대화형 실행용이므로 창을 표시하고 기다리지 않는다.
$process = Start-Process -FilePath $ExecutablePath -WorkingDirectory (Split-Path -Parent $ExecutablePath) -PassThru
Write-Host "Riverworks를 실행했습니다 (PID $($process.Id)). 게임 창에서 대화형으로 플레이하세요."
