# Writes the icons phones use when DisplayDeck is added to the home screen (iOS and Android).
# Run from the repo root:  .\brand\make-web-icons.ps1
param([string]$Out = (Join-Path (Split-Path $PSScriptRoot) 'web\public'))
. (Join-Path $PSScriptRoot 'Brand.ps1')

# Home-screen icons must be opaque, full-bleed squares: the phone rounds the corners itself, and any
# transparent corner shows up black. The mark sits inside the central 60% so no mask can clip it.
function New-TouchIconBitmap([int]$size) {
    New-BrandBitmap $size {
        param($g, $S)
        $g.Clear($script:BrandColors.Ink)   # opaque all the way to the edges
        $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush ((New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF 0, $S), $script:BrandColors.InkTop, $script:BrandColors.Ink)
        $g.FillRectangle($grad, -4, 0, $S + 8, $S)
        $box = 0.56 * $S; $off = ($S - $box) / 2
        Draw-Screens $g $off $off $box $script:BrandColors.Outline @($script:BrandColors.GreenTop, $script:BrandColors.GreenBot)
        $grad.Dispose()
    }
}

# Scaling blends the outermost pixels with transparency; paint onto an opaque, no-alpha bitmap to remove it.
function New-OpaqueTouchIcon([int]$size) {
    $src = New-TouchIconBitmap $size
    $dst = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = [System.Drawing.Graphics]::FromImage($dst)
    $g.Clear($script:BrandColors.Ink)
    $g.DrawImageUnscaled($src, 0, 0)
    $g.Dispose(); $src.Dispose()
    $dst
}

Save-Png (New-OpaqueTouchIcon 180) (Join-Path $Out 'apple-touch-icon.png')
Save-Png (New-OpaqueTouchIcon 192) (Join-Path $Out 'icon-192.png')
Save-Png (New-OpaqueTouchIcon 512) (Join-Path $Out 'icon-512.png')
Write-Host "Home-screen icons written to $Out"
