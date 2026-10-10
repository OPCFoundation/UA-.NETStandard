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
public sealed partial class FileSystemWorkflowTests
{

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
    private static readonly string[] s_exportCopiesExactBytesAndClosesUaBeforeDisposingTheDestinatioExpected =
    [
        "ua-close",
        "destination-dispose",
    ];

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
}
