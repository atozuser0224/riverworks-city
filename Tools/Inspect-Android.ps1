[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$ApkPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactDirectory = Join-Path $projectRoot 'Artifacts'
$reportPath = Join-Path $artifactDirectory 'android-inspection.json'

if ([string]::IsNullOrWhiteSpace($ApkPath)) {
    $ApkPath = Join-Path $projectRoot 'Builds\Android\Riverworks.apk'
} elseif (-not [System.IO.Path]::IsPathRooted($ApkPath)) {
    $ApkPath = Join-Path $projectRoot $ApkPath
}
$ApkPath = [System.IO.Path]::GetFullPath($ApkPath)

function Write-InspectionReport {
    param([Parameter(Mandatory = $true)]$Report)

    New-Item -ItemType Directory -Path $artifactDirectory -Force | Out-Null
    $json = $Report | ConvertTo-Json -Depth 8
    [System.IO.File]::WriteAllText(
        $reportPath,
        $json + [Environment]::NewLine,
        (New-Object System.Text.UTF8Encoding($true))
    )
}

function Add-UniqueDirectory {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [System.Collections.Generic.List[string]]$Directories,
        [AllowNull()]
        [AllowEmptyString()]
        [string]$Path
    )

    if ([string]::IsNullOrWhiteSpace($Path)) { return }
    try {
        $fullPath = [System.IO.Path]::GetFullPath($Path)
    } catch {
        return
    }
    foreach ($existing in $Directories) {
        if ([string]::Equals($existing, $fullPath, [System.StringComparison]::OrdinalIgnoreCase)) {
            return
        }
    }
    $Directories.Add($fullPath)
}

function Get-UnityVersion {
    $versionFile = Join-Path $projectRoot 'ProjectSettings\ProjectVersion.txt'
    if (-not (Test-Path -LiteralPath $versionFile -PathType Leaf)) { return $null }
    $versionLine = Get-Content -LiteralPath $versionFile -Encoding UTF8 |
        Where-Object { $_ -match '^m_EditorVersion:\s*(.+)$' } |
        Select-Object -First 1
    if ($versionLine -and $versionLine -match '^m_EditorVersion:\s*(.+)$') {
        return $Matches[1].Trim()
    }
    return $null
}

