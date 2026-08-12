namespace Matraca.MacSpike;

/// <summary>
/// Ponto de entrada do spike. Uma perna por argumento — e isso nao e' conveniencia de
/// linha de comando: e' o que faz a falha de uma prova nao levar junto as outras.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        string leg = args.Length > 0 ? args[0] : "--alive";
        SpikeLog.Banner(leg);
        SpikeLog.Info($"vivo — pid {Environment.ProcessId}, {System.Runtime.InteropServices.RuntimeInformation.OSDescription}, "
                    + $"{System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
        SpikeLog.Info($"executavel: {Environment.ProcessPath}");
        SpikeLog.Info($"base dir  : {AppContext.BaseDirectory}");

        try
        {
            switch (leg)
            {
                case "--alive":
                    SpikeLog.Info("Passo 0: o casco assinado esta' de pe'. Saindo.");
                    return 0;

                default:
                    SpikeLog.Error($"perna desconhecida: {leg}");
                    SpikeLog.Info("uso: Matraca [--alive]");
                    return 2;
            }
        }
        catch (Exception ex)
        {
            SpikeLog.Error("perna estourou", ex);
            return 1;
        }
    }
}
