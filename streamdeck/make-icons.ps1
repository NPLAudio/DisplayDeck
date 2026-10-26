# Generates the plugin's PNG icons from the shared brand drawing code.
# Run from the streamdeck folder:  .\make-icons.ps1
param([string]$Root = (Join-Path $PSScriptRoot 'com.nplaudio.displaydeck.sdPlugin\imgs'))
. (Join-Path (Split-Path $PSScriptRoot) 'brand\Brand.ps1')

$white = $script:BrandColors.White

# One entry per action: its folder, and the glyph it uses.
$actions = @(
    @('profile', 'screens'),
    @('next', 'next'),
    @('undo', 'undo'),
    @('audio', 'speaker'),
    @('status', 'screens')
)

foreach ($a in $actions) {
    $dir = Join-Path $Root "actions\$($a[0])"
    # shown in Stream Deck's action list
    Save-Png (New-GlyphBitmap 20 $a[1] $white) (Join-Path $dir 'icon.png')
    Save-Png (New-GlyphBitmap 40 $a[1] $white) (Join-Path $dir 'icon@2x.png')
    # default key face, and the picture on the dial in the Stream Deck app
    Save-Png (New-KeyFaceBitmap 72 $a[1]) (Join-Path $dir 'key.png')
    Save-Png (New-KeyFaceBitmap 144 $a[1]) (Join-Path $dir 'key@2x.png')
    Save-Png (New-KeyFaceBitmap 72 $a[1]) (Join-Path $dir 'encoder-icon.png')
    Save-Png (New-KeyFaceBitmap 144 $a[1]) (Join-Path $dir 'encoder-icon@2x.png')
}

# Category icon (white mark on transparent) and the plugin's own icon (the app icon)
Save-Png (New-GlyphBitmap 28 'screens' $white) (Join-Path $Root 'plugin\category-icon.png')
Save-Png (New-GlyphBitmap 56 'screens' $white) (Join-Path $Root 'plugin\category-icon@2x.png')
Save-Png (New-AppIconBitmap 256) (Join-Path $Root 'plugin\marketplace.png')
Save-Png (New-AppIconBitmap 512) (Join-Path $Root 'plugin\marketplace@2x.png')
Write-Host "Icons written to $Root"
