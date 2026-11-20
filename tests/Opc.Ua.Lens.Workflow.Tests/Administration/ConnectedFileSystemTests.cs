/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
[Category("LensDesktopWorkflow")]
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
            bool parentAvailable = kind != "orphan";
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
                        ? ObjectTypeIds.FileDirectoryType : parentAvailable ? parent : NodeId.Null;
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
            Assert.That(captured.Roots, Has.Count.EqualTo(1), "Unreachable roots retain their saved configuration.");
            Assert.That(captured.Roots[0].NodeId, Is.EqualTo(root.ToString()));
            Assert.That(captured.Roots[0].DisplayName, Is.EqualTo("Saved files"));
            int queries = typeQueries;
            if (kind == "orphan")
            {
                await plugin.OnConnectionStateChangedAsync(CancellationToken.None).ConfigureAwait(true);
                Assert.That(typeQueries, Is.EqualTo(queries + 1), "Failed attachments must be retried.");
                Assert.That(plugin.Roots.Any(node => node.Name == "Saved files"), Is.False);
                parentAvailable = true;
                await plugin.OnConnectionStateChangedAsync(CancellationToken.None).ConfigureAwait(true);
                Assert.That(plugin.Roots.Count(node => node.Name == "Saved files"), Is.EqualTo(1));
                queries = typeQueries;
            }
            await plugin.OnConnectionStateChangedAsync(CancellationToken.None).ConfigureAwait(true);
            Assert.That(typeQueries, Is.EqualTo(queries));
            await context.Desktop.Connection.DisconnectAsync().ConfigureAwait(true);
            await plugin.OnConnectionStateChangedAsync(CancellationToken.None).ConfigureAwait(true);
            Assert.That(plugin.Roots, Is.Empty);
            Assert.That(plugin.SelectedNode, Is.Null);
            Assert.That(plugin.CaptureState().Deserialize(
                FileSystemStateJsonContext.Default.FileSystemState)!.Roots, Has.Count.EqualTo(1));
        });
    }
}
