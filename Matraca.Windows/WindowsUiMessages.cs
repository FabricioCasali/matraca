using System.Text.Json;

namespace Matraca;

internal sealed class WindowsUiMessages
{
    private readonly bool _english;

    public WindowsUiMessages(string effectiveUiLanguage)
    {
        _english = string.Equals(
            effectiveUiLanguage,
            UiLanguageResolver.EnglishUnitedStates,
            StringComparison.OrdinalIgnoreCase);
    }

    public static WindowsUiMessages Current()
    {
        try
        {
            RawConfig raw = WindowsConfig.LoadRaw();
            return new(UiLanguageResolver.ResolveEffective(raw.uiLanguage));
        }
        catch
        {
            return new(UiLanguageResolver.ResolveEffective(null));
        }
    }

    public string TrayStarting => _english ? "Matraca - starting..." : "Matraca - iniciando...";
    public string SettingsMenu => _english ? "Settings..." : "Configurações...";
    public string HistoryMenu => _english ? "Dictation history..." : "Histórico de ditados...";
    public string LogMenu => _english ? "Open matraca.log" : "Abrir matraca.log";
    public string ConfigFolderMenu => _english ? "Open config folder" : "Abrir pasta de configuração";
    public string ExitMenu => _english ? "Exit" : "Sair";

    public string DiscoveryTitle => _english ? "Discovery mode" : "Modo descoberta";
    public string DiscoveryMessage => _english
        ? "Press your custom key. The code appears here and in matraca.log. Then put it in appsettings.json (the \"hotkey\" field)."
        : "Pressione sua tecla personalizada. O código aparece aqui e no matraca.log. Depois coloque-o em appsettings.json (campo \"hotkey\").";
    public string WaitTitle => _english ? "Please wait" : "Aguarde";
    public string WaitMessage => _english
        ? "Finish dictation before opening the panel."
        : "Encerre o ditado antes de abrir o painel.";
    public string MenuWaitMessage => _english
        ? "Finish dictation before opening the menu."
        : "Encerre o ditado antes de abrir o menu.";
    public string MenuUnavailableTitle => _english ? "Menu unavailable" : "Menu indisponível";
    public string InterfaceErrorTitle => _english ? "Interface operation failed" : "Falha na operação da interface";
    public string MenuUnavailableMessage => _english
        ? "The dictation could not be paused safely."
        : "Não foi possível pausar o ditado com segurança.";

    public string RestartPrompt => _english
        ? "Switching between GPU and CPU only takes effect after Matraca restarts.\n\nEverything else is already applied. Restart now?"
        : "A troca entre GPU e CPU só entra em vigor após reiniciar o Matraca.\n\nTodo o restante já foi aplicado. Reiniciar agora?";

    public string ConfigRejectedTitle => _english ? "Configuration rejected" : "Configuração rejeitada";
    public string ConfigRejectedMessage => _english
        ? "The configuration could not be applied. See matraca.log."
        : "A configuração não pôde ser aplicada. Consulte matraca.log.";

    public string FileModelTitle => _english ? "Select Whisper model" : "Selecionar modelo Whisper";
    public string FileModelFilter => _english ? "Whisper models (*.bin)\0*.bin\0\0" : "Modelos Whisper (*.bin)\0*.bin\0\0";
    public string FileSoundTitle => _english ? "Select sound" : "Selecionar som";
    public string FileSoundFilter => _english
        ? "Audio (*.wav;*.mp3)\0*.wav;*.mp3\0All files (*.*)\0*.*\0\0"
        : "Áudio (*.wav;*.mp3)\0*.wav;*.mp3\0Todos os arquivos (*.*)\0*.*\0\0";

    public string FatalError => _english
        ? "Matraca encountered a fatal error. See matraca.log."
        : "O Matraca encontrou um erro fatal. Veja matraca.log.";

    public string HudReadyTitle => "Matraca";
    public string HudListeningTitle => _english ? "Listening" : "Ouvindo você";
    public string HudThinkingTitle => _english ? "Processing" : "Processando";
    public string HudWritingTitle => _english ? "Writing" : "Inserindo";
    public string HudAttentionTitle => _english ? "Attention" : "Atenção";
    public string HudWriting => _english ? "Writing text" : "Inserindo texto";
    public string HudWritingStreaming => _english ? "Listening · writing a segment" : "Ouvindo · inserindo trecho";
    public string HudDone => _english ? "Text delivered" : "Texto entregue";
    public string HudDoneStreaming => _english ? "Segment delivered · still listening" : "Trecho entregue · ainda ouvindo";
    public string HudDeliveryError => _english
        ? "Delivery failed · check the history"
        : "Entrega falhou · confira o histórico";

    public string UnknownError => _english
        ? "Matraca could not complete this operation. See matraca.log."
        : "O Matraca não conseguiu concluir esta operação. Consulte matraca.log.";

    public string BridgeError(string method, Exception exception)
    {
        if (exception is OperationCanceledException)
            return _english ? "Operation canceled." : "Operação cancelada.";
        if (exception is JsonException)
            return _english ? "The interface request is invalid." : "A solicitação da interface é inválida.";
        if (exception is UnauthorizedAccessException)
            return _english ? "Windows denied this operation." : "O Windows negou esta operação.";
        if (exception is NotSupportedException)
            return _english ? "This interface operation is not available." : "Esta operação da interface não está disponível.";
        if (exception is ArgumentException)
            return _english ? "The provided value is invalid." : "O valor informado é inválido.";

        return method switch
        {
            "config.set" => ConfigRejectedMessage,
            "file.pick" => _english ? "The file picker could not be opened." : "Não foi possível abrir o seletor de arquivo.",
            "sound.preview" => _english ? "The sound could not be previewed." : "Não foi possível ouvir o som.",
            "history.copy" => _english ? "The text could not be copied." : "Não foi possível copiar o texto.",
            "history.repaste" => _english ? "The text could not be reinserted." : "Não foi possível recolar o texto.",
            "mic.monitor.start" or "mic.monitor.stop" => _english
                ? "The microphone monitor is unavailable."
                : "O monitor de microfone está indisponível.",
            "model.download.start" or "model.download.cancel" => _english
                ? "The model download could not be completed."
                : "Não foi possível concluir o download do modelo.",
            "permissions.open-settings" => _english
                ? "Windows settings could not be opened."
                : "Não foi possível abrir os ajustes do Windows.",
            _ => UnknownError,
        };
    }

    public string ConfigError(Exception exception)
        => exception is UnauthorizedAccessException
            ? (_english ? "Windows denied the configuration file." : "O Windows negou acesso ao arquivo de configuração.")
            : ConfigRejectedMessage;
}
