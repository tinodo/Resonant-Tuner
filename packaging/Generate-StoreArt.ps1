$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$outputDirectory = Join-Path $PSScriptRoot 'store-assets'
[System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null

function New-Color([string]$hex) { [System.Drawing.ColorTranslator]::FromHtml($hex) }

# Draws the Resonant mark (defined in a 256-unit space) centred on ($cx, $cy) at the given scale.
function Write-Mark($graphics, [single]$cx, [single]$cy, [single]$scale) {
    $cyan = [System.Drawing.Pen]::new((New-Color '#58E4F4'), 14)
    $magenta = [System.Drawing.Pen]::new((New-Color '#EE83D3'), 7)
    $white = [System.Drawing.Pen]::new((New-Color '#F0F5F3'), 14)
    $state = $graphics.Save()
    try {
        $cyan.StartCap = $cyan.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $white.StartCap = $white.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $graphics.TranslateTransform($cx, $cy)
        $graphics.ScaleTransform($scale, $scale)
        $graphics.TranslateTransform(-128, -115)
        $graphics.DrawArc($cyan, 23, 23, 210, 210, 195, 150)
        $graphics.DrawArc($magenta, 41, 41, 174, 174, 195, 150)
        $graphics.DrawLine($white, 96, 86, 96, 132)
        $graphics.DrawArc($white, 96, 110, 64, 44, 0, 180)
        $graphics.DrawLine($white, 160, 86, 160, 132)
        $graphics.DrawLine($white, 128, 154, 128, 207)
    }
    finally {
        $graphics.Restore($state)
        $white.Dispose(); $magenta.Dispose(); $cyan.Dispose()
    }
}

function Write-Background($graphics, [int]$width, [int]$height) {
    $rectangle = [System.Drawing.Rectangle]::new(0, 0, $width, $height)
    $graphics.FillRectangle([System.Drawing.SolidBrush]::new((New-Color '#0C1116')), $rectangle)
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddEllipse(-$width * 0.25, -$height * 0.25, $width * 1.5, $height * 1.5)
    $brush = [System.Drawing.Drawing2D.PathGradientBrush]::new($path)
    $brush.CenterColor = New-Color '#1A2833'
    $brush.SurroundColors = @((New-Color '#0C1116'))
    $graphics.FillRectangle($brush, $rectangle)
    $brush.Dispose(); $path.Dispose()
}

function Write-Text($graphics, [string]$text, [single]$x, [single]$y, [single]$size, [string]$colorHex, [string]$family, [bool]$center, [single]$width) {
    $font = [System.Drawing.Font]::new($family, $size, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
    $brush = [System.Drawing.SolidBrush]::new((New-Color $colorHex))
    $format = [System.Drawing.StringFormat]::new()
    if ($center) { $format.Alignment = [System.Drawing.StringAlignment]::Center }
    $layout = [System.Drawing.RectangleF]::new($x, $y, $width, $size * 2)
    $graphics.DrawString($text, $font, $brush, $layout, $format)
    $format.Dispose(); $brush.Dispose(); $font.Dispose()
}

function New-Art([string]$name, [int]$width, [int]$height, [scriptblock]$draw) {
    $bitmap = [System.Drawing.Bitmap]::new($width, $height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
        Write-Background $graphics $width $height
        & $draw $graphics
        $bitmap.Save((Join-Path $outputDirectory $name), [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $graphics.Dispose(); $bitmap.Dispose() }
}

$title = 'Segoe UI Semibold'
$body = 'Segoe UI'

New-Art 'StoreIcon-300x300.png' 300 300 { param($g) Write-Mark $g 150 150 0.9 }

New-Art 'Poster-720x1080.png' 720 1080 {
    param($g)
    Write-Mark $g 360 400 2.3
    Write-Text $g 'Resonant Tuner' 0 760 72 '#F0F5F3' $title $true 720
    Write-Text $g 'Offline instrument tuner' 0 860 34 '#9AA6B1' $body $true 720
}

New-Art 'BoxArt-1080x1080.png' 1080 1080 {
    param($g)
    Write-Mark $g 540 430 3.2
    Write-Text $g 'Resonant Tuner' 0 800 96 '#F0F5F3' $title $true 1080
    Write-Text $g 'Offline instrument tuner' 0 920 40 '#9AA6B1' $body $true 1080
}

New-Art 'Hero-1920x1080.png' 1920 1080 {
    param($g)
    Write-Mark $g 560 540 3.4
    Write-Text $g 'Resonant Tuner' 1000 450 100 '#F0F5F3' $title $false 880
    Write-Text $g 'An offline instrument tuner for Windows' 1004 590 44 '#9AA6B1' $body $false 880
}

Write-Output "Generated Store artwork in $outputDirectory"
