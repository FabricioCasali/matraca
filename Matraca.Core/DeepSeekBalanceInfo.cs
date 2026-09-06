namespace Matraca.Core;

public sealed record DeepSeekBalanceInfo(
    string Currency,
    decimal TotalBalance,
    decimal GrantedBalance,
    decimal ToppedUpBalance);
