# Builds the tray app and the helper, stages the install layout, and produces the MSI.
#   .\build.ps1 -Version 2.0.0  -> dist\DisplayDeck-<version>.msi
# One-time setup (local tool, no global install):
#   dotnet tool install wix --version 5.0.2 --tool-path .\tools
param([string]$Version = '2.0.0')
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
& .\stage.ps1
New-Item -ItemType Directory -Force dist | Out-Null
$out = "dist\DisplayDeck-$Version.msi"
& .\tools\wix.exe build Product.wxs -d Version=$Version -arch x64 -o $out
if ($LASTEXITCODE -ne 0) { throw 'wix build failed' }

# Independent check on the finished MSI: read its File table and refuse to produce an installer that
# carries any executable we did not build or expect.
$installer = New-Object -ComObject WindowsInstaller.Installer
$db = $installer.GetType().InvokeMember('OpenDatabase', 'InvokeMethod', $null, $installer, @((Resolve-Path $out).Path, 0))
$view = $db.GetType().InvokeMember('OpenView', 'InvokeMethod', $null, $db, @('SELECT FileName FROM File'))
$view.GetType().InvokeMember('Execute', 'InvokeMethod', $null, $view, $null) | Out-Null
$shipped = @()
while ($rec = $view.GetType().InvokeMember('Fetch', 'InvokeMethod', $null, $view, $null)) {
    $shipped += ($rec.GetType().InvokeMember('StringData', 'GetProperty', $null, $rec, @(1)) -split '\|')[-1]
}
$view.GetType().InvokeMember('Close', 'InvokeMethod', $null, $view, $null) | Out-Null
$exes = $shipped | Where-Object { $_ -match '\.(exe|dll|chm|sys|msi)$' } | Sort-Object
$expected = 'DisplayDeck.exe', 'DisplayDeckCtl.exe', 'DisplayDeckCli.exe', 'node.exe'
$bad = $exes | Where-Object { $expected -notcontains $_ }
if ($bad) { Remove-Item $out -Force; throw "MSI contains unexpected executables ($($bad -join ', ')); deleted $out" }
Write-Host "MSI check: $($shipped.Count) files, executables: $($exes -join ', ')"

Get-Item $out | Select-Object Name, @{n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } }
