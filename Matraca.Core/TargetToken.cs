namespace Matraca.Core;

/// <summary>Process-local identity for a native target known only by the platform adapter.</summary>
public sealed record TargetToken(Guid Value)
{
    public static TargetToken Create() => new(Guid.NewGuid());
}
