/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Platform.Storage;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client.FileSystem;
using UaLens.Plugins.FileSystem;
using UaLens.Tests.Desktop;

namespace UaLens.Tests.Administration;

[TestFixture]
[NonParallelizable]
public sealed class FileSystemWorkflowTests
{
    [Test]
    [Platform("Win,Linux")]
    public Task FsNodeLoadRefreshAndFailureReplaceTheExactChildSnapshot()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var wire = new FileSystemProtocolTestDriver();
            var log = new AdministrationLogCapture();
            var root = new FsNode(wire.Client, "Production files", log);
            var directoryId = new NodeId(6201);
            var fileId = new NodeId(6202);
            wire.AddChild(FileSystemProtocolTestDriver.RootId, directoryId, "Archive", directory: true);
            wire.AddChild(FileSystemProtocolTestDriver.RootId, fileId, "counts.csv");
            Assert.That(root.Children.Single().IsPlaceholder, Is.True);
            Assert.That(root.Children.Single().Glyph, Is.EqualTo("…"));

            await root.LoadChildrenAsync(CancellationToken.None).ConfigureAwait(true);

            Assert.That(root.Children.Select(n => n.Name), Is.EqualTo(s_fsNodeLoadRefreshAndFailureReplaceTheExactChildSnapshotExpected));
            Assert.That(root.Children.Select(n => n.Glyph), Is.EqualTo(s_fsNodeLoadRefreshAndFailureReplaceTheExactChildSnapshotExpected2));
            Assert.That(root.Children.Select(n => n.FullPath), Is.EqualTo(s_fsNodeLoadRefreshAndFailureReplaceTheExactChildSnapshotExpected3));
            Assert.That(root.Children.All(n => ReferenceEquals(n.Root, root)), Is.True);
            Assert.That(root.Children[0].AsDirectory!.NodeId, Is.EqualTo(directoryId));
            Assert.That(root.Children[1].AsFile!.NodeId, Is.EqualTo(fileId));
            Assert.That(root.Glyph, Is.EqualTo("root"));
            Assert.That(root.DisplaySize, Is.Empty);
            Assert.That(root.ChildrenLoaded, Is.True);

            wire.Children[FileSystemProtocolTestDriver.RootId].RemoveAt(0);
            await root.LoadChildrenAsync(CancellationToken.None).ConfigureAwait(true);
            Assert.That(root.Children.Select(n => n.Name), Is.EqualTo(s_fsNodeLoadRefreshAndFailureReplaceTheExactChildSnapshotExpected4));
            var failure = new IOException("directory snapshot denied");
            wire.BrowseFailure = failure;
            await root.RefreshAsync(CancellationToken.None).ConfigureAwait(true);
            Assert.That(root.Children, Is.Empty);
            Assert.That(root.ChildrenLoaded, Is.True);
            Assert.That(log.Entries.Single().Exception, Is.SameAs(failure));
            Assert.That(log.Entries.Single().Message, Does.Contain("/"));

