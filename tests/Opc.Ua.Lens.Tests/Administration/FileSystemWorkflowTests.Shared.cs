// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
