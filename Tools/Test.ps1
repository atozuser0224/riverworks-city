[CmdletBinding()]
param(
    [switch]$Runtime,
    [switch]$FactoryRuntime,
    [switch]$CompactUiRuntime,
    [switch]$FeelRuntime,
    [switch]$TutorialRuntime,
    [switch]$ResidentActivityRuntime,
    [switch]$ResearchTreeRuntime,
    [string]$ExecutablePath,
    [ValidateRange(1, 3600)]
    [int]$RuntimeTimeoutSeconds = 300
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$testProject = Join-Path $projectRoot 'Tests\Riverworks.Tests.csproj'
$artifactDirectory = Join-Path $projectRoot 'Artifacts'

function Quote-NativeArgument {
    param([Parameter(Mandatory = $true)][string]$Value)
    if ($Value.IndexOf('"') -ge 0) {
        throw "명령줄 인수에 사용할 수 없는 따옴표가 있습니다: $Value"
    }
    return '"' + $Value + '"'
}

function Invoke-RuntimeSmoke {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$SmokeFlag,
        [Parameter(Mandatory = $true)][string]$OutputDirectory,
        [Parameter(Mandatory = $true)][string]$ResultFileName,
        [Parameter(Mandatory = $true)][string]$LogFileName,
        [Parameter(Mandatory = $true)][string]$SuccessMarker,
        [Parameter(Mandatory = $true)][string]$PlayerPath,
        [Parameter(Mandatory = $true)][int]$TimeoutSeconds
    )

    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    $resultPath = Join-Path $OutputDirectory $ResultFileName
    $logPath = Join-Path $OutputDirectory $LogFileName
    foreach ($stalePath in @($resultPath, $logPath)) {
        if (Test-Path -LiteralPath $stalePath) {
            Remove-Item -LiteralPath $stalePath -Force
        }
    }

    $startedAtUtc = [DateTime]::UtcNow
    $arguments = @(
        $SmokeFlag,
        '-job-worker-count', '2',
        '-riverworks-output', (Quote-NativeArgument $OutputDirectory),
        '-logFile', (Quote-NativeArgument $logPath)
    ) -join ' '

    Write-Host "$Name 스모크 테스트를 시작합니다: $PlayerPath"
    $process = Start-Process -FilePath $PlayerPath -ArgumentList $arguments -WorkingDirectory (Split-Path -Parent $PlayerPath) -WindowStyle Hidden -PassThru
    $process.PriorityClass = 'BelowNormal'
    try {
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            try { $process.Kill() } catch { Write-Warning "시간 초과 프로세스 종료 중 오류: $($_.Exception.Message)" }
            try { $process.WaitForExit(5000) | Out-Null } catch {}
            throw "$Name 스모크 테스트가 ${TimeoutSeconds}초 안에 끝나지 않았습니다. 로그: $logPath"
        }
        $exitCode = $process.ExitCode
    } finally {
        $process.Dispose()
    }

    if ($exitCode -ne 0) {
        throw "$Name 스모크 테스트가 종료 코드 ${exitCode}로 실패했습니다. 로그: $logPath"
    }
    foreach ($freshPath in @($resultPath, $logPath)) {
        if (-not (Test-Path -LiteralPath $freshPath -PathType Leaf)) {
            throw "$Name 스모크 테스트 출력이 없습니다: $freshPath"
        }
        $freshFile = Get-Item -LiteralPath $freshPath
        if ($freshFile.LastWriteTimeUtc -lt $startedAtUtc.AddSeconds(-2)) {
            throw "$Name 스모크 테스트 출력이 이번 실행에서 갱신되지 않았습니다: $freshPath"
        }
    }

    $resultText = Get-Content -LiteralPath $resultPath -Raw -Encoding UTF8
    if ($resultText -notmatch '(?m)^ERRORS\s+0\s*$') {
        throw "$Name 결과에 'ERRORS 0'이 없습니다: $resultPath"
    }
    $buildMatch = [System.Text.RegularExpressions.Regex]::Match(
        $resultText,
        '(?mi)^RUN[^\r\n]*\bBUILD(?:GUID)?\s+([0-9a-f]{32})(?:\s|$)'
    )
    if (-not $buildMatch.Success) {
        throw "$Name 결과에 유효한 32자리 빌드 GUID가 없습니다: $resultPath"
    }

    $logText = Get-Content -LiteralPath $logPath -Raw -Encoding UTF8
    if ($logText -notmatch [System.Text.RegularExpressions.Regex]::Escape($SuccessMarker)) {
        throw "$Name 성공 마커가 로그에 없습니다: $logPath"
    }

    Write-Host "$Name 스모크 테스트 성공: $resultPath"
    return $buildMatch.Groups[1].Value.ToLowerInvariant()
}

