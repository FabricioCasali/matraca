using Matraca.Core;
using Xunit;

namespace Matraca.Core.Tests;

public sealed class UiMessageCatalogTests
{
    [Fact]
    public void PortugueseBrazilIsTheDefaultCatalog()
    {
        var catalog = new UiMessageCatalog(UiLanguageResolver.PortugueseBrazil);

        Assert.Equal("Matraca — GRAVANDO (solte para parar)", catalog.Recording(true));
        Assert.Equal("Erro", catalog.ErrorTitle);
        Assert.Equal("Destino fixado", catalog.PinnedTargetTitle);
    }

    [Fact]
    public void EnglishCatalogTranslatesGeneratedMessagesAndPreservesWindowTitle()
    {
        var catalog = new UiMessageCatalog(UiLanguageResolver.EnglishUnitedStates);

        Assert.Equal("Matraca — RECORDING (press again to stop)", catalog.Recording(false));
        Assert.Equal("Error", catalog.ErrorTitle);
        Assert.Equal(
            "Dictation will always go to: My private editor\nPress F16 again to release.",
            catalog.PinnedTarget("My private editor", "F16"));
    }
}
