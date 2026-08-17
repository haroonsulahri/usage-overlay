$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$projectRoot = Split-Path -Parent $PSScriptRoot
$outputDirectory = Join-Path $projectRoot 'src\QuotaRail\Assets'
$outputPath = Join-Path $outputDirectory 'QuotaRail.ico'
[void](New-Item -ItemType Directory -Path $outputDirectory -Force)

function New-RoundedRectanglePath {
    param(
        [double]$X,
        [double]$Y,
        [double]$Width,
        [double]$Height,
        [double]$Radius
    )

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $diameter = [Math]::Max(2, $Radius * 2)
    $path.AddArc([float]$X, [float]$Y, [float]$diameter, [float]$diameter, 180, 90)
    $path.AddArc([float]($X + $Width - $diameter), [float]$Y, [float]$diameter, [float]$diameter, 270, 90)
    $path.AddArc([float]($X + $Width - $diameter), [float]($Y + $Height - $diameter), [float]$diameter, [float]$diameter, 0, 90)
    $path.AddArc([float]$X, [float]($Y + $Height - $diameter), [float]$diameter, [float]$diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconPngBytes {
    param([int]$Size)

    $bitmap = New-Object System.Drawing.Bitmap $Size, $Size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)

    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality

        $inset = [Math]::Max(1, $Size * 0.047)
        $tileRadius = $Size * 0.21
        $tilePath = New-RoundedRectanglePath $inset $inset ($Size - 2 * $inset) ($Size - 2 * $inset) $tileRadius
        $tileBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 27, 27, 29))
        $graphics.FillPath($tileBrush, $tilePath)
        $tileBrush.Dispose()

        $borderWidth = [Math]::Max(0.8, $Size * 0.008)
        $tilePen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 59, 59, 63)), ([float]$borderWidth)
        $graphics.DrawPath($tilePen, $tilePath)
        $tilePen.Dispose()
        $tilePath.Dispose()

        $railWidth = [Math]::Max(4, $Size * 0.20)
        $railHeight = $Size * 0.66
        $railX = ($Size - $railWidth) / 2
        $railY = $Size * 0.17
        $railRadius = $railWidth / 2
        $railPath = New-RoundedRectanglePath $railX $railY $railWidth $railHeight $railRadius
        $trackBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 35, 35, 38))
        $graphics.FillPath($trackBrush, $railPath)
        $trackBrush.Dispose()

        $graphicsState = $graphics.Save()
        $graphics.SetClip($railPath)
        $fillTop = $railY + $railHeight * 0.34
        $fillRectangle = New-Object System.Drawing.RectangleF ([float]$railX), ([float]$fillTop), ([float]$railWidth), ([float]($railY + $railHeight - $fillTop))
        $fillBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush `
            $fillRectangle, `
            ([System.Drawing.Color]::FromArgb(255, 85, 200, 120)), `
            ([System.Drawing.Color]::FromArgb(255, 240, 180, 94)), `
            ([System.Drawing.Drawing2D.LinearGradientMode]::Vertical)
        $graphics.FillRectangle($fillBrush, $fillRectangle)
        $fillBrush.Dispose()

        $capHeight = [Math]::Max(1, $Size * 0.018)
        $capInset = [Math]::Max(1, $Size * 0.04)
        $capBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(158, 247, 255, 249))
        $graphics.FillRectangle(
            $capBrush,
            [float]($railX + $capInset),
            [float]$fillTop,
            [float]($railWidth - 2 * $capInset),
            [float]$capHeight)
        $capBrush.Dispose()
        $graphics.Restore($graphicsState)

        $railPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 85, 85, 90)), ([float]$borderWidth)
        $graphics.DrawPath($railPen, $railPath)
        $railPen.Dispose()
        $railPath.Dispose()

        $stream = New-Object System.IO.MemoryStream
        try {
            $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            return $stream.ToArray()
        }
        finally {
            $stream.Dispose()
        }
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$images = foreach ($size in $sizes) {
    [pscustomobject]@{
        Size = $size
        Bytes = New-IconPngBytes -Size $size
    }
}

$stream = [System.IO.File]::Open($outputPath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write)
$writer = New-Object System.IO.BinaryWriter $stream

try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$images.Count)

    $offset = 6 + 16 * $images.Count
    foreach ($image in $images) {
        $dimension = if ($image.Size -ge 256) { 0 } else { $image.Size }
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$image.Bytes.Length)
        $writer.Write([uint32]$offset)
        $offset += $image.Bytes.Length
    }

    foreach ($image in $images) {
        $writer.Write([byte[]]$image.Bytes)
    }
}
finally {
    $writer.Dispose()
    $stream.Dispose()
}

Write-Host "Generated $outputPath with sizes: $($sizes -join ', ') px"
