using Matraca.Core;
using Xunit;

namespace Matraca.Core.Tests;

public sealed class TargetTokenTests
{
    [Fact]
    public void TokensHaveValueSemanticsWithoutNativeHandles()
    {
        var value = Guid.NewGuid();

        Assert.Equal(new TargetToken(value), new TargetToken(value));
        Assert.NotEqual(TargetToken.Create(), TargetToken.Create());
    }
}
