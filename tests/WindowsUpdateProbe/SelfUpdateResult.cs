namespace WindowsUpdateProbe;

internal sealed class SelfUpdateResult
{
    public bool Verified { get; set; }
    public int OriginalPid { get; set; }
    public int? OriginalExitCode { get; set; }
    public int? HelperPid { get; set; }
    public int? ReopenedPid { get; set; }
    public DateTimeOffset? OriginalExitedAtUtc { get; set; }
    public DateTimeOffset? InstallerStartedAtUtc { get; set; }
    public DateTimeOffset? ReopenedAtUtc { get; set; }
    public bool InstallerStartedAfterOriginalExit { get; set; }
    public bool InstallSignatureRecheckVerified { get; set; }
    public bool CloseVetoVerified { get; set; }
    public bool CloseCallbackObserved { get; set; }
    public bool WaitGateVerified { get; set; }
    public bool DataPreserved { get; set; }
    public string? ReopenedVersion { get; set; }
    public string? Failure { get; set; }
}
