using NAudio.Wave;

namespace Matraca;

/// <summary>
/// Enumera e resolve os microfones disponiveis. A config guarda o NOME do dispositivo,
/// nao o indice: indices do WaveIn mudam quando se pluga/despluga qualquer aparelho de audio,
/// entao gravar o indice faria o app trocar de microfone sozinho.
/// </summary>
internal static class AudioDevices
{
    /// <summary>Indice do WAVE_MAPPER — deixa o Windows escolher o dispositivo padrao.</summary>
    public const int DefaultDevice = -1;

    /// <summary>Nomes dos microfones disponiveis, na ordem em que o WaveIn os enumera.</summary>
    public static List<string> ListNames()
    {
        var names = new List<string>();
        try
        {
            for (int i = 0; i < WaveInEvent.DeviceCount; i++)
            {
                try { names.Add(WaveInEvent.GetCapabilities(i).ProductName); }
                catch (Exception ex) { Logger.Warn($"Microfone {i} ilegivel: {ex.Message}"); }
            }
        }
        catch (Exception ex) { Logger.Warn("Falha ao listar microfones: " + ex.Message); }
        return names;
    }

    /// <summary>
    /// Traduz o nome configurado no indice do WaveIn. Vazio, desconhecido ou desconectado
    /// caem no dispositivo padrao — melhor gravar pelo microfone errado do que nao gravar.
    /// </summary>
    public static int Resolve(string? configuredName)
    {
        var wanted = (configuredName ?? "").Trim();
        if (wanted.Length == 0) return DefaultDevice;

        var names = ListNames();
        for (int i = 0; i < names.Count; i++)
            if (string.Equals(names[i], wanted, StringComparison.OrdinalIgnoreCase))
                return i;

        // O Windows corta o ProductName em 31 caracteres na API antiga do WaveIn, entao um
        // nome longo salvo por outra via pode nao bater exatamente.
        for (int i = 0; i < names.Count; i++)
            if (wanted.StartsWith(names[i], StringComparison.OrdinalIgnoreCase) ||
                names[i].StartsWith(wanted, StringComparison.OrdinalIgnoreCase))
                return i;

        Logger.Warn($"Microfone '{wanted}' nao encontrado; usando o padrao do Windows.");
        return DefaultDevice;
    }
}
