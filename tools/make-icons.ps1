# Gera os icones do Matraca (microfone) em .ico multi-resolucao (PNG embutido).
# Uso: powershell -ExecutionPolicy Bypass -File tools\make-icons.ps1
Add-Type -AssemblyName System.Drawing

$outDir = Split-Path -Parent $PSScriptRoot   # raiz do projeto
$sizes  = 16,20,24,32,48,64,128

function Add-RoundRect($path, [single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $d = $r * 2
    $path.AddArc($x,            $y,            $d, $d, 180, 90)
    $path.AddArc($x + $w - $d,  $y,            $d, $d, 270, 90)
    $path.AddArc($x + $w - $d,  $y + $h - $d,  $d, $d,   0, 90)
    $path.AddArc($x,            $y + $h - $d,  $d, $d,  90, 90)
    $path.CloseFigure()
}

function New-MicBitmap([int]$S, [string]$hex1, [string]$hex2) {
    $bmp = New-Object System.Drawing.Bitmap($S, $S, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.Clear([System.Drawing.Color]::Transparent)

    # fundo arredondado com gradiente
    $pad = [single]($S * 0.05)
    $rw  = [single]($S - 2*$pad)
    $rect = New-Object System.Drawing.RectangleF($pad, $pad, $rw, $rw)
    $bg = New-Object System.Drawing.Drawing2D.GraphicsPath
    Add-RoundRect $bg $pad $pad $rw $rw ([single]($S*0.22))
    $c1 = [System.Drawing.ColorTranslator]::FromHtml($hex1)
    $c2 = [System.Drawing.ColorTranslator]::FromHtml($hex2)
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $c1, $c2, 90.0)
    $g.FillPath($grad, $bg)

    # microfone (branco)
    $white = [System.Drawing.Color]::White
    $wb  = New-Object System.Drawing.SolidBrush($white)
    $pen = New-Object System.Drawing.Pen($white, [single]($S*0.065))
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round

    $center = [single]($S/2)
    $cw = [single]($S*0.24); $ch = [single]($S*0.40)
    $cx = [single]($center - $cw/2); $cy = [single]($S*0.16)

    # capsula (stadium)
    $cap = New-Object System.Drawing.Drawing2D.GraphicsPath
    Add-RoundRect $cap $cx $cy $cw $ch ([single]($cw/2))
    $g.FillPath($wb, $cap)

    # arco em U sob a capsula
    $aw = [single]($cw*2.0); $ah = [single]($ch*1.0)
    $ax = [single]($center - $aw/2); $ay = [single]($cy + $ch*0.15)
    $g.DrawArc($pen, $ax, $ay, $aw, $ah, 0, 180)

    # haste + base
    $stemTop = [single]($ay + $ah)
    $baseY   = [single]($S*0.86)
    $g.DrawLine($pen, $center, $stemTop, $center, $baseY)
    $g.DrawLine($pen, [single]($center-$cw*0.75), $baseY, [single]($center+$cw*0.75), $baseY)

    $g.Dispose()
    return $bmp
}

# Converte um Bitmap num frame DIB (BITMAPINFOHEADER + BGRA bottom-up + mascara AND).
function Get-DibFrame([System.Drawing.Bitmap]$bmp) {
    $S = $bmp.Width
    $rect = New-Object System.Drawing.Rectangle(0, 0, $S, $S)
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
                          [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $stride = $data.Stride
    $buf = New-Object byte[] ($stride * $S)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $buf, 0, $buf.Length)
    $bmp.UnlockBits($data)

    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    # BITMAPINFOHEADER (40 bytes). biHeight = 2*S (cor + mascara)
    $bw.Write([uint32]40); $bw.Write([int32]$S); $bw.Write([int32]($S*2))
    $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]0)
    $bw.Write([uint32]($S*$S*4)); $bw.Write([int32]0); $bw.Write([int32]0)
    $bw.Write([uint32]0); $bw.Write([uint32]0)
    # pixels BGRA, bottom-up
    for ($y = $S-1; $y -ge 0; $y--) { $bw.Write($buf, $y*$stride, $S*4) }
    # mascara AND (1bpp, linhas alinhadas a 4 bytes) toda zero -> alpha controla
    $maskRow = [int][Math]::Floor((($S + 31) / 32)) * 4
    $bw.Write((New-Object byte[] ($maskRow * $S)))
    $bw.Flush()
    $bytes = $ms.ToArray()
    $bw.Close(); $ms.Dispose()
    return ,$bytes
}

function Save-Ico([string]$path, [string]$hex1, [string]$hex2) {
    $frames = New-Object 'System.Collections.Generic.List[byte[]]'
    foreach ($s in $sizes) {
        $bmp = New-MicBitmap $s $hex1 $hex2
        $frames.Add((Get-DibFrame $bmp))   # List.Add evita o flatten do +=
        $bmp.Dispose()
    }
    $fs = [System.IO.File]::Create($path)
    $bw = New-Object System.IO.BinaryWriter($fs)
    $bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)  # ICONDIR
    $offset = 6 + 16*$sizes.Count
    for ($i=0; $i -lt $sizes.Count; $i++) {
        $s = $sizes[$i]; $len = $frames[$i].Length
        $bw.Write([byte]($(if ($s -ge 256) {0} else {$s})))   # width
        $bw.Write([byte]($(if ($s -ge 256) {0} else {$s})))   # height
        $bw.Write([byte]0); $bw.Write([byte]0)                # colors, reserved
        $bw.Write([uint16]1); $bw.Write([uint16]32)           # planes, bpp
        $bw.Write([uint32]$len); $bw.Write([uint32]$offset)   # size, offset
        $offset += $len
    }
    foreach ($f in $frames) { $bw.Write($f) }
    $bw.Flush(); $bw.Close(); $fs.Close()
    Write-Output ("gerado: $path  ($((Get-Item $path).Length) bytes)")
}

Save-Ico (Join-Path $outDir 'app.ico')  '#3D7BE0' '#1E3C72'   # ocioso  - azul
Save-Ico (Join-Path $outDir 'rec.ico')  '#F0433A' '#A11212'   # gravando - vermelho
Save-Ico (Join-Path $outDir 'busy.ico') '#F2B33A' '#B8791A'   # transcrevendo - ambar
