using System.ComponentModel;
using System.Runtime.InteropServices;
using Matraca.Core;

namespace Matraca.Mac.Platform;

internal sealed class MacSingleInstance : IDisposable
{
    private const string LibSystem = "/usr/lib/libSystem.B.dylib";
    private const int LockExclusive = 2;
    private const int LockNonBlocking = 4;
    private const int LockUnlock = 8;
    private const int WouldBlock = 35;

    private FileStream? _file;

    private MacSingleInstance(FileStream file) => _file = file;

    public static MacSingleInstance? TryAcquire(AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        Directory.CreateDirectory(paths.DataDirectory);
        string lockPath = Path.Combine(paths.DataDirectory, "instance.lock");
        var file = new FileStream(
            lockPath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.ReadWrite);

        int descriptor = file.SafeFileHandle.DangerousGetHandle().ToInt32();
        if (flock(descriptor, LockExclusive | LockNonBlocking) == 0)
            return new MacSingleInstance(file);

        int error = Marshal.GetLastPInvokeError();
        file.Dispose();
        if (error == WouldBlock) return null;
        throw new IOException(
            $"Could not lock the Matraca instance file: {new Win32Exception(error).Message}");
    }

    public void Dispose()
    {
        FileStream? file = Interlocked.Exchange(ref _file, null);
        if (file == null) return;

        int descriptor = file.SafeFileHandle.DangerousGetHandle().ToInt32();
        _ = flock(descriptor, LockUnlock);
        file.Dispose();
        // Never unlink a lock file: a contender could lock a replacement inode.
    }

    [DllImport(LibSystem, SetLastError = true)]
    private static extern int flock(int descriptor, int operation);
}
