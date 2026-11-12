# Genera src/ScreenLove/app.ico (cuore bianco su quadrato rosa) in più dimensioni.
# Stesso disegno di Services/IconFactory.cs. Da rieseguire solo se cambia il logo.
param([string]$Out = "$PSScriptRoot\..\src\ScreenLove\app.ico")

Add-Type -AssemblyName System.Drawing

function New-HeartPng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 32.0

    $r = New-Object System.Drawing.RectangleF (1 * $s), (1 * $s), (30 * $s), (30 * $s)
    $d = 14 * $s
    $bg = New-Object System.Drawing.Drawing2D.GraphicsPath
    $bg.AddArc($r.X, $r.Y, $d, $d, 180, 90)
    $bg.AddArc($r.Right - $d, $r.Y, $d, $d, 270, 90)
    $bg.AddArc($r.Right - $d, $r.Bottom - $d, $d, $d, 0, 90)
    $bg.AddArc($r.X, $r.Bottom - $d, $d, $d, 90, 90)
    $bg.CloseFigure()
    $accent = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0xFF, 0x3D, 0x7F))
    $g.FillPath($accent, $bg)

    $heart = New-Object System.Drawing.Drawing2D.GraphicsPath
    $heart.FillMode = [System.Drawing.Drawing2D.FillMode]::Winding
    $heart.AddEllipse(6.5 * $s, 8 * $s, 10 * $s, 10 * $s)
    $heart.AddEllipse(15.5 * $s, 8 * $s, 10 * $s, 10 * $s)
    $heart.AddPolygon([System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF (7.2 * $s), (15.5 * $s)),
        (New-Object System.Drawing.PointF (24.8 * $s), (15.5 * $s)),
        (New-Object System.Drawing.PointF (16 * $s), (25.5 * $s))))
    $g.FillPath([System.Drawing.Brushes]::White, $heart)
    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return , $ms.ToArray()
}

# Formato ICO: intestazione + una voce per immagine + i dati PNG.
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$images = [System.Collections.Generic.List[byte[]]]::new()
foreach ($size in $sizes) { $images.Add([byte[]](New-HeartPng $size)) }

$ico = New-Object System.IO.MemoryStream
$w = [System.IO.BinaryWriter]::new($ico)
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$images[$i].Length); $w.Write([UInt32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $w.Write($img) }
$w.Flush()
$target = [System.IO.Path]::GetFullPath($Out)
[System.IO.File]::WriteAllBytes($target, $ico.ToArray())
Write-Host "Creato $target ($($ico.Length) byte, $($sizes.Count) dimensioni)"
