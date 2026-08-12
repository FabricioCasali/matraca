using Matraca.MacSpike.Interop;
using Matraca.MacSpike.Tap;

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

                case "--tap":
                    return RunTap();

                default:
                    SpikeLog.Error($"perna desconhecida: {leg}");
                    SpikeLog.Info("uso: Matraca [--alive|--tap]");
                    return 2;
            }
        }
        catch (Exception ex)
        {
            SpikeLog.Error("perna estourou", ex);
            return 1;
        }
    }

    /// <summary>Passo 1 — risco 1: o event tap no F13, e o religamento depois do timeout.</summary>
    private static int RunTap()
    {
        Frameworks.EnsureLoaded();
        ObjCClasses.Warm();
        MainThread.Init();

        bool trusted = Accessibility.IsTrusted(prompt: true);
        SpikeLog.Info($"AXIsProcessTrusted = {trusted}");
        if (!trusted)
        {
            SpikeLog.Warn("Sem permissao de Acessibilidade. O dialogo do sistema deve ter "
                        + "subido; conceda e RODE DE NOVO (a permissao nao vale para o "
                        + "processo ja' em execucao).");
        }

        KeyTap.OnHotkey = () => SpikeLog.Info("*** handler do atalho rodou (fora da thread do tap) ***");
        if (!KeyTap.Start()) return 1;

        SpikeLog.Info("");
        SpikeLog.Info("ROTEIRO DESTA PERNA:");
        SpikeLog.Info("  1. aperte varias teclas: cada keyDown loga o keycode;");
        SpikeLog.Info("  2. aperte F13 com o TextEdit na frente: o log dispara e NADA e'");
        SpikeLog.Info("     digitado la' (o tap engoliu o evento);");
        SpikeLog.Info("  3. aperte F14 e depois qualquer tecla: tem de aparecer o WARN de");
        SpikeLog.Info("     timeout, o religamento, e a tecla seguinte voltando ao log.");
        SpikeLog.Info("  Ctrl+C encerra.");
        SpikeLog.Info("");

        // A run loop principal nunca retorna. E' o contrato de threads do projeto.
        CoreGraphics.CFRunLoopRun();
        KeyTap.Stop();
        return 0;
    }
}
