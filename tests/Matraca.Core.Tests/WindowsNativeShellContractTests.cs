using Xunit;

namespace Matraca.Core.Tests;

public sealed class WindowsNativeShellContractTests
{
    [Fact]
    public void WindowsStartsTheNativeApplicationWithoutWindowsForms()
    {
        string program = ReadProjectFile("Matraca.Windows", "Program.cs");
        string project = ReadProjectFile("Matraca.Windows", "Matraca.Windows.csproj");

        Assert.Contains("new WindowsApplication()", program);
        Assert.DoesNotContain("Application.Run", program);
        Assert.DoesNotContain("UseWindowsForms", project);
        Assert.Contains("'%(Reference.Filename)' == 'Microsoft.Web.WebView2.WinForms'", project);
        Assert.DoesNotContain("PackageReference Include=\"NAudio\" ", project);
    }

    [Fact]
    public void KeyboardHookOwnsAPumpedThreadAndTrayConstantsUseWmUser()
    {
        string hook = ReadProjectFile("Matraca.Windows", "WindowsKeyboardHook.cs");
        string native = ReadProjectFile("Matraca.Windows", "WindowsNativeMethods.cs");

        Assert.Contains("new Thread(RunHookLoop)", hook);
        Assert.Contains("GetMessageW(out WindowsMessage message", hook);
        Assert.Contains("PostThreadMessageW(", hook);
        Assert.Contains("internal const uint NinSelect = WmUser;", native);
        Assert.Contains("internal const uint NinKeySelect = WmUser + 1;", native);
    }

    [Fact]
    public void NativeSoundSelectionPreservesMp3AndCompressedWaveSupport()
    {
        string picker = ReadProjectFile("Matraca.Windows", "WindowsFilePicker.cs");
        string messages = ReadProjectFile("Matraca.Windows", "WindowsUiMessages.cs");
        string beeper = ReadProjectFile("Matraca.Windows", "Beeper.cs");

        Assert.Contains("FileSoundFilter", picker);
        Assert.Contains("*.wav;*.mp3", messages);
        Assert.Contains("WaveFormatConversionStream.CreatePcmStream(reader)", beeper);
    }

    [Fact]
    public void NativeSurfacesFollowTheEffectiveInterfaceLanguage()
    {
        string application = ReadProjectFile("Matraca.Windows", "WindowsApplication.cs");
        string bridge = ReadProjectFile("Matraca.Windows", "WindowsWebBridge.cs");
        string hud = ReadProjectFile("Matraca.Windows", "WindowsHudWindow.cs");
        string messages = ReadProjectFile("Matraca.Windows", "WindowsUiMessages.cs");

        Assert.Contains("ApplyNativeLocalization(next)", application);
        Assert.Contains("effectiveUiLanguage = config[\"effectiveUiLanguage\"]", bridge);
        Assert.Contains("ShellState.Writing => \"writing\"", hud);
        Assert.Contains("effectiveUiLanguage = _effectiveUiLanguage", hud);
        Assert.DoesNotContain("Contains(\"escrevendo\"", hud);
        Assert.Contains("Settings...", messages);
        Assert.Contains("Configurações...", messages);
    }

    [Fact]
    public void WebPanelUsesTheSharedFramelessTitleBar()
    {
        string window = ReadProjectFile("Matraca.Windows", "WindowsWebViewWindow.cs");
        string native = ReadProjectFile("Matraca.Windows", "WindowsNativeMethods.cs");

        Assert.Contains("WindowsNativeMethods.WsFramelessResizableWindow", window);
        Assert.DoesNotContain("WindowsNativeMethods.WsOverlappedWindow", window);
        Assert.Contains("WsPopup | WsThickFrame | WsMinimizeBox | WsMaximizeBox", native);
        Assert.DoesNotContain("WsSystemMenu | WsThickFrame", native);
        Assert.Contains("case WindowsNativeMethods.WmNcCalcSize:", window);
        Assert.Contains("case WindowsNativeMethods.WmNcHitTest:", window);
        Assert.Contains("HitTestResizeBorder(window, lParam)", window);
        Assert.Contains("WsThickFrame", native);
        Assert.Contains("WmNcLButtonDown", native);
        Assert.Contains("HtCaption", native);
        Assert.Contains("WindowsNativeMethods.GetCursorPos(out WindowsPoint point)", window);
        Assert.Contains("(point.X & 0xFFFF) | ((point.Y & 0xFFFF) << 16)", window);
        Assert.Contains("WindowsNativeMethods.IsZoomed(_window.Handle)", window);
        Assert.Contains("? WindowsNativeMethods.SwRestore", window);
        Assert.Contains(": WindowsNativeMethods.SwMaximize", window);
    }

