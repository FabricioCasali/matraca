using System.Text;

namespace Matraca.Core;

public static class TranscriptionPromptBuilder
{
    public const int MaxPromptChars = 800;

    public static string Build(string[]? vocabulary, Action<string>? warning = null)
    {
        if (vocabulary == null || vocabulary.Length == 0) return "";

        var prompt = new StringBuilder();
        foreach (var term in vocabulary)
        {
            if (prompt.Length + term.Length + 2 > MaxPromptChars)
            {
                warning?.Invoke($"Vocabulario truncado em {MaxPromptChars} caracteres; "
                              + "termos do fim da lista foram ignorados.");
                break;
            }
            if (prompt.Length > 0) prompt.Append(", ");
            prompt.Append(term);
        }
        return prompt.ToString();
    }
}
