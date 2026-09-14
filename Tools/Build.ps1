[CmdletBinding()]
param(
    [string]$OutputDirectory,
    [ValidateRange(1, 16)]
    [int]$WorkerCount = 2
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$versionFile = Join-Path $projectRoot 'ProjectSettings\ProjectVersion.txt'
$artifactDirectory = Join-Path $projectRoot 'Artifacts'
$buildLog = Join-Path $artifactDirectory 'build.log'
$buildResult = Join-Path $artifactDirectory 'build-result.txt'
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $projectRoot 'Builds\Windows' }
if (-not [System.IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory = Join-Path $projectRoot $OutputDirectory }
$windowsOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
$buildsPrefix = (Join-Path $projectRoot 'Builds').TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if (-not $windowsOutput.StartsWith($buildsPrefix, [System.StringComparison]::OrdinalIgnoreCase)) { throw '빌드 출력은 이 프로젝트의 Builds 폴더 안에 있어야 합니다.' }
$gameExecutable = Join-Path $windowsOutput 'Riverworks.exe'

function Resolve-FullPath {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$BasePath
    )

    $expanded = [System.Environment]::ExpandEnvironmentVariables($Path.Trim())
    if ($expanded.Length -ge 2 -and
        (($expanded[0] -eq '"' -and $expanded[$expanded.Length - 1] -eq '"') -or
         ($expanded[0] -eq "'" -and $expanded[$expanded.Length - 1] -eq "'"))) {
        $expanded = $expanded.Substring(1, $expanded.Length - 2)
    }
    if ([string]::IsNullOrWhiteSpace($expanded)) {
        throw 'UNITY_EDITOR_PATH가 비어 있습니다.'
    }
    if (-not [System.IO.Path]::IsPathRooted($expanded)) {
        $expanded = Join-Path $BasePath $expanded
    }
    return [System.IO.Path]::GetFullPath($expanded)
}

function Get-UnityEditorPath {
    if (-not (Test-Path -LiteralPath $versionFile -PathType Leaf)) {
        throw "Unity 버전 파일을 찾을 수 없습니다: $versionFile"
    }

    $versionLine = Get-Content -LiteralPath $versionFile -Encoding UTF8 |
        Where-Object { $_ -match '^m_EditorVersion:\s*(.+)$' } |
        Select-Object -First 1
    if (-not $versionLine -or $versionLine -notmatch '^m_EditorVersion:\s*(.+)$') {
        throw 'ProjectVersion.txt에서 Unity 버전을 읽을 수 없습니다.'
    }
    $unityVersion = $Matches[1].Trim()

    # 따옴표, 공백, %환경변수%, 프로젝트 기준 상대 경로를 모두 허용한다.
    if ($env:UNITY_EDITOR_PATH) {
        $configured = Resolve-FullPath -Path $env:UNITY_EDITOR_PATH -BasePath $projectRoot
        if (-not (Test-Path -LiteralPath $configured -PathType Leaf)) {
            throw "UNITY_EDITOR_PATH가 존재하는 파일을 가리키지 않습니다: $configured"
        }
        return $configured
    }

    $candidates = @()
    if ($env:LOCALAPPDATA) {
        $candidates += Join-Path $env:LOCALAPPDATA "RiverworksBuildTools\Unity\$unityVersion\Editor\Unity.exe"
    }
    foreach ($baseDirectory in @($env:ProgramFiles, ${env:ProgramFiles(x86)}, $env:LOCALAPPDATA)) {
        if ($baseDirectory) {
            $candidates += Join-Path $baseDirectory "Unity\Hub\Editor\$unityVersion\Editor\Unity.exe"
        }
    }

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
    }

    throw "Unity $unityVersion 실행 파일을 찾지 못했습니다. Unity Hub로 해당 버전을 설치하거나 UNITY_EDITOR_PATH를 설정하세요."
}

