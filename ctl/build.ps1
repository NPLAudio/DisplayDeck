# Builds bin\DisplayDeckCtl.exe (the Windows helper) with the C# compiler that ships with Windows.
param([string]$Out = (Join-Path (Split-Path $PSScriptRoot) 'bin\DisplayDeckCtl.exe'))
$ErrorActionPreference = 'Stop'
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null
& $csc /nologo /target:exe /platform:x64 /out:$Out `
    /r:System.Web.Extensions.dll /r:System.Xml.Linq.dll /r:System.Xml.dll /r:System.Core.dll "$PSScriptRoot\*.cs"
if ($LASTEXITCODE -ne 0) { throw 'DisplayDeckCtl compile failed' }
$test = & $Out selftest | ConvertFrom-Json
if (-not $test.ok) { throw "DisplayDeckCtl selftest failed: $($test.failed -join '; ')" }
Write-Host "Built $Out (selftest: $($test.passed) checks passed)"
