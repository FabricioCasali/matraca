using System.Reflection;
using Matraca.Core;
using Xunit;

namespace Matraca.Core.Tests;

public sealed class PlatformContractsTests
{
    [Fact]
    public void CoreDefinesExactlyTheFivePlatformInterfaces()
    {
        var interfaces = typeof(IKeyboardHook).Assembly
            .GetTypes()
            .Where(type => type.IsInterface)
            .Select(type => type.Name)
            .Order()
            .ToArray();

        Assert.Equal(
            new[] { "IAudioCapture", "IKeyboardHook", "IShell", "ITargetWindow", "ITextSink" },
            interfaces);
    }

    [Fact]
    public void PlatformContractsDoNotExposeNativeHandles()
    {
        var interfaceTypes = typeof(IKeyboardHook).Assembly
            .GetTypes()
            .Where(type => type.IsInterface);

        var exposedTypes = interfaceTypes
            .SelectMany(type => type.GetMembers())
            .OfType<MethodInfo>()
            .SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType)
                .Append(method.ReturnType));

        Assert.DoesNotContain(exposedTypes, type => type == typeof(IntPtr));
    }

    [Fact]
    public void AudioContractFixesTheWhisperSampleRate()
        => Assert.Equal(16000, IAudioCapture.RequiredSampleRate);

    [Fact]
    public void TargetContractCanReleaseOpaqueResources()
        => Assert.NotNull(typeof(ITargetWindow).GetMethod(nameof(ITargetWindow.Release)));
}
