[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [Alias('WindowsZipPath', 'ReleaseZipPath')]
    [ValidateNotNullOrEmpty()]
    [string]$ZipPath,

    [Alias('ChecksumPath')]
    [string]$Sha256Path,

    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$ExpectedVersion = '0.6.2',

    [Alias('AndroidApkPath')]
    [string]$ApkPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Resolve-InputFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Description
    )
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw "$Description 파일이 없습니다: $fullPath"
    }
    return $fullPath
}

function Get-EntryBytes {
    param(
        [Parameter(Mandatory = $true)]$Entry,
        [Parameter(Mandatory = $true)][int]$Count
    )
    $buffer = New-Object byte[] $Count
    $stream = $Entry.Open()
    try {
        $read = $stream.Read($buffer, 0, $buffer.Length)
    } finally {
        $stream.Dispose()
    }
    if ($read -lt $Count) {
        throw "ZIP 엔트리가 예상보다 짧습니다: $($Entry.FullName)"
    }
    return $buffer
}

function Get-EntryText {
    param([Parameter(Mandatory = $true)]$Entry)
    $stream = $Entry.Open()
    $reader = New-Object System.IO.StreamReader($stream, (New-Object System.Text.UTF8Encoding($false, $true)), $true)
    try {
        return $reader.ReadToEnd()
    } finally {
        $reader.Dispose()
        $stream.Dispose()
    }
}

function Assert-PeEntry {
    param([Parameter(Mandatory = $true)]$Entry)
    $header = Get-EntryBytes -Entry $Entry -Count 2
    if ($header[0] -ne 0x4D -or $header[1] -ne 0x5A) {
        throw "Windows PE 헤더가 올바르지 않습니다: $($Entry.FullName)"
    }
}

function Assert-PngEntry {
    param([Parameter(Mandatory = $true)]$Entry)
    $header = Get-EntryBytes -Entry $Entry -Count 8
    $expected = @(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A)
    for ($index = 0; $index -lt $expected.Count; $index++) {
        if ($header[$index] -ne $expected[$index]) {
            throw "PNG 헤더가 올바르지 않습니다: $($Entry.FullName)"
        }
    }
}