function Get-AndroidToolLocations {
    $sdkRoots = New-Object 'System.Collections.Generic.List[string]'
    $javaCandidates = New-Object 'System.Collections.Generic.List[string]'
    $unityVersion = Get-UnityVersion

    $androidPlayerRoots = New-Object 'System.Collections.Generic.List[string]'
    if ($env:UNITY_EDITOR_PATH) {
        $editorDirectory = Split-Path ([System.IO.Path]::GetFullPath($env:UNITY_EDITOR_PATH)) -Parent
        Add-UniqueDirectory -Directories $androidPlayerRoots -Path (Join-Path $editorDirectory 'Data\PlaybackEngines\AndroidPlayer')
    }
    if ($unityVersion -and $env:LOCALAPPDATA) {
        Add-UniqueDirectory -Directories $androidPlayerRoots -Path (
            Join-Path $env:LOCALAPPDATA "RiverworksBuildTools\Unity\$unityVersion\Editor\Data\PlaybackEngines\AndroidPlayer"
        )
    }
    if ($unityVersion -and $env:ProgramFiles) {
        Add-UniqueDirectory -Directories $androidPlayerRoots -Path (
            Join-Path $env:ProgramFiles "Unity\Hub\Editor\$unityVersion\Editor\Data\PlaybackEngines\AndroidPlayer"
        )
    }

    foreach ($androidPlayerRoot in $androidPlayerRoots) {
        Add-UniqueDirectory -Directories $sdkRoots -Path (Join-Path $androidPlayerRoot 'SDK')
        $javaCandidates.Add((Join-Path $androidPlayerRoot 'OpenJDK\bin\java.exe'))
    }
    Add-UniqueDirectory -Directories $sdkRoots -Path $env:ANDROID_SDK_ROOT
    Add-UniqueDirectory -Directories $sdkRoots -Path $env:ANDROID_HOME
    if ($env:LOCALAPPDATA) {
        Add-UniqueDirectory -Directories $sdkRoots -Path (Join-Path $env:LOCALAPPDATA 'Android\Sdk')
    }
    if ($env:JAVA_HOME) {
        $javaCandidates.Add((Join-Path $env:JAVA_HOME 'bin\java.exe'))
    }

    $aapt2 = $null
    $apksigner = $null
    foreach ($sdkRoot in $sdkRoots) {
        if (-not (Test-Path -LiteralPath $sdkRoot -PathType Container)) { continue }
        $buildToolsRoot = Join-Path $sdkRoot 'build-tools'
        if (-not (Test-Path -LiteralPath $buildToolsRoot -PathType Container)) { continue }

        $versions = @(Get-ChildItem -LiteralPath $buildToolsRoot -Directory -ErrorAction SilentlyContinue |
            Sort-Object @{ Expression = {
                $parsedVersion = New-Object System.Version
                if ([System.Version]::TryParse($_.Name, [ref]$parsedVersion)) { $parsedVersion } else { [System.Version]'0.0' }
            }; Descending = $true }, @{ Expression = { $_.Name }; Descending = $true })
        foreach ($versionDirectory in $versions) {
            if (-not $aapt2) {
                $candidate = Join-Path $versionDirectory.FullName 'aapt2.exe'
                if (Test-Path -LiteralPath $candidate -PathType Leaf) { $aapt2 = $candidate }
            }
            if (-not $apksigner) {
                $candidate = Join-Path $versionDirectory.FullName 'apksigner.bat'
                if (Test-Path -LiteralPath $candidate -PathType Leaf) { $apksigner = $candidate }
            }
            if ($aapt2 -and $apksigner) { break }
        }
        if ($aapt2 -and $apksigner) { break }
    }

    $java = $null
    foreach ($candidate in $javaCandidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            $java = [System.IO.Path]::GetFullPath($candidate)
            break
        }
    }

    [pscustomobject]@{
        Aapt2 = $aapt2
        ApkSigner = $apksigner
        Java = $java
    }
}

function Invoke-NativeTool {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )

    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $output = @(& $FilePath @Arguments 2>&1 | ForEach-Object { $_.ToString() })
        $exitCode = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previousPreference
    }
    [pscustomobject]@{
        ExitCode = $exitCode
        Output = ($output -join [Environment]::NewLine)
    }
}

$failures = New-Object 'System.Collections.Generic.List[string]'
$report = [ordered]@{
    schemaVersion = 1
    inspectedAtUtc = [DateTime]::UtcNow.ToString('o')
    status = 'failed'
    apk = [ordered]@{
        path = $ApkPath
        sizeBytes = $null
        sha256 = $null
    }
    archive = [ordered]@{
        status = 'not_run'
        required = [ordered]@{
            'AndroidManifest.xml' = $false
            'lib/arm64-v8a/libil2cpp.so' = $false
            'lib/arm64-v8a/libunity.so' = $false
            'assets/bin/Data/' = $false
        }
        missing = @()
    }
    manifest = [ordered]@{
        status = 'not_run'
        packageName = $null
        minSdkVersion = $null
        targetSdkVersion = $null
        tool = $null
    }
    signature = [ordered]@{
        status = 'not_run'
        verified = $null
        tool = $null
    }
    failures = @()
}

if (-not (Test-Path -LiteralPath $ApkPath -PathType Leaf)) {
    $failures.Add("APK file does not exist: $ApkPath")
    $report.failures = $failures.ToArray()
    Write-InspectionReport -Report $report
    throw "Android APK inspection failed because the APK is missing. Report: $reportPath"
}

$apkFile = Get-Item -LiteralPath $ApkPath
$report.apk.sizeBytes = $apkFile.Length
$report.apk.sha256 = (Get-FileHash -LiteralPath $ApkPath -Algorithm SHA256).Hash.ToLowerInvariant()

