using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Matraca;

internal sealed class WindowsWebWindow : Form
{
    private const string VirtualHost = "matraca.local";
    private readonly WindowsWebBridge _bridge;
    private readonly WebView2 _webView;
    private Task? _initialization;
    private string _route = "home";
    private bool _allowClose;

    public WindowsWebWindow(WindowsWebBridge bridge)
    {
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        _webView = new WebView2 { Dock = DockStyle.Fill };
        Text = "Matraca";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1120, 760);
        MinimumSize = new Size(840, 620);
        Icon = LoadWindowIcon();
        Controls.Add(_webView);
        Shown += OnShown;
        FormClosing += OnFormClosing;
        _bridge.MessageProduced += PostJson;
        _bridge.CloseWindowRequested += HideExplicitly;
    }

    public void Open(string route)
    {
        _route = NormalizeRoute(route);
        _bridge.PrepareToOpen(Handle);
        if (!Visible) Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
        NavigateToRoute();
    }

    public void Shutdown()
    {
        if (IsDisposed) return;
        _allowClose = true;
        Close();
        Dispose();
    }

    private async void OnShown(object? sender, EventArgs eventArgs)
    {
        try
        {
            _initialization ??= InitializeAsync();
            await _initialization;
            NavigateToRoute();
        }
        catch (Exception exception)
        {
            if (IsDisposed || Disposing || _allowClose) return;
            Logger.Error("Falha ao iniciar WebView2", exception);
            MessageBox.Show(
                this,
                "Não consegui abrir o painel compartilhado. Verifique se o WebView2 Runtime está instalado.\n\n"
                    + exception.Message,
                "Matraca",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private async Task InitializeAsync()
    {
        string assetRoot = Path.Combine(AppContext.BaseDirectory, "Web");
        if (!File.Exists(Path.Combine(assetRoot, "index.html")))
            throw new DirectoryNotFoundException($"Assets web não encontrados em {assetRoot}.");

        string userDataFolder = Path.Combine(WindowsConfig.Paths.DataDirectory, "WebView2");
        Directory.CreateDirectory(userDataFolder);
        CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(
            userDataFolder: userDataFolder);
        await _webView.EnsureCoreWebView2Async(environment);
        if (IsDisposed || Disposing || _allowClose) return;
        CoreWebView2 core = _webView.CoreWebView2;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.SetVirtualHostNameToFolderMapping(
            VirtualHost,
            assetRoot,
            CoreWebView2HostResourceAccessKind.DenyCors);
        core.NavigationStarting += OnNavigationStarting;
        core.NewWindowRequested += OnNewWindowRequested;
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += OnWebResourceRequested;
        core.WebMessageReceived += OnWebMessageReceived;
        core.Navigate(BuildUri(_route));
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs eventArgs)
    {
        if (!IsLocalUri(eventArgs.Uri)) eventArgs.Cancel = true;
    }

    private static void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs eventArgs)
        => eventArgs.Handled = true;

    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs eventArgs)
    {
        if (IsLocalUri(eventArgs.Request.Uri)) return;
        eventArgs.Response = _webView.CoreWebView2.Environment.CreateWebResourceResponse(
            Stream.Null,
            403,
            "Forbidden",
            "Content-Type: text/plain");
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs eventArgs)
    {
        if (!IsLocalUri(eventArgs.Source)) return;
        string json;
        try { json = eventArgs.TryGetWebMessageAsString(); }
        catch { return; }

        string? response = await _bridge.HandleAsync(json);
        if (response != null) PostJson(response);
    }

    private void PostJson(string json)
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(() => PostJson(json)); } catch (InvalidOperationException) { }
            return;
        }
        if (_webView.CoreWebView2 == null) return;
        string literal = JsonSerializer.Serialize(json);
        _ = _webView.ExecuteScriptAsync($"globalThis.matraca?.onMessage({literal});");
    }

    private void NavigateToRoute()
    {
        if (_webView.CoreWebView2 == null) return;
        string literal = JsonSerializer.Serialize(_route);
        _ = _webView.ExecuteScriptAsync($"location.hash = {literal};");
    }

    private void HideExplicitly()
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(HideExplicitly); } catch (InvalidOperationException) { }
            return;
        }
        Hide();
        _bridge.WindowClosed();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        if (_allowClose) return;
        eventArgs.Cancel = true;
        Hide();
        _bridge.WindowClosed();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _bridge.MessageProduced -= PostJson;
            _bridge.CloseWindowRequested -= HideExplicitly;
            if (_webView.CoreWebView2 != null)
            {
                _webView.CoreWebView2.NavigationStarting -= OnNavigationStarting;
                _webView.CoreWebView2.NewWindowRequested -= OnNewWindowRequested;
                _webView.CoreWebView2.WebResourceRequested -= OnWebResourceRequested;
                _webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
            }
            _webView.Dispose();
            Icon?.Dispose();
        }
        base.Dispose(disposing);
    }

    private static string NormalizeRoute(string route)
        => route is "history" or "settings" or "microphone" or "onboarding" ? route : "home";

    private static string BuildUri(string route) => $"https://{VirtualHost}/index.html#{route}";

    private static bool IsLocalUri(string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && uri.Host.Equals(VirtualHost, StringComparison.OrdinalIgnoreCase);

    private static Icon? LoadWindowIcon()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "app.ico");
        try { return File.Exists(path) ? new Icon(path) : null; }
        catch { return null; }
    }
}
