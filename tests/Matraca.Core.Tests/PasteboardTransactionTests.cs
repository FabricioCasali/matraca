using Matraca.Mac.Platform.Text;
using Xunit;

namespace Matraca.Core.Tests;

public sealed class PasteboardTransactionTests
{
    [Fact]
    public void RestoresEveryItemAndTypeAsRawData()
    {
        var original = new PasteboardSnapshot([
            new PasteboardItemSnapshot([
                new KeyValuePair<string, byte[]>("public.utf8-plain-text", [0x61, 0x62]),
                new KeyValuePair<string, byte[]>("public.html", [0x3c, 0x62, 0x3e]),
            ]),
            new PasteboardItemSnapshot([
                new KeyValuePair<string, byte[]>("com.example.binary", [0x00, 0xff, 0x7f, 0x00]),
            ]),
        ]);
        var pasteboard = new FakePasteboard(original);

        Assert.True(PasteboardTransaction.TryBegin(pasteboard, "ditado acentuado", out var transaction));
        Assert.Equal(PasteboardRestoreResult.Restored, transaction!.Restore());

        AssertSnapshot(original, pasteboard.Current);
    }

    [Fact]
    public void ConcurrentUserChangeIsNeverOverwritten()
    {
        var pasteboard = new FakePasteboard(FakePasteboard.Snapshot("public.text", [1]));
        var userChange = FakePasteboard.Snapshot("public.png", [0x89, 0x50, 0x4e, 0x47]);
        Assert.True(PasteboardTransaction.TryBegin(pasteboard, "dictated", out var transaction));

        pasteboard.SetUserClipboard(userChange);

        Assert.Equal(PasteboardRestoreResult.OwnershipLost, transaction!.Restore());
        AssertSnapshot(userChange, pasteboard.Current);
    }

    [Fact]
    public void ChangeBetweenSnapshotAndWriteRefusesTheTransaction()
    {
        var pasteboard = new FakePasteboard(FakePasteboard.Snapshot("public.text", [1]))
        {
            ChangeBeforeWrite = true,
        };

        Assert.False(PasteboardTransaction.TryBegin(pasteboard, "dictated", out var transaction));

        Assert.Null(transaction);
        AssertSnapshot(FakePasteboard.Snapshot("public.data", [9, 8, 7]), pasteboard.Current);
    }

    [Fact]
    public void RestoreFailureIsExplicitWhileMatracaStillOwnsThePasteboard()
    {
        var pasteboard = new FakePasteboard(FakePasteboard.Snapshot("public.text", [1]))
        {
            FailRestore = true,
        };
        Assert.True(PasteboardTransaction.TryBegin(pasteboard, "dictated", out var transaction));

        Assert.Equal(PasteboardRestoreResult.Failed, transaction!.Restore());
    }

    private static void AssertSnapshot(PasteboardSnapshot expected, PasteboardSnapshot actual)
    {
        Assert.Equal(expected.Items.Count, actual.Items.Count);
        for (int itemIndex = 0; itemIndex < expected.Items.Count; itemIndex++)
        {
            var expectedValues = expected.Items[itemIndex].DataByType;
            var actualValues = actual.Items[itemIndex].DataByType;
            Assert.Equal(expectedValues.Count, actualValues.Count);
            for (int typeIndex = 0; typeIndex < expectedValues.Count; typeIndex++)
            {
                Assert.Equal(expectedValues[typeIndex].Key, actualValues[typeIndex].Key);
                Assert.Equal(expectedValues[typeIndex].Value, actualValues[typeIndex].Value);
            }
        }
    }
}
