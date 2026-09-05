using System.Collections.Generic;
using System.Drawing;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace Matraca;

internal sealed class WindowsWebViewHost : IDisposable
{
    private const string VirtualHost = "matraca.local";

    private readonly nint _parentWindow;
    private readonly string _assetRoot;
    private readonly string _userDataFolder;
    private readonly Action<Action> _dispatch;
    private readonly Queue<string> _pendingMessages = new();
    private CoreWebView2Environment? _environment;
    private CoreWebView2Controller? _controller;
    private CoreWebView2? _core;
    private Task? _initialization;
    private string _route = "home";
    private bool _documentReady;
    private bool _bridgeReady;
    private bool _disposed;

    public WindowsWebViewHost(
        nint parentWindow,
        string assetRoot,
        string userDataFolder,
        Action<Action> dispatch)
    {
        if (parentWindow == nint.Zero) throw new ArgumentException("O HWND pai e obrigatorio.", nameof(parentWindow));
        ArgumentException.ThrowIfNullOrWhiteSpace(assetRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(userDataFolder);
        ArgumentNullException.ThrowIfNull(dispatch);

        _parentWindow = parentWindow;
        _assetRoot = Path.GetFullPath(assetRoot);
        _userDataFolder = Path.GetFullPath(userDataFolder);
        _dispatch = dispatch;
    }

    public event Action<string>? MessageReceived;

    public Task InitializeAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialization != null) return _initialization;
        if (!File.Exists(Path.Combine(_assetRoot, "index.html")))
            throw new DirectoryNotFoundException($"Assets web nao encontrados em {_assetRoot}.");

        Directory.CreateDirectory(_userDataFolder);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _initialization = completion.Task;

        Task<CoreWebView2Environment> environmentTask;
        try
        {
            environmentTask = CoreWebView2Environment.CreateAsync(userDataFolder: _userDataFolder);
        }
        catch (Exception exception)
        {
            completion.SetException(exception);
            return _initialization;
        }

