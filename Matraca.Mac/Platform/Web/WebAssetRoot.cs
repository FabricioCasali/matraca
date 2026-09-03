namespace Matraca.Mac.Platform.Web;

internal static class WebAssetRoot
{
    public static string Resolve()
    {
        string[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, "Web"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Resources", "Web")),
        ];

        return candidates.FirstOrDefault(Directory.Exists)
            ?? throw new DirectoryNotFoundException(
                $"Matraca web assets were not found. Checked: {string.Join(", ", candidates)}");
    }
}