Write-Host 'C# 시뮬레이션 테스트를 실행합니다.'
& dotnet run --project $testProject -c Release
$dotnetExitCode = $LASTEXITCODE
if ($dotnetExitCode -ne 0) {
    throw "C# 테스트가 종료 코드 ${dotnetExitCode}로 실패했습니다."
}

if (-not $Runtime -and -not $FactoryRuntime -and -not $CompactUiRuntime -and -not $FeelRuntime -and -not $TutorialRuntime -and -not $ResidentActivityRuntime -and -not $ResearchTreeRuntime) {
    Write-Host 'C# 테스트 성공. 실행 파일 검증은 -Runtime 또는 -FactoryRuntime을 지정하면 추가로 실행됩니다.'
    return
}

if (-not $ExecutablePath) {
    $ExecutablePath = Join-Path $projectRoot 'Builds\Windows\Riverworks.exe'
}
$ExecutablePath = [System.IO.Path]::GetFullPath($ExecutablePath)
if (-not (Test-Path -LiteralPath $ExecutablePath -PathType Leaf)) {
    throw "런타임 테스트용 실행 파일이 없습니다: $ExecutablePath"
}

$runtimeBuildGuid = $null
$factoryBuildGuid = $null
$compactUiBuildGuid = $null
$feelBuildGuid = $null
$tutorialBuildGuid = $null
if ($Runtime) {
    $runtimeBuildGuid = Invoke-RuntimeSmoke -Name '도시 런타임' -SmokeFlag '-riverworks-smoke' -OutputDirectory (Join-Path $artifactDirectory 'Smoke') -ResultFileName 'runtime-results.txt' -LogFileName 'runtime.log' -SuccessMarker 'RIVERWORKS_RUNTIME_SMOKE_PASS' -PlayerPath $ExecutablePath -TimeoutSeconds $RuntimeTimeoutSeconds
}
if ($FactoryRuntime) {
    $factoryBuildGuid = Invoke-RuntimeSmoke -Name '도시 통합 물류' -SmokeFlag '-riverworks-shared-city-smoke' -OutputDirectory (Join-Path $artifactDirectory 'SharedCitySmoke') -ResultFileName 'shared-city-results.txt' -LogFileName 'shared-city-runtime.log' -SuccessMarker 'SHARED_CITY_RUNTIME_PASS' -PlayerPath $ExecutablePath -TimeoutSeconds $RuntimeTimeoutSeconds
}
if ($runtimeBuildGuid -and $factoryBuildGuid -and $runtimeBuildGuid -ne $factoryBuildGuid) {
    throw "도시와 공장 런타임 결과의 빌드 GUID가 다릅니다: $runtimeBuildGuid / $factoryBuildGuid"
}
if ($CompactUiRuntime) {
    $compactUiBuildGuid = Invoke-RuntimeSmoke -Name '컴팩트 도시 UI' -SmokeFlag '-riverworks-compact-ui-smoke' -OutputDirectory (Join-Path $artifactDirectory 'CompactUiSmoke') -ResultFileName 'compact-ui-results.txt' -LogFileName 'compact-ui-runtime.log' -SuccessMarker 'COMPACT_UI_RUNTIME_PASS' -PlayerPath $ExecutablePath -TimeoutSeconds $RuntimeTimeoutSeconds
    foreach ($otherGuid in @($runtimeBuildGuid, $factoryBuildGuid)) {
        if ($otherGuid -and $otherGuid -ne $compactUiBuildGuid) {
            throw "UI 검사와 게임 실행 결과의 빌드 GUID가 다릅니다: $compactUiBuildGuid / $otherGuid"
        }
    }
}

