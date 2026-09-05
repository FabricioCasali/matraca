namespace Matraca;

internal interface IWindowsWebBridgeApp
{
    event Action<ShellState, string>? StateChanged;
    event Action<Config>? ConfigChanged;

    Config CurrentConfig { get; }
    string RuntimeGpu { get; }
    ShellState CurrentState { get; }
    string CurrentStateText { get; }
    bool PostProcessingActive { get; }

    RawConfig LoadRawConfig();
    IReadOnlyList<string> ListAudioDevices();
    List<DictationHistoryEntry> HistorySnapshot();
    bool RemoveHistory(DateTime at, string text);
    Task<(RawConfig Config, bool RestartRequired)> ApplyAndSaveConfigPatchAsync(string patchJson);
    bool CopyText(string text);
    void CaptureWebTarget(nint excludedWindow);
    void ReleaseWebTarget();
    TargetToken? TakeWebTarget();
    Task<TextDeliveryResult> RepasteAsync(string text, TargetToken target);
    void ShowWebError(string message);
    Task BeginMicrophoneMonitorAsync();
    Task EndMicrophoneMonitorAsync();
}
