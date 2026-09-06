namespace Matraca;

internal sealed class WindowsTextSink : ITextSink
{
    private readonly WindowsTargetWindow _targets;

    public WindowsTextSink(WindowsTargetWindow targets)
    {
        _targets = targets;
    }

    public Task<TextDeliveryResult> DeliverAsync(
        TextDeliveryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (cancellationToken.IsCancellationRequested)
            return Task.FromResult(TextDeliveryResult.Cancelled);

        try { return Task.FromResult(Deliver(request)); }
        catch (Exception exception)
        {
            Logger.Error("Falha ao entregar o texto", exception);
            return Task.FromResult(TextDeliveryResult.Failed);
        }
    }

    private TextDeliveryResult Deliver(TextDeliveryRequest request)
    {
        if (!Enum.IsDefined(request.Method)) return TextDeliveryResult.InvalidRequest;
        if (request.Method is TextDeliveryMethod.TargetWithFocus or TextDeliveryMethod.TargetWithoutFocus)
        {
            if (request.Target == null) return TextDeliveryResult.InvalidRequest;
            if (!_targets.TryResolveDescriptor(request.Target, out WindowsTargetDescriptor descriptor)
                || !TextInjector.IsWindowAlive(descriptor.Window))
                return TextDeliveryResult.TargetUnavailable;

            if (request.Method == TextDeliveryMethod.TargetWithoutFocus)
                return TextInjector.SendToWindow(descriptor.Window, request.Text, request.PressEnter);

            return TextInjector.DeliverWithFocus(
                    descriptor.Window,
                    descriptor.FocusedControl,
                    request.Text,
                    request.PressEnter)
                ? TextDeliveryResult.Delivered
                : TextDeliveryResult.Failed;
        }

        if (request.Target != null) return TextDeliveryResult.InvalidRequest;
        bool success = TextInjector.PasteText(
            request.Text,
            request.PressEnter,
            request.Method == TextDeliveryMethod.Clipboard ? "clipboard" : "unicode");
        return success ? TextDeliveryResult.Delivered : TextDeliveryResult.Failed;
    }

    public void Dispose() { }
}