try {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead($ApkPath)
    try {
        $entryNames = @($zip.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
        $report.archive.required['AndroidManifest.xml'] = $entryNames -ccontains 'AndroidManifest.xml'
        $report.archive.required['lib/arm64-v8a/libil2cpp.so'] = $entryNames -ccontains 'lib/arm64-v8a/libil2cpp.so'
        $report.archive.required['lib/arm64-v8a/libunity.so'] = $entryNames -ccontains 'lib/arm64-v8a/libunity.so'
        $report.archive.required['assets/bin/Data/'] = @($entryNames | Where-Object {
            $_.StartsWith('assets/bin/Data/', [System.StringComparison]::Ordinal)
        }).Count -gt 0

        $missingEntries = @($report.archive.required.GetEnumerator() |
            Where-Object { -not $_.Value } |
            ForEach-Object { $_.Key })
        $report.archive.missing = $missingEntries
        if ($missingEntries.Count -gt 0) {
            $report.archive.status = 'failed'
            $failures.Add('Missing essential APK entries: ' + ($missingEntries -join ', '))
        } else {
            $report.archive.status = 'passed'
        }
    } finally {
        $zip.Dispose()
    }
} catch {
    $report.archive.status = 'failed'
    $failures.Add('APK is not a readable ZIP archive.')
}

$tools = Get-AndroidToolLocations
if ($tools.Aapt2) {
    $report.manifest.tool = $tools.Aapt2
    $aaptResult = Invoke-NativeTool -FilePath $tools.Aapt2 -Arguments @('dump', 'badging', $ApkPath)
    if ($aaptResult.ExitCode -ne 0) {
        $report.manifest.status = 'failed'
        $failures.Add('aapt2 could not inspect AndroidManifest.xml.')
    } else {
        if ($aaptResult.Output -match "(?m)^package:\s+name='([^']+)'") {
            $report.manifest.packageName = $Matches[1]
        }
        if ($aaptResult.Output -match "(?m)^(?:sdkVersion|minSdkVersion):'([^']+)'") {
            $report.manifest.minSdkVersion = $Matches[1]
        }
        if ($aaptResult.Output -match "(?m)^targetSdkVersion:'([^']+)'") {
            $report.manifest.targetSdkVersion = $Matches[1]
        }
        if (-not ($report.manifest.packageName -and $report.manifest.minSdkVersion -and $report.manifest.targetSdkVersion)) {
            $report.manifest.status = 'failed'
            $failures.Add('aapt2 output did not include package, minimum SDK, and target SDK metadata.')
        } elseif ($report.manifest.minSdkVersion -ne '26') {
            $report.manifest.status = 'failed'
            $failures.Add("Android minimum SDK must be 26, but the APK declares $($report.manifest.minSdkVersion).")
        } else {
            $report.manifest.status = 'passed'
        }
    }
} else {
    $report.manifest.status = 'not_available'
}

if ($tools.ApkSigner -and $tools.Java) {
    $report.signature.tool = $tools.ApkSigner
    $oldJavaHome = $env:JAVA_HOME
    $oldPath = $env:PATH
    try {
        $javaHome = Split-Path (Split-Path $tools.Java -Parent) -Parent
        $env:JAVA_HOME = $javaHome
        $env:PATH = (Join-Path $javaHome 'bin') + [System.IO.Path]::PathSeparator + $oldPath
        # Do not pass --print-certs: the report intentionally contains no certificate or keystore details.
        $signingResult = Invoke-NativeTool -FilePath $tools.ApkSigner -Arguments @('verify', $ApkPath)
        if ($signingResult.ExitCode -eq 0) {
            $report.signature.status = 'passed'
            $report.signature.verified = $true
        } else {
            $report.signature.status = 'failed'
            $report.signature.verified = $false
            $failures.Add('apksigner verification failed.')
        }
    } finally {
        $env:JAVA_HOME = $oldJavaHome
        $env:PATH = $oldPath
    }
} elseif ($tools.ApkSigner) {
    $report.signature.status = 'not_available'
    $report.signature.tool = $tools.ApkSigner
} else {
    $report.signature.status = 'not_available'
}

$report.failures = $failures.ToArray()
if ($failures.Count -eq 0) {
    $report.status = 'passed'
}
Write-InspectionReport -Report $report

if ($report.status -ne 'passed') {
    throw "Android APK inspection failed. Report: $reportPath"
}

Write-Host "Android APK inspection passed: $ApkPath"
Write-Host "Inspection report: $reportPath"
