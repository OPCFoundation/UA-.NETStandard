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

public sealed partial class FileSystemWorkflowTests
{

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
}
