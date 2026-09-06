using Xunit;

namespace Matraca.Core.Tests;

public sealed class InstallerContractTests
{
    [Fact]
    public void ManifestVariantIsScopedToTheWindowsProject()
    {
        string project = ReadProjectFile("Matraca.Windows", "Matraca.Windows.csproj");
        string installer = ReadProjectFile("installer", "build-installer.ps1");
        string uiAccess = ReadProjectFile("setup-uiaccess.ps1");

        Assert.Contains("<MatracaApplicationManifest", project);
        Assert.Contains("<ApplicationManifest>$(MatracaApplicationManifest)</ApplicationManifest>", project);
        Assert.Contains("-p:MatracaApplicationManifest=", installer);
        Assert.Contains("-p:MatracaApplicationManifest=", uiAccess);
        Assert.DoesNotContain("-p:ApplicationManifest=", installer);
        Assert.DoesNotContain("-p:ApplicationManifest=", uiAccess);
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
