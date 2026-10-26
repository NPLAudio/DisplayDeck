# Writes the app icon (tray, EXE, installer, Start menu) from the shared brand drawing code.
param([string]$Out = (Join-Path $PSScriptRoot 'app.ico'))
. (Join-Path (Split-Path $PSScriptRoot) 'brand\Brand.ps1')
Write-Ico $Out @(16, 20, 24, 32, 40, 48, 64, 128, 256)
Write-Host "Wrote $Out"
