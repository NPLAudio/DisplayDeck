# DisplayDeck brand drawing library (dot-source this file).
#
# The mark is two "screens" offset diagonally: an outlined one behind and a solid, lit one in front. It reads as
# a multi-display arrangement at any size, and the lit screen means "the active layout". The accent is emerald,
# the same green the Stream Deck keys use for "active".
#
# Everything is drawn at 4x and scaled down, so small sizes stay crisp.
Add-Type -AssemblyName System.Drawing

$script:BrandColors = @{
    Ink       = [System.Drawing.Color]::FromArgb(255, 24, 24, 27)      # zinc-900
    InkTop    = [System.Drawing.Color]::FromArgb(255, 44, 44, 49)      # tile gradient top
    White     = [System.Drawing.Color]::FromArgb(255, 250, 250, 250)   # zinc-50
    Outline   = [System.Drawing.Color]::FromArgb(190, 250, 250, 250)   # white, 75%
    Dim       = [System.Drawing.Color]::FromArgb(150, 250, 250, 250)   # white, 60%
    GreenTop  = [System.Drawing.Color]::FromArgb(255, 52, 211, 153)    # emerald-400
    GreenBot  = [System.Drawing.Color]::FromArgb(255, 5, 150, 105)     # emerald-600
    Green     = [System.Drawing.Color]::FromArgb(255, 34, 197, 94)     # green-500 (the "active" accent)
}

function New-RoundedPath([double]$x, [double]$y, [double]$w, [double]$h, [double]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [Math]::Min($r * 2, [Math]::Min($w, $h))
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    $p
}

# Draws the two screens inside the square box (x, y, size).
#   $back  = color of the outlined screen behind (top-left)
#   $front = a Color, or an array of two Colors for a top-to-bottom gradient, for the solid screen (bottom-right)
function Draw-Screens($g, [double]$x, [double]$y, [double]$size, [System.Drawing.Color]$back, $front) {
    $w = 0.62 * $size; $h = 0.39 * $size
    $stroke = 0.075 * $size; $r = 0.075 * $size

    # back screen: outline, inset by half the stroke so the outside edge lands on the box
    $bx = $x; $by = $y + 0.06 * $size
    $pen = New-Object System.Drawing.Pen $back, $stroke
    $path = New-RoundedPath ($bx + $stroke / 2) ($by + $stroke / 2) ($w - $stroke) ($h - $stroke) ($r - $stroke / 2)
    $g.DrawPath($pen, $path)
    $pen.Dispose(); $path.Dispose()

    # front screen: solid (or gradient)
    $fx = $x + 0.38 * $size; $fy = $y + 0.52 * $size
    if ($front -is [array]) {
        $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush ((New-Object System.Drawing.PointF 0, $fy), (New-Object System.Drawing.PointF 0, ($fy + $h)), $front[0], $front[1])
    } else {
        $brush = New-Object System.Drawing.SolidBrush $front
    }
    $path = New-RoundedPath $fx $fy $w $h $r
    $g.FillPath($brush, $path)
    $brush.Dispose(); $path.Dispose()
}

# Strokes a simple glyph by name inside the square box (x, y, size): 'next', 'undo', 'speaker'.
function Draw-Glyph($g, [string]$name, [double]$x, [double]$y, [double]$size, [System.Drawing.Color]$color) {
    $k = $size / 100.0
    $pen = New-Object System.Drawing.Pen $color, (7 * $k)
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
    $brush = New-Object System.Drawing.SolidBrush $color
    $P = { param($px, $py) New-Object System.Drawing.PointF ([float]($x + $px * $k)), ([float]($y + $py * $k)) }
    switch ($name) {
        'next' {
            $g.DrawLine($pen, (& $P 12 50), (& $P 86 50))
            $g.DrawLines($pen, @((& $P 58 22), (& $P 87 50), (& $P 58 78)))
        }
        'undo' {
            $g.DrawLines($pen, @((& $P 32 18), (& $P 12 38), (& $P 32 58)))
            $g.DrawLine($pen, (& $P 12 38), (& $P 62 38))
            $g.DrawArc($pen, ($x + 28 * $k), ($y + 38 * $k), (60 * $k), (44 * $k), -90, 180)
            $g.DrawLine($pen, (& $P 58 82), (& $P 36 82))
        }
        'speaker' {
            $g.FillPolygon($brush, @((& $P 10 38), (& $P 28 38), (& $P 50 18), (& $P 50 82), (& $P 28 62), (& $P 10 62)))
            $g.DrawArc($pen, ($x + 34 * $k), ($y + 32 * $k), (30 * $k), (36 * $k), -60, 120)
            $g.DrawArc($pen, ($x + 40 * $k), ($y + 14 * $k), (48 * $k), (72 * $k), -60, 120)
        }
    }
    $pen.Dispose(); $brush.Dispose()
}

