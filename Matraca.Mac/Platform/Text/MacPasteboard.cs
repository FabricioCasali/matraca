using System.Runtime.InteropServices;
using System.Text;
using Matraca.Core;
using Matraca.Mac.Platform.Interop;

namespace Matraca.Mac.Platform.Text;

internal sealed unsafe class MacPasteboard : IPasteboard
{
    private const string ObjCLibrary = "/usr/lib/libobjc.A.dylib";
    private const string StringType = "public.utf8-plain-text";
    private const string MarkerType = "io.github.fabriciocasali.matraca.delivery-marker";
    private const int CaptureAttempts = 3;

    private static readonly IntPtr NSPasteboard = GetClass("NSPasteboard");
    private static readonly IntPtr NSPasteboardItem = GetClass("NSPasteboardItem");
    private static readonly IntPtr NSData = GetClass("NSData");
    private static readonly IntPtr NSMutableArray = GetClass("NSMutableArray");
    private static readonly IntPtr GeneralPasteboard = Selector("generalPasteboard");
    private static readonly IntPtr PasteboardItems = Selector("pasteboardItems");
    private static readonly IntPtr Types = Selector("types");
    private static readonly IntPtr DataForType = Selector("dataForType:");
    private static readonly IntPtr DataWithBytesLength = Selector("dataWithBytes:length:");
    private static readonly IntPtr Bytes = Selector("bytes");
    private static readonly IntPtr Length = Selector("length");
    private static readonly IntPtr ChangeCount = Selector("changeCount");
    private static readonly IntPtr ClearContents = Selector("clearContents");
    private static readonly IntPtr WriteObjects = Selector("writeObjects:");
    private static readonly IntPtr SetDataForType = Selector("setData:forType:");
    private static readonly IntPtr AddObject = Selector("addObject:");

    public bool TryCapture(out PasteboardSnapshot snapshot, out long changeCount)
    {
        using var pool = AutoreleasePool.New();
        IntPtr pasteboard = ObjC.Send(NSPasteboard, GeneralPasteboard);
        for (int attempt = 0; attempt < CaptureAttempts; attempt++)
        {
            long before = GetChangeCount(pasteboard);
            if (!TryReadSnapshot(pasteboard, out snapshot))
            {
                changeCount = 0;
                return false;
            }

            long after = GetChangeCount(pasteboard);
            if (before == after)
            {
                changeCount = after;
                return true;
            }
        }

        snapshot = new PasteboardSnapshot(Array.Empty<PasteboardItemSnapshot>());
        changeCount = 0;
        Logger.Warn("O clipboard mudou durante o snapshot; entrega por clipboard recusada.");
        return false;
    }

    public bool TryWriteText(
        PasteboardSnapshot previous,
        string text,
        byte[] marker,
        long expectedChangeCount,
        out long ownChangeCount)
    {
        using var pool = AutoreleasePool.New();
        IntPtr pasteboard = ObjC.Send(NSPasteboard, GeneralPasteboard);
        IntPtr previousItems = IntPtr.Zero;
        IntPtr ownItems = IntPtr.Zero;
        ownChangeCount = 0;

        try
        {
            previousItems = CreateNativeItems(previous);
            ownItems = CreateNativeItems(new PasteboardSnapshot([
                new PasteboardItemSnapshot([
                    new KeyValuePair<string, byte[]>(StringType, Encoding.UTF8.GetBytes(text)),
                    new KeyValuePair<string, byte[]>(MarkerType, marker),
                ]),
            ]));

            if (GetChangeCount(pasteboard) != expectedChangeCount) return false;

            long clearChangeCount = ObjC.SendNInt(pasteboard, ClearContents);
            if (!SendBool(pasteboard, WriteObjects, ownItems))
            {
                if (GetChangeCount(pasteboard) == clearChangeCount
                    && !WritePreparedSnapshot(pasteboard, previous, previousItems))
                    throw new InvalidOperationException("NSPasteboard recusou a escrita e o rollback.");
                return false;
            }

            ownChangeCount = clearChangeCount;
            if (ContainsOwnedChange(pasteboard, ownChangeCount, marker)) return true;
            if (GetChangeCount(pasteboard) == ownChangeCount
                && !WritePreparedSnapshot(pasteboard, previous, previousItems))
                throw new InvalidOperationException("NSPasteboard nao confirmou a marca e o rollback falhou.");
            ownChangeCount = 0;
            return false;
        }
        finally
        {
            Release(previousItems);
            Release(ownItems);
        }
    }

    public bool WriteText(string text)
    {
        if (!TryCapture(out PasteboardSnapshot previous, out long changeCount)) return false;
        return TryWriteText(
            previous,
            text,
            Guid.NewGuid().ToByteArray(),
            changeCount,
            out _);
    }

    public PasteboardRestoreResult RestoreIfOwned(
        PasteboardSnapshot snapshot,
        long ownChangeCount,
        byte[] marker)
    {
        using var pool = AutoreleasePool.New();
        IntPtr pasteboard = ObjC.Send(NSPasteboard, GeneralPasteboard);
        IntPtr nativeItems = IntPtr.Zero;
        try
        {
            nativeItems = CreateNativeItems(snapshot);
            if (!ContainsOwnedChange(pasteboard, ownChangeCount, marker))
                return PasteboardRestoreResult.OwnershipLost;
            return WritePreparedSnapshot(pasteboard, snapshot, nativeItems)
                ? PasteboardRestoreResult.Restored
                : PasteboardRestoreResult.Failed;
        }
        catch (Exception exception)
        {
            Logger.Error("Falha ao restaurar o clipboard do Mac", exception);
            return PasteboardRestoreResult.Failed;
        }
        finally
        {
            Release(nativeItems);
        }
    }

