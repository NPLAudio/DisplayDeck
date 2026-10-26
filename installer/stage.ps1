# Assembles the install layout in installer\stage (exactly what the MSI lays down).
param([string]$NodeExe = (Get-Command node).Source)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$repo = Split-Path $here
$stage = Join-Path $here 'stage'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force "$stage\node", "$stage\web", "$stage\bin", "$stage\licenses" | Out-Null

& powershell -NoProfile -ExecutionPolicy Bypass -File "$here\make-icon.ps1" | Out-Null
New-Item -ItemType Directory -Force "$here\build" | Out-Null
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $csc /nologo /target:winexe /out:"$here\build\DisplayDeck.exe" /win32icon:"$here\app.ico" `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll "$repo\tray\Tray.cs"
if ($LASTEXITCODE -ne 0) { throw 'Tray compile failed' }

# The Windows helper (display layouts, audio devices); build.ps1 also runs its self-test.
& "$repo\ctl\build.ps1" -Out "$here\build\DisplayDeckCtl.exe"

Copy-Item "$here\build\DisplayDeck.exe", "$here\app.ico" $stage
Copy-Item "$here\build\DisplayDeckCtl.exe" "$stage\bin"
Copy-Item $NodeExe "$stage\node\node.exe"
Copy-Item "$repo\web\server.js", "$repo\web\package.json", "$repo\web\README.md" "$stage\web"
Copy-Item "$repo\web\lib", "$repo\web\public" "$stage\web" -Recurse

# Licenses: ours, the third-party notices, and the Node.js license (required with the bundled node.exe).
Copy-Item "$repo\LICENSE", "$repo\THIRD-PARTY-NOTICES.md" $stage
$nodeLicense = Join-Path (Split-Path $NodeExe) 'LICENSE'
if (-not (Test-Path $nodeLicense)) { throw "Node's LICENSE not found next to $NodeExe; it must ship with node.exe" }
Copy-Item $nodeLicense "$stage\licenses\node-LICENSE.txt"

# Only our own executables and the Node runtime may ship. Anything else is a packaging mistake.
$expected = 'DisplayDeck.exe', 'bin\DisplayDeckCtl.exe', 'node\node.exe'
$found = Get-ChildItem $stage -Recurse -File | Where-Object { $_.Extension -match '^\.(exe|dll|chm|sys|msi)$' } |
    ForEach-Object { $_.FullName.Substring($stage.Length + 1) }
$unexpected = $found | Where-Object { $expected -notcontains $_ }
if ($unexpected) { throw "Unexpected executables in the package: $($unexpected -join ', ')" }
Write-Host "Staged to $stage ($($found.Count) executables, all expected)"