if ($FeelRuntime) {
    $feelBuildGuid = Invoke-RuntimeSmoke -Name 'Feel 연출' -SmokeFlag '-riverworks-feel-smoke' -OutputDirectory (Join-Path $artifactDirectory 'FeelSmoke') -ResultFileName 'feel-results.txt' -LogFileName 'feel-runtime.log' -SuccessMarker 'RIVERWORKS_FEEL_SMOKE_PASS' -PlayerPath $ExecutablePath -TimeoutSeconds $RuntimeTimeoutSeconds
    foreach ($otherGuid in @($runtimeBuildGuid, $factoryBuildGuid, $compactUiBuildGuid)) {
        if ($otherGuid -and $otherGuid -ne $feelBuildGuid) {
            throw "Feel과 게임 실행 결과의 빌드 GUID가 다릅니다: $feelBuildGuid / $otherGuid"
        }
    }
}
if ($TutorialRuntime) {
    $tutorialBuildGuid = Invoke-RuntimeSmoke -Name '단우 튜토리얼' -SmokeFlag '-riverworks-tutorial-smoke' -OutputDirectory (Join-Path $artifactDirectory 'TutorialSmoke') -ResultFileName 'tutorial-results.txt' -LogFileName 'tutorial-runtime.log' -SuccessMarker 'RIVERWORKS_TUTORIAL_SMOKE_PASS' -PlayerPath $ExecutablePath -TimeoutSeconds $RuntimeTimeoutSeconds
    foreach ($otherGuid in @($runtimeBuildGuid, $factoryBuildGuid, $compactUiBuildGuid, $feelBuildGuid)) {
        if ($otherGuid -and $otherGuid -ne $tutorialBuildGuid) {
            throw "튜토리얼과 게임 실행 결과의 빌드 GUID가 다릅니다: $tutorialBuildGuid / $otherGuid"
        }
    }
}
if ($ResidentActivityRuntime) {
    $residentBuildGuid = Invoke-RuntimeSmoke -Name '주민 모델과 작업' -SmokeFlag '-riverworks-resident-activity-smoke' -OutputDirectory (Join-Path $artifactDirectory 'ResidentActivitySmoke') -ResultFileName 'resident-activity-results.txt' -LogFileName 'resident-activity-runtime.log' -SuccessMarker 'RIVERWORKS_RESIDENT_ACTIVITY_SMOKE_PASS' -PlayerPath $ExecutablePath -TimeoutSeconds $RuntimeTimeoutSeconds
    foreach ($otherGuid in @($runtimeBuildGuid, $factoryBuildGuid, $compactUiBuildGuid, $feelBuildGuid, $tutorialBuildGuid)) {
        if ($otherGuid -and $otherGuid -ne $residentBuildGuid) {
            throw "주민 활동과 게임 실행 결과의 빌드 GUID가 다릅니다: $residentBuildGuid / $otherGuid"
        }
    }
}
if ($ResearchTreeRuntime) {
    $researchBuildGuid = Invoke-RuntimeSmoke -Name '연결형 연구 지도' -SmokeFlag '-riverworks-research-tree-smoke' -OutputDirectory (Join-Path $artifactDirectory 'ResearchTreeSmoke') -ResultFileName 'research-tree-results.txt' -LogFileName 'research-tree-runtime.log' -SuccessMarker 'RIVERWORKS_RESEARCH_TREE_SMOKE_PASS' -PlayerPath $ExecutablePath -TimeoutSeconds $RuntimeTimeoutSeconds
    foreach ($otherGuid in @($runtimeBuildGuid, $factoryBuildGuid, $compactUiBuildGuid, $feelBuildGuid, $tutorialBuildGuid)) {
        if ($otherGuid -and $otherGuid -ne $researchBuildGuid) { throw '연구 지도 검사와 다른 게임 실행 결과의 빌드 GUID가 다릅니다.' }
    }
}
Write-Host '요청한 모든 테스트가 성공했습니다.'
