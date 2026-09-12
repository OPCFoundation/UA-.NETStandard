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
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.FileSystem;
using UaLens.Tests.Desktop;

namespace UaLens.Tests.Administration;

[TestFixture]
[Platform("Win,Linux")]
[NonParallelizable]
public sealed class ConnectedFileSystemTests
{
    [TestCase("directory")]
    [TestCase("file")]
    [TestCase("derivedParent")]
    [TestCase("orphan")]
    public Task RestoredRootsAttachOnceAndFilesResolveTheirContainingDirectory(string kind)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new ConnectedProtocolContext();
            await context.ConnectAsync().ConfigureAwait(true);
            NodeId root = new("Restored", 2);
            NodeId parent = new("ParentDirectory", 2);
            NodeId derived = new("DerivedDirectoryType", 2);
            int typeQueries = 0;
            context.Browse = (ids, _) =>
            {
                BrowseDescription request = ids[0];
                NodeId target = NodeId.Null;
                if (request.ReferenceTypeId == ReferenceTypeIds.HasTypeDefinition)
                {
                    typeQueries++;
                    target = request.NodeId == root
                        ? kind == "directory" ? ObjectTypeIds.FileDirectoryType : ObjectTypeIds.FileType
                        : kind == "derivedParent" ? derived : ObjectTypeIds.FileDirectoryType;
                }
                else if (request.BrowseDirection == BrowseDirection.Inverse)
                {
                    target = request.ReferenceTypeId == ReferenceTypeIds.HasSubtype
                        ? ObjectTypeIds.FileDirectoryType : kind == "orphan" ? NodeId.Null : parent;
                }
                return ValueTask.FromResult(new BrowseResponse
                {
                    Results = [new BrowseResult
                    {
                        References = target.IsNull ? [] : [new ReferenceDescription { NodeId = target }]
                    }]
                });
            };
            await using var plugin = new FileSystemPlugin(context.Host);
            var state = new FileSystemState();
            state.Roots.Add(new FileSystemState.RootSpec { NodeId = root.ToString(), DisplayName = "Saved files" });
            await plugin.RestoreStateAsync(JsonSerializer.SerializeToElement(
                state, FileSystemStateJsonContext.Default.FileSystemState)).ConfigureAwait(true);
            Assert.That(plugin.Roots, Is.Empty, "Restoring intent must not contact the server.");
            await plugin.OnConnectionStateChangedAsync(CancellationToken.None).ConfigureAwait(true);
            Assert.That(plugin.Roots.Any(node => node.Name == "Server.FileSystem"), Is.True);
            Assert.That(plugin.Roots.Any(node => node.Name == "Saved files"), Is.EqualTo(kind != "orphan"));
            FileSystemState captured = plugin.CaptureState().Deserialize(
                FileSystemStateJsonContext.Default.FileSystemState)!;
            Assert.That(captured.Roots, Has.Count.EqualTo(kind == "orphan" ? 0 : 1));
            if (kind != "orphan")
            {
                Assert.That(captured.Roots[0].NodeId, Is.EqualTo(root.ToString()));
                Assert.That(captured.Roots[0].DisplayName, Is.EqualTo("Saved files"));
            }
            int queries = typeQueries;
            await plugin.OnConnectionStateChangedAsync(CancellationToken.None).ConfigureAwait(true);
            Assert.That(typeQueries, Is.EqualTo(queries));
            await context.Desktop.Connection.DisconnectAsync().ConfigureAwait(true);
            await plugin.OnConnectionStateChangedAsync(CancellationToken.None).ConfigureAwait(true);
            Assert.That(plugin.Roots, Is.Empty);
            Assert.That(plugin.SelectedNode, Is.Null);
        });
    }
}
