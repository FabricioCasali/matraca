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
        Assert.Contains("platform = \"macos\"", macos);
        Assert.Contains("filePick = false", macos);
        Assert.Contains("soundPreview = false", macos);
        Assert.Contains("historyClear = true", macos);
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

    private static string ReadProjectFile(params string[] parts)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Matraca.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine([directory!.FullName, .. parts]));
    }
}
