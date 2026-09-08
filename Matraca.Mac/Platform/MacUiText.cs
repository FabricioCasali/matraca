using Matraca.Core;

namespace Matraca.Mac.Platform;

internal sealed class MacUiText
{
    private readonly bool _english;

    public MacUiText(string? effectiveUiLanguage)
    {
        _english = string.Equals(
            effectiveUiLanguage,
            UiLanguageResolver.EnglishUnitedStates,
            StringComparison.OrdinalIgnoreCase);
    }

    public string MenuStarting => _english ? "Starting..." : "Iniciando...";
    public string MenuOpen => _english ? "Open Matraca..." : "Abrir Matraca...";
    public string MenuQuit => _english ? "Quit Matraca" : "Sair do Matraca";
    public string AccessibilityLabel => _english ? "Matraca menu bar item" : "Item do Matraca na barra de menus";
    public string AccessibilityRequiredMessage => _english
        ? "Grant Accessibility permission and restart Matraca."
        : "Conceda permissao de Acessibilidade e reinicie o Matraca.";
    public string KeyboardUnavailableAfterSleep => _english
        ? "Matraca - keyboard unavailable after sleep."
        : "Matraca - teclado indisponivel apos repouso.";
    public string KeyboardUnavailable => _english
        ? "Matraca - keyboard unavailable; check Accessibility."
        : "Matraca - teclado indisponivel; confira Acessibilidade.";
    public string RestartTitle => _english ? "Restart Matraca" : "Reinicie o Matraca";
    public string RestartMessage => _english
        ? "The GPU/CPU change takes effect on the next startup."
        : "A troca entre GPU e CPU passa a valer na proxima inicializacao.";
    public string ConfigurationRejectedTitle => _english ? "Configuration rejected" : "Configuracao rejeitada";
    public string ConfigurationRejectedMessage => _english
        ? "The change was not applied. Check the configuration and try again."
        : "A alteracao nao foi aplicada. Confira a configuracao e tente novamente.";
    public string RuntimeUnavailable => _english
        ? "Dictation is not available on this Mac."
        : "O runtime de ditado nao esta disponivel neste Mac.";
    public string OperationFailed => _english
        ? "The operation could not be completed."
        : "A operacao nao pode ser concluida.";
    public string PermissionDenied => _english
        ? "The required macOS permission was denied."
        : "A permissao necessaria do macOS foi negada.";
    public string InvalidRequest => _english
        ? "The interface sent an invalid request."
        : "A interface enviou uma solicitacao invalida.";
    public string NotImplemented => _english
        ? "This operation is not available on macOS."
        : "Esta operacao nao esta disponivel no macOS.";
    public string Canceled => _english ? "The operation was canceled." : "A operacao foi cancelada.";
    public string MicrophoneUnavailable => _english
        ? "The microphone is unavailable. Check the connection and try again."
        : "O microfone esta indisponivel. Confira a conexao e tente novamente.";
    public string RepasteFailed => _english
        ? "The text could not be pasted again."
        : "Nao foi possivel recolar o texto.";
    public string ConfirmContinue => _english ? "Continue" : "Continuar";
    public string ConfirmCancel => _english ? "Cancel" : "Cancelar";
    public string ConfirmFallback => _english ? "Confirm action?" : "Confirmar acao?";

    public string HudListening => _english ? "Listening" : "Ouvindo voce";
    public string HudThinking => _english ? "Processing text" : "Processando texto";
    public string HudWriting => _english ? "Inserting text" : "Inserindo texto";
    public string HudAttention => _english ? "Attention" : "Atencao";
    public string HudReady => "Matraca";
    public string HudListeningInserting => _english
        ? "Listening · inserting segment"
        : "Ouvindo · inserindo trecho";
    public string HudInserting => _english ? "Inserting text" : "Inserindo texto";
    public string HudSegmentDelivered => _english
        ? "Segment delivered · still listening"
        : "Trecho entregue · ainda ouvindo";
    public string HudTextDelivered => _english ? "Text delivered" : "Texto entregue";
    public string HudDeliveryFailed => _english
        ? "Delivery failed · check history"
        : "Entrega falhou · confira o historico";

    public string ErrorFor(string code) => code switch
    {
        "not_implemented" => NotImplemented,
        "invalid_request" => InvalidRequest,
        "permission_denied" => PermissionDenied,
        "canceled" => Canceled,
        _ => OperationFailed,
    };

    public string StatusTitle(ShellState state) => state switch
    {
        ShellState.Recording => _english ? "Matraca REC" : "Matraca GRAV",
        ShellState.Busy or ShellState.Writing => "Matraca ...",
        ShellState.Error => "Matraca !",
        _ => "Matraca",
    };
}
