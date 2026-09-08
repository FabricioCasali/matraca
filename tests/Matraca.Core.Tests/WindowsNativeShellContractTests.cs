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
        string beeper = ReadProjectFile("Matraca.Windows", "Beeper.cs");

        Assert.Contains("*.wav;*.mp3", picker);
        Assert.Contains("WaveFormatConversionStream.CreatePcmStream(reader)", beeper);
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
