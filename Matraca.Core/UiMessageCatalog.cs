namespace Matraca.Core;

public sealed class UiMessageCatalog
{
    private readonly bool _english;

    public UiMessageCatalog(string effectiveUiLanguage)
    {
        _english = string.Equals(
            effectiveUiLanguage,
            UiLanguageResolver.EnglishUnitedStates,
            StringComparison.OrdinalIgnoreCase);
    }

    public string DiscoveryMode => _english
        ? "Matraca — DISCOVERY MODE"
        : "Matraca — MODO DESCOBERTA";

    public string Suspended => _english
        ? "Matraca - suspended"
        : "Matraca - suspenso";

    public string LoadingModel => _english
        ? "Matraca — loading model..."
        : "Matraca — carregando modelo...";

    public string ModelLoadErrorState => _english
        ? "Matraca — ERROR loading model"
        : "Matraca — ERRO ao carregar modelo";

    public string Transcribing => _english
        ? "Matraca — transcribing..."
        : "Matraca — transcrevendo...";

    public string Writing => _english
        ? "Matraca - writing..."
        : "Matraca - escrevendo...";

    public string Recording(bool hotkeyNeedsKeyUp)
        => _english
            ? $"Matraca — RECORDING ({(hotkeyNeedsKeyUp ? "release to stop" : "press again to stop")})"
            : $"Matraca — GRAVANDO ({(hotkeyNeedsKeyUp ? "solte para parar" : "aperte de novo p/ parar")})";

    public string Ready(string hotkey)
        => _english
            ? $"Matraca — ready ({hotkey})"
            : $"Matraca — pronto ({hotkey})";

    public string PinnedState(string title)
        => _english
            ? $"Matraca — pinned to: {title}"
            : $"Matraca — fixado em: {title}";

    public string Unloaded(string hotkey)
        => _english
            ? $"Matraca — idle, VRAM released ({hotkey})"
            : $"Matraca — ocioso, VRAM liberada ({hotkey})";

    public string KeyDiscoveredTitle => _english ? "Key detected" : "Tecla detectada";

    public string KeyDiscoveredMessage(string name)
        => _english
            ? $"Shortcut: {name}\nPut \"hotkey\": \"{name}\" in appsettings.json."
            : $"Atalho: {name}\nColoque \"hotkey\": \"{name}\" em appsettings.json.";

    public string ErrorTitle => _english ? "Error" : "Erro";

    public string MicrophoneError => _english
        ? "Could not access the microphone. See matraca.log."
        : "Nao consegui acessar o microfone. Veja matraca.log.";

    public string TranscriptionError => _english
        ? "Transcription failed. See matraca.log."
        : "Falha ao transcrever. Veja matraca.log.";

    public string ModelNotLoadedError => _english
        ? "Model is not loaded. See matraca.log."
        : "Modelo nao carregado. Veja matraca.log.";

    public string EmptyAudioTitle => _english ? "Nothing to deliver" : "Vazio";

    public string EmptyAudioMessage => _english
        ? "I could not understand any audio."
        : "Nao entendi nenhum audio.";

    public string PinnedTargetUnavailableTitle => _english
        ? "Pinned window disappeared"
        : "Janela fixada sumiu";

    public string PinnedTargetUnavailableMessage => _english
        ? "It was closed; dictation returns to the focused window."
        : "Ela foi fechada; o ditado volta pra janela em foco.";

    public string PinnedTargetTitle => _english ? "Destination pinned" : "Destino fixado";

    public string UnpinnedTitle => _english ? "Destination released" : "Destino liberado";

    public string UnpinnedMessage => _english
        ? "Dictation returns to the focused window."
        : "O ditado volta pra janela em foco.";

    public string NoTargetTitle => _english ? "Nothing to pin" : "Nada pra fixar";

    public string NoTargetMessage => _english
        ? "Could not identify the focused window."
        : "Nao consegui identificar a janela em foco.";

    public string PinnedTarget(string title, string pinHotkey)
        => _english
            ? $"Dictation will always go to: {title}\nPress {pinHotkey} again to release."
            : $"O ditado vai sempre para: {title}\nAperte {pinHotkey} de novo para liberar.";

    public string ModelReloadFallback => _english
        ? "The new model could not be loaded; the previous configuration was kept."
        : "O novo modelo nao carregou; mantive a configuracao anterior.";

    public string ReadyTitle => _english ? "Ready" : "Pronto";

    public string ReadyNotification(string hotkey, string mode, string gpu)
        => _english
            ? $"Shortcut: {hotkey} · mode: {mode} · {gpu}."
            : $"Atalho: {hotkey} · modo: {mode} · {gpu}.";

    public string ModelLoadErrorNotification => _english
        ? "Could not load the Whisper model. See matraca.log."
        : "Nao consegui carregar o modelo Whisper. Veja matraca.log.";

    public string WindowWithoutTitle => _english ? "(untitled window)" : "(janela sem titulo)";
}
