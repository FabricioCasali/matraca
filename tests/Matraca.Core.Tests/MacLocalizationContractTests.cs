using Xunit;

namespace Matraca.Core.Tests;

public sealed class MacLocalizationContractTests
{
    [Fact]
    public void MacLoadsConfigBeforeConstructingTheStatusItem()
    {
        string program = ReadProjectFile("Matraca.Mac", "Program.cs");
        Assert.True(program.IndexOf("MacConfig.Load()", StringComparison.Ordinal)
            < program.IndexOf("new MacStatusItem", StringComparison.Ordinal));
    }

    [Fact]
    public void MacBridgePublishesTheLanguageContractOnBootstrapAndChanges()
    {
        string bridge = ReadProjectFile("Matraca.Mac", "Platform", "Web", "MacWebBridge.cs");
        Assert.Contains("BuildSnapshot", bridge);
        Assert.Contains("BuildConfig", bridge);
        Assert.Contains("\"config.set\"", bridge);
        Assert.Contains("\"config.changed\"", bridge);
        Assert.Contains("ConfigSnapshot.Create(raw)", bridge);
        Assert.Contains("EffectiveUiLanguage", bridge);
    }

    [Fact]
    public void MacUsesExplicitWritingStateAndDoesNotInferItFromPortugueseText()
    {
        string hud = ReadProjectFile("Matraca.Mac", "Platform", "Web", "MacHudWindowController.cs");
        string bridge = ReadProjectFile("Matraca.Mac", "Platform", "Web", "MacWebBridge.cs");
        Assert.Contains("ShellState.Writing", hud);
        Assert.Contains("state is ShellState.Busy or ShellState.Writing", hud);
        Assert.Contains("effectiveUiLanguage = _app.CurrentConfig.EffectiveUiLanguage", hud);
        Assert.DoesNotContain("Contains(\"escrevendo\"", hud);
        Assert.Contains("ShellState.Writing => \"writing\"", bridge);
    }

    [Fact]
    public void MacNativeSurfacesUseTheLiveLanguageAndSafeErrors()
    {
        string status = ReadProjectFile("Matraca.Mac", "Platform", "MacStatusItem.cs");
        string navigation = ReadProjectFile("Matraca.Mac", "Platform", "Web", "MacWebNavigationDelegate.cs");
        string host = ReadProjectFile("Matraca.Mac", "Platform", "Web", "MacWebViewHost.cs");
        string bridge = ReadProjectFile("Matraca.Mac", "Platform", "Web", "MacWebBridge.cs");
        Assert.Contains("SetLanguage", status);
        Assert.Contains("ApplyNativeLocalization(config)", ReadProjectFile("Matraca.Mac", "MacTrayApp.cs"));
        Assert.Contains("SetAccessibilityLabel", status);
        Assert.Contains("LocalizedText", navigation);
        Assert.Contains("ConfirmContinue", navigation);
        Assert.Contains("effectiveLanguage", host);
        Assert.Contains("message = new MacUiText(EffectiveUiLanguage).ErrorFor(code)", bridge);
        Assert.DoesNotContain("error = new { code, message }", bridge);
    }

    [Fact]
    public void MacBundleShipsLocalizedMicrophoneUsageDescriptions()
    {
        string plist = ReadProjectFile("Matraca.Mac", "Info.plist");
        string pack = ReadProjectFile("Matraca.Mac", "pack.sh");
        string portuguese = ReadProjectFile("Matraca.Mac", "Resources", "pt-BR.lproj", "InfoPlist.strings");
        string english = ReadProjectFile("Matraca.Mac", "Resources", "en.lproj", "InfoPlist.strings");
        Assert.Contains("CFBundleLocalizations", plist);
        Assert.Contains("NSMicrophoneUsageDescription", portuguese);
        Assert.Contains("NSMicrophoneUsageDescription", english);
        Assert.Contains("Resources/pt-BR.lproj", pack);
        Assert.Contains("Resources/en.lproj", pack);
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