function Assert-ReleaseZip {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Version
    )

    $archive = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        foreach ($entry in $archive.Entries) {
            $name = $entry.FullName.Replace('\', '/')
            if ($name.StartsWith('/') -or $name -match '(^|/)\.\.(/|$)') {
                throw "안전하지 않은 ZIP 엔트리 경로가 있습니다: $($entry.FullName)"
            }
            if ($name -match '(?i)(^|/)(Artifacts|Library|Logs|Temp|UserSettings|obj|cache|\.git|\.codex)(/|$)' -or
                $name -match '(?i)\.(log|tmp|cache|bak|dmp|db|sqlite)$' -or
                $name -match '(?i)(^|/)output_log\.txt$' -or
                $name -match '(?i)(^|/)city-v1\.json(?:\..*)?$' -or
                $name -match '(?i)(^|/)(smoke-test|editor-save-test)\.json(?:\..*)?$' -or
                $name -match '(?i)(^|/)(\.env(?:\..*)?|credentials?(?:\..*)?|secrets?(?:\..*)?|api[-_]?keys?(?:\..*)?)$') {
                throw "개발/실행 상태 또는 민감 설정 파일은 릴리스 ZIP에 넣을 수 없습니다: $($entry.FullName)"
            }
        }

        $executableMatches = @($archive.Entries | Where-Object {
            $_.FullName.Replace('\', '/') -match '(^|/)Riverworks\.exe$'
        })
        if ($executableMatches.Count -ne 1) {
            throw "ZIP에는 Riverworks.exe가 정확히 하나 있어야 합니다: $Path"
        }
        $executableName = $executableMatches[0].FullName.Replace('\', '/')
        $prefixLength = $executableName.Length - 'Riverworks.exe'.Length
        $prefix = $executableName.Substring(0, $prefixLength)

        $requiredPaths = @(
            'Riverworks.exe',
            'UnityPlayer.dll',
            'UnityCrashHandler64.exe',
            'Riverworks_Data/Managed/Assembly-CSharp.dll',
            'Riverworks_Data/globalgamemanagers',
            'Riverworks_Data/level0',
            'Riverworks_Data/resources.assets',
            'Riverworks_Data/sharedassets0.assets',
            'MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll',
            'MonoBleedingEdge/EmbedRuntime/MonoPosixHelper.dll',
            'preview.png',
            'START-HERE.ko.txt',
            'PLAYER_GUIDE.ko.md',
            'ATTRIBUTION.md',
            'OFL.txt'
        )
        if ([Version]$Version -ge [Version]'0.5.0') {
            $requiredPaths += @('UI_ASSETS.md', 'KENNEY-UI-LICENSE.txt', 'KENNEY-ICONS-LICENSE.txt')
        }
        if ([Version]$Version -ge [Version]'0.6.0') {
            $requiredPaths += @(
                'Riverworks_Data/Managed/MoreMountains.Tools.dll', 'FEEL-LICENSE.txt', 'FEEL_GUIDE.ko.md',
                'TUTORIAL_GUIDE.ko.md', 'RESIDENT_AI.ko.md', 'PIXEL_FONT.md',
                'Galmuri-OFL.txt', 'Galmuri-ATTRIBUTION.md'
            )
        }
        if ([Version]$Version -ge [Version]'0.6.2') {
            $requiredPaths += @('UI_POLISH.ko.md', 'Images/research-v0.6.2.png', 'Images/guidance-v0.6.2.png')
        }
        $requiredEntries = @{}
        foreach ($relativePath in $requiredPaths) {
            $expectedPath = $prefix + $relativePath
            $matches = @($archive.Entries | Where-Object {
                $_.FullName.Replace('\', '/') -ieq $expectedPath
            })
            if ($matches.Count -ne 1) {
                throw "ZIP 필수 파일이 없거나 중복되었습니다: $relativePath"
            }
            if ($matches[0].Length -le 0) {
                throw "ZIP 필수 파일이 비어 있습니다: $relativePath"
            }
            $requiredEntries[$relativePath] = $matches[0]
        }

        Assert-PeEntry -Entry $requiredEntries['Riverworks.exe']
        Assert-PeEntry -Entry $requiredEntries['UnityPlayer.dll']
        Assert-PeEntry -Entry $requiredEntries['UnityCrashHandler64.exe']
        Assert-PeEntry -Entry $requiredEntries['Riverworks_Data/Managed/Assembly-CSharp.dll']
        Assert-PeEntry -Entry $requiredEntries['MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll']
        Assert-PeEntry -Entry $requiredEntries['MonoBleedingEdge/EmbedRuntime/MonoPosixHelper.dll']
        Assert-PngEntry -Entry $requiredEntries['preview.png']

        $startText = Get-EntryText -Entry $requiredEntries['START-HERE.ko.txt']
        if ($startText -notmatch ('(?<![0-9.])' + [Regex]::Escape($Version) + '(?![0-9.])')) {
            throw "START-HERE.ko.txt에 릴리스 버전 $Version 표시가 없습니다."
        }
        $metadataBytes = Get-EntryBytes -Entry $requiredEntries['Riverworks_Data/globalgamemanagers'] -Count ([int]$requiredEntries['Riverworks_Data/globalgamemanagers'].Length)
        $metadataText = [System.Text.Encoding]::ASCII.GetString($metadataBytes)
        if ($metadataText.IndexOf($Version, [StringComparison]::Ordinal) -lt 0) {
            throw "globalgamemanagers 내부 플레이어 버전이 $Version이 아닙니다."
        }

        Write-Host "Windows 릴리스 ZIP 검증 성공: $Path"
        Write-Host "필수 파일 $($requiredPaths.Count)개, 패키지 엔트리 $($archive.Entries.Count)개"
    } finally {
        $archive.Dispose()
    }
}

function Assert-Sha256Sidecar {
    param(
        [Parameter(Mandatory = $true)][string]$ArchivePath,
        [Parameter(Mandatory = $true)][string]$SidecarPath
    )

    $sidecarText = Get-Content -LiteralPath $SidecarPath -Raw -Encoding UTF8
    $match = [Regex]::Match($sidecarText, '^\s*([0-9a-fA-F]{64})(?:\s+\*?([^\r\n]+))?\s*$')
    if (-not $match.Success) {
        throw "SHA-256 사이드카 형식이 올바르지 않습니다: $SidecarPath"
    }
    if ($match.Groups[2].Success -and
        ([System.IO.Path]::GetFileName($match.Groups[2].Value.Trim()) -ine [System.IO.Path]::GetFileName($ArchivePath))) {
        throw "SHA-256 사이드카의 파일명이 릴리스 ZIP과 다릅니다: $SidecarPath"
    }
    $actualHash = (Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash
    if ($actualHash -ine $match.Groups[1].Value) {
        throw "릴리스 ZIP의 SHA-256이 사이드카와 일치하지 않습니다: $SidecarPath"
    }
    Write-Host "SHA-256 사이드카 검증 성공: $SidecarPath"
}

function Assert-AndroidApk {
    param([Parameter(Mandatory = $true)][string]$Path)

    # APK도 ZIP 컨테이너이므로 Android SDK나 aapt 없이 엔트리만 검사한다.
    $archive = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $requiredPaths = @(
            'AndroidManifest.xml',
            'classes.dex',
            'lib/arm64-v8a/libunity.so',
            'lib/arm64-v8a/libil2cpp.so'
        )
        foreach ($requiredPath in $requiredPaths) {
            $matches = @($archive.Entries | Where-Object {
                $_.FullName.Replace('\', '/') -ieq $requiredPath
            })
            if ($matches.Count -ne 1) {
                throw "APK 필수 파일이 없거나 중복되었습니다: $requiredPath"
            }
            if ($matches[0].Length -le 0) {
                throw "APK 필수 파일이 비어 있습니다: $requiredPath"
            }
        }
        Write-Host "Android APK ZIP 구조 검증 성공: $Path"
    } finally {
        $archive.Dispose()
    }
}

Add-Type -AssemblyName System.IO.Compression.FileSystem

$resolvedZipPath = Resolve-InputFile -Path $ZipPath -Description 'Windows 릴리스 ZIP'
if (-not $Sha256Path) {
    $Sha256Path = $resolvedZipPath + '.sha256'
}
$resolvedSha256Path = Resolve-InputFile -Path $Sha256Path -Description 'SHA-256 사이드카'
Assert-ReleaseZip -Path $resolvedZipPath -Version $ExpectedVersion
Assert-Sha256Sidecar -ArchivePath $resolvedZipPath -SidecarPath $resolvedSha256Path

if ($ApkPath) {
    $resolvedApkPath = Resolve-InputFile -Path $ApkPath -Description 'Android APK'
    Assert-AndroidApk -Path $resolvedApkPath
} else {
    Write-Host 'Android APK 경로가 지정되지 않아 APK 검증을 건너뜁니다.'
}
