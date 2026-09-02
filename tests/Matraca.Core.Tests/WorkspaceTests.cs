using System.Reflection;
using Xunit;

namespace Matraca.Core.Tests;

public sealed class WorkspaceTests
{
    [Fact]
    public void CoreAssemblyLoadsFromProjectReference()
    {
        var assembly = Assembly.Load("Matraca.Core");

        Assert.Equal("Matraca.Core", assembly.GetName().Name);
    }
}
