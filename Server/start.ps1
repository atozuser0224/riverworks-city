$ErrorActionPreference = 'Stop'

$nodeVersionText = (& node --version)
if ($LASTEXITCODE -ne 0 -or $nodeVersionText -notmatch '^v(?<Major>\d+)\.') {
    throw 'Node.js 22 or newer is required.'
}

if ([int]$Matches.Major -lt 22) {
    throw 'Node.js 22 or newer is required.'
}

Push-Location -LiteralPath $PSScriptRoot
try {
    & node 'src/index.js'
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
