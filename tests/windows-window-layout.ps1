param([Parameter(Mandatory)][string]$AssemblyPath)
$ErrorActionPreference = 'Stop'

# Calls only the built sizing routine and read-only Win32 APIs. No window or app is started.
$assembly = [Reflection.Assembly]::LoadFrom($AssemblyPath)
$flags = [Reflection.BindingFlags]'Static,NonPublic'
$window = $assembly.GetType('Matraca.WindowsWebViewWindow', $true)
$native = $assembly.GetType('Matraca.WindowsNativeMethods', $true)
$rectangle = $assembly.GetType('Matraca.WindowsRectangle', $true)
$dpi = $native.GetMethod('GetDpiForSystem', $flags).Invoke($null, @())
if ($dpi -eq 0) { $dpi = [uint32]96 }
$areaArgs = [object[]]@([uint32]48, [uint32]0, [Activator]::CreateInstance($rectangle), [uint32]0)
if (!$native.GetMethod('SystemParametersInfo', $flags).Invoke($null, $areaArgs)) {
    throw 'SPI_GETWORKAREA failed; cannot validate current screen.'
}
$area = $areaArgs[2]
$bounds = $window.GetMethod('InitialBounds', $flags).Invoke($null, @())
$sizes = @()
foreach ($size in @(@(1120, 760), @(1200, 820))) {
    $rect = [Activator]::CreateInstance($rectangle)
    $rect.Right = [int][Math]::Round($size[0] * $dpi / 96.0)
    $rect.Bottom = [int][Math]::Round($size[1] * $dpi / 96.0)
    $adjustArgs = [object[]]@($rect, $native.GetField('WsFramelessResizableWindow', $flags).GetRawConstantValue(), $false, [uint32]0, $dpi)
    if (!$native.GetMethod('AdjustWindowRectExForDpi', $flags).Invoke($null, $adjustArgs)) {
        throw 'AdjustWindowRectExForDpi failed.'
    }
    $sizes += @{ width = $adjustArgs[0].Right - $adjustArgs[0].Left; height = $adjustArgs[0].Bottom - $adjustArgs[0].Top }
}
$areaWidth = $area.Right - $area.Left
$areaHeight = $area.Bottom - $area.Top
if ($bounds.Right - $bounds.Left -ne [Math]::Min($sizes[1].width, $areaWidth) -or
    $bounds.Bottom - $bounds.Top -ne [Math]::Min($sizes[1].height, $areaHeight) -or
    $bounds.Left -lt $area.Left -or $bounds.Top -lt $area.Top -or
    $bounds.Right -gt $area.Right -or $bounds.Bottom -gt $area.Bottom) {
    throw 'Built InitialBounds does not fit the current work area.'
}
[pscustomobject]@{
    dpi = $dpi
    workArea = "${areaWidth}x${areaHeight}"
    previousOuterPixels = "$($sizes[0].width)x$($sizes[0].height)"
    newOuterPixelsBeforeClamp = "$($sizes[1].width)x$($sizes[1].height)"
    builtInitialBoundsPixels = "$($bounds.Right - $bounds.Left)x$($bounds.Bottom - $bounds.Top)"
    result = 'PASS: actual Win32 sizing; no application window opened'
} | ConvertTo-Json
