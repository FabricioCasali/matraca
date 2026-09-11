namespace MacUpdateProbe;

internal static class GateTests
{
    internal static int Run()
    {
        var gate = new InstallGate();
        void Expect(int operation, bool expected)
        {
            if (gate.Handle(operation) != expected)
                throw new InvalidOperationException($"Gate: operação {operation}, esperado {expected}");
        }
        Expect(99, false);
        Expect(1, true);  // Recording blocks both consent acquisition and quit.
        Expect(4, false);
        Expect(6, false);
        Expect(3, false);
        Expect(2, true);  // Stopping capture does not imply delivery drained.
        Expect(4, false);
        Expect(6, false);
        Expect(1, false);
        Expect(3, true);
        Expect(4, true);
        Expect(4, false); // No duplicate installation.
        Expect(1, false); // No new recording after acquiring the install lease.
        Expect(6, true);
        Expect(5, true);  // Failed/cancelled cycle releases the lease.
        Expect(1, true);
        Console.WriteLine("PASS: gate gerenciado; captura, drenagem, exclusão e recuperação. Não é prova Mac.");
        return 0;
    }
}
