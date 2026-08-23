[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$projectRoot = Split-Path -Parent $PSScriptRoot
$sourcePath = Join-Path $projectRoot 'NexaPlay\Assets\NexaPlay-512.png'
$pngPath = Join-Path $projectRoot 'NexaPlay.Owner\Assets\NexaPlay-Owner-512.png'
$icoPath = Join-Path $projectRoot 'NexaPlay.Owner\Assets\NexaPlay-Owner.ico'
$assetDirectory = Split-Path -Parent $pngPath
New-Item -ItemType Directory -Path $assetDirectory -Force | Out-Null

$source = [System.Drawing.Image]::FromFile($sourcePath)
$canvas = [System.Drawing.Bitmap]::new(512, 512, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [System.Drawing.Graphics]::FromImage($canvas)
try {
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.DrawImage($source, 0, 0, 512, 512)

    $shield = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $points = [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new(402, 266),
        [System.Drawing.PointF]::new(482, 294),
        [System.Drawing.PointF]::new(474, 390),
        [System.Drawing.PointF]::new(402, 474),
        [System.Drawing.PointF]::new(330, 390),
        [System.Drawing.PointF]::new(322, 294)
    )
    $shield.AddClosedCurve($points, 0.16)

    $shadowMatrix = [System.Drawing.Drawing2D.Matrix]::new()
    $shadowMatrix.Translate(4, 7)
    $shadow = [System.Drawing.Drawing2D.GraphicsPath]$shield.Clone()
    $shadow.Transform($shadowMatrix)
    $shadowBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(190, 0, 0, 0))
    $graphics.FillPath($shadowBrush, $shadow)

    $glowPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(115, 255, 196, 72), 20)
    $glowPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $graphics.DrawPath($glowPen, $shield)

    $shieldBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        [System.Drawing.PointF]::new(330, 280),
        [System.Drawing.PointF]::new(472, 462),
        [System.Drawing.Color]::FromArgb(255, 16, 31, 58),
        [System.Drawing.Color]::FromArgb(255, 83, 30, 128))
    $graphics.FillPath($shieldBrush, $shield)

    $goldPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 255, 211, 99), 9)
    $goldPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $graphics.DrawPath($goldPen, $shield)
    $cyanPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 91, 234, 255), 3)
    $cyanPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $graphics.DrawPath($cyanPen, $shield)

    $innerShield = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $innerPoints = [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new(402, 292),
        [System.Drawing.PointF]::new(458, 311),
        [System.Drawing.PointF]::new(452, 379),
        [System.Drawing.PointF]::new(402, 440),
        [System.Drawing.PointF]::new(352, 379),
        [System.Drawing.PointF]::new(346, 311)
    )
    $innerShield.AddClosedCurve($innerPoints, 0.16)
    $innerPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(155, 120, 242, 255), 4)
    $graphics.DrawPath($innerPen, $innerShield)

    $keyGlow = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(90, 70, 225, 255))
    $graphics.FillEllipse($keyGlow, 368, 324, 68, 68)
    $keyBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 244, 252, 255))
    $graphics.FillEllipse($keyBrush, 383, 338, 38, 38)
    $keyPath = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $keyPath.AddPolygon([System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new(394, 365),
        [System.Drawing.PointF]::new(410, 365),
        [System.Drawing.PointF]::new(419, 410),
        [System.Drawing.PointF]::new(385, 410)
    ))
    $graphics.FillPath($keyBrush, $keyPath)

    $canvas.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)

    $sizes = @(16, 24, 32, 48, 64, 128, 256)
    $images = [System.Collections.Generic.List[byte[]]]::new()
    foreach ($size in $sizes) {
        $resized = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $resizeGraphics = [System.Drawing.Graphics]::FromImage($resized)
        try {
            $resizeGraphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
            $resizeGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $resizeGraphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $resizeGraphics.DrawImage($canvas, 0, 0, $size, $size)
            $memory = [System.IO.MemoryStream]::new()
            try { $resized.Save($memory, [System.Drawing.Imaging.ImageFormat]::Png); $images.Add($memory.ToArray()) }
            finally { $memory.Dispose() }
        }
        finally { $resizeGraphics.Dispose(); $resized.Dispose() }
    }

    $file = [System.IO.FileStream]::new($icoPath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write)
    $writer = [System.IO.BinaryWriter]::new($file)
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$sizes.Count)
        $offset = 6 + (16 * $sizes.Count)
        for ($index = 0; $index -lt $sizes.Count; $index++) {
            $size = $sizes[$index]
            $bytes = $images[$index]
            $writer.Write([byte]($(if ($size -eq 256) { 0 } else { $size })))
            $writer.Write([byte]($(if ($size -eq 256) { 0 } else { $size })))
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$bytes.Length)
            $writer.Write([uint32]$offset)
            $offset += $bytes.Length
        }
        foreach ($bytes in $images) { $writer.Write($bytes) }
    }
    finally { $writer.Dispose(); $file.Dispose() }
}
finally {
    $graphics.Dispose()
    $canvas.Dispose()
    $source.Dispose()
}

Write-Host "Owner icon ready: $pngPath"
Write-Host "Owner executable icon ready: $icoPath"
