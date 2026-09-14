[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '0.6.1',

    [string]$WindowsBuildPath,

    [string]$OutputDirectory,

    [string]$PreviewPath,

    [string]$MoviePath,

    [switch]$IncludeGateway = $true,

    [switch]$Force
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $WindowsBuildPath) { $WindowsBuildPath = Join-Path $projectRoot 'Builds\Windows' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $projectRoot 'Builds' }
$windowsBuild = [System.IO.Path]::GetFullPath($WindowsBuildPath)
$releaseOutput = [System.IO.Path]::GetFullPath($OutputDirectory)

function Assert-ExistingFile {
    param([Parameter(Mandatory = $true)][string]$Path, [Parameter(Mandatory = $true)][string]$Label)
    $resolved = [System.IO.Path]::GetFullPath($Path)
    if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) { throw "$Label 파일이 없습니다: $resolved" }
    if ((Get-Item -LiteralPath $resolved).Length -le 0) { throw "$Label 파일이 비어 있습니다: $resolved" }
    return $resolved
}

function Assert-ExistingDirectory {
    param([Parameter(Mandatory = $true)][string]$Path, [Parameter(Mandatory = $true)][string]$Label)
    $resolved = [System.IO.Path]::GetFullPath($Path)
    if (-not (Test-Path -LiteralPath $resolved -PathType Container)) { throw "$Label 폴더가 없습니다: $resolved" }
    return $resolved
}

function Assert-OutputPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    $resolved = [System.IO.Path]::GetFullPath($Path)
    $outputPrefix = $releaseOutput.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($outputPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "출력 파일은 지정된 출력 폴더 안에 있어야 합니다: $resolved"
    }
    if (Test-Path -LiteralPath $resolved) {
        if (-not $Force) { throw "출력 파일이 이미 있습니다. 덮어쓰려면 -Force를 지정하세요: $resolved" }
        Remove-Item -LiteralPath $resolved -Force
    }
    return $resolved
}

function Copy-ReleaseFile {
    param([Parameter(Mandatory = $true)][string]$Source, [Parameter(Mandatory = $true)][string]$RelativeDestination)
    $sourceFile = Assert-ExistingFile -Path $Source -Label $RelativeDestination
    $destination = Join-Path $script:packageRoot $RelativeDestination
    $destinationDirectory = Split-Path -Parent $destination
    if (-not (Test-Path -LiteralPath $destinationDirectory -PathType Container)) {
        New-Item -ItemType Directory -Path $destinationDirectory | Out-Null
    }
    Copy-Item -LiteralPath $sourceFile -Destination $destination
}

function Copy-ReleaseDirectory {
    param([Parameter(Mandatory = $true)][string]$Source, [Parameter(Mandatory = $true)][string]$RelativeDestination)
    $sourceDirectory = Assert-ExistingDirectory -Path $Source -Label $RelativeDestination
    $destination = Join-Path $script:packageRoot $RelativeDestination
    New-Item -ItemType Directory -Path $destination | Out-Null
    foreach ($child in Get-ChildItem -LiteralPath $sourceDirectory -Force) {
        Copy-Item -LiteralPath $child.FullName -Destination $destination -Recurse
    }
}