    [Fact]
    public void WebViewChildCannotCoverTheNativeResizeFrame()
    {
        string window = ReadProjectFile("Matraca.Windows", "WindowsWebViewWindow.cs");
        string host = ReadProjectFile("Matraca.Windows", "WindowsWebViewHost.cs");
        Assert.Contains("ApplyClientBounds(window, lParam);", window);
        Assert.Contains("WindowsPoint border = ResizeBorder(WindowsNativeMethods.GetDpiForWindow(window));", window);
        Assert.Contains("Scale(DefaultClientWidth, dpi) + 2 * border.X", window);
        Assert.Contains("bounds.Left += x;", window);
        Assert.Contains("bounds.Top += y;", window);
        Assert.Contains("bounds.Right -= x;", window);
        Assert.Contains("bounds.Bottom -= y;", window);
        Assert.Contains("bounds.Top = Math.Max(bounds.Top, info.WorkArea.Top);", window);
        Assert.Contains("bounds.Bottom = Math.Min(bounds.Bottom, info.WorkArea.Bottom);", window);
        Assert.Contains("ApplyDpiBounds(window, lParam);\n                ResizeHost();", window.Replace("\r\n", "\n"));
        Assert.Contains("_host.Resize(client.Width, client.Height);", window);
        Assert.Contains("new Rectangle(0, 0, width, height)", host);
    }

    [Fact]
    public void ResizeFrameOwnsPaintingWithSystemFallbackAndHotAppearance()
    {
        string window = ReadProjectFile("Matraca.Windows", "WindowsWebViewWindow.cs");
        Assert.Contains("ApplyFrameAppearance(theme);", window);
        Assert.Contains("WindowsPanelColors.Dark : WindowsPanelColors.Light", window);
        Assert.Contains("WindowsNativeMethods.GetHighContrast", window);
        Assert.Contains("(contrast.Flags & 1) == 0", window);
        Assert.Contains("custom ? 1 : 0", window);
        Assert.Contains("DwmSetWindowAttribute(_window.Handle, 2, ref policy, sizeof(int)) < 0", window);
        Assert.Contains("case WindowsNativeMethods.WmNcPaint:", window);
        Assert.Contains("ExcludeClipRect(dc, x, y, x + client.Width, y + client.Height)", window);
        Assert.Contains("WindowsNativeMethods.DeleteObject(brush)", window);
        Assert.Contains("WindowsNativeMethods.ReleaseDC(window, dc)", window);
        Assert.Contains("case 0x001A:", window);
        Assert.Contains("case 0x031A:", window);
        Assert.Contains("case 0x031E:", window);
    }

    [Fact]
    public void PanelDefaultsMatchDesignAndClampToNativeWorkArea()
    {
        using var tokens = System.Text.Json.JsonDocument.Parse(
            ReadProjectFile("design", "docs", "design-system-tokens.json"));
        var size = tokens.RootElement.GetProperty("window");
        int width = size.GetProperty("defaultWidth").GetInt32();
        int height = size.GetProperty("defaultHeight").GetInt32();
        string windows = ReadProjectFile("Matraca.Windows", "WindowsWebViewWindow.cs");
        string mac = ReadProjectFile("Matraca.Mac", "Platform", "Web", "MacWebViewHost.cs");
        Assert.Contains($"DefaultClientWidth = {width};", windows);
        Assert.Contains($"DefaultClientHeight = {height};", windows);
        Assert.Contains($"double width = {width},", mac);
        Assert.Contains($"double height = {height},", mac);
        Assert.Contains("Math.Min(bounds.Width, workArea.Width)", windows);
        Assert.Contains("Math.Min(bounds.Height, workArea.Height)", windows);
        Assert.Contains("Math.Min(info.MinimumTrackSize.X, monitorInfo.WorkArea.Width)", windows);
        Assert.Contains("Math.Min(info.MinimumTrackSize.Y, monitorInfo.WorkArea.Height)", windows);
        Assert.Contains("Math.Min(frame.Size.Width, visible.Size.Width)", mac);
        Assert.Contains("Math.Min(frame.Size.Height, visible.Size.Height)", mac);
        Assert.Contains("ObjC.sel_registerName(\"visibleFrame\")", mac);
    }

    [Fact]
    public void PinnedTargetsPreserveTheFocusedChildControl()
    {
        string target = ReadProjectFile("Matraca.Windows", "WindowsTargetWindow.cs");
        string sink = ReadProjectFile("Matraca.Windows", "WindowsTextSink.cs");
        string injector = ReadProjectFile("Matraca.Windows", "TextInjector.cs");

        Assert.Contains("TextInjector.GetFocusedControl(handle)", target);
        Assert.Contains("descriptor.FocusedControl", sink);
        Assert.Contains("RestoreFocusedControl(hwnd, focusedControl)", injector);
        Assert.Contains("Thread.Sleep(TargetActivationSettleMs)", injector);
        Assert.Contains("GetForegroundWindow() != hwnd", injector);
        Assert.Contains("GetGUIThreadInfo", injector);
        Assert.Contains("GetForegroundWindow() == hwnd", injector);
    }

    [Theory]
    [InlineData("TrayApp.cs")]
    [InlineData("SettingsForm.cs")]
    [InlineData("OnboardingForm.cs")]
    [InlineData("HistoryForm.cs")]
    [InlineData("FocusBorder.cs")]
    [InlineData("WindowsWebWindow.cs")]
    [InlineData("WindowsShell.cs")]
    public void WindowsFormsShellFilesAreRemoved(string fileName)
    {
        Assert.False(File.Exists(ProjectPath("Matraca.Windows", fileName)));
    }

    private static string ReadProjectFile(params string[] parts) => File.ReadAllText(ProjectPath(parts));

    private static string ProjectPath(params string[] parts)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Matraca.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return Path.Combine([directory!.FullName, .. parts]);
    }
}
