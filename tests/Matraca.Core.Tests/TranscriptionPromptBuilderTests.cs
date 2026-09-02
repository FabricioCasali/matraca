using Xunit;

namespace Matraca.Core.Tests;

public sealed class TranscriptionPromptBuilderTests
{
    [Fact]
    public void EmptyVocabularyBuildsNoPrompt()
    {
        Assert.Equal("", TranscriptionPromptBuilder.Build(null));
        Assert.Equal("", TranscriptionPromptBuilder.Build([]));
    }

    [Fact]
    public void VocabularyBuildsCommaSeparatedPrompt()
        => Assert.Equal(
            "Matraca, Whisper.net, Claude Code",
            TranscriptionPromptBuilder.Build(["Matraca", "Whisper.net", "Claude Code"]));

    [Fact]
    public void PromptStopsBeforeLimitAndWarnsOnce()
    {
        var first = new string('a', 400);
        var warnings = new List<string>();

        var prompt = TranscriptionPromptBuilder.Build(
            [first, new string('b', 399), "ignored"],
            warnings.Add);

        Assert.Equal(first, prompt);
        Assert.Single(warnings);
        Assert.Contains(TranscriptionPromptBuilder.MaxPromptChars.ToString(), warnings[0]);
    }
}
