<#
    Builds the two images the installer wizard shows, from the application icon, so setup looks
    like the thing it installs rather than like Inno Setup's stock artwork.

    The results are committed beside this script: the installer has to compile on a machine that
    has never run this, and the inputs only change when the icon does.

        pwsh src/Narula.Image.Convertor.Setup/make-wizard-images.ps1

    Sizes are Inno's own: 164x314 for the panel on the finish page, 55x55 for the badge in the
    top corner of every other page.
#>

Add-Type -AssemblyName System.Drawing

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$iconPath = Join-Path $here "..\Narula.Image.Convertor\icon.ico"

# The 256px frame is stored as PNG inside the .ico, and System.Drawing reads that frame
# correctly only when asked for it by size.
$icon = New-Object System.Drawing.Icon($iconPath, 256, 256)
$art = $icon.ToBitmap()

function New-Panel {
    # ArtSize, not Art: PowerShell variable names are case-insensitive, so a parameter named
    # $Art would silently become the $art image itself inside this function.
    param([int]$Width, [int]$Height, [string]$Hex, [int]$ArtSize, [int]$OffsetY, [string]$Out)

    $bmp = New-Object System.Drawing.Bitmap($Width, $Height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic

    $colour = [System.Drawing.ColorTranslator]::FromHtml($Hex)
    $g.Clear($colour)

    $x = [int](($Width - $ArtSize) / 2)
    $y = [int](($Height - $ArtSize) / 2) + $OffsetY
    $g.DrawImage($art, $x, $y, $ArtSize, $ArtSize)

    $g.Dispose()
    $bmp.Save((Join-Path $here $Out), [System.Drawing.Imaging.ImageFormat]::Bmp)
    $bmp.Dispose()
    "wrote $Out ($Width x $Height)"
}

# The panel tint is the window's own source card, so the installer and the application agree
# about what colour this product is.
New-Panel -Width 164 -Height 314 -Hex "#EFF3FF" -ArtSize 108 -OffsetY -30 -Out "wizard-large.bmp"
New-Panel -Width 55 -Height 55 -Hex "#FFFFFF" -ArtSize 48 -OffsetY 0 -Out "wizard-small.bmp"

$art.Dispose()
$icon.Dispose()
