# Shows the native folder dialog IN FRONT and prints the chosen path (nothing if cancelled).
# Inputs are environment variables, so no quoting is needed: DD_TITLE (dialog text), DD_INITIAL (start folder).
Add-Type -AssemblyName System.Windows.Forms
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class DdFront {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
}
"@

# Invisible topmost owner. A background process may not take the foreground, so a tap of the Alt
# key (which Windows counts as user input) is used to earn the right before activating it.
$owner = New-Object System.Windows.Forms.Form
$owner.TopMost = $true
$owner.ShowInTaskbar = $false
$owner.StartPosition = 'Manual'
$owner.Location = New-Object System.Drawing.Point(-2000, -2000)
$owner.Size = New-Object System.Drawing.Size(1, 1)
$owner.Show()
[DdFront]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero)   # Alt down
[DdFront]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)   # Alt up
[void][DdFront]::SetForegroundWindow($owner.Handle)
$owner.Activate()

$dlg = New-Object System.Windows.Forms.FolderBrowserDialog
$dlg.Description = if ($env:DD_TITLE) { $env:DD_TITLE } else { 'Choose a folder' }
$dlg.ShowNewFolderButton = $true
if ($env:DD_INITIAL -and (Test-Path -LiteralPath $env:DD_INITIAL -PathType Container)) { $dlg.SelectedPath = $env:DD_INITIAL }
if ($dlg.ShowDialog($owner) -eq [System.Windows.Forms.DialogResult]::OK) { [Console]::Out.Write($dlg.SelectedPath) }
$owner.Dispose()