            wire.BrowseFailure = null;
            await root.RefreshAsync(CancellationToken.None).ConfigureAwait(true);
            Assert.That(root.Children.Single().Info!.NodeId, Is.EqualTo(fileId));
            Assert.That(wire.Calls, Is.Empty);
        });
    }

    [TestCase(0UL, "0 B")]
    [TestCase(1023UL, "1023 B")]
    [TestCase(1024UL, "1.0 KiB")]
    [TestCase(1048575UL, "1024.0 KiB")]
    [TestCase(1048576UL, "1.0 MiB")]
    [TestCase(1073741823UL, "1024.0 MiB")]
    [TestCase(1073741824UL, "1.0 GiB")]
    [Platform("Win,Linux")]
    public Task FileMetadataUsesWireValuesAndPreservesLastSuccessfulSnapshotOnFailure(ulong size, string display)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var wire = new FileSystemProtocolTestDriver();
            var log = new AdministrationLogCapture();
            var root = new FsNode(wire.Client, "Root", log);
            var fileId = new NodeId(6203);
            wire.AddChild(FileSystemProtocolTestDriver.RootId, fileId, "report.bin");
            wire.Metadata(fileId, "Size", new Variant(size));
            wire.Metadata(fileId, "LastModifiedTime",
                new Variant(new DateTimeUtc(2025, 3, 4, 12, 13, 14)));
            await root.LoadChildrenAsync(CancellationToken.None).ConfigureAwait(true);
            FsNode file = root.Children.Single();
            Assert.That(file.DisplayLastModified, Is.Empty);

            await file.RefreshAsync(CancellationToken.None).ConfigureAwait(true);

            Assert.That(file.Size, Is.EqualTo(size));
            Assert.That(file.DisplaySize, Is.EqualTo(display));
            Assert.That(file.DisplayLastModified, Is.EqualTo("2025-03-04 12:13:14Z"));
            Assert.That(wire.Translations.Select(p => p.RelativePath.Elements[0].TargetName.Name), Is.EqualTo(
                s_fileMetadataUsesWireValuesAndPreservesLastSuccessfulSnapshotOExpected));
            Assert.That(file.Children, Is.Empty);
            var failure = new IOException("metadata denied");
            wire.ReadFailure = failure;
            await file.RefreshAsync(CancellationToken.None).ConfigureAwait(true);
            Assert.That(file.Size, Is.EqualTo(size));
            Assert.That(file.DisplayLastModified, Is.EqualTo("2025-03-04 12:13:14Z"));
            Assert.That(log.Entries.Single().Exception, Is.SameAs(failure));
            Assert.That(log.Entries.Single().Message, Does.Contain("/report.bin"));
        });
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(20019)]
    [Platform("Win,Linux")]
    public Task ImportWritesExactBytesAndClosesEveryUaFileHandle(int length)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new AdministrationWorkflowContext();
            await using var plugin = new FileSystemPlugin(context.Host);
            var wire = new FileSystemProtocolTestDriver(8192);
            var root = new FsNode(wire.Client, "Import root", context.Log);
            await root.LoadChildrenAsync(CancellationToken.None).ConfigureAwait(true);
            plugin.Roots.Add(root);
            plugin.SelectedNode = root;
            using var temporary = new TemporaryCertificateStores();
            string local = Path.Combine(temporary.Root, "production.bin");
            byte[] bytes = Enumerable.Range(0, length).Select(i => (byte)((i * 17 + 3) % 251)).ToArray();
            await File.WriteAllBytesAsync(local, bytes).ConfigureAwait(true);
            var id = new NodeId(6301);
            ArrangeImport(wire, id, "production.bin", 41, bytes);

            await plugin.ImportFilesAsync(root, new[] { local }).ConfigureAwait(true);

            wire.VerifyComplete();
            Assert.That(plugin.Status, Is.EqualTo("● Imported 1 of 1 file"));
            Assert.That(plugin.SelectedChildren.Single().FullPath, Is.EqualTo("/production.bin"));
            Assert.That(plugin.SelectedChildren.Single().AsFile!.NodeId, Is.EqualTo(id));
            Assert.That(await File.ReadAllBytesAsync(local).ConfigureAwait(true), Is.EqualTo(bytes));
            byte[] written = wire.Calls.Where(c => c.MethodId == new NodeId(Methods.FileType_Write))
                .SelectMany(c => Bytes(c.InputArguments[1])).ToArray();
            Assert.That(written, Is.EqualTo(bytes));
            Assert.That(context.Log.Entries.Any(e => e.Exception is not null), Is.False);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    [Platform("Win,Linux")]
    public Task ImportContinuesAfterFaultOrCancellationAndReleasesTheFailedHandle(bool canceled)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new AdministrationWorkflowContext();
            await using var plugin = new FileSystemPlugin(context.Host);
            var wire = new FileSystemProtocolTestDriver();
            var root = new FsNode(wire.Client, "Root", context.Log);
            await root.LoadChildrenAsync(CancellationToken.None).ConfigureAwait(true);
            plugin.Roots.Add(root);
            plugin.SelectedNode = root;
            using var temporary = new TemporaryCertificateStores();
            string[] names = ["first.bin", "denied.bin", "last.bin"];
            string[] paths = names.Select(n => Path.Combine(temporary.Root, n)).ToArray();
            byte[] bytes = [5, 4, 3, 2, 1, 0, 9];
            Exception failure = canceled
                ? new OperationCanceledException("upload canceled")
                : new IOException("upload rejected");
            for (int i = 0; i < paths.Length; i++)
            {
                await File.WriteAllBytesAsync(paths[i], bytes).ConfigureAwait(true);
                ArrangeImport(wire, new NodeId((uint)(6310 + i)), names[i], (uint)(51 + i), bytes,
                    i == 1 ? failure : null);
            }

            await plugin.ImportFilesAsync(root, paths).ConfigureAwait(true);

            wire.VerifyComplete();
            Assert.That(plugin.Status, Is.EqualTo("● Imported 2 of 3 files"));
            Assert.That(plugin.SelectedChildren.Select(n => n.Name), Is.EqualTo(names));
            Assert.That(wire.Calls.Count(c => c.MethodId == new NodeId(Methods.FileType_Close)), Is.EqualTo(3));
            AdministrationLogCapture.Entry error = context.Log.Entries.Single(e => e.Exception is not null);
            Assert.That(error.Exception, Is.SameAs(failure));
            Assert.That(error.Message, Does.Contain(paths[1]).And.Contain("/"));
            Assert.That(paths.All(File.Exists), Is.True);
        });
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(11)]
    public async Task ExportCopiesExactBytesAndClosesUaBeforeDisposingTheDestination(int length)
    {
        await using var context = new AdministrationWorkflowContext();
        await using var plugin = new FileSystemPlugin(context.Host);
        var wire = new FileSystemProtocolTestDriver();
        var id = new NodeId(6401);
        UaFileInfo file = await CreateFileAsync(wire, id).ConfigureAwait(false);
        var releases = new List<string>();
        wire.OnClose = () => releases.Add("ua-close");
        await using var destination = new OwnedStorageStream(releases);
        var storage = new Mock<IStorageFile>(MockBehavior.Strict);
        storage.Setup(f => f.OpenWriteAsync()).ReturnsAsync(destination);
        byte[] bytes = Enumerable.Range(0, length).Select(i => (byte)(i + 10)).ToArray();
        ArrangeExport(wire, id, bytes);

        await plugin.ExportFileToAsync(file, storage.Object).ConfigureAwait(false);

        wire.VerifyComplete();
        Assert.That(destination.ToArray(), Is.EqualTo(bytes));
        Assert.That(destination.CanWrite, Is.False);
        Assert.That(releases, Is.EqualTo(s_exportCopiesExactBytesAndClosesUaBeforeDisposingTheDestinatioExpected));
        Assert.That(plugin.Status, Is.EqualTo("● Exported results.bin"));
        storage.Verify(f => f.OpenWriteAsync(), Times.Once);
        storage.VerifyNoOtherCalls();
    }

    [TestCase("destination-open")]
    [TestCase("source-open")]
    [TestCase("read")]
    [TestCase("write")]
    [TestCase("cancel-read")]
    public async Task ExportFaultsReleaseOnlyAcquiredResourcesAndNeverReportSuccess(string stage)
    {
        await using var context = new AdministrationWorkflowContext();
        await using var plugin = new FileSystemPlugin(context.Host);
        var wire = new FileSystemProtocolTestDriver();
        var id = new NodeId(6402);
        UaFileInfo file = await CreateFileAsync(wire, id).ConfigureAwait(false);
        var releases = new List<string>();
        wire.OnClose = () => releases.Add("ua-close");
        Exception failure = stage == "cancel-read"
            ? new OperationCanceledException("controlled cancellation")
            : new IOException("controlled " + stage + " failure");
        await using var destination = new OwnedStorageStream(releases, stage == "write" ? failure : null);
        var storage = new Mock<IStorageFile>(MockBehavior.Strict);
        if (stage == "destination-open")
        {
            storage.Setup(f => f.OpenWriteAsync()).ThrowsAsync(failure);
        }
        else
        {
            storage.Setup(f => f.OpenWriteAsync()).ReturnsAsync(destination);
            wire.Expect(id, new NodeId(Methods.FileType_Open), [new Variant((byte)1)], [new Variant(71u)],
                failure: stage == "source-open" ? failure : null);
            if (stage != "source-open")
            {
                wire.Expect(id, new NodeId(Methods.FileType_Read), [new Variant(71u), new Variant(4)],
                    [new Variant(new byte[] { 21, 22 }.ToByteString())],
                    failure: stage is "read" or "cancel-read" ? failure : null);
                wire.ExpectClose(id, 71);
            }
        }

        await plugin.ExportFileToAsync(file, storage.Object).ConfigureAwait(false);

        wire.VerifyComplete();
        string[] expected = stage switch
        {
            "destination-open" => [],
            "source-open" => ["destination-dispose"],
            _ => ["ua-close", "destination-dispose"]
        };
        Assert.That(releases, Is.EqualTo(expected));
        Assert.That(destination.ToArray(), Is.Empty);
        Assert.That(plugin.Status, Is.EqualTo("● Export failed: " + failure.Message));
        Assert.That(context.Log.Entries.Single(e => e.Exception is not null).Exception, Is.SameAs(failure));
        storage.Verify(f => f.OpenWriteAsync(), Times.Once);
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    [Platform("Win,Linux")]
    public Task DeleteRequiresScopedConsentAndRefreshesTheSelectedParent(bool directory, bool accept)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new AdministrationWorkflowContext();
            await using var plugin = new FileSystemPlugin(context.Host);
            var wire = new FileSystemProtocolTestDriver();
            var targetId = new NodeId(6501);
            var siblingId = new NodeId(6502);
            wire.AddChild(FileSystemProtocolTestDriver.RootId, targetId, "target", directory);
            wire.AddChild(FileSystemProtocolTestDriver.RootId, siblingId, "keep.txt");
            var root = new FsNode(wire.Client, "Root", context.Log);
            await root.LoadChildrenAsync(CancellationToken.None).ConfigureAwait(true);
            plugin.Roots.Add(root);
            FsNode target = root.Children[0];
            plugin.SelectedNode = target;
            if (directory)
            {
                await target.LoadChildrenAsync(CancellationToken.None).ConfigureAwait(true);
            }
            if (accept)
            {
                wire.Expect(FileSystemProtocolTestDriver.RootId,
                    new NodeId(Methods.FileDirectoryType_DeleteFileSystemObject),
                    [new Variant(targetId)], [], () => wire.Children[FileSystemProtocolTestDriver.RootId].RemoveAt(0));
            }
            Task operation = Task.CompletedTask;
            Window dialog = await DesktopInteraction.OpenedAsync<Window>(() => operation = plugin.DeleteAsync())
                .ConfigureAwait(true);
            try
            {
                string message = ((DockPanel)dialog.Content!).Children.OfType<TextBlock>().Single().Text!;
                Assert.That(message, Does.Contain("/target").And.Contain("cannot be undone"));
                Assert.That(message.Contains("ALL", StringComparison.Ordinal), Is.EqualTo(directory));
                Button cancel = Button(dialog, "Cancel");
                Assert.That(cancel.IsDefault, Is.True);
                Assert.That(wire.Calls, Is.Empty, "The server must not see a mutation before consent.");
                DesktopInteraction.Click(accept ? Button(dialog, "Delete") : cancel);
                await operation.ConfigureAwait(true);
                wire.VerifyComplete();
                Assert.That(root.Children.Select(n => n.Name),
                    Is.EqualTo(accept
                        ? s_deleteRequiresScopedConsentAndRefreshesTheSelectedParentExpected
                        : s_deleteRequiresScopedConsentAndRefreshesTheSelectedParentExpected2));
                Assert.That(plugin.SelectedNode, Is.SameAs(accept ? root : target));
                Assert.That(plugin.Status, Is.EqualTo(accept ? "● 1 root · /" : "● Delete cancelled."));
                if (accept)
                {
                    Assert.That(plugin.SelectedChildren.Single().Info!.NodeId, Is.EqualTo(siblingId));
                }
            }
            finally
            {
                dialog.Close();
                await operation.ConfigureAwait(true);
            }
        });
    }

    [Test]
    [Platform("Win,Linux")]
    public Task DeleteFailureKeepsTheTargetAndReportsItsPath()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new AdministrationWorkflowContext();
            await using var plugin = new FileSystemPlugin(context.Host);
            var wire = new FileSystemProtocolTestDriver();
            var id = new NodeId(6503);
            wire.AddChild(FileSystemProtocolTestDriver.RootId, id, "protected.bin");
            var root = new FsNode(wire.Client, "Root", context.Log);
            await root.LoadChildrenAsync(CancellationToken.None).ConfigureAwait(true);
            plugin.Roots.Add(root);
            plugin.SelectedNode = root.Children.Single();
            var failure = new IOException("delete denied");
            wire.Expect(FileSystemProtocolTestDriver.RootId,
                new NodeId(Methods.FileDirectoryType_DeleteFileSystemObject),
                [new Variant(id)], [], failure: failure);
            Task operation = Task.CompletedTask;
            Window dialog = await DesktopInteraction.OpenedAsync<Window>(() => operation = plugin.DeleteAsync())
                .ConfigureAwait(true);
            try
            {
                DesktopInteraction.Click(Button(dialog, "Delete"));
                await operation.ConfigureAwait(true);
                wire.VerifyComplete();
                Assert.That(plugin.SelectedNode!.Info!.NodeId, Is.EqualTo(id));
                Assert.That(root.Children.Single().Name, Is.EqualTo("protected.bin"));
                Assert.That(plugin.Status, Is.EqualTo("● Delete failed: delete denied"));
                Assert.That(context.Log.Entries.Single().Message, Does.Contain("/protected.bin"));
                Assert.That(context.Log.Entries.Single().Exception, Is.SameAs(failure));
            }
            finally
            {
                dialog.Close();
                await operation.ConfigureAwait(true);
            }
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    [Platform("Win,Linux")]
    public Task CreateFolderUsesTheSelectedFilesParentAndOnlyTheAcceptedName(bool accept)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new AdministrationWorkflowContext();
            await using var plugin = new FileSystemPlugin(context.Host);
            var wire = new FileSystemProtocolTestDriver();
            wire.AddChild(FileSystemProtocolTestDriver.RootId, new NodeId(6601), "existing.bin");
            var root = new FsNode(wire.Client, "Root", context.Log);
            await root.LoadChildrenAsync(CancellationToken.None).ConfigureAwait(true);
            plugin.Roots.Add(root);
            plugin.SelectedNode = root.Children.Single();
            var directoryId = new NodeId(6602);
            if (accept)
            {
                wire.Expect(FileSystemProtocolTestDriver.RootId, new NodeId(Methods.FileDirectoryType_CreateDirectory),
                    [new Variant("Reports")], [new Variant(directoryId)],
                    () => wire.AddChild(FileSystemProtocolTestDriver.RootId, directoryId, "Reports", directory: true));
            }
            Task operation = Task.CompletedTask;
            NameInputDialog dialog = await DesktopInteraction.OpenedAsync<NameInputDialog>(
                () => operation = plugin.NewFolderAsync()).ConfigureAwait(true);
            try
            {
                DesktopInteraction.Control<TextBox>(dialog, "NameBox").Text = "  Reports  ";
                Assert.That(wire.Calls, Is.Empty);
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(
                    dialog, accept ? "OkButton" : "CancelButton"));
                await operation.ConfigureAwait(true);
                wire.VerifyComplete();
                Assert.That(root.Children.Select(n => n.Name), Is.EqualTo(
                    accept
                        ? s_createFolderUsesTheSelectedFilesParentAndOnlyTheAcceptedNameExpected
                        : s_createFolderUsesTheSelectedFilesParentAndOnlyTheAcceptedNameExpected2));
                Assert.That(plugin.SelectedNode!.Name, Is.EqualTo("existing.bin"));
            }
            finally
            {
                dialog.Close();
                await operation.ConfigureAwait(true);
            }
        });
    }

    [TestCase("  renamed.bin  ", true)]
    [TestCase(" original.bin ", false)]
    [TestCase("  ", false)]
    [Platform("Win,Linux")]
    public Task RenameUsesMoveRatherThanCopyAndDoesNotSendUnchangedOrEmptyNames(string name, bool changes)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new AdministrationWorkflowContext();
            await using var plugin = new FileSystemPlugin(context.Host);
            var wire = new FileSystemProtocolTestDriver();
            var id = new NodeId(6603);
            wire.AddChild(FileSystemProtocolTestDriver.RootId, id, "original.bin");
            var root = new FsNode(wire.Client, "Root", context.Log);
            await root.LoadChildrenAsync(CancellationToken.None).ConfigureAwait(true);
            plugin.Roots.Add(root);
            plugin.SelectedNode = root.Children.Single();
            if (changes)
            {
                wire.Expect(FileSystemProtocolTestDriver.RootId, new NodeId(Methods.FileDirectoryType_MoveOrCopy),
                    [new Variant(id), new Variant(FileSystemProtocolTestDriver.RootId),
                        new Variant(false), new Variant("renamed.bin")], [new Variant(id)], () =>
                    {
                        wire.Children[FileSystemProtocolTestDriver.RootId].Clear();
                        wire.AddChild(FileSystemProtocolTestDriver.RootId, id, "renamed.bin");
                    });
            }
            Task operation = Task.CompletedTask;
            NameInputDialog dialog = await DesktopInteraction.OpenedAsync<NameInputDialog>(
                () => operation = plugin.RenameAsync()).ConfigureAwait(true);
            try
            {
                Assert.That(DesktopInteraction.Control<TextBox>(dialog, "NameBox").Text, Is.EqualTo("original.bin"));
                DesktopInteraction.Control<TextBox>(dialog, "NameBox").Text = name;
                Assert.That(wire.Calls, Is.Empty);
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "OkButton"));
                await operation.ConfigureAwait(true);
                wire.VerifyComplete();
                Assert.That(root.Children.Single().Name, Is.EqualTo(changes ? "renamed.bin" : "original.bin"));
                Assert.That(root.Children.Single().Info!.NodeId, Is.EqualTo(id));
            }
            finally
            {
                dialog.Close();
                await operation.ConfigureAwait(true);
            }
        });
    }

    [Test]
    [Platform("Win,Linux")]
    public Task ExpansionLoadsOnceAndExplicitRefreshCanReplaceTheSnapshotWithAnEmptyDirectory()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var wire = new FileSystemProtocolTestDriver();
            var id = new NodeId(6701);
            wire.AddChild(FileSystemProtocolTestDriver.RootId, id, "production.csv");
            var root = new FsNode(wire.Client, "Production files", null);
            await DesktopInteraction.CollectionChangedAsync(root.Children,
                () => root.Children.Count == 1 && root.Children[0].Info?.NodeId == id,
                () => root.IsExpanded = true).ConfigureAwait(true);
            Assert.That(root.ChildrenLoaded, Is.True);
            Assert.That(root.Children.Single().FullPath, Is.EqualTo("/production.csv"));
            Assert.That(wire.Browses.Select(b => b.NodeId), Is.EqualTo(new[] { FileSystemProtocolTestDriver.RootId }));
            root.IsExpanded = false;
            root.IsExpanded = true;
            Assert.That(wire.Browses, Has.Count.EqualTo(1));

            wire.Children[FileSystemProtocolTestDriver.RootId].Clear();
            await root.RefreshAsync(CancellationToken.None).ConfigureAwait(true);

            Assert.That(root.Children, Is.Empty);
            Assert.That(root.ChildrenLoaded, Is.True);
            Assert.That(root.IsExpanded, Is.True);
            Assert.That(wire.Browses, Has.Count.EqualTo(2));
            Assert.That(wire.Calls, Is.Empty);
        });
    }

    private static void ArrangeImport(
        FileSystemProtocolTestDriver wire, NodeId id, string name, uint handle, byte[] bytes, Exception? failure = null)
    {
        wire.Expect(FileSystemProtocolTestDriver.RootId, new NodeId(Methods.FileDirectoryType_CreateFile),
            [new Variant(name), new Variant(false)], [new Variant(id), new Variant(0u)],
            () => wire.AddChild(FileSystemProtocolTestDriver.RootId, id, name));
        wire.Expect(id, new NodeId(Methods.FileType_Open), [new Variant((byte)6)], [new Variant(handle)]);
        for (int offset = 0; offset < bytes.Length; offset += wire.Client.Options.ChunkSize)
        {
            int size = Math.Min(wire.Client.Options.ChunkSize, bytes.Length - offset);
            byte[] chunk = bytes.AsSpan(offset, size).ToArray();
            wire.Expect(id, new NodeId(Methods.FileType_Write),
                [new Variant(handle), new Variant(chunk.ToByteString())], [], failure: failure);
            if (failure is not null)
            {
                break;
            }
        }
        wire.ExpectClose(id, handle);
    }

    private static async Task<UaFileInfo> CreateFileAsync(FileSystemProtocolTestDriver wire, NodeId id)
    {
        wire.Expect(FileSystemProtocolTestDriver.RootId, new NodeId(Methods.FileDirectoryType_CreateFile),
            [new Variant("results.bin"), new Variant(false)], [new Variant(id), new Variant(0u)],
            () => wire.Names[id] = new QualifiedName("results.bin"));
        return await wire.Client.Root.CreateFileAsync("results.bin").ConfigureAwait(false);
    }

    private static void ArrangeExport(FileSystemProtocolTestDriver wire, NodeId id, byte[] bytes)
    {
        wire.Expect(id, new NodeId(Methods.FileType_Open), [new Variant((byte)1)], [new Variant(71u)]);
        for (int offset = 0; offset < bytes.Length; offset += 4)
        {
            byte[] chunk = bytes.AsSpan(offset, Math.Min(4, bytes.Length - offset)).ToArray();
            wire.Expect(id, new NodeId(Methods.FileType_Read), [new Variant(71u), new Variant(4)],
                [new Variant(chunk.ToByteString())]);
        }
        wire.Expect(id, new NodeId(Methods.FileType_Read), [new Variant(71u), new Variant(4)],
            [new Variant(Array.Empty<byte>().ToByteString())]);
        wire.ExpectClose(id, 71);
    }

    private static byte[] Bytes(Variant value)
    {
        Assert.That(value.TryGetValue(out ByteString bytes), Is.True);
        return bytes.Memory.ToArray();
    }

    private static Button Button(Window window, string text)
    {
        return window.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Content, text));
    }

    private sealed class OwnedStorageStream(List<string> releases, Exception? writeFailure = null) : MemoryStream
    {
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return writeFailure is null
                ? base.WriteAsync(buffer, cancellationToken)
                : ValueTask.FromException(writeFailure);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return writeFailure is null
                ? base.WriteAsync(buffer, offset, count, cancellationToken)
                : Task.FromException(writeFailure);
        }

        public override ValueTask DisposeAsync()
        {
            if (!m_disposed)
            {
                m_disposed = true;
                releases.Add("destination-dispose");
            }
            return base.DisposeAsync();
        }

        private bool m_disposed;
    }

    private static readonly string[] s_fsNodeLoadRefreshAndFailureReplaceTheExactChildSnapshotExpected =
    [
        "Archive",
        "counts.csv",
    ];
    private static readonly string[] s_fsNodeLoadRefreshAndFailureReplaceTheExactChildSnapshotExpected2 =
    [
        "dir",
        "file",
    ];
    private static readonly string[] s_fsNodeLoadRefreshAndFailureReplaceTheExactChildSnapshotExpected3 =
    [
        "/Archive",
        "/counts.csv",
    ];
    private static readonly string[] s_fsNodeLoadRefreshAndFailureReplaceTheExactChildSnapshotExpected4 =
    [
        "counts.csv",
    ];
    private static readonly string[] s_fileMetadataUsesWireValuesAndPreservesLastSuccessfulSnapshotOExpected =
    [
        "Size",
        "Writable",
        "UserWritable",
        "OpenCount",
        "MimeType",
        "MaxByteStringLength",
        "LastModifiedTime",
    ];
    private static readonly string[] s_exportCopiesExactBytesAndClosesUaBeforeDisposingTheDestinatioExpected =
    [
        "ua-close",
        "destination-dispose",
    ];
    private static readonly string[] s_deleteRequiresScopedConsentAndRefreshesTheSelectedParentExpected =
    [
        "keep.txt",
    ];
    private static readonly string[] s_deleteRequiresScopedConsentAndRefreshesTheSelectedParentExpected2 =
    [
        "target",
        "keep.txt",
    ];
    private static readonly string[] s_createFolderUsesTheSelectedFilesParentAndOnlyTheAcceptedNameExpected =
    [
        "existing.bin",
        "Reports",
    ];
    private static readonly string[] s_createFolderUsesTheSelectedFilesParentAndOnlyTheAcceptedNameExpected2 =
    [
        "existing.bin",
    ];
}