    private static bool TryReadSnapshot(IntPtr pasteboard, out PasteboardSnapshot snapshot)
    {
        var snapshots = new List<PasteboardItemSnapshot>();
        IntPtr items = ObjC.Send(pasteboard, PasteboardItems);
        nuint itemCount = items == IntPtr.Zero ? 0 : ObjC.SendNUInt(items, ObjCSelectors.Count);

        for (nuint itemIndex = 0; itemIndex < itemCount; itemIndex++)
        {
            IntPtr item = ObjC.SendNUInt(items, ObjCSelectors.ObjectAtIndex, itemIndex);
            IntPtr types = ObjC.Send(item, Types);
            if (types == IntPtr.Zero)
            {
                snapshot = new PasteboardSnapshot(Array.Empty<PasteboardItemSnapshot>());
                return false;
            }

            nuint typeCount = ObjC.SendNUInt(types, ObjCSelectors.Count);
            var values = new List<KeyValuePair<string, byte[]>>(checked((int)typeCount));
            for (nuint typeIndex = 0; typeIndex < typeCount; typeIndex++)
            {
                IntPtr nativeType = ObjC.SendNUInt(types, ObjCSelectors.ObjectAtIndex, typeIndex);
                string? type = NSStringRef.To(nativeType);
                IntPtr data = ObjC.Send(item, DataForType, nativeType);
                byte[]? bytes = ReadData(data);
                if (string.IsNullOrEmpty(type) || bytes == null)
                {
                    snapshot = new PasteboardSnapshot(Array.Empty<PasteboardItemSnapshot>());
                    return false;
                }
                values.Add(new KeyValuePair<string, byte[]>(type, bytes));
            }
            snapshots.Add(new PasteboardItemSnapshot(values));
        }

        snapshot = new PasteboardSnapshot(snapshots);
        return true;
    }

    private static IntPtr CreateNativeItems(PasteboardSnapshot snapshot)
    {
        IntPtr items = ObjC.New(NSMutableArray);
        if (items == IntPtr.Zero) throw new InvalidOperationException("NSMutableArray init failed.");

        try
        {
            foreach (PasteboardItemSnapshot snapshotItem in snapshot.Items)
            {
                IntPtr item = ObjC.New(NSPasteboardItem);
                if (item == IntPtr.Zero) throw new InvalidOperationException("NSPasteboardItem init failed.");
                try
                {
                    foreach ((string type, byte[] value) in snapshotItem.DataByType)
                    {
                        IntPtr nativeType = NSStringRef.From(type);
                        IntPtr data;
                        fixed (byte* bytes = value)
                            data = SendData(NSData, DataWithBytesLength, bytes, (nuint)value.Length);
                        if (nativeType == IntPtr.Zero
                            || data == IntPtr.Zero
                            || !SendBool(item, SetDataForType, data, nativeType))
                            throw new InvalidOperationException($"NSPasteboardItem recusou o tipo '{type}'.");
                    }
                    ObjC.SendVoid(items, AddObject, item);
                }
                finally
                {
                    Release(item);
                }
            }
            return items;
        }
        catch
        {
            Release(items);
            throw;
        }
    }

    private static bool WritePreparedSnapshot(
        IntPtr pasteboard,
        PasteboardSnapshot snapshot,
        IntPtr nativeItems)
    {
        ObjC.SendNInt(pasteboard, ClearContents);
        return snapshot.Items.Count == 0 || SendBool(pasteboard, WriteObjects, nativeItems);
    }

    private static bool ContainsOwnedChange(IntPtr pasteboard, long ownChangeCount, byte[] marker)
    {
        if (GetChangeCount(pasteboard) != ownChangeCount) return false;

        IntPtr items = ObjC.Send(pasteboard, PasteboardItems);
        if (items == IntPtr.Zero || ObjC.SendNUInt(items, ObjCSelectors.Count) != 1) return false;
        IntPtr item = ObjC.SendNUInt(items, ObjCSelectors.ObjectAtIndex, 0);
        IntPtr nativeMarkerType = NSStringRef.From(MarkerType);
        byte[]? currentMarker = ReadData(ObjC.Send(item, DataForType, nativeMarkerType));
        return currentMarker != null
            && currentMarker.AsSpan().SequenceEqual(marker)
            && GetChangeCount(pasteboard) == ownChangeCount;
    }

    private static byte[]? ReadData(IntPtr data)
    {
        if (data == IntPtr.Zero) return null;
        nuint length = ObjC.SendNUInt(data, Length);
        if (length > int.MaxValue) return null;
        var result = new byte[(int)length];
        if (result.Length == 0) return result;
        IntPtr bytes = ObjC.Send(data, Bytes);
        if (bytes == IntPtr.Zero) return null;
        Marshal.Copy(bytes, result, 0, result.Length);
        return result;
    }

    private static long GetChangeCount(IntPtr pasteboard)
        => ObjC.SendNInt(pasteboard, ChangeCount);

    private static IntPtr GetClass(string name)
    {
        Frameworks.EnsureLoaded();
        IntPtr value = ObjC.objc_getClass(name);
        return value != IntPtr.Zero
            ? value
            : throw new TypeLoadException($"Objective-C class not found: {name}");
    }

    private static IntPtr Selector(string name) => ObjC.sel_registerName(name);

    private static void Release(IntPtr value)
    {
        if (value != IntPtr.Zero) ObjC.SendVoid(value, ObjCSelectors.Release);
    }

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendData(
        IntPtr receiver,
        IntPtr selector,
        byte* bytes,
        nuint length);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SendBool(IntPtr receiver, IntPtr selector, IntPtr argument);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SendBool(
        IntPtr receiver,
        IntPtr selector,
        IntPtr first,
        IntPtr second);
}
