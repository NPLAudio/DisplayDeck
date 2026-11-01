# Publishes a DisplayDeck release: the installer and the Stream Deck plugin, each with a checksum, on one GitHub release.
#   .\release.ps1 -Notes notes.md            version comes from web\package.json
#   .\release.ps1 -Notes notes.md -DryRun    build and check everything, but do not tag or publish
# -Notes is a file with the bullet list of changes (the download line is added for you).
# Before running: bump the version in web\package.json, commit and push to main. The plugin gets the same version.
param(
    [Parameter(Mandatory)][string]$Notes,
    [switch]$DryRun
)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

function Git { & git @args; if ($LASTEXITCODE -ne 0) { throw "git $($args -join ' ') failed" } }

$version = (Get-Content web\package.json -Raw | ConvertFrom-Json).version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "web\package.json has an unexpected version: $version" }
$tag = "v$version"
if (-not (Test-Path $Notes)) { throw "Notes file not found: $Notes" }
$gh = (Get-Command gh -ErrorAction SilentlyContinue).Source
if (-not $gh) { $gh = 'C:\Program Files\GitHub CLI\gh.exe' }
if (-not (Test-Path $gh)) { throw 'The GitHub CLI (gh) was not found.' }

# The tag must name exactly what was built, so the tree has to be committed, pushed and on main.
& "$PSScriptRoot\streamdeck\sync-version.ps1" | Out-Null
if ((git status --porcelain | Measure-Object).Count -ne 0) {
    throw 'There are uncommitted changes (the plugin manifest version may have just changed). Commit and push them, then run this again.'
}
if ((git rev-parse --abbrev-ref HEAD) -ne 'main') { throw 'Releases are made from main.' }
Git fetch origin main --quiet
if ((git rev-parse HEAD) -ne (git rev-parse origin/main)) { throw 'main is not the same as origin/main. Push (or pull) first.' }
if (git tag -l $tag) { throw "The tag $tag already exists." }

Write-Host "Testing the web server..."
Push-Location web; try { npm test; if ($LASTEXITCODE -ne 0) { throw 'web tests failed' } } finally { Pop-Location }

Write-Host "Building the installer and the plugin..."
& "$PSScriptRoot\installer\build.ps1" -Version $version
$msi = "installer\dist\DisplayDeck-$version.msi"
$builtPlugin = 'streamdeck\dist\com.nplaudio.displaydeck.streamDeckPlugin'
if (-not (Test-Path $msi)) { throw "$msi was not built" }
if (-not (Test-Path $builtPlugin)) { throw 'The Stream Deck plugin was not built (it needs Node.js 24).' }
$plugin = "installer\dist\DisplayDeck-StreamDeck-$version.streamDeckPlugin"
Copy-Item $builtPlugin $plugin -Force

$assets = @()
foreach ($f in $msi, $plugin) {
    $hash = (Get-FileHash $f -Algorithm SHA256).Hash.ToLower()
    "$hash  $(Split-Path $f -Leaf)" | Set-Content -Encoding ascii "$f.sha256" -NoNewline
    $assets += $f, "$f.sha256"
}

$msiName = "DisplayDeck-$version.msi"; $pluginName = "DisplayDeck-StreamDeck-$version.streamDeckPlugin"
$body = @"
Download ``$msiName`` (Windows 10 or 11, 64-bit) and check it against ``$msiName.sha256``. The installer is unsigned, so SmartScreen may warn: choose More info, then Run anyway.

The Stream Deck plugin is already inside the installer (tray menu, Install Stream Deck plugin). ``$pluginName`` is the same plugin as a separate file: open it to install it in Stream Deck 7.6 or newer.

$((Get-Content $Notes -Raw).Trim())

Upgrade by running the new installer over the old one.
"@
$notesFile = Join-Path $env:TEMP "release-notes-$version.md"
$body | Set-Content -Encoding utf8 $notesFile

if ($DryRun) {
    Write-Host "Dry run: built and checked for $tag. Nothing was tagged or published."
    $assets | ForEach-Object { Get-Item $_ | Select-Object Name, @{n = 'KB'; e = { [math]::Round($_.Length / 1KB) } } }
    return
}

Git tag $tag HEAD
Git push origin $tag
& $gh release create $tag @assets --title "DisplayDeck $version" --notes-file $notesFile --latest
if ($LASTEXITCODE -ne 0) { throw 'gh release create failed (the tag was pushed; the release can be created again with the same assets)' }
Write-Host "Published $tag"