function New-ExplicitZip {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string[]]$AllowedRelativePaths,
        [Parameter(Mandatory = $true)][string]$Destination
    )
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::Open($Destination, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($relativePath in $AllowedRelativePaths) {
            $itemPath = Join-Path $Root $relativePath
            if (Test-Path -LiteralPath $itemPath -PathType Leaf) {
                [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                    $archive, $itemPath, $relativePath.Replace('\', '/'),
                    [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
                continue
            }
            if (-not (Test-Path -LiteralPath $itemPath -PathType Container)) {
                throw "ZIP 허용 목록 항목이 없습니다: $itemPath"
            }
            $itemRoot = [System.IO.Path]::GetFullPath($itemPath).TrimEnd([System.IO.Path]::DirectorySeparatorChar)
            foreach ($file in Get-ChildItem -LiteralPath $itemRoot -Recurse -File) {
                $child = $file.FullName.Substring($itemRoot.Length).TrimStart([System.IO.Path]::DirectorySeparatorChar)
                $entryName = (Join-Path $relativePath $child).Replace('\', '/')
                [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                    $archive, $file.FullName, $entryName,
                    [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
            }
        }
    } finally {
        $archive.Dispose()
    }
}

function Write-Sha256Sidecar {
    param(
        [Parameter(Mandatory = $true)][string]$ArchivePath,
        [Parameter(Mandatory = $true)][string]$SidecarPath
    )
    $hash = (Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    [System.IO.File]::WriteAllText($SidecarPath, "$hash  $([System.IO.Path]::GetFileName($ArchivePath))`r`n", (New-Object System.Text.UTF8Encoding($false)))
    return $SidecarPath
}

if (-not (Test-Path -LiteralPath $releaseOutput -PathType Container)) {
    New-Item -ItemType Directory -Path $releaseOutput -Force | Out-Null
}
$stagingParent = Join-Path $releaseOutput '.release-staging'
New-Item -ItemType Directory -Path $stagingParent -Force | Out-Null
$stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
$stagingRoot = Join-Path $stagingParent ("$stamp-$([Guid]::NewGuid().ToString('N'))")
New-Item -ItemType Directory -Path $stagingRoot | Out-Null

$script:packageRoot = Join-Path $stagingRoot "Riverworks-v$Version-Windows-x64"
New-Item -ItemType Directory -Path $script:packageRoot | Out-Null

$runtimeFiles = @('Riverworks.exe', 'UnityPlayer.dll', 'UnityCrashHandler64.exe', 'dstorage.dll', 'dstoragecore.dll')
$runtimeDirectories = @('Riverworks_Data', 'MonoBleedingEdge', 'D3D12')
foreach ($name in $runtimeFiles) { Copy-ReleaseFile -Source (Join-Path $windowsBuild $name) -RelativeDestination $name }
foreach ($name in $runtimeDirectories) { Copy-ReleaseDirectory -Source (Join-Path $windowsBuild $name) -RelativeDestination $name }

$documentMap = [ordered]@{
    'PLAYER_GUIDE.ko.md' = 'Docs\PLAYER_GUIDE.ko.md'
    'FACTORY_GUIDE.ko.md' = 'Docs\FACTORY_GUIDE.ko.md'
    'UI_ASSETS.md' = 'Docs\UI_ASSETS.md'
    'FEEL_GUIDE.ko.md' = 'Docs\FEEL_GUIDE.ko.md'
    'TUTORIAL_GUIDE.ko.md' = 'Docs\TUTORIAL_GUIDE.ko.md'
    'RESIDENT_AI.ko.md' = 'Docs\RESIDENT_AI.ko.md'
    'PIXEL_FONT.md' = 'Docs\PIXEL_FONT.md'
    'RESIDENT_VISUALS.ko.md' = 'Docs\RESIDENT_VISUALS.ko.md'
    'VERIFICATION.md' = 'Docs\VERIFICATION.md'
    'ATTRIBUTION.md' = 'Assets\Resources\Fonts\ATTRIBUTION.md'
    'OFL.txt' = 'Assets\Resources\Fonts\OFL.txt'
    'Galmuri-ATTRIBUTION.md' = 'Assets\Resources\Fonts\Galmuri-ATTRIBUTION.md'
    'Galmuri-OFL.txt' = 'Assets\Resources\Fonts\Galmuri-OFL.txt'
    'KENNEY-UI-LICENSE.txt' = 'Assets\Resources\UI\Kenney\LICENSE_UI_PACK.txt'
    'KENNEY-ICONS-LICENSE.txt' = 'Assets\Resources\UI\Kenney\LICENSE_GAME_ICONS.txt'
    'FEEL-LICENSE.txt' = 'Assets\Feel\license.txt'
    'QUATERNIUS-CHARACTERS-LICENSE-CC0.txt' = 'Assets\ThirdParty\Quaternius\Characters\LICENSE-CC0.txt'
    'QUATERNIUS-ANIMATIONS-LICENSE-CC0.txt' = 'Assets\ThirdParty\Quaternius\Characters\Animations\LICENSE-CC0.txt'
    'QUATERNIUS-ANIMATIONS-SOURCE-README.txt' = 'Assets\ThirdParty\Quaternius\Characters\Animations\SOURCE-README.txt'
    'QUATERNIUS-PROVENANCE.md' = 'Assets\ThirdParty\Quaternius\Characters\PROVENANCE.md'
}
foreach ($entry in $documentMap.GetEnumerator()) {
    Copy-ReleaseFile -Source (Join-Path $projectRoot $entry.Value) -RelativeDestination $entry.Key
}

if ($PreviewPath) {
    $selectedPreview = Assert-ExistingFile -Path $PreviewPath -Label '릴리스 미리보기'
} else {
    $preferredPreview = Join-Path $projectRoot 'Artifacts\ResidentActivitySmoke\01-resident-activity-overview-1600x900.png'
    if (Test-Path -LiteralPath $preferredPreview -PathType Leaf) {
        $selectedPreview = $preferredPreview
    } else {
        $tutorialPreview = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Artifacts\TutorialSmoke') -File -Filter '*.png' -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
        if (-not $tutorialPreview) {
            throw '최신 preview가 없습니다. -PreviewPath 또는 주민 활동 실행 검사의 overview PNG를 준비하세요.'
        }
        $selectedPreview = $tutorialPreview.FullName
    }
}
Copy-ReleaseFile -Source $selectedPreview -RelativeDestination 'preview.png'

if (-not $MoviePath) {
    $residentMovieDirectory = Join-Path $projectRoot 'Artifacts\ResidentActivitySmoke'
    if (Test-Path -LiteralPath $residentMovieDirectory -PathType Container) {
        $latestResidentMovie = Get-ChildItem -LiteralPath $residentMovieDirectory -File -Filter '*.mp4' |
            Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
        if ($latestResidentMovie) { $MoviePath = $latestResidentMovie.FullName }
    }
}
if ($MoviePath) {
    $resolvedMovie = [System.IO.Path]::GetFullPath($MoviePath)
    if ([System.IO.Path]::GetFileName($resolvedMovie) -ieq 'factory-process.mp4' -or
        $resolvedMovie.StartsWith($windowsBuild.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw '과거 Windows 빌드의 factory 영상은 재사용할 수 없습니다. 이번 런타임에서 새로 녹화한 영상만 -MoviePath로 지정하세요.'
    }
    Copy-ReleaseFile -Source $resolvedMovie -RelativeDestination 'gameplay.mp4'
}

$startHere = @"
Riverworks v$Version - Windows x64

실행 방법
1. ZIP 전체를 원하는 폴더에 압축 해제합니다.
2. Riverworks.exe를 실행합니다. Riverworks_Data 등 동봉 폴더를 옮기거나 삭제하지 마세요.
3. 처음 시작하면 게임 안의 튜토리얼 안내를 따라 도시와 공장 작업을 진행하세요.

주요 안내
- PLAYER_GUIDE.ko.md: 조작과 기본 플레이
- FACTORY_GUIDE.ko.md: 공장 배치, 생산, 업그레이드
- TUTORIAL_GUIDE.ko.md: 튜토리얼 진행
- RESIDENT_VISUALS.ko.md: 주민 역할과 행동 표시
- RESIDENT_AI.ko.md: 주민 AI 기능과 로컬 게이트웨이 설정

주민 AI의 외부 LLM 기능은 선택 사항입니다. API 키가 없으면 외부 LLM 호출은 비활성화되며,
게임의 기본 시뮬레이션과 규칙 기반 주민 기능은 계속 사용할 수 있습니다. API 키는 게임 ZIP에
포함하지 말고 RESIDENT_AI.ko.md의 로컬 환경 변수 안내에 따라 직접 설정하세요.
"@
[System.IO.File]::WriteAllText((Join-Path $script:packageRoot 'START-HERE.ko.txt'), $startHere, (New-Object System.Text.UTF8Encoding($true)))

$windowsAllowed = @($runtimeFiles + $runtimeDirectories + @($documentMap.Keys) + @('preview.png', 'START-HERE.ko.txt'))
if ($MoviePath) { $windowsAllowed += 'gameplay.mp4' }
$windowsZip = Assert-OutputPath -Path (Join-Path $releaseOutput "Riverworks-v$Version-Windows-x64.zip")
$windowsShaPath = Assert-OutputPath -Path ($windowsZip + '.sha256')
New-ExplicitZip -Root $script:packageRoot -AllowedRelativePaths $windowsAllowed -Destination $windowsZip
$windowsSha = Write-Sha256Sidecar -ArchivePath $windowsZip -SidecarPath $windowsShaPath

Write-Host "Windows ZIP: $windowsZip"
Write-Host "Windows SHA-256: $windowsSha"
Write-Host "검토용 staging(자동 삭제하지 않음): $stagingRoot"

if ($IncludeGateway) {
    $script:packageRoot = Join-Path $stagingRoot "Riverworks-Resident-Gateway-v$Version"
    New-Item -ItemType Directory -Path $script:packageRoot | Out-Null
    Copy-ReleaseDirectory -Source (Join-Path $projectRoot 'Server\src') -RelativeDestination 'src'
    Copy-ReleaseFile -Source (Join-Path $projectRoot 'Server\package.json') -RelativeDestination 'package.json'
    Copy-ReleaseFile -Source (Join-Path $projectRoot 'Server\start.ps1') -RelativeDestination 'start.ps1'
    Copy-ReleaseFile -Source (Join-Path $projectRoot 'Server\.env.example') -RelativeDestination '.env.example'
    Copy-ReleaseFile -Source (Join-Path $projectRoot 'Docs\RESIDENT_AI.ko.md') -RelativeDestination 'RESIDENT_AI.ko.md'
    Copy-ReleaseFile -Source (Join-Path $projectRoot 'Docs\VERIFICATION.md') -RelativeDestination 'VERIFICATION.md'
    $gatewayGuidePath = Join-Path $script:packageRoot 'RESIDENT_AI.ko.md'
    $gatewayGuideText = [IO.File]::ReadAllText($gatewayGuidePath).Replace('Server/.env', '.env').Replace('.\Server\start.ps1', '.\start.ps1')
    [IO.File]::WriteAllText($gatewayGuidePath, $gatewayGuideText, (New-Object System.Text.UTF8Encoding($true)))
    $gatewayStart = @"
Riverworks Resident Gateway v$Version

이 ZIP은 선택 기능인 로컬 주민 AI 게이트웨이 소스입니다. Node.js 22 이상이 필요합니다.
1. .env.example을 별도 .env로 복사하고 본인의 공급자 키를 입력합니다.
2. PowerShell에서 .\start.ps1을 실행합니다.
3. 자세한 설정과 보안 경계는 RESIDENT_AI.ko.md를 확인합니다.

실제 .env와 공급자 키는 배포 ZIP에 포함되어 있지 않습니다.
"@
    [System.IO.File]::WriteAllText((Join-Path $script:packageRoot 'START-HERE.ko.txt'), $gatewayStart, (New-Object System.Text.UTF8Encoding($true)))

    $forbidden = Get-ChildItem -LiteralPath $script:packageRoot -Recurse -Force | Where-Object {
        $_.Name -match '^(\.env|\.data|logs?|node_modules|test|tests)$' -and $_.Name -ne '.env.example'
    }
    if ($forbidden) { throw "게이트웨이 staging에 금지 항목이 있습니다: $($forbidden.FullName -join ', ')" }
    foreach ($textFile in Get-ChildItem -LiteralPath $script:packageRoot -Recurse -File) {
        $text = [System.IO.File]::ReadAllText($textFile.FullName)
        if ($text -match '(?i)(sk-[a-z0-9_-]{16,}|AIza[0-9A-Za-z_-]{20,}|gh[pousr]_[A-Za-z0-9]{20,})') {
            throw "게이트웨이 staging에서 실제 키로 보이는 문자열을 발견했습니다: $($textFile.FullName)"
        }
    }

    $gatewayAllowed = @('src', 'package.json', 'start.ps1', '.env.example', 'RESIDENT_AI.ko.md', 'VERIFICATION.md', 'START-HERE.ko.txt')
    $gatewayZip = Assert-OutputPath -Path (Join-Path $releaseOutput "Riverworks-Resident-Gateway-v$Version.zip")
    $gatewayShaPath = Assert-OutputPath -Path ($gatewayZip + '.sha256')
    New-ExplicitZip -Root $script:packageRoot -AllowedRelativePaths $gatewayAllowed -Destination $gatewayZip
    $gatewaySha = Write-Sha256Sidecar -ArchivePath $gatewayZip -SidecarPath $gatewayShaPath
    Write-Host "Gateway ZIP: $gatewayZip"
    Write-Host "Gateway SHA-256: $gatewaySha"
}
