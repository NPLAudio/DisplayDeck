# Builds, tests and packs the Stream Deck plugin into dist\com.nplaudio.displaydeck.streamDeckPlugin.
# Needs Node.js 24 or newer (Elgato's SDK requirement). Looks on PATH, then in an nvm-windows install.
# Prints the package path on success. With -Optional, a missing Node 24 is a warning instead of an error.
# The caller's working directory and PATH are restored however this script ends.
param([switch]$Optional)
$ErrorActionPreference = 'Stop'

function Find-Node24 {
    $onPath = Get-Command node -ErrorAction SilentlyContinue
    if ($onPath -and ([version]((& $onPath.Source -v).TrimStart('v'))).Major -ge 24) { return Split-Path $onPath.Source }
    $nvm = if ($env:NVM_HOME) { $env:NVM_HOME } else { Join-Path $env:LOCALAPPDATA 'nvm' }
    Get-ChildItem $nvm -Directory -Filter 'v*' -ErrorAction SilentlyContinue |
        Where-Object { [version]($_.Name.TrimStart('v')) -ge [version]'24.0.0' } |
        Sort-Object { [version]($_.Name.TrimStart('v')) } -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}

$originalPath = $env:Path
Push-Location $PSScriptRoot
try {
    $dir = Find-Node24
    if (-not $dir) {
        $msg = 'Node.js 24 or newer is needed to build the Stream Deck plugin (install it with nvm-windows: nvm install 24).'
        if ($Optional) { Write-Warning "$msg Skipping the plugin."; return }
        throw $msg
    }

    # npm and rollup write progress to stderr, which Windows PowerShell 5.1 would treat as an error under 'Stop'.
    # Failures are caught by the exit-code checks below instead.
    $ErrorActionPreference = 'Continue'
    $env:Path = "$dir;$env:Path"
    Write-Host "Using Node $(node -v) from $dir"

    if (-not (Test-Path node_modules)) { npm install --no-audit --no-fund; if ($LASTEXITCODE -ne 0) { throw 'npm install failed' } }
    npm test; if ($LASTEXITCODE -ne 0) { throw 'plugin tests failed' }
    npm run pack; if ($LASTEXITCODE -ne 0) { throw 'plugin pack failed' }

    $out = Join-Path $PSScriptRoot 'dist\com.nplaudio.displaydeck.streamDeckPlugin'
    if (-not (Test-Path $out)) { throw "Expected $out but it was not created" }
    Write-Host "Plugin package: $out"
} finally {
    Pop-Location
    $env:Path = $originalPath
}
