param(
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$brandDirectory = Join-Path $ProjectRoot 'assets\brand'
$sizeDirectory = Join-Path $brandDirectory 'icon-sizes'
$appAssetDirectory = Join-Path $ProjectRoot 'src\LockPC.App\Assets'
New-Item -ItemType Directory -Path $brandDirectory, $sizeDirectory, $appAssetDirectory -Force | Out-Null

$night = [System.Drawing.ColorTranslator]::FromHtml('#101525')
$cool = [System.Drawing.ColorTranslator]::FromHtml('#63E6D2')
$peel = [System.Drawing.ColorTranslator]::FromHtml('#22BFA9')
$hot = [System.Drawing.ColorTranslator]::FromHtml('#FF7043')
$moon = [System.Drawing.ColorTranslator]::FromHtml('#F5F7FA')

function New-RoundedPath([float]$x, [float]$y, [float]$width, [float]$height, [float]$radius) {
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $diameter = $radius * 2
    $path.AddArc($x, $y, $diameter, $diameter, 180, 90)
    $path.AddArc($x + $width - $diameter, $y, $diameter, $diameter, 270, 90)
    $path.AddArc($x + $width - $diameter, $y + $height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($x, $y + $height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-StarPath {
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $path.StartFigure()
    $path.AddBezier(512, 148, 554, 323, 641, 410, 820, 452)
    $path.AddBezier(820, 452, 641, 494, 554, 581, 512, 756)
    $path.AddBezier(512, 756, 470, 581, 383, 494, 204, 452)
    $path.AddBezier(204, 452, 383, 410, 470, 323, 512, 148)
    $path.CloseFigure()
    return $path
}

function Set-Quality([System.Drawing.Graphics]$graphics) {
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
}

function Draw-Mark([System.Drawing.Graphics]$graphics, [float]$scale, [float]$originX, [float]$originY, [bool]$withBackground) {
    $state = $graphics.Save()
    $graphics.TranslateTransform($originX, $originY)
    $graphics.ScaleTransform($scale, $scale)

    if ($withBackground) {
        $background = New-RoundedPath 32 32 960 960 224
        $backgroundBrush = [System.Drawing.SolidBrush]::new($night)
        $graphics.FillPath($backgroundBrush, $background)
        $backgroundBrush.Dispose()
        $background.Dispose()
    }

    $star = New-StarPath
    $starBrush = [System.Drawing.SolidBrush]::new($hot)
    $graphics.FillPath($starBrush, $star)
    $starBrush.Dispose()
    $star.Dispose()

    $patchState = $graphics.Save()
    $graphics.TranslateTransform(512, 520)
    $graphics.RotateTransform(-8)
    $graphics.TranslateTransform(-512, -520)

    $patch = New-RoundedPath 258 342 508 356 104
    $coolBrush = [System.Drawing.SolidBrush]::new($cool)
    $graphics.FillPath($coolBrush, $patch)

    foreach ($pauseX in 426, 536) {
        $pause = New-RoundedPath $pauseX 438 62 166 28
        $nightBrush = [System.Drawing.SolidBrush]::new($night)
        $graphics.FillPath($nightBrush, $pause)
        $nightBrush.Dispose()
        $pause.Dispose()
    }

    $fold = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $fold.AddPolygon([System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new(660, 698),
        [System.Drawing.PointF]::new(766, 698),
        [System.Drawing.PointF]::new(766, 592)
    ))
    $peelBrush = [System.Drawing.SolidBrush]::new($peel)
    $graphics.FillPath($peelBrush, $fold)

    $peelBrush.Dispose()
    $fold.Dispose()
    $coolBrush.Dispose()
    $patch.Dispose()
    $graphics.Restore($patchState)
    $graphics.Restore($state)
}

function New-IconPng([int]$size, [string]$path) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $bitmap.SetResolution(96, 96)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    Set-Quality $graphics
    $graphics.Clear([System.Drawing.Color]::Transparent)
    Draw-Mark $graphics ($size / 1024.0) 0 0 $true
    $graphics.Dispose()
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
}

function New-LogoPng([string]$path, [bool]$darkBackground) {
    $bitmap = [System.Drawing.Bitmap]::new(1800, 560, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $bitmap.SetResolution(96, 96)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    Set-Quality $graphics
    $graphics.Clear($(if ($darkBackground) { $night } else { [System.Drawing.Color]::Transparent }))
    Draw-Mark $graphics 0.5 38 22 $false

    $wordFont = [System.Drawing.Font]::new('Microsoft YaHei UI', 132, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $taglineFont = [System.Drawing.Font]::new('Microsoft YaHei UI', 32, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $format = [System.Drawing.StringFormat]::GenericTypographic
    $hotBrush = [System.Drawing.SolidBrush]::new($hot)
    $coolBrush = [System.Drawing.SolidBrush]::new($cool)
    $taglineBrush = [System.Drawing.SolidBrush]::new($(if ($darkBackground) { $moon } else { $night }))

    $graphics.DrawString('AI', $wordFont, $hotBrush, 570, 142, $format)
    $aiWidth = $graphics.MeasureString('AI', $wordFont, 1000, $format).Width
    $graphics.DrawString('退烧贴', $wordFont, $coolBrush, 570 + $aiWidth - 3, 142, $format)
    $graphics.DrawString('纵然 AI 风姿千千万，休要给我一双熊猫眼。', $taglineFont, $taglineBrush, 580, 350, $format)

    $taglineBrush.Dispose()
    $coolBrush.Dispose()
    $hotBrush.Dispose()
    $taglineFont.Dispose()
    $wordFont.Dispose()
    $graphics.Dispose()
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
}

$iconSizes = 16, 20, 24, 32, 48, 64, 128, 256, 512, 1024
foreach ($size in $iconSizes) {
    New-IconPng $size (Join-Path $sizeDirectory "ai-cooling-patch-icon-$size.png")
}
Copy-Item -LiteralPath (Join-Path $sizeDirectory 'ai-cooling-patch-icon-1024.png') -Destination (Join-Path $brandDirectory 'ai-cooling-patch-icon-1024.png') -Force

New-LogoPng (Join-Path $brandDirectory 'ai-cooling-patch-logo.png') $false
New-LogoPng (Join-Path $brandDirectory 'ai-cooling-patch-logo-on-dark.png') $true

$icoSizes = 16, 20, 24, 32, 48, 64, 128, 256
$entries = foreach ($size in $icoSizes) {
    [PSCustomObject]@{
        Size = $size
        Bytes = [System.IO.File]::ReadAllBytes((Join-Path $sizeDirectory "ai-cooling-patch-icon-$size.png"))
    }
}

$icoPath = Join-Path $appAssetDirectory 'app.ico'
$stream = [System.IO.File]::Create($icoPath)
$writer = [System.IO.BinaryWriter]::new($stream)
$writer.Write([UInt16]0)
$writer.Write([UInt16]1)
$writer.Write([UInt16]$entries.Count)
$offset = 6 + (16 * $entries.Count)
foreach ($entry in $entries) {
    $dimension = if ($entry.Size -eq 256) { 0 } else { $entry.Size }
    $writer.Write([Byte]$dimension)
    $writer.Write([Byte]$dimension)
    $writer.Write([Byte]0)
    $writer.Write([Byte]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]32)
    $writer.Write([UInt32]$entry.Bytes.Length)
    $writer.Write([UInt32]$offset)
    $offset += $entry.Bytes.Length
}
foreach ($entry in $entries) {
    $writer.Write([byte[]]$entry.Bytes)
}
$writer.Dispose()
$stream.Dispose()

Write-Output "Generated brand assets in $brandDirectory"
Write-Output "Generated Windows icon at $icoPath"
