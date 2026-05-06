$ErrorActionPreference = "Stop"

$repo = Resolve-Path (Join-Path $PSScriptRoot "..")
$assets = Join-Path $repo "NetBuddies.App\Assets"
New-Item -ItemType Directory -Force -Path $assets | Out-Null

Add-Type -AssemblyName System.Drawing

function New-IconPng {
    param(
        [int]$Size,
        [string]$Path
    )

    $bitmap = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $scale = $Size / 256.0
    function S([double]$value) { return [single]($value * $scale) }

    $background = [System.Drawing.RectangleF]::new((S 12), (S 12), (S 232), (S 232))
    $shapePath = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $radius = S 46
    $shapePath.AddArc($background.X, $background.Y, $radius, $radius, 180, 90)
    $shapePath.AddArc($background.Right - $radius, $background.Y, $radius, $radius, 270, 90)
    $shapePath.AddArc($background.Right - $radius, $background.Bottom - $radius, $radius, $radius, 0, 90)
    $shapePath.AddArc($background.X, $background.Bottom - $radius, $radius, $radius, 90, 90)
    $shapePath.CloseFigure()

    $brush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        $background,
        [System.Drawing.Color]::FromArgb(255, 9, 112, 205),
        [System.Drawing.Color]::FromArgb(255, 118, 210, 255),
        [System.Drawing.Drawing2D.LinearGradientMode]::ForwardDiagonal)
    $graphics.FillPath($brush, $shapePath)
    $graphics.DrawPath([System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 4, 82, 156), (S 6)), $shapePath)

    $shine = [System.Drawing.RectangleF]::new((S 34), (S 28), (S 188), (S 74))
    $graphics.FillEllipse([System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(62, 255, 255, 255)), $shine)

    $green = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        [System.Drawing.RectangleF]::new((S 120), (S 72), (S 92), (S 130)),
        [System.Drawing.Color]::FromArgb(255, 126, 219, 71),
        [System.Drawing.Color]::FromArgb(255, 38, 143, 47),
        [System.Drawing.Drawing2D.LinearGradientMode]::Vertical)
    $blue = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        [System.Drawing.RectangleF]::new((S 46), (S 62), (S 112), (S 150)),
        [System.Drawing.Color]::FromArgb(255, 116, 207, 255),
        [System.Drawing.Color]::FromArgb(255, 12, 105, 196),
        [System.Drawing.Drawing2D.LinearGradientMode]::Vertical)

    $graphics.FillEllipse($blue, (S 63), (S 48), (S 58), (S 58))
    $graphics.FillEllipse($green, (S 135), (S 62), (S 54), (S 54))
    $graphics.FillEllipse($blue, (S 42), (S 102), (S 96), (S 98))
    $graphics.FillEllipse($green, (S 112), (S 112), (S 92), (S 90))
    $graphics.DrawEllipse([System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(95, 255, 255, 255), (S 5)), (S 63), (S 48), (S 58), (S 58))
    $graphics.DrawEllipse([System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(95, 255, 255, 255), (S 5)), (S 135), (S 62), (S 54), (S 54))

    $networkPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(210, 255, 255, 255), (S 6))
    $networkPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $networkPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $graphics.DrawLine($networkPen, (S 92), (S 174), (S 164), (S 174))
    $graphics.FillEllipse([System.Drawing.SolidBrush]::new([System.Drawing.Color]::White), (S 82), (S 164), (S 20), (S 20))
    $graphics.FillEllipse([System.Drawing.SolidBrush]::new([System.Drawing.Color]::White), (S 154), (S 164), (S 20), (S 20))

    $starBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 255, 229, 88))
    $graphics.FillEllipse($starBrush, (S 184), (S 42), (S 26), (S 26))
    $graphics.DrawLine([System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 255, 246, 170), (S 5)), (S 197), (S 34), (S 197), (S 76))
    $graphics.DrawLine([System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 255, 246, 170), (S 5)), (S 176), (S 55), (S 218), (S 55))

    $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose()
    $bitmap.Dispose()
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$pngs = @()
foreach ($size in $sizes) {
    $path = Join-Path $assets "netbuddies-$size.png"
    New-IconPng -Size $size -Path $path
    $pngs += [pscustomobject]@{ Size = $size; Path = $path; Bytes = [System.IO.File]::ReadAllBytes($path) }
}

$icoPath = Join-Path $assets "netbuddies.ico"
$stream = [System.IO.File]::Create($icoPath)
$writer = [System.IO.BinaryWriter]::new($stream)
$writer.Write([UInt16]0)
$writer.Write([UInt16]1)
$writer.Write([UInt16]$pngs.Count)

$offset = 6 + (16 * $pngs.Count)
foreach ($png in $pngs) {
    $writer.Write([byte]$(if ($png.Size -eq 256) { 0 } else { $png.Size }))
    $writer.Write([byte]$(if ($png.Size -eq 256) { 0 } else { $png.Size }))
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]32)
    $writer.Write([UInt32]$png.Bytes.Length)
    $writer.Write([UInt32]$offset)
    $offset += $png.Bytes.Length
}

foreach ($png in $pngs) {
    $writer.Write($png.Bytes)
}

$writer.Dispose()
$stream.Dispose()
Write-Host "Created $icoPath"
