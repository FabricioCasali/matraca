using Xunit;

namespace Matraca.Core.Tests;

public sealed class InstallerContractTests
{
    [Fact]
    public void ManifestVariantIsScopedToTheWindowsProject()
    {
        string project = ReadProjectFile("Matraca.Windows", "Matraca.Windows.csproj");
        string installer = ReadProjectFile("installer", "build-installer.ps1");

        Assert.Contains("<MatracaApplicationManifest", project);
        Assert.Contains("<ApplicationManifest>$(MatracaApplicationManifest)</ApplicationManifest>", project);
        Assert.Contains("-p:MatracaApplicationManifest=", installer);
        Assert.DoesNotContain("-p:ApplicationManifest=", installer);
    }

    [Fact]
    public void InstallerDefaultsToProjectVersionAndKeepsExplicitVersionOverride()
    {
        string installer = ReadProjectFile("installer", "build-installer.ps1");
        Assert.Contains("[string]$Version", installer);
        Assert.Contains("if ([string]::IsNullOrWhiteSpace($Version))", installer);
        Assert.Contains("dotnet msbuild $csproj -getProperty:Version -nologo", installer);
        Assert.Contains("-p:Version=$Version", installer);
        Assert.Contains("/DMyAppVersion=$Version", installer);
        Assert.DoesNotContain("$Version = '1.0.0'", installer);
    }

    [Fact]
    public void ReleaseStopsAfterFailedTestsBeforeBuilding()
    {
        string workflow = ReadProjectFile(".github", "workflows", "release.yml").Replace("\r\n", "\n");
        Assert.Contains("dotnet test Matraca.sln -c Release\n          if ($LASTEXITCODE) { exit $LASTEXITCODE }", workflow);
        Assert.Contains("dotnet build Matraca.sln -c Release\n          if ($LASTEXITCODE) { exit $LASTEXITCODE }", workflow);
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
