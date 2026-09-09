# Windows Resize Probe

MT-038: delivery to main accepted by the user after reopening the isolated build,
with the DWM shadow/rounded-frame tradeoff communicated. This acceptance does not
establish the assisted checks listed below; those remain tracked under MT-006 in
`docs/BOARD.md`. MT-036 security work remains independent and pending.

Runs production geometry, appearance and painting handlers and `WindowsWebViewHost`
from the supplied Windows build, with a real WebView2 child and fictitious local
HTML. Does not construct `WindowsApplication`, `WindowsConfig` or the bridge.
Creates only in-memory Config objects (mode/palette); reads no personal app files.
Requires Windows, WebView2 Runtime and normal contrast for the pixel assertions.
The profile argument must name a directory that does not exist. PNGs are written
beside it, using the same prefix. Use a fresh temporary path outside the repository.

```powershell
dotnet run --project tests/WindowsResizeProbe/WindowsResizeProbe.csproj -- <absolute-Matraca.dll> <absolute-fresh-temporary-profile>
```

Creates and disposes only its own non-activating tool window. Checks controller and
actual child bounds, HTCLIENT inside the child, all eight uncovered native resize
hits, maximize/work area, restore, and unchanged foreground window. Exercises the
production DPI handler including appearance with a synthetic WM_DPICHANGED at the
current scale; 96/144/192 queries check metrics, not physical monitor changes.
No simulated input, physical drag, application startup or existing app shutdown.

The shell object is deliberately uninitialized: only its window and WebView host
and owned icon set are assigned, then production ApplyAppearance receives the
in-memory Config. Selected real window messages call the built production procedure;
other messages use DefWindowProc. This does not test the full application lifecycle.
Exceptions in callbacks fail the probe. The original geometry-only harness did not
wait for the HTML to render or explicitly make the controller visible: it could
pass with an unpainted client. The current probe waits for the fixture document,
shows its controller, and asserts the real WebView pixel against the generated token.

PrintWindow(PW_RENDERFULLCONTENT) captures only this HWND, never the desktop or
other windows. All restored non-client pixels must equal the actual WebView's
background token (#111417 dark, #FBFCFD light), across 15 mode/palette selections
on the same live window. Also captures synthetic activation and theme/settings
notifications, maximized, restored and current-scale DPI states. Maximized outer
pixels lie outside the work area and are clipped by Windows; they are not a visible
border. Its visible top/bottom and side samples are checked instead, together with
the geometry assertion that the client equals the work area.

Optional `--baseline` captures the original frame, BORDER_COLOR alone and
CAPTION_COLOR plus BORDER_COLOR without applying the production appearance fix.
It is a diagnostic comparison, not a passing regression suite. On the reported
build at DPI 96 it reproduces seven #F3F3F3 top rows and the white inner edge;
BORDER_COLOR changes neither, CAPTION_COLOR leaves the white edge.

The fix keeps NCCALCSIZE and resize hit tests unchanged. WM_NCPAINT owns the band,
excludes the client and releases its GDI resources. DWMNCRP_DISABLED avoids competing
system paint and its shadow/rounded-frame finish. High contrast, failed contrast
query or failed DWM policy call falls back to system painting. No undocumented
dark-mode ordinals or Windows-11-only color attributes are needed in production.
The neutral background is generated from DS tokens by `Matraca.Web/export-design.cjs`;
`--check` validates both CSS and native colors. No design values were changed.

DS 1.0.1: FND-001/003, CMP-004, A11Y-001. References consulted for this fix:

- https://learn.microsoft.com/en-us/windows/win32/dwm/customframe
- https://learn.microsoft.com/en-us/microsoft-edge/webview2/reference/win32/icorewebview2controller
- https://learn.microsoft.com/en-us/windows/win32/hidpi/wm-dpichanged
- https://github.com/MicrosoftEdge/WebView2Feedback/issues/2243
- https://learn.microsoft.com/en-us/windows/win32/gdi/wm-ncpaint
- https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-ncactivate
- https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute
- https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmncrenderingpolicy
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindowdc

The DWM documentation requires placing controls outside the custom frame;
WebView2 Bounds are relative to the parent client. Reserving non-client space
keeps the existing host bounds, HUD, HTML titlebar and screen-coordinate drag
unchanged. Maximized client bounds intersect the monitor work area instead of
retaining a resize inset. Minimum outer size remains clamped to that work area.
Initial outer size includes the exact same resize metrics as the client inset.
Physical dragging, cross-monitor DPI, real system theme transitions, Windows 10,
high contrast and screen-reader behavior still require assisted validation. The
probe never changes OS preferences. Source-contract tests cover the fallback guard,
not execution under actual high contrast. PrintWindow is not a desktop-composition
capture and the fixture is not the complete application UI.

Run .NET contracts in the normal repository output (`dotnet test
tests/Matraca.Core.Tests/Matraca.Core.Tests.csproj`): their existing source locators
walk ancestors of AppContext.BaseDirectory and fail from external artifacts paths.
The isolated application build can use `dotnet build Matraca.sln
-p:EnableWindowsTargeting=true --artifacts-path <temporary-build-directory>`.
