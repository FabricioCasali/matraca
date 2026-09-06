namespace Matraca.Core;

public sealed record DeepSeekBalance(
    bool IsAvailable,
    IReadOnlyList<DeepSeekBalanceInfo> Balances);
