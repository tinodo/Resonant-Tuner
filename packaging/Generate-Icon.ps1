$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetDirectory = Join-Path $PSScriptRoot '../src/Resonant.App/Assets'
[System.IO.Directory]::CreateDirectory($assetDirectory) | Out-Null
$images = [System.Collections.Generic.List[byte[]]]::new()
$sizes = @(16, 24, 32, 48, 64, 256)
foreach ($size in $sizes) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.ScaleTransform($size / 256.0, $size / 256.0)
    $background = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#0C1116'))
    $cyan = [System.Drawing.Pen]::new([System.Drawing.ColorTranslator]::FromHtml('#58E4F4'), 14)
    $magenta = [System.Drawing.Pen]::new([System.Drawing.ColorTranslator]::FromHtml('#EE83D3'), 7)
    $white = [System.Drawing.Pen]::new([System.Drawing.ColorTranslator]::FromHtml('#F0F5F3'), 14)
    try {
        $cyan.StartCap = $cyan.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $white.StartCap = $white.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $graphics.FillEllipse($background, 0, 0, 256, 256)
        $graphics.DrawArc($cyan, 23, 23, 210, 210, 195, 150)
        $graphics.DrawArc($magenta, 41, 41, 174, 174, 195, 150)
        $graphics.DrawLine($white, 96, 86, 96, 132)
        $graphics.DrawArc($white, 96, 110, 64, 44, 0, 180)
        $graphics.DrawLine($white, 160, 86, 160, 132)
        $graphics.DrawLine($white, 128, 154, 128, 207)
        if ($size -eq 256) {
            $bitmap.Save((Join-Path $assetDirectory 'Resonant.png'), [System.Drawing.Imaging.ImageFormat]::Png)
        }
        $stream = [System.IO.MemoryStream]::new()
        try {
            $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            $images.Add($stream.ToArray())
        }
        finally { $stream.Dispose() }
    }
    finally {
        $white.Dispose()
        $magenta.Dispose()
        $cyan.Dispose()
        $background.Dispose()
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}
$output = [System.IO.File]::Create((Join-Path $assetDirectory 'Resonant.ico'))
$writer = [System.IO.BinaryWriter]::new($output)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($index = 0; $index -lt $sizes.Count; $index++) {
        $dimension = if ($sizes[$index] -eq 256) { 0 } else { $sizes[$index] }
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$images[$index].Length)
        $writer.Write([uint32]$offset)
        $offset += $images[$index].Length
    }
    foreach ($image in $images) { $writer.Write([byte[]]$image) }
}
finally { $writer.Dispose() }
Write-Output 'Generated Resonant.ico: 16, 24, 32, 48, 64 and 256 pixels.'