function Quote-NativeArgument {
    param([Parameter(Mandatory = $true)][string]$Value)
    if ($Value.IndexOf('"') -ge 0) {
        throw "명령줄 인수에 사용할 수 없는 따옴표가 있습니다: $Value"
    }
    return '"' + $Value + '"'
}

New-Item -ItemType Directory -Path $artifactDirectory -Force | Out-Null
$unityEditor = Get-UnityEditorPath

# 과거 성공 파일이 이번 실행의 결과로 오인되지 않게 먼저 제거한다.
foreach ($stalePath in @($buildLog, $buildResult)) {
    if (Test-Path -LiteralPath $stalePath) {
        Remove-Item -LiteralPath $stalePath -Force
    }
}

$startedAtUtc = [DateTime]::UtcNow
$arguments = @(
    '-batchmode', '-nographics', '-quit',
    '-job-worker-count', $WorkerCount,
    '-gc-helper-count', '1',
    '-diag-debug-shader-compiler',
    '-buildTarget', 'Win64',
    '-projectPath', (Quote-NativeArgument $projectRoot),
    '-riverworks-build-output', (Quote-NativeArgument $windowsOutput),
    '-executeMethod', 'Riverworks.Editor.BuildAutomation.Build',
    '-logFile', (Quote-NativeArgument $buildLog)
) -join ' '

Write-Host "Unity 빌드를 시작합니다: $unityEditor"
$process = Start-Process -FilePath $unityEditor -ArgumentList $arguments -WindowStyle Hidden -PassThru
$process.PriorityClass = 'BelowNormal'
$buildLogicalCores = [Math]::Min(4, [Environment]::ProcessorCount)
$process.ProcessorAffinity = [IntPtr]((1 -shl $buildLogicalCores) - 1)
$null = $process.Handle
$process.WaitForExit()
$exitCode = $process.ExitCode
$process.Dispose()
if ($exitCode -ne 0) {
    throw "Unity 빌드가 종료 코드 ${exitCode}로 실패했습니다. 로그: $buildLog"
}

foreach ($freshPath in @($buildLog, $buildResult)) {
    if (-not (Test-Path -LiteralPath $freshPath -PathType Leaf)) {
        throw "Unity는 정상 종료했지만 이번 빌드 출력이 없습니다: $freshPath"
    }
    $freshFile = Get-Item -LiteralPath $freshPath
    if ($freshFile.LastWriteTimeUtc -lt $startedAtUtc.AddSeconds(-2)) {
        throw "빌드 출력이 이번 실행에서 갱신되지 않았습니다: $freshPath"
    }
}

$resultText = Get-Content -LiteralPath $buildResult -Raw -Encoding UTF8
if ($resultText -notmatch '(?m)^SUCCESS\s*$') {
    throw "빌드 결과가 성공을 나타내지 않습니다: $buildResult"
}
if ($resultText -notmatch '(?m)^Errors\s+0\s*$') {
    throw "빌드 결과에 오류 0이 기록되지 않았습니다: $buildResult"
}
$logText = Get-Content -LiteralPath $buildLog -Raw -Encoding UTF8
if ($logText -notmatch 'RIVERWORKS_BUILD_SUCCESS') {
    throw "Unity 로그에 빌드 성공 마커가 없습니다: $buildLog"
}
if (-not (Test-Path -LiteralPath $gameExecutable -PathType Leaf)) {
    throw "성공 결과는 기록됐지만 게임 실행 파일이 없습니다: $gameExecutable"
}
# Unity reuses the engine launcher timestamp. Freshness is proved by this run's
# BuildPipeline report/log and the runtime build GUID, not the launcher stub date.
$managedAssembly = Join-Path $windowsOutput 'Riverworks_Data\Managed\Assembly-CSharp.dll'
if (-not (Test-Path -LiteralPath $managedAssembly -PathType Leaf)) { throw "게임 코드 어셈블리가 없습니다: $managedAssembly" }

Write-Host "빌드 성공: $gameExecutable"
Write-Host "Unity 로그: $buildLog"
