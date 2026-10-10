using Xunit;

namespace Matraca.Core.Tests;

public sealed class CopyLastTranscriptionContractTests
{
    [Fact]
    public void WindowsMenuHasTheLocalizedCopyAction()
    {
        string messages = ReadProjectFile("Matraca.Windows", "WindowsUiMessages.cs");
        Assert.Contains("Copiar última transcrição", messages);
        Assert.Contains("Copy last transcription", messages);
    }

    [Fact]
    public void WindowsCanEnableAndDisableTheNativeMenuCommand()
    {
        string tray = ReadProjectFile("Matraca.Windows", "WindowsTrayIcon.cs");
        string native = ReadProjectFile("Matraca.Windows", "WindowsNativeMethods.cs");
        Assert.Contains("EnableMenuItem", tray + native);
        Assert.Matches("(?i)Mf_?ByCommand", tray + native);
        Assert.Matches("(?i)Mf_?Grayed", tray + native);
        Assert.Matches("(?i)Mf_?Enabled", tray + native);
    }

    [Fact]
    public void MacMenuHasTheLocalizedCopyAction()
    {
        string messages = ReadProjectFile("Matraca.Mac", "Platform", "MacUiText.cs");
        Assert.Contains("Copiar última transcrição", messages);
        Assert.Contains("Copy last transcription", messages);
    }

    [Fact]
    public void MacMenuValidatesAvailabilityThroughTheActionTarget()
    {
        string target = ReadProjectFile("Matraca.Mac", "Platform", "MacMenuActionTarget.cs");
        string selectors = ReadProjectFile("Matraca.Mac", "Platform", "Interop", "ObjCSelectors.cs");
        Assert.Contains("Func<bool>", target);
        Assert.Contains("validateMenuItem:", target + selectors);
    }

    private static string ReadProjectFile(params string[] parts)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Matraca.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine([directory!.FullName, .. parts]));
    }
}
