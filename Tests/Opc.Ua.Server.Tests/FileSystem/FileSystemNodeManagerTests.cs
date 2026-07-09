/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.FileSystem;
using Opc.Ua.Server.Tests.NodeManager;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.FileSystem
{
    /// <summary>
    /// Deterministic offline tests for <see cref="FileSystemNodeManager"/> and
    /// <see cref="FileSystemNodeManagerFactory"/> using a mocked server and a
    /// physical provider rooted at a temp directory.
    /// </summary>
    [TestFixture]
    [Category("FileSystem")]
    public class FileSystemNodeManagerTests
    {
        private string m_root = null!;
        private ITelemetryContext m_telemetry = null!;

        [SetUp]
        public void SetUp()
        {
            m_root = Path.Combine(Path.GetTempPath(), "fsnm-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_root);
            m_telemetry = NUnitTelemetryContext.Create();
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(m_root))
                {
                    Directory.Delete(m_root, recursive: true);
                }
            }
            catch (IOException)
            {
                // best-effort
            }
        }

        private FileSystemNodeManager CreateManager(out PhysicalFileSystemProvider provider)
        {
            Mock<IServerInternal> mockServer = DeterministicServerMock.Create(out _);
            mockServer.Setup(s => s.Telemetry).Returns(m_telemetry);
            provider = new PhysicalFileSystemProvider(m_root, "TestMount");
            var config = new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration
                {
                    MaxNotificationQueueSize = 100,
                    MaxDurableNotificationQueueSize = 200
                }
            };
            return new FileSystemNodeManager(mockServer.Object, config, provider);
        }

        [Test]
        public void ConstructorWithNullProviderThrows()
        {
            Mock<IServerInternal> mockServer = DeterministicServerMock.Create(out _);
            mockServer.Setup(s => s.Telemetry).Returns(m_telemetry);

            Assert.That(
                () => new FileSystemNodeManager(mockServer.Object, new ApplicationConfiguration(), null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void ConstructorWithEmptyMountNameThrows()
        {
            Mock<IServerInternal> mockServer = DeterministicServerMock.Create(out _);
            mockServer.Setup(s => s.Telemetry).Returns(m_telemetry);
            var provider = new EmptyMountProvider();

            Assert.That(
                () => new FileSystemNodeManager(mockServer.Object, new ApplicationConfiguration(), provider),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void ProviderAndNamespaceIndexAreExposed()
        {
            using FileSystemNodeManager manager = CreateManager(out PhysicalFileSystemProvider provider);

            Assert.That(manager.Provider, Is.SameAs(provider));
            Assert.That(manager.NamespaceIndex, Is.GreaterThan((ushort)0));
        }

        [Test]
        public void NewReturnsNullForNullNode()
        {
            using FileSystemNodeManager manager = CreateManager(out _);

            NodeId result = manager.New(manager.SystemContext, null!);

            Assert.That(result, Is.EqualTo(NodeId.Null));
        }

        [Test]
        public async Task CreateAddressSpaceAddsRootReferenceAsync()
        {
            using FileSystemNodeManager manager = CreateManager(out _);
            var externalReferences = new Dictionary<NodeId, IList<IReference>>();

            await manager.CreateAddressSpaceAsync(externalReferences, CancellationToken.None);

            Assert.That(externalReferences.ContainsKey(ObjectIds.FileSystem), Is.True);
            Assert.That(externalReferences[ObjectIds.FileSystem], Has.Count.EqualTo(1));
            Assert.That(
                externalReferences[ObjectIds.FileSystem][0].ReferenceTypeId,
                Is.EqualTo(ReferenceTypeIds.HasComponent));
        }

        [Test]
        public void GetParentNodeIdReturnsNullForRootPath()
        {
            using FileSystemNodeManager manager = CreateManager(out _);

            Assert.That(manager.GetParentNodeId(string.Empty), Is.EqualTo(NodeId.Null));
        }

        [Test]
        public void GetParentNodeIdReturnsRootForTopLevelEntry()
        {
            using FileSystemNodeManager manager = CreateManager(out _);

            NodeId parent = manager.GetParentNodeId("file.txt");

            Assert.That(FileSystemNodeId.TryParse(parent, out FileSystemNodeId parsed), Is.True);
            Assert.That(parsed.RootType, Is.EqualTo(FileSystemNodeId.Root));
        }

        [Test]
        public void GetParentNodeIdReturnsDirectoryForNestedEntry()
        {
            using FileSystemNodeManager manager = CreateManager(out _);

            NodeId parent = manager.GetParentNodeId("folder/sub/file.txt");

            Assert.That(FileSystemNodeId.TryParse(parent, out FileSystemNodeId parsed), Is.True);
            Assert.That(parsed.RootType, Is.EqualTo(FileSystemNodeId.Directory));
            Assert.That(parsed.ProviderPath, Is.EqualTo("folder/sub"));
        }

        [Test]
        public void CombineProviderPathHandlesEmptyParent()
        {
            using FileSystemNodeManager manager = CreateManager(out _);

            Assert.That(manager.CombineProviderPath(string.Empty, "a"), Is.EqualTo("a"));
        }

        [Test]
        public void CombineProviderPathJoinsWithSlash()
        {
            using FileSystemNodeManager manager = CreateManager(out _);

            Assert.That(manager.CombineProviderPath("folder", "b"), Is.EqualTo("folder/b"));
            Assert.That(manager.CombineProviderPath("folder/", "b"), Is.EqualTo("folder/b"));
        }

        [Test]
        public void GetOrCreateHandleReturnsSameHandleForSameNodeId()
        {
            using FileSystemNodeManager manager = CreateManager(out _);
            NodeId nodeId = FileSystemNodeId.BuildFile("f.txt", manager.NamespaceIndex);

            FileHandle? first = manager.GetOrCreateHandle(nodeId, "f.txt");
            FileHandle? second = manager.GetOrCreateHandle(nodeId, "f.txt");

            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.SameAs(first));
        }

        [Test]
        public void ForgetHandleRemovesHandle()
        {
            using FileSystemNodeManager manager = CreateManager(out _);
            NodeId nodeId = FileSystemNodeId.BuildFile("f.txt", manager.NamespaceIndex);
            FileHandle? first = manager.GetOrCreateHandle(nodeId, "f.txt");

            manager.ForgetHandle(nodeId);
            FileHandle? afterForget = manager.GetOrCreateHandle(nodeId, "f.txt");

            Assert.That(afterForget, Is.Not.SameAs(first));
        }

        [Test]
        public void ForgetHandleForUnknownNodeIdIsNoOp()
        {
            using FileSystemNodeManager manager = CreateManager(out _);

            Assert.DoesNotThrow(
                () => manager.ForgetHandle(FileSystemNodeId.BuildFile("unknown", manager.NamespaceIndex)));
        }

        [Test]
        public async Task GetManagerHandleAsyncReturnsEmptyForNodesOutsideNamespaceAsync()
        {
            using FileSystemNodeManager manager = CreateManager(out _);

            NodeHandle? handle = await GetManagerHandleAsync(manager, new NodeId("1:file.txt", 999))
                .ConfigureAwait(false);

            Assert.That(handle, Is.Null);
        }

        [Test]
        public async Task GetManagerHandleAsyncReturnsParsedHandleForFileSystemNodeAsync()
        {
            using FileSystemNodeManager manager = CreateManager(out _);
            NodeId fileId = FileSystemNodeId.BuildFile("file.txt", manager.NamespaceIndex);

            NodeHandle? handle = await GetManagerHandleAsync(manager, fileId)
                .ConfigureAwait(false);

            Assert.That(handle, Is.Not.Null);
            Assert.That(handle!.NodeId, Is.EqualTo(fileId));
            Assert.That(handle.Validated, Is.False);
            Assert.That(handle.ParsedNodeId, Is.Not.Null);
        }

        [Test]
        public async Task GetManagerHandleAsyncReturnsEmptyForMalformedFileSystemNodeAsync()
        {
            using FileSystemNodeManager manager = CreateManager(out _);

            NodeHandle? handle = await GetManagerHandleAsync(manager, new NodeId("not-a-file-system-id", manager.NamespaceIndex))
                .ConfigureAwait(false);

            Assert.That(handle, Is.Null);
        }

        [Test]
        public async Task ValidateNodeAsyncCreatesDirectoryAndFileStatesAsync()
        {
            Directory.CreateDirectory(Path.Combine(m_root, "folder"));
            File.WriteAllText(Path.Combine(m_root, "file.txt"), "content");
            using FileSystemNodeManager manager = CreateManager(out _);

            NodeHandle? rootHandle = await GetManagerHandleAsync(
                manager,
                FileSystemNodeId.BuildRoot(manager.NamespaceIndex))
                .ConfigureAwait(false);
            NodeHandle? directoryHandle = await GetManagerHandleAsync(
                manager,
                FileSystemNodeId.BuildDirectory("folder", manager.NamespaceIndex))
                .ConfigureAwait(false);
            NodeHandle? fileHandle = await GetManagerHandleAsync(
                manager,
                FileSystemNodeId.BuildFile("file.txt", manager.NamespaceIndex))
                .ConfigureAwait(false);

            NodeState? root = await ValidateNodeAsync(manager, rootHandle!, new Dictionary<NodeId, NodeState>())
                .ConfigureAwait(false);
            NodeState? directory = await ValidateNodeAsync(
                manager,
                directoryHandle!,
                new Dictionary<NodeId, NodeState>())
                .ConfigureAwait(false);
            NodeState? file = await ValidateNodeAsync(manager, fileHandle!, new Dictionary<NodeId, NodeState>())
                .ConfigureAwait(false);

            Assert.That(root, Is.TypeOf<DirectoryObjectState>());
            Assert.That(directory, Is.TypeOf<DirectoryObjectState>());
            Assert.That(file, Is.TypeOf<FileObjectState>());
            Assert.That(rootHandle!.Validated, Is.True);
            Assert.That(directoryHandle!.Validated, Is.True);
            Assert.That(fileHandle!.Validated, Is.True);
        }

        [Test]
        public async Task ValidateNodeAsyncReturnsCachedNodeAndNullCacheEntriesAsync()
        {
            using FileSystemNodeManager manager = CreateManager(out _);
            NodeId fileId = FileSystemNodeId.BuildFile("file.txt", manager.NamespaceIndex);
            NodeHandle? cachedHandle = await GetManagerHandleAsync(manager, fileId)
                .ConfigureAwait(false);
            var cachedNode = new BaseDataVariableState(parent: null)
            {
                NodeId = fileId,
                BrowseName = new QualifiedName("Cached", manager.NamespaceIndex)
            };
            var cache = new Dictionary<NodeId, NodeState> { [fileId] = cachedNode };

            NodeState? resolved = await ValidateNodeAsync(manager, cachedHandle!, cache)
                .ConfigureAwait(false);

            NodeHandle? nullHandle = await GetManagerHandleAsync(manager, fileId)
                .ConfigureAwait(false);
            cache[fileId] = null!;
            NodeState? nullResolved = await ValidateNodeAsync(manager, nullHandle!, cache)
                .ConfigureAwait(false);

            Assert.That(resolved, Is.SameAs(cachedNode));
            Assert.That(cachedHandle!.Validated, Is.True);
            Assert.That(nullResolved, Is.Null);
        }

        [Test]
        public async Task ValidateNodeAsyncReturnsNullForMissingEntryAndMissingComponentAsync()
        {
            File.WriteAllText(Path.Combine(m_root, "file.txt"), "content");
            using FileSystemNodeManager manager = CreateManager(out _);

            NodeHandle? missingEntryHandle = await GetManagerHandleAsync(
                manager,
                FileSystemNodeId.BuildFile("missing.txt", manager.NamespaceIndex))
                .ConfigureAwait(false);
            NodeId missingComponentId = new FileSystemNodeId(
                FileSystemNodeId.File,
                "file.txt",
                manager.NamespaceIndex,
                "MissingComponent").ToNodeId();
            NodeHandle? missingComponentHandle = await GetManagerHandleAsync(manager, missingComponentId)
                .ConfigureAwait(false);

            NodeState? missingEntry = await ValidateNodeAsync(
                manager,
                missingEntryHandle!,
                new Dictionary<NodeId, NodeState>())
                .ConfigureAwait(false);
            NodeState? missingComponent = await ValidateNodeAsync(
                manager,
                missingComponentHandle!,
                new Dictionary<NodeId, NodeState>())
                .ConfigureAwait(false);

            Assert.That(missingEntry, Is.Null);
            Assert.That(missingComponent, Is.Null);
        }

        [Test]
        public async Task DeleteAddressSpaceAsyncDisposesAndClearsHandlesAsync()
        {
            using FileSystemNodeManager manager = CreateManager(out _);
            NodeId nodeId = FileSystemNodeId.BuildFile("f.txt", manager.NamespaceIndex);
            FileHandle? first = manager.GetOrCreateHandle(nodeId, "f.txt");

            await manager.DeleteAddressSpaceAsync(CancellationToken.None)
                .ConfigureAwait(false);
            FileHandle? afterDelete = manager.GetOrCreateHandle(nodeId, "f.txt");

            Assert.That(afterDelete, Is.Not.SameAs(first));
        }

        [Test]
        public void FactoryWithNullProviderThrows()
        {
            Assert.That(
                () => new FileSystemNodeManagerFactory(null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void FactoryExposesNamespaceUri()
        {
            var provider = new PhysicalFileSystemProvider(m_root, "MyMount");
            var factory = new FileSystemNodeManagerFactory(provider);

            ArrayOf<string> namespaces = factory.NamespacesUris;

            Assert.That(namespaces, Has.Count.EqualTo(1));
            Assert.That(
                namespaces[0],
                Is.EqualTo(FileSystemNodeManager.NamespaceUriBase + "/MyMount"));
        }

        [Test]
        public void FactoryCreateReturnsSyncNodeManager()
        {
            Mock<IServerInternal> mockServer = DeterministicServerMock.Create(out _);
            mockServer.Setup(s => s.Telemetry).Returns(m_telemetry);
            var provider = new PhysicalFileSystemProvider(m_root, "MyMount");
            var factory = new FileSystemNodeManagerFactory(provider);

            INodeManager nodeManager = factory.Create(mockServer.Object, new ApplicationConfiguration());

            Assert.That(nodeManager, Is.Not.Null);
        }

        [Test]
        public async Task FactoryCreateAsyncReturnsAsyncNodeManagerAsync()
        {
            Mock<IServerInternal> mockServer = DeterministicServerMock.Create(out _);
            mockServer.Setup(s => s.Telemetry).Returns(m_telemetry);
            var provider = new PhysicalFileSystemProvider(m_root, "MyMount");
            var factory = new FileSystemNodeManagerFactory(provider);

            IAsyncNodeManager nodeManager = await factory.CreateAsync(
                mockServer.Object, new ApplicationConfiguration());

            Assert.That(nodeManager, Is.Not.Null);
        }

        private static async Task<NodeHandle?> GetManagerHandleAsync(
            FileSystemNodeManager manager,
            NodeId nodeId)
        {
            MethodInfo method = typeof(FileSystemNodeManager).GetMethod(
                "GetManagerHandleAsync",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            var cache = new Dictionary<NodeId, NodeState>();
            var result = (ValueTask<NodeHandle>)method.Invoke(
                manager,
                [manager.SystemContext, nodeId, cache, CancellationToken.None])!;
            return await result.ConfigureAwait(false);
        }

        private static async Task<NodeState?> ValidateNodeAsync(
            FileSystemNodeManager manager,
            NodeHandle handle,
            IDictionary<NodeId, NodeState> cache)
        {
            MethodInfo method = typeof(FileSystemNodeManager).GetMethod(
                "ValidateNodeAsync",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            var result = (ValueTask<NodeState>)method.Invoke(
                manager,
                [manager.SystemContext, handle, cache, CancellationToken.None])!;
            return await result.ConfigureAwait(false);
        }

        /// <summary>
        /// A provider whose MountName is empty, used to drive the
        /// namespace-URI validation guard in the node manager.
        /// </summary>
        private sealed class EmptyMountProvider : IFileSystemProvider
        {
            public string MountName => string.Empty;

            public bool IsWritable => false;

            public ValueTask<FileSystemEntry?> GetEntryAsync(string path, CancellationToken ct)
            {
                return new ValueTask<FileSystemEntry?>((FileSystemEntry?)null);
            }

            public async IAsyncEnumerable<FileSystemEntry> EnumerateAsync(
                string path,
                [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
            {
                await Task.CompletedTask.ConfigureAwait(false);
                yield break;
            }

            public ValueTask<Stream> OpenReadAsync(string path, CancellationToken ct)
            {
                throw new NotSupportedException();
            }

            public ValueTask<Stream> OpenWriteAsync(string path, FileWriteMode mode, CancellationToken ct)
            {
                throw new NotSupportedException();
            }

            public ValueTask CreateDirectoryAsync(string path, CancellationToken ct)
            {
                throw new NotSupportedException();
            }

            public ValueTask CreateFileAsync(string path, CancellationToken ct)
            {
                throw new NotSupportedException();
            }

            public ValueTask DeleteAsync(string path, CancellationToken ct)
            {
                throw new NotSupportedException();
            }

            public ValueTask MoveAsync(string source, string target, CancellationToken ct)
            {
                throw new NotSupportedException();
            }

            public ValueTask CopyAsync(string source, string target, CancellationToken ct)
            {
                throw new NotSupportedException();
            }
        }
    }
}