        ContinueOnOwner(environmentTask, completed => EnvironmentCreated(completed, completion), completion);
        return _initialization;
    }

    public void Navigate(string route)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _route = route;
        if (_core == null || !_documentReady || !_bridgeReady) return;
        ExecuteScript($"location.hash = {JsonSerializer.Serialize(route)};");
    }

    public void PostJson(string json)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using (JsonDocument.Parse(json)) { }

        if (_core == null || !_documentReady || !_bridgeReady)
        {
            _pendingMessages.Enqueue(json);
            return;
        }

        PostJsonCore(json);
    }

    public void Resize(int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_controller == null || width <= 0 || height <= 0) return;
        _controller.Bounds = new Rectangle(0, 0, width, height);
    }

    public void SetVisible(bool visible)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_controller != null) _controller.IsVisible = visible;
    }

    public void NotifyParentWindowPositionChanged()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _controller?.NotifyParentWindowPositionChanged();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        MessageReceived = null;
        _pendingMessages.Clear();

        if (_core != null)
        {
            _core.NavigationStarting -= OnNavigationStarting;
            _core.NavigationCompleted -= OnNavigationCompleted;
            _core.NewWindowRequested -= OnNewWindowRequested;
            _core.WebResourceRequested -= OnWebResourceRequested;
            _core.WebMessageReceived -= OnWebMessageReceived;
        }

        _controller?.Close();
        _controller = null;
        _core = null;
        _environment = null;
    }

    private void EnvironmentCreated(
        Task<CoreWebView2Environment> completed,
        TaskCompletionSource completion)
    {
        if (!TryTakeResult(completed, completion, out CoreWebView2Environment environment)) return;
        if (_disposed)
        {
            completion.TrySetCanceled();
            return;
        }

        _environment = environment;
        Task<CoreWebView2Controller> controllerTask;
        try
        {
            controllerTask = environment.CreateCoreWebView2ControllerAsync(_parentWindow);
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
            return;
        }

        ContinueOnOwner(controllerTask, created => ControllerCreated(created, completion), completion);
    }

    private void ControllerCreated(
        Task<CoreWebView2Controller> completed,
        TaskCompletionSource completion)
    {
        if (!TryTakeResult(completed, completion, out CoreWebView2Controller controller)) return;
        if (_disposed)
        {
            controller.Close();
            completion.TrySetCanceled();
            return;
        }

        _controller = controller;
        _controller.AllowExternalDrop = false;
        _core = controller.CoreWebView2;
        _core.Settings.AreDefaultContextMenusEnabled = false;
        _core.Settings.AreDevToolsEnabled = false;
        _core.Settings.IsStatusBarEnabled = false;
        _core.Settings.IsZoomControlEnabled = false;
        _core.Settings.AreHostObjectsAllowed = false;
        _core.SetVirtualHostNameToFolderMapping(
            VirtualHost,
            _assetRoot,
            CoreWebView2HostResourceAccessKind.DenyCors);
        _core.NavigationStarting += OnNavigationStarting;
        _core.NavigationCompleted += OnNavigationCompleted;
        _core.NewWindowRequested += OnNewWindowRequested;
        _core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        _core.WebResourceRequested += OnWebResourceRequested;
        _core.WebMessageReceived += OnWebMessageReceived;
        _core.Navigate(BuildUri(_route));
        completion.TrySetResult();
    }

    private void ContinueOnOwner<T>(
        Task<T> task,
        Action<Task<T>> continuation,
        TaskCompletionSource completion)
    {
        _ = task.ContinueWith(
            completed =>
            {
                try { _dispatch(() => continuation(completed)); }
                catch (Exception exception) { completion.TrySetException(exception); }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static bool TryTakeResult<T>(
        Task<T> completed,
        TaskCompletionSource completion,
        out T result)
    {
        if (completed.IsCanceled)
        {
            completion.TrySetCanceled();
            result = default!;
            return false;
        }
        if (completed.Exception != null)
        {
            completion.TrySetException(completed.Exception.InnerExceptions);
            result = default!;
            return false;
        }

        result = completed.Result;
        return true;
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs eventArgs)
    {
        if (!IsLocalUri(eventArgs.Uri))
        {
            eventArgs.Cancel = true;
            return;
        }

        _documentReady = false;
        _bridgeReady = false;
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs eventArgs)
    {
        _documentReady = eventArgs.IsSuccess && IsLocalUri(_core?.Source);
        if (!_documentReady) return;
        ExecuteScript($"location.hash = {JsonSerializer.Serialize(_route)};");
        FlushPendingMessages();
    }

    private static void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs eventArgs)
        => eventArgs.Handled = true;

    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs eventArgs)
    {
        if (IsLocalUri(eventArgs.Request.Uri)) return;
        eventArgs.Response = _environment!.CreateWebResourceResponse(
            Stream.Null,
            403,
            "Forbidden",
            "Content-Type: text/plain");
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs eventArgs)
    {
        if (!IsLocalUri(eventArgs.Source)) return;
        string json;
        try { json = eventArgs.TryGetWebMessageAsString(); }
        catch { return; }

        if (IsBridgeReady(json))
        {
            _bridgeReady = true;
            FlushPendingMessages();
        }
        MessageReceived?.Invoke(json);
    }

    private void FlushPendingMessages()
    {
        if (_core == null || !_documentReady || !_bridgeReady) return;
        while (_pendingMessages.TryDequeue(out string? json)) PostJsonCore(json);
    }

    private void PostJsonCore(string json)
        => ExecuteScript($"globalThis.matraca?.onMessage({JsonSerializer.Serialize(json)});");

    private void ExecuteScript(string script)
    {
        Task<string> task = _core!.ExecuteScriptAsync(script);
        _ = task.ContinueWith(
            failed => Logger.Error(
                "Falha ao executar script na interface WebView2",
                failed.Exception!.GetBaseException()),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    private static bool IsBridgeReady(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            return root.TryGetProperty("version", out JsonElement version)
                && version.ValueKind == JsonValueKind.Number
                && version.GetInt32() == 1
                && root.TryGetProperty("type", out JsonElement type)
                && type.ValueKind == JsonValueKind.String
                && type.GetString() == "bridge.ready";
        }
        catch (JsonException) { return false; }
    }

    private static string BuildUri(string route) => $"https://{VirtualHost}/index.html#{route}";

    private static bool IsLocalUri(string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            && uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && uri.Host.Equals(VirtualHost, StringComparison.OrdinalIgnoreCase)
            && uri.Port == 443
            && string.IsNullOrEmpty(uri.UserInfo);
}
