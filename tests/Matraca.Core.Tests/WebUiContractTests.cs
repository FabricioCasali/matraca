using Xunit;

namespace Matraca.Core.Tests;

public sealed class WebUiContractTests
{
    [Fact]
    public void NativeSnapshotsDeclarePlatformCapabilities()
    {
        string windows = ReadProjectFile("Matraca.Windows", "WindowsWebBridge.cs");
        string macos = ReadProjectFile("Matraca.Mac", "Platform", "Web", "MacWebBridge.cs");

        Assert.Contains("platform = \"windows\"", windows);
        Assert.Contains("filePick = true", windows);
        Assert.Contains("soundPreview = true", windows);
        Assert.Contains("historyClear = true", windows);
        Assert.Contains("reviewUsage = entry.ReviewUsage", windows);
        Assert.Contains("platform = \"macos\"", macos);
        Assert.Contains("filePick = false", macos);
        Assert.Contains("soundPreview = false", macos);
        Assert.Contains("historyClear = true", macos);
        Assert.Contains("reviewUsage = entry.ReviewUsage", macos);
    }

    [Fact]
    public void SharedUiWiresParityMethodsBehindCapabilities()
    {
        string html = ReadProjectFile("Matraca.Web", "wwwroot", "index.html");
        string script = ReadProjectFile("Matraca.Web", "wwwroot", "app.js");

        Assert.Contains("data-capability=\"filePick\"", html);
        Assert.Contains("data-capability=\"soundPreview\"", html);
        Assert.Contains("data-capability=\"historyClear\"", html);
        Assert.Contains("data-hotkey-capture=\"hotkey\" data-hotkey-help-target=\"[data-onboarding-hotkey-help]\"", html);
        Assert.Contains("request(\"file.pick\"", script);
        Assert.Contains("request(\"sound.preview\"", script);
        Assert.Contains("request(\"history.clear\")", script);
        Assert.Contains("data-history-total-tokens", html);
        Assert.Contains("confirm(\"Limpar todo o histórico local?", script);
    }

    [Fact]
    public void VadUsesTheCoreRangeAndOneVisualTransform()
    {
        string html = ReadProjectFile("Matraca.Web", "wwwroot", "index.html");
        string script = ReadProjectFile("Matraca.Web", "wwwroot", "app.js");

        Assert.Contains("data-threshold-input type=\"range\" min=\"0.001\" max=\"0.5\"", html);
        Assert.Contains("const maximum = .5;", script);
        Assert.Contains("const speech = Number(frame.rms) > Number(frame.threshold);", script);
        Assert.Contains("levelPosition(frame.rms)", script);
        Assert.Contains("levelPosition(frame.threshold)", script);
    }

    [Fact]
    public void SharedUiWiresTheCustomWindowsTitleBar()
    {
        string html = ReadProjectFile("Matraca.Web", "wwwroot", "index.html");
        string script = ReadProjectFile("Matraca.Web", "wwwroot", "app.js");

        Assert.Contains("data-window-minimize", html);
        Assert.Contains("data-window-maximize", html);
        Assert.Contains("data-window-close", html);
        Assert.Contains("request(\"window.minimize\")", script);
        Assert.Contains("request(\"window.toggleMaximize\")", script);
        Assert.Contains("request(\"window.close\")", script);
        Assert.Contains("request(\"window.drag\")", script);
    }

    [Fact]
    public void SharedUiUsesReadableTypeTokensAndSolidSelectColors()
    {
        string css = ReadProjectFile("Matraca.Web", "wwwroot", "styles.css");
        string hud = ReadProjectFile("Matraca.Web", "wwwroot", "hud.css");

        Assert.Contains("--font-caption: 11px", css);
        Assert.Contains("--font-body: 13px", css);
        Assert.Contains(".select option", css);
        Assert.Contains("background: var(--control-bg)", css);
        Assert.Contains(".state-copy span { color:#b4b4c7;font-size:12px }", hud);
    }

    [Fact]
    public void HomeWiresModeSelectionAndToggleDictation()
    {
        string html = ReadProjectFile("Matraca.Web", "wwwroot", "index.html");
        string script = ReadProjectFile("Matraca.Web", "wwwroot", "app.js");
        string windows = ReadProjectFile("Matraca.Windows", "WindowsWebBridge.cs");
        string macos = ReadProjectFile("Matraca.Mac", "Platform", "Web", "MacWebBridge.cs");

        Assert.Contains("data-dictation-toggle", html);
        Assert.Contains("<button data-mode=\"hold\"", html);
        Assert.Contains("request(\"dictation.toggle\")", script);
        Assert.Contains("[data-home-mode] [data-mode]", script);
        Assert.Contains("\"dictation.toggle\"", windows);
        Assert.Contains("\"dictation.toggle\"", macos);
    }

    [Fact]
    public void HomeUpdatesAndOperatesOnTheLastDeliveredPhrase()
    {
        string html = ReadProjectFile("Matraca.Web", "wwwroot", "index.html");
        string script = ReadProjectFile("Matraca.Web", "wwwroot", "app.js");
        string windows = ReadProjectFile("Matraca.Windows", "WindowsWebBridge.cs");
        string macos = ReadProjectFile("Matraca.Mac", "Platform", "Web", "MacWebBridge.cs");

        Assert.Contains("data-last-phrase-copy", html);
        Assert.Contains("data-last-phrase-repaste", html);
        Assert.Contains("data-last-phrase-delete", html);
        Assert.Contains("message.type === \"dictation.completed\"", script);
        Assert.Contains("Emit(\"dictation.completed\"", windows);
        Assert.Contains("Emit(\"dictation.completed\"", macos);
    }

    [Fact]
    public void ReviewSettingsOfferDeepSeekModelReasoningAndDedicatedCredential()
    {
        string html = ReadProjectFile("Matraca.Web", "wwwroot", "index.html");
        string script = ReadProjectFile("Matraca.Web", "wwwroot", "app.js");
        string processor = ReadProjectFile("Matraca.Core", "TextPostProcessor.cs");

        Assert.Contains("<option value=\"deepseek\">DeepSeek</option>", html);
        Assert.Contains("deepseek-v4-flash", html);
        Assert.Contains("deepseek-v4-pro", html);
        Assert.Contains("data-config-field=\"postProcessReasoning\"", html);
        Assert.Contains("postProcessDeepSeekApiKey", script);
        Assert.Contains("DEEPSEEK_API_KEY", processor);
        Assert.Contains("OpenAiCompatibleTextReviewer.DeepSeekEndpoint", processor);
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
