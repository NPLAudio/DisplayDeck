# Gives the Stream Deck plugin the same version as DisplayDeck (web\package.json), written as the four-part number
# Stream Deck wants (2.6.3 becomes 2.6.3.0). Does nothing when it already matches. Prints the version.
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot
$app = (Get-Content (Join-Path $repo 'web\package.json') -Raw | ConvertFrom-Json).version
if ($app -notmatch '^\d+\.\d+\.\d+$') { throw "web\package.json has an unexpected version: $app" }
$plugin = "$app.0"

$manifest = Join-Path $PSScriptRoot 'com.nplaudio.displaydeck.sdPlugin\manifest.json'
$text = [IO.File]::ReadAllText($manifest)
$re = [regex]'(?m)^(\s*"Version":\s*")\d+(\.\d+){3}(")'   # the plugin's own version, not the minimum Stream Deck version
if (-not $re.IsMatch($text)) { throw 'The plugin version was not found in manifest.json' }
$next = $re.Replace($text, "`${1}$plugin`${3}", 1)
if ($next -ne $text) {
    [IO.File]::WriteAllText($manifest, $next, (New-Object Text.UTF8Encoding($false)))
    Write-Host "Plugin version set to $plugin"
}
$plugin
