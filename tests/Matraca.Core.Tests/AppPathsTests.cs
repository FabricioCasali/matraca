using Xunit;

namespace Matraca.Core.Tests;

public sealed class AppPathsTests
{
    [Fact]
    public void WindowsPathsUseLocalApplicationData()
    {
        var paths = AppPaths.ForWindows(@"C:\Users\Ada\AppData\Local");

        Assert.Equal(@"C:\Users\Ada\AppData\Local\Matraca", paths.DataDirectory);
        Assert.Equal(@"C:\Users\Ada\AppData\Local\Matraca\appsettings.json", paths.ConfigFile);
        Assert.Equal(@"C:\Users\Ada\AppData\Local\Matraca\matraca.log", paths.LogFile);
        Assert.Equal(@"C:\Users\Ada\AppData\Local\Matraca\history.json", paths.HistoryFile);
        Assert.Equal(@"C:\Users\Ada\AppData\Local\Matraca\ai-usage.json", paths.AiUsageFile);
        Assert.Equal(@"C:\Users\Ada\AppData\Local\Matraca\models", paths.ModelsDirectory);
    }

    [Fact]
    public void CanonicalDataTokenAndSeparatorsResolveOnWindows()
    {
        var paths = AppPaths.ForWindows(@"C:\Users\tester\AppData\Local");

        Assert.Equal(
            @"C:\Users\tester\AppData\Local\Matraca\models\model.bin",
            paths.ResolvePath("%MATRACA_DATA%/models/model.bin"));
    }

    [Fact]
    public void CanonicalDataTokenAndSeparatorsResolveOnMac()
    {
        var paths = AppPaths.ForMac("/Users/tester");

        Assert.Equal(
            "/Users/tester/Library/Application Support/Matraca/models/model.bin",
            paths.ResolvePath(@"%MATRACA_DATA%\models\model.bin"));
    }

    [Fact]
    public void MacPathsUseApplicationSupport()
    {
        var paths = AppPaths.ForMac("/Users/ada");

        Assert.Equal("/Users/ada/Library/Application Support/Matraca", paths.DataDirectory);
        Assert.Equal("/Users/ada/Library/Application Support/Matraca/appsettings.json", paths.ConfigFile);
        Assert.Equal("/Users/ada/Library/Application Support/Matraca/matraca.log", paths.LogFile);
        Assert.Equal("/Users/ada/Library/Application Support/Matraca/history.json", paths.HistoryFile);
        Assert.Equal("/Users/ada/Library/Application Support/Matraca/ai-usage.json", paths.AiUsageFile);
        Assert.Equal("/Users/ada/Library/Application Support/Matraca/models", paths.ModelsDirectory);
    }

    [Fact]
    public void FactoriesTrimTrailingSeparators()
    {
        Assert.Equal(@"C:\Temp\Matraca", AppPaths.ForWindows(@"C:\Temp\").DataDirectory);
        Assert.Equal("/tmp/home/Library/Application Support/Matraca", AppPaths.ForMac("/tmp/home/").DataDirectory);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void WindowsRejectsMissingBasePath(string path)
        => Assert.Throws<ArgumentException>(() => AppPaths.ForWindows(path));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void MacRejectsMissingHome(string path)
        => Assert.Throws<ArgumentException>(() => AppPaths.ForMac(path));
}
