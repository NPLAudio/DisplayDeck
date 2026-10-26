# Draws a simple two-monitor icon and writes a multi-size PNG-based .ico.
param([string]$Out = (Join-Path $PSScriptRoot 'app.ico'))
Add-Type -AssemblyName System.Drawing

function New-Png([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $accent = [System.Drawing.Color]::FromArgb(37, 99, 235)
    $s = $size / 32.0
    $pen = New-Object System.Drawing.Pen $accent, ([Math]::Max(1.5, 2.2 * $s))
    $fill = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(232, 240, 255))
    $solid = New-Object System.Drawing.SolidBrush $accent
    # back (wide) monitor, front (small) monitor, stand
    $g.FillRectangle($fill, 2*$s, 5*$s, 20*$s, 13*$s); $g.DrawRectangle($pen, 2*$s, 5*$s, 20*$s, 13*$s)
    $g.FillRectangle($solid, 15*$s, 13*$s, 15*$s, 12*$s)
    $g.FillRectangle($solid, 8*$s, 22*$s, 6*$s, 2*$s); $g.FillRectangle($solid, 6*$s, 24*$s, 10*$s, 2*$s)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    , $ms.ToArray()
}

$sizes = 16, 32, 48, 256
$pngs = $sizes | ForEach-Object { , (New-Png $_) }
$fs = [System.IO.File]::Create($Out)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]; $len = $pngs[$i].Length
    $w.Write([byte]($(if ($sz -ge 256) { 0 } else { $sz }))); $w.Write([byte]($(if ($sz -ge 256) { 0 } else { $sz })))
    $w.Write([byte]0); $w.Write([byte]0); $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$len); $w.Write([uint32]$offset)
    $offset += $len
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Close(); $fs.Close()
Write-Host "Wrote $Out"
