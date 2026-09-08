param([string]$PreviewPath, [string]$ExePath)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class MatracaIconProbe {
    [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    public static extern IntPtr LoadImageW(IntPtr instance, string name, uint type, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr icon);
}
'@
$root = Split-Path -Parent $PSScriptRoot
$icons = Join-Path $root 'Matraca.Windows/Icons'
$files = @('app.ico') + @(foreach ($theme in 'light','dark') {
    foreach ($palette in 'olive','ochre','terracotta','plum','teal') { "Icons/app-$palette-$theme.ico" }
    foreach ($state in 'idle','recording','busy','error') { "Icons/$state-$theme.ico" }
})
$count = 0
foreach ($file in $files) {
    foreach ($size in 16,20,24,32,40,48,64,128,256) {
        $handle = [MatracaIconProbe]::LoadImageW(0, (Join-Path $root "Matraca.Windows/$file"), 1, $size, $size, 0x10)
        if ($handle -eq 0) { throw "LoadImage falhou: $file $size" }
        try {
            $icon = [System.Drawing.Icon]::FromHandle($handle)
            if ($icon.Width -ne $size -or $icon.Height -ne $size) { throw "Dimensao incorreta: $file $size" }
            $count++
        } finally {
            if ($icon) { $icon.Dispose(); $icon = $null }
            [void][MatracaIconProbe]::DestroyIcon($handle)
        }
    }
}
"LoadImage nativo: $count frames OK"
if ($ExePath) {
    $embedded = [System.Drawing.Icon]::ExtractAssociatedIcon($ExePath)
    if (!$embedded) { throw 'EXE sem icone extraivel.' }
    $handle = [MatracaIconProbe]::LoadImageW(0, (Join-Path $root 'Matraca.Windows/app.ico'), 1, $embedded.Width, $embedded.Height, 0x10)
    if ($handle -eq 0) { $embedded.Dispose(); throw 'Falha ao carregar referencia do EXE.' }
    $expected = [System.Drawing.Icon]::FromHandle($handle)
    $actualBitmap = $embedded.ToBitmap()
    $expectedBitmap = $expected.ToBitmap()
    try {
        for ($y = 0; $y -lt $actualBitmap.Height; $y++) {
            for ($x = 0; $x -lt $actualBitmap.Width; $x++) {
                if ($actualBitmap.GetPixel($x,$y).ToArgb() -ne $expectedBitmap.GetPixel($x,$y).ToArgb()) {
                    throw "Icone embutido no EXE diverge do app.ico oficial em $x,$y."
                }
            }
        }
        'Icone extraido do EXE corresponde pixel a pixel ao app.ico oficial.'
    } finally {
        $actualBitmap.Dispose(); $expectedBitmap.Dispose(); $expected.Dispose(); $embedded.Dispose()
        [void][MatracaIconProbe]::DestroyIcon($handle)
    }
}
if (!$PreviewPath) { return }
if (!(Test-Path -LiteralPath (Split-Path -Parent $PreviewPath))) { throw 'Pasta de preview inexistente.' }
$bitmap = [System.Drawing.Bitmap]::new(640, 320)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
try {
    $graphics.Clear([System.Drawing.Color]::White)
    $graphics.FillRectangle([System.Drawing.Brushes]::Black, 0, 160, 640, 160)
    $row = 0
    foreach ($theme in 'light','dark') {
        $column = 0
        foreach ($state in 'idle','recording','busy','error') {
            $x = 8 + $column * 160
            foreach ($size in 16,24,32,48) {
                $handle = [MatracaIconProbe]::LoadImageW(0, (Join-Path $icons "$state-$theme.ico"), 1, $size, $size, 0x10)
                if ($handle -eq 0) { throw 'Falha ao renderizar preview.' }
                try {
                    $icon = [System.Drawing.Icon]::FromHandle($handle)
                    $graphics.DrawIcon($icon, $x, 12 + $row * 160)
                } finally { $icon.Dispose(); [void][MatracaIconProbe]::DestroyIcon($handle) }
                $x += $size + 4
            }
            $column++
        }
        $column = 0
        foreach ($palette in 'olive','ochre','terracotta','plum','teal') {
            $handle = [MatracaIconProbe]::LoadImageW(0, (Join-Path $icons "app-$palette-$theme.ico"), 1, 64, 64, 0x10)
            if ($handle -eq 0) { throw 'Falha no preview de aplicativo.' }
            try {
                $icon = [System.Drawing.Icon]::FromHandle($handle)
                $graphics.DrawIcon($icon, 12 + $column * 125, 84 + $row * 160)
            } finally { $icon.Dispose(); [void][MatracaIconProbe]::DestroyIcon($handle) }
            $column++
        }
        $row++
    }
    $bitmap.Save($PreviewPath, [System.Drawing.Imaging.ImageFormat]::Png)
    "Preview: $PreviewPath"
} finally { $graphics.Dispose(); $bitmap.Dispose() }
