$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$aniRoot = Split-Path $PSScriptRoot -Parent
$aniSource = [System.Drawing.Image]::FromFile((Join-Path $aniRoot 'Assets\anitv-icon.png'))
$aniSizes = @(16,24,32,48,64,128,256)
$aniFrames = @()
try {
    foreach ($aniSize in $aniSizes) {
        $aniBitmap = [System.Drawing.Bitmap]::new($aniSize, $aniSize)
        $aniGraphics = [System.Drawing.Graphics]::FromImage($aniBitmap)
        $aniStream = [System.IO.MemoryStream]::new()
        try {
            $aniGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $aniGraphics.DrawImage($aniSource, 0, 0, $aniSize, $aniSize)
            $aniBitmap.Save($aniStream, [System.Drawing.Imaging.ImageFormat]::Png)
            $aniFrames += ,$aniStream.ToArray()
        } finally { $aniGraphics.Dispose(); $aniBitmap.Dispose(); $aniStream.Dispose() }
    }
} finally { $aniSource.Dispose() }
$aniOutput = [System.IO.File]::Create((Join-Path $aniRoot 'Assets\anitv.ico'))
$aniWriter = [System.IO.BinaryWriter]::new($aniOutput)
try {
    $aniWriter.Write([uint16]0); $aniWriter.Write([uint16]1); $aniWriter.Write([uint16]$aniSizes.Count)
    $aniOffset = 6 + 16 * $aniSizes.Count
    for ($i = 0; $i -lt $aniSizes.Count; $i++) {
        $aniDimension = if ($aniSizes[$i] -eq 256) { 0 } else { $aniSizes[$i] }
        $aniWriter.Write([byte]$aniDimension); $aniWriter.Write([byte]$aniDimension)
        $aniWriter.Write([byte]0); $aniWriter.Write([byte]0)
        $aniWriter.Write([uint16]1); $aniWriter.Write([uint16]32)
        $aniWriter.Write([uint32]$aniFrames[$i].Length); $aniWriter.Write([uint32]$aniOffset)
        $aniOffset += $aniFrames[$i].Length
    }
    foreach ($aniFrame in $aniFrames) { $aniWriter.Write([byte[]]$aniFrame) }
} finally { $aniWriter.Dispose() }
Write-Host 'Created Assets\anitv.ico (16–256 px)'
