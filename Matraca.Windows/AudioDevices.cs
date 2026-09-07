using NAudio.Wave;
using System.Runtime.InteropServices;

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
        // Never derive native indices from ListNames: unreadable entries may be omitted.
        for (int i = 0; i < WaveInEvent.DeviceCount; i++)
            if (string.Equals(WaveInEvent.GetCapabilities(i).ProductName, wanted,
                StringComparison.OrdinalIgnoreCase)) return i;

        uint result = waveInMessage(new IntPtr(DefaultDevice), 0x2015, out uint device, out _);
        if (result != 0 || device >= WaveInEvent.DeviceCount)
            throw new InvalidOperationException("Nao foi possivel resolver o microfone padrao do Windows.");
        return checked((int)device);
    }

    public static string RealName(int device)
    {
        string name = WaveInEvent.GetCapabilities(device).ProductName.Trim();
        if (name.Length == 0 || ListNames().Count(candidate =>
            string.Equals(candidate.Trim(), name, StringComparison.OrdinalIgnoreCase)) != 1)
            throw new InvalidOperationException("O microfone nao possui nome unico; nao e seguro associar seu ajuste.");
        return name;
    }

    [DllImport("winmm.dll")]
    private static extern uint waveInMessage(IntPtr device, uint message, out uint preferredDevice, out uint flags);
}
