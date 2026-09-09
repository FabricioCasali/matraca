using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Matraca;

internal static class Program
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static readonly ConcurrentQueue<Action> Actions = new();
    private static object Shell = null!;
    private static Type ShellType = null!;
    private static Exception? CallbackFailure;

    // Loads the NEW build, never WindowsApplication/WindowsConfig. Only this probe's
    // non-activating window and fresh WebView2 profile are created and disposed.
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length is < 2 or > 3 || (args.Length == 3 && args[2] != "--baseline"))
            throw new ArgumentException("Usage: WindowsResizeProbe <Matraca.dll> <fresh-profile-directory> [--baseline]");
        bool baseline = args.Length == 3;
        string assemblyPath = Path.GetFullPath(args[0]);
        string profile = Path.GetFullPath(args[1]);
        if (Directory.Exists(profile)) throw new ArgumentException("Profile must not exist; personal profiles are forbidden.");
        NativeLibrary.Load(Path.Combine(Path.GetDirectoryName(assemblyPath)!, "runtimes",
            "win-" + RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(), "native", "WebView2Loader.dll"));
        AssemblyLoadContext.Default.Resolving += (_, name) =>
            AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(Path.GetDirectoryName(assemblyPath)!, name.Name + ".dll"));
        var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
        Native.SetThreadDpiAwarenessContext(new nint(-4));
        ShellType = assembly.GetType("Matraca.WindowsWebViewWindow", true)!;
        Shell = RuntimeHelpers.GetUninitializedObject(ShellType);
        foreach (uint dpi in new uint[] { 96, 144, 192 })
        {
            object border = ShellType.GetMethod("ResizeBorder", All)!.Invoke(null, [dpi])!;
            int x = (int)border.GetType().GetField("X")!.GetValue(border)!;
            int y = (int)border.GetType().GetField("Y")!.GetValue(border)!;
            Check(x > 0 && y > 0, "positive system resize metrics");
            Console.WriteLine($"PASS system metrics only: DPI={dpi}, border={x},{y} (not a monitor transition)");
        }
        var procedure = Delegate.CreateDelegate(assembly.GetType("Matraca.WindowsWindowProcedure", true)!,
            typeof(Program).GetMethod(nameof(WindowProcedure), All)!);
        var windowType = assembly.GetType("Matraca.WindowsNativeWindow", true)!;
        object initial = ShellType.GetMethod("InitialBounds", All)!.Invoke(null, null)!;
        int Edge(string name) => (int)initial.GetType().GetField(name)!.GetValue(initial)!;
        using var icons = (IDisposable)Activator.CreateInstance(assembly.GetType("Matraca.WindowsIconSet", true)!)!;
        using var window = (IDisposable)Activator.CreateInstance(windowType,
            ["ResizeProbe", procedure, "Matraca isolated resize probe", (uint)0x80070000, (uint)0x08000080,
                Edge("Left"), Edge("Top"), Edge("Right") - Edge("Left"), Edge("Bottom") - Edge("Top")])!;
        nint handle = (nint)windowType.GetProperty("Handle")!.GetValue(window)!;
        ShellType.GetField("_window", All)!.SetValue(Shell, window);
        ShellType.GetField("_icons", All)!.SetValue(Shell, icons);
        if (!baseline) Appearance("dark", "olive");
        var hostType = assembly.GetType("Matraca.WindowsWebViewHost", true)!;
        using var host = (IDisposable)Activator.CreateInstance(hostType,
            [handle, AppContext.BaseDirectory, profile, (Action<Action>)Actions.Enqueue, "fixture.html", false])!;
        ShellType.GetField("_host", All)!.SetValue(Shell, host);
        Task initialized = (Task)hostType.GetMethod("InitializeAsync")!.Invoke(host, null)!;
        PumpUntil(() => initialized.IsCompleted);
        initialized.GetAwaiter().GetResult();
        InvokeShell("ResizeHost");
        var core = hostType.GetField("_core", All)!.GetValue(host)!;
        Task<string> document = (Task<string>)core.GetType().GetMethod("ExecuteScriptAsync")!.Invoke(core, ["document.title"])!;
        PumpUntil(() => document.IsCompleted);
        document.GetAwaiter().GetResult();
        nint foreground = Native.GetForegroundWindow();
        Native.ShowWindow(handle, 4); // SW_SHOWNOACTIVATE, also WS_EX_NOACTIVATE.
        PumpUntil(() => Script(core, "document.readyState === 'complete' && document.title === 'Isolated resize probe'") == "true");
        hostType.GetMethod("SetVisible")!.Invoke(host, [true]);
        Script(core, "document.documentElement.dataset.theme='dark'");
        if (baseline)
        {
            Capture(handle, profile + "-before.png", false);
            uint color = 0x171411;
            Console.WriteLine($"Border HRESULT={Native.DwmSetWindowAttribute(handle, 34, ref color, 4):X8}");
            Capture(handle, profile + "-border-only.png", false);
            Console.WriteLine($"Caption HRESULT={Native.DwmSetWindowAttribute(handle, 35, ref color, 4):X8}");
            Capture(handle, profile + "-caption-border.png", false);
            return 0;
        }
        foreach (string mode in new[] { "dark", "light", "system" })
        foreach (string palette in new[] { "olive", "ochre", "terracotta", "plum", "teal" })
        {
            Appearance(mode, palette);
            Script(core, $"document.documentElement.dataset.palette='{palette}';" + (mode == "system"
                ? "delete document.documentElement.dataset.theme" : $"document.documentElement.dataset.theme='{mode}'"));
            Verify(handle, host, $"{mode}-{palette}");
            Capture(handle, $"{profile}-{mode}-{palette}.png", true);
        }
        Appearance("dark", "olive");
        Script(core, "document.documentElement.dataset.theme='dark'");
        Native.SendMessageW(handle, 0x0086, 1, 0);
        Capture(handle, profile + "-active-message.png", true);
        Native.SendMessageW(handle, 0x0086, 0, 0);
        Native.SendMessageW(handle, 0x031A, 0, 0);
        Native.SendMessageW(handle, 0x001A, 0, 0);
        Capture(handle, profile + "-system-messages.png", true);
        Verify(handle, host, "restored");
        Native.ShowWindow(handle, 3);
        Verify(handle, host, "maximized");
        Capture(handle, profile + "-maximized.png", true);
        Native.ShowWindow(handle, 9);
        Verify(handle, host, "restored-again");
        Capture(handle, profile + "-restored.png", true);

        var suggested = new WindowsRectangle { Left = 90, Top = 90, Right = 1050, Bottom = 750 };
        nint memory = Marshal.AllocHGlobal(Marshal.SizeOf<WindowsRectangle>());
        try
        {
            Marshal.StructureToPtr(suggested, memory, false);
            uint dpi = Native.GetDpiForWindow(handle);
            Native.SendMessageW(handle, 0x02E0, (nint)(dpi | dpi << 16), memory);
            Verify(handle, host, "dpi-message-current-scale");
            Capture(handle, profile + "-dpi.png", true);
        }
        finally { Marshal.FreeHGlobal(memory); }
        Check(Native.GetForegroundWindow() == foreground, "probe did not change foreground window");
        Console.WriteLine($"PASS build={assemblyPath}; MVID={assembly.ManifestModule.ModuleVersionId}; DPI={Native.GetDpiForWindow(handle)}");
        Console.WriteLine("Real HWND/WebView2 geometry and hit tests; no physical drag or cross-monitor DPI transition tested.");
        GC.KeepAlive(procedure);
        return 0;
    }

    private static nint WindowProcedure(nint window, uint message, nint wParam, nint lParam)
    {
        try
        {
            if (message is 0x0083 or 0x0084 or 0x0085 or 0x0086 or 0x0024 or 0x0005 or 0x0003 or 0x02E0
                || (message is 0x001A or 0x031A or 0x031E && ShellType.GetField("_appearance", All)!.GetValue(Shell) != null))
                return (nint)ShellType.GetMethod("WindowProcedure", All)!.Invoke(Shell, [window, message, wParam, lParam])!;
        }
        catch (Exception exception) { CallbackFailure = exception; }
        return Native.DefWindowProcW(window, message, wParam, lParam);
    }

    private static void Verify(nint window, object host, string label)
    {
        InvokeShell("ResizeHost");
        if (CallbackFailure != null) throw CallbackFailure;
        Check(Native.GetWindowRect(window, out var outer), "window bounds");
        Check(Native.GetClientRect(window, out var client), "client bounds");
        var origin = new WindowsPoint();
        Check(Native.ClientToScreen(window, ref origin), "client origin");
        var controller = host.GetType().GetField("_controller", All)!.GetValue(host)!;
        var bounds = (System.Drawing.Rectangle)controller.GetType().GetProperty("Bounds")!.GetValue(controller)!;
        Check(bounds == new System.Drawing.Rectangle(0, 0, client.Width, client.Height), "WebView fills only native client");
        var center = new WindowsPoint { X = client.Width / 2, Y = client.Height / 2 };
        nint child = Native.ChildWindowFromPointEx(window, center, 0);
        Check(child != 0 && child != window, "real WebView child exists");
        Check(Native.GetWindowRect(child, out var childRect), "WebView child bounds");
        Check(childRect.Left == origin.X && childRect.Top == origin.Y && childRect.Width == client.Width && childRect.Height == client.Height,
            "real child matches controller bounds");
        Native.ClientToScreen(window, ref center);
        Check(Hit(child, center.X, center.Y) == 1, "WebView child returns HTCLIENT inside content");
        if (!Native.IsZoomed(window))
        {
            Check(origin.X > outer.Left && origin.Y > outer.Top, "resize band lies outside client");
            foreach (var (x, y, expected) in new[] {
                (outer.Left, outer.Top + outer.Height / 2, 10), (outer.Right - 1, outer.Top + outer.Height / 2, 11),
                (outer.Left + outer.Width / 2, outer.Top, 12), (outer.Left + outer.Width / 2, outer.Bottom - 1, 15),
                (outer.Left, outer.Top, 13), (outer.Right - 1, outer.Top, 14),
                (outer.Left, outer.Bottom - 1, 16), (outer.Right - 1, outer.Bottom - 1, 17) })
            {
                Check(Hit(window, x, y) == expected, $"{label}: native hit {expected}");
                var point = new WindowsPoint { X = x, Y = y };
                Native.ScreenToClient(window, ref point);
                nint coveringChild = Native.ChildWindowFromPointEx(window, point, 0);
                Check(coveringChild == 0 || coveringChild == window, "border not covered by child");
            }
        }
        else
        {
            Check(Hit(window, origin.X, origin.Y) == 1, "maximized has no resize hit");
            // Production ApplyMinimumSize reads the current monitor; get its work area
            // through the same assembly without accessing other application windows.
            var native = ShellType.Assembly.GetType("Matraca.WindowsNativeMethods")!;
            nint monitor = (nint)native.GetMethod("MonitorFromWindow", All)!.Invoke(null, [window, (uint)2])!;
            var infoType = ShellType.Assembly.GetType("Matraca.WindowsMonitorInfo")!;
            object info = Activator.CreateInstance(infoType)!;
            infoType.GetField("Size")!.SetValue(info, (uint)Marshal.SizeOf(infoType));
            object[] args = [monitor, info];
            Check((bool)native.GetMethod("GetMonitorInfo", All)!.Invoke(null, args)!, "monitor info");
            object area = infoType.GetField("WorkArea")!.GetValue(args[1])!;
            int Field(string name) => (int)area.GetType().GetField(name)!.GetValue(area)!;
            Check(origin.X == Field("Left") && origin.Y == Field("Top") && origin.X + client.Width == Field("Right")
                && origin.Y + client.Height == Field("Bottom"), "maximized content exactly fits work area");
        }
        Console.WriteLine($"PASS {label}: outer={outer.Width}x{outer.Height}, client={client.Width}x{client.Height}, inset={origin.X - outer.Left},{origin.Y - outer.Top}; child=HTCLIENT");
    }

    private static int Hit(nint window, int x, int y) => (int)Native.SendMessageW(window, 0x0084, 0, (nint)((x & 0xffff) | (y & 0xffff) << 16));
    private static string Script(object core, string script)
    {
        var task = (Task<string>)core.GetType().GetMethod("ExecuteScriptAsync")!.Invoke(core, [script])!;
        PumpUntil(() => task.IsCompleted);
        return task.GetAwaiter().GetResult();
    }

    private static void Appearance(string mode, string palette)
    {
        var method = ShellType.GetMethod("ApplyAppearance", All)!;
        var type = method.GetParameters()[0].ParameterType;
        object config = Activator.CreateInstance(type)!;
        type.GetProperty("ThemeMode")!.SetValue(config, mode);
        type.GetProperty("Palette")!.SetValue(config, palette);
        method.Invoke(Shell, [config]);
    }

    private static void Capture(nint window, string path, bool verify)
    {
        DateTime ready = DateTime.UtcNow.AddMilliseconds(400);
        PumpUntil(() => DateTime.UtcNow >= ready);
        Native.GetWindowRect(window, out var bounds);
        using var bitmap = new System.Drawing.Bitmap(bounds.Width, bounds.Height);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        {
            nint dc = graphics.GetHdc();
            try { Check(Native.PrintWindow(window, dc, 2), "native window capture"); }
            finally { graphics.ReleaseHdc(dc); }
        }
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        Console.WriteLine($"CAPTURE {path}: top=" + string.Join(',', Enumerable.Range(0, 12).Select(y => bitmap.GetPixel(bounds.Width / 2, y).Name))
            + "; left=" + string.Join(',', Enumerable.Range(0, 12).Select(x => bitmap.GetPixel(x, bounds.Height / 2).Name)));
        if (!verify) return;
        Check((bool)ShellType.GetField("_paintFrame", All)!.GetValue(Shell)!, "custom frame enabled (run normal themes outside high contrast)");
        Native.GetClientRect(window, out var client);
        var origin = new WindowsPoint();
        Native.ClientToScreen(window, ref origin);
        int left = origin.X - bounds.Left, top = origin.Y - bounds.Top;
        uint color = (uint)ShellType.GetField("_frameColor", All)!.GetValue(Shell)!;
        int expected = System.Drawing.Color.FromArgb((int)(color & 255), (int)(color >> 8 & 255), (int)(color >> 16 & 255)).ToArgb();
        Check(bitmap.GetPixel(left + client.Width / 2, top + 2).ToArgb() == expected, "real WebView background matches native token");
        if (Native.IsZoomed(window))
        {
            // Verify() proves the client equals the work area. The outer frame is
            // off-screen/behind the taskbar, so PrintWindow's black pixels there
            // are not visible window paint. Check the visible perimeter instead.
            for (int x = left; x < left + client.Width; x++)
            {
                Check(bitmap.GetPixel(x, top).ToArgb() == expected, "maximized visible top");
                Check(bitmap.GetPixel(x, top + client.Height - 1).ToArgb() == expected, "maximized visible bottom");
            }
            foreach (int y in new[] { top + 2, top + client.Height - 2 })
            {
                Check(bitmap.GetPixel(left, y).ToArgb() == expected, "maximized visible left");
                Check(bitmap.GetPixel(left + client.Width - 1, y).ToArgb() == expected, "maximized visible right");
            }
            Console.WriteLine("PASS maximized visible perimeter matches background; outer clipped pixels excluded");
            return;
        }
        for (int y = 0; y < bounds.Height; y++)
        for (int x = 0; x < bounds.Width; x++)
            if (x < left || x >= left + client.Width || y < top || y >= top + client.Height)
                Check(bitmap.GetPixel(x, y).ToArgb() == expected, $"frame pixel {x},{y}: {bitmap.GetPixel(x, y).Name}, expected {expected:X8}");
        Console.WriteLine("PASS all non-client pixels match the WebView background token");
    }
    private static void InvokeShell(string method) => ShellType.GetMethod(method, All)!.Invoke(Shell, null);
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void PumpUntil(Func<bool> finished)
    {
        var timeout = DateTime.UtcNow.AddSeconds(30);
        while (!finished())
        {
            while (Actions.TryDequeue(out var action)) action();
            while (Native.PeekMessageW(out var message, 0, 0, 0, 1))
            {
                Native.TranslateMessage(ref message);
                Native.DispatchMessageW(ref message);
            }
            if (DateTime.UtcNow > timeout) throw new TimeoutException("WebView2 probe timeout");
            Thread.Sleep(5);
        }
    }
}
