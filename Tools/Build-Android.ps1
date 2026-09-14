[CmdletBinding()]
param(
    [switch] $ExportGradle
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$versionFile = Join-Path $projectRoot 'ProjectSettings\ProjectVersion.txt'
$artifactDirectory = Join-Path $projectRoot 'Artifacts'
$buildLog = Join-Path $artifactDirectory 'android-build.log'
$resultPath = Join-Path $artifactDirectory 'android-build-result.txt'

if (-not (Test-Path -LiteralPath $versionFile -PathType Leaf)) {
    throw "Unity 버전 파일을 찾을 수 없습니다: $versionFile"
}
$versionLine = Get-Content -LiteralPath $versionFile -Encoding UTF8 | Where-Object { $_ -match '^m_EditorVersion:\s*(.+)$' } | Select-Object -First 1
if (-not $versionLine -or $versionLine -notmatch '^m_EditorVersion:\s*(.+)$') {
    throw 'ProjectVersion.txt에서 Unity 버전을 읽을 수 없습니다.'
}
$unityVersion = $Matches[1].Trim()

if ($env:UNITY_EDITOR_PATH) {
    $unityEditor = [System.IO.Path]::GetFullPath($env:UNITY_EDITOR_PATH)
} else {
    $portableEditor = Join-Path $env:LOCALAPPDATA "RiverworksBuildTools\Unity\$unityVersion\Editor\Unity.exe"
    $installedEditor = Join-Path $env:ProgramFiles "Unity\Hub\Editor\$unityVersion\Editor\Unity.exe"
    # A user-writable portable editor is preferred when it contains Android support.
    $portableAndroid = Join-Path (Split-Path $portableEditor -Parent) 'Data\PlaybackEngines\AndroidPlayer'
    if ((Test-Path -LiteralPath $portableEditor -PathType Leaf) -and (Test-Path -LiteralPath $portableAndroid -PathType Container)) {
        $unityEditor = $portableEditor
    } else {
        $unityEditor = $installedEditor
    }
}
if (-not (Test-Path -LiteralPath $unityEditor -PathType Leaf)) {
    throw "Unity $unityVersion 실행 파일을 찾지 못했습니다: $unityEditor"
}

$androidRoot = Join-Path (Split-Path (Split-Path $unityEditor -Parent) -Parent) 'Editor\Data\PlaybackEngines\AndroidPlayer'
$requiredTools = @(
    (Join-Path $androidRoot 'OpenJDK\bin\java.exe'),
    (Join-Path $androidRoot 'SDK\platform-tools'),
    (Join-Path $androidRoot 'SDK\build-tools'),
    (Join-Path $androidRoot 'NDK')
)
foreach ($requiredTool in $requiredTools) {
    if (-not (Test-Path -LiteralPath $requiredTool)) {
        throw "Unity Android Build Support가 완전히 설치되지 않았습니다: $requiredTool`nUnity Hub에서 Android Build Support, SDK/NDK Tools, OpenJDK를 설치하세요."
    }
}

New-Item -ItemType Directory -Path $artifactDirectory -Force | Out-Null
if (Test-Path -LiteralPath $resultPath) { Remove-Item -LiteralPath $resultPath -Force }
$method = if ($ExportGradle) { 'Riverworks.Editor.MobileBuildAutomation.ExportAndroidGradleProject' } else { 'Riverworks.Editor.MobileBuildAutomation.BuildAndroidApk' }
$arguments = @(
    '-batchmode', '-nographics', '-quit',
    '-buildTarget', 'Android',
    '-projectPath', ('"' + $projectRoot + '"'),
    '-executeMethod', $method,
    '-logFile', ('"' + $buildLog + '"')
) -join ' '

Write-Host "Android 빌드를 시작합니다: $method"
$process = Start-Process -FilePath $unityEditor -ArgumentList $arguments -WindowStyle Hidden -PassThru
$null = $process.Handle
$process.WaitForExit()
if ($process.ExitCode -ne 0) {
    throw "Unity Android 빌드가 종료 코드 $($process.ExitCode)로 실패했습니다. 로그: $buildLog"
}
if (-not (Test-Path -LiteralPath $resultPath -PathType Leaf)) {
    throw "Unity는 정상 종료했지만 이번 Android 빌드 결과 파일이 없습니다: $resultPath"
}
$resultText = Get-Content -LiteralPath $resultPath -Raw -Encoding UTF8
if ($resultText -notmatch '(?m)^SUCCESS\s*$') { throw "Android 빌드 결과가 성공이 아닙니다: $resultPath" }

$expectedOutput = if ($ExportGradle) { Join-Path $projectRoot 'Builds\Android\GradleProject' } else { Join-Path $projectRoot 'Builds\Android\Riverworks.apk' }
if (-not (Test-Path -LiteralPath $expectedOutput)) { throw "빌드 출력이 없습니다: $expectedOutput" }
Write-Host "Android 빌드 성공: $expectedOutput"
Write-Host "Unity 로그: $buildLog"