# Renders at 4x and returns a bitmap of the requested size. $draw receives ($g, $S) with S = pixel size at 4x.
function New-BrandBitmap([int]$size, [scriptblock]$draw) {
    $big = $size * 4
    $bmp = New-Object System.Drawing.Bitmap $big, $big
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    & $draw $g $big
    $g.Dispose()
    $out = New-Object System.Drawing.Bitmap $size, $size
    $og = [System.Drawing.Graphics]::FromImage($out)
    $og.InterpolationMode = 'HighQualityBicubic'
    $og.SmoothingMode = 'HighQuality'
    $og.PixelOffsetMode = 'HighQuality'
    $og.CompositingQuality = 'HighQuality'
    $og.DrawImage($bmp, 0, 0, $size, $size)
    $og.Dispose(); $bmp.Dispose()
    $out
}

# The app icon: dark tile, an outlined screen behind and an emerald, lit screen in front.
function New-AppIconBitmap([int]$size) {
    New-BrandBitmap $size {
        param($g, $S)
        $tile = New-RoundedPath 0 0 $S $S (0.225 * $S)
        $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush ((New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF 0, $S), $script:BrandColors.InkTop, $script:BrandColors.Ink)
        $g.FillPath($grad, $tile)
        $edge = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(40, 255, 255, 255)), ([Math]::Max(1, 0.012 * $S))
        $g.DrawPath($edge, $tile)
        $box = 0.64 * $S; $off = ($S - $box) / 2
        Draw-Screens $g $off $off $box $script:BrandColors.Outline @($script:BrandColors.GreenTop, $script:BrandColors.GreenBot)
        $grad.Dispose(); $edge.Dispose(); $tile.Dispose()
    }
}

# A flat, borderless version for icon sizes where a tile would waste pixels (action list, category).
function New-GlyphBitmap([int]$size, [string]$name, [System.Drawing.Color]$color) {
    New-BrandBitmap $size {
        param($g, $S)
        $m = 0.06 * $S; $box = $S - 2 * $m
        if ($name -eq 'screens') { Draw-Screens $g $m $m $box $color $color }
        else { Draw-Glyph $g $name $m $m $box $color }
    }
}

# A Stream Deck key face: dark square with a centered glyph (the layout mark carries the green "active" screen).
function New-KeyFaceBitmap([int]$size, [string]$name) {
    New-BrandBitmap $size {
        param($g, $S)
        $g.Clear($script:BrandColors.Ink)
        $box = 0.52 * $S; $off = ($S - $box) / 2
        if ($name -eq 'screens') { Draw-Screens $g $off $off $box $script:BrandColors.Outline $script:BrandColors.Green }
        else { Draw-Glyph $g $name ($S * 0.27) ($S * 0.27) ($S * 0.46) $script:BrandColors.White }
    }
}

function Save-Png($bitmap, [string]$path) {
    New-Item -ItemType Directory -Force (Split-Path $path) | Out-Null
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
}

# Writes a multi-size .ico (PNG-compressed entries, which every supported Windows version reads).
function Write-Ico([string]$path, [int[]]$sizes) {
    $pngs = foreach ($s in $sizes) {
        $bmp = New-AppIconBitmap $s
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        , $ms.ToArray()
    }
    New-Item -ItemType Directory -Force (Split-Path $path) | Out-Null
    $fs = [System.IO.File]::Create($path)
    $w = New-Object System.IO.BinaryWriter $fs
    $w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
        $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
        $w.Write([uint16]1); $w.Write([uint16]32)
        $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
        $offset += $pngs[$i].Length
    }
    foreach ($p in $pngs) { $w.Write($p) }
    $w.Close(); $fs.Close()
}
