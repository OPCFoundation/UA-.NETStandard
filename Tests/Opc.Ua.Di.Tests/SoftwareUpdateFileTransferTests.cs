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

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Di.Server.Builders;
using Opc.Ua.Di.Server.SoftwareUpdate;

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// Direct-invocation tests for the
    /// <c>PackageLoadingType.FileTransfer</c> wiring delivered in
    /// SU Phase 3. Exercises the OPC 10000-5 §11.4
    /// <c>GenerateFileForWrite</c> → file <c>Open</c> / <c>Write</c> /
    /// <c>Close</c> → <c>CloseAndCommit</c> flow against an in-proc
    /// fixture and asserts the uploaded payload lands in the
    /// <see cref="ISoftwarePackageStore"/>.
    /// </summary>
    [TestFixture]
    [Category("DI")]
    [Category("DeviceBuilder")]
    [Category("SoftwareUpdate")]
    [Category("FileTransfer")]
    public sealed class SoftwareUpdateFileTransferTests
    {
        private DiServerFixture m_fixture = null!;
        private MemoryPackageStore m_store = null!;

        [SetUp]
        public async Task SetUpAsync()
        {
            m_fixture = new DiServerFixture();
            await m_fixture.StartAsync().ConfigureAwait(false);
            m_store = new MemoryPackageStore();
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            await m_fixture.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task WithSoftwareUpdateMaterialisesFileTransferUnderPackageLoading()
        {
            (NodeState _, PackageLoadingState loading, _) =
                await CreateDeviceAsync("FtMaterialise").ConfigureAwait(false);

            Assert.That(loading.FileTransfer, Is.Not.Null,
                "Package loading must materialise the FileTransfer slot.");
            Assert.That(loading.FileTransfer!.GenerateFileForWrite, Is.Not.Null);
            Assert.That(loading.FileTransfer.CloseAndCommit, Is.Not.Null);
            Assert.That(loading.FileTransfer.GenerateFileForWrite!.OnCall,
                Is.Not.Null,
                "GenerateFileForWrite must have an OnCall handler.");
            Assert.That(loading.FileTransfer.CloseAndCommit!.OnCall,
                Is.Not.Null,
                "CloseAndCommit must have an OnCall handler.");
        }

        [Test]
        public async Task GenerateFileForWriteReturnsValidNodeIdAndHandle()
        {
            (NodeState _, PackageLoadingState loading, _) =
                await CreateDeviceAsync("FtGenerate").ConfigureAwait(false);

            (NodeId fileNodeId, uint fileHandle) = InvokeGenerateFileForWrite(
                loading.FileTransfer!, Variant.Null);

            Assert.That(fileNodeId.IsNull, Is.False);
            Assert.That(fileHandle, Is.GreaterThan(0u));
        }

        [Test]
        public async Task EndToEndUploadCommitsPayloadToPackageStore()
        {
            const string packageId = "acme-firmware-2.0";
            byte[] payload = MakeSequentialPayload(4096);

            (NodeState _, PackageLoadingState loading, _) =
                await CreateDeviceAsync("FtEndToEnd").ConfigureAwait(false);

            (NodeId fileNodeId, uint commitHandle) = InvokeGenerateFileForWrite(
                loading.FileTransfer!, new Variant(packageId));

            NodeState fileObject = ResolveNode(fileNodeId);
            uint openHandle = InvokeOpen(fileObject, mode: 6);

            // Write in two equal chunks to exercise the append path.
            InvokeWrite(fileObject, openHandle, payload.AsSpan(0, 2048).ToArray());
            InvokeWrite(fileObject, openHandle, payload.AsSpan(2048).ToArray());

            InvokeClose(fileObject, openHandle);
            NodeId completion = InvokeCloseAndCommit(
                loading.FileTransfer!, commitHandle);

            Assert.That(completion, Is.EqualTo(NodeId.Null),
                "CompletionStateMachine is not implemented in v1.");

            List<SoftwarePackage> packages = await m_store
                .ListAsync().ToListAsync().ConfigureAwait(false);
            Assert.That(packages, Has.Count.EqualTo(1));
            Assert.That(packages[0].Id, Is.EqualTo(packageId));
            Assert.That(packages[0].SizeBytes, Is.EqualTo(payload.LongLength));

            byte[] stored = await m_store
                .ReadAllAsync(packageId).ConfigureAwait(false);
            Assert.That(stored, Is.EqualTo(payload));
        }

        [Test]
        public async Task CloseAndCommitRemovesTransientFileFromAddressSpace()
        {
            (NodeState _, PackageLoadingState loading, _) =
                await CreateDeviceAsync("FtCleanup").ConfigureAwait(false);

            (NodeId fileNodeId, uint commitHandle) = InvokeGenerateFileForWrite(
                loading.FileTransfer!, Variant.Null);

            NodeState fileObject = ResolveNode(fileNodeId);
            uint openHandle = InvokeOpen(fileObject, mode: 6);
            InvokeClose(fileObject, openHandle);
            InvokeCloseAndCommit(loading.FileTransfer!, commitHandle);

            // After commit, the transient FileState must be gone from
            // the manager's predefined-node table.
            Assert.That(TryFindPredefinedNode(fileNodeId), Is.Null,
                "Transient upload FileState must be detached after commit.");
        }

        [Test]
        public async Task GenerateFileForWriteRejectsTooManyConcurrentHandles()
        {
            (NodeState _, PackageLoadingState loading, _) =
                await CreateDeviceAsync("FtHandleLimit").ConfigureAwait(false);

            // Cap is 8; allocate 8 and assert the 9th fails.
            for (int i = 0; i < 8; i++)
            {
                InvokeGenerateFileForWrite(
                    loading.FileTransfer!, Variant.Null);
            }

            ServiceResult result = TryInvokeGenerateFileForWrite(
                loading.FileTransfer!, Variant.Null);
            Assert.That(result.StatusCode,
                Is.EqualTo(StatusCodes.BadTooManyOperations));
        }

        [Test]
        public async Task OpenRejectsUnsupportedModes()
        {
            (NodeState _, PackageLoadingState loading, _) =
                await CreateDeviceAsync("FtBadMode").ConfigureAwait(false);

            (NodeId fileNodeId, _) = InvokeGenerateFileForWrite(
                loading.FileTransfer!, Variant.Null);
            NodeState fileObject = ResolveNode(fileNodeId);

            ServiceResult result = TryInvokeOpen(fileObject, mode: 1); // Read
            Assert.That(result.StatusCode,
                Is.EqualTo(StatusCodes.BadNotSupported));
        }

        [Test]
        public async Task CloseAndCommitReturnsBadInvalidArgumentForUnknownHandle()
        {
            (NodeState _, PackageLoadingState loading, _) =
                await CreateDeviceAsync("FtBadHandle").ConfigureAwait(false);

            ServiceResult result = TryInvokeCloseAndCommit(
                loading.FileTransfer!, fileHandle: 99u);
            Assert.That(result.StatusCode,
                Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public async Task ReadOnTransientUploadFileReturnsBadNotSupported()
        {
            (NodeState _, PackageLoadingState loading, _) =
                await CreateDeviceAsync("FtReadOnly").ConfigureAwait(false);

            (NodeId fileNodeId, _) = InvokeGenerateFileForWrite(
                loading.FileTransfer!, Variant.Null);
            NodeState fileObject = ResolveNode(fileNodeId);
            uint openHandle = InvokeOpen(fileObject, mode: 6);

            ServiceResult result = TryInvokeRead(fileObject, openHandle, length: 16);
            Assert.That(result.StatusCode,
                Is.EqualTo(StatusCodes.BadNotSupported));
        }

        // ------------------------------------------------------------------
        // setup helpers
        // ------------------------------------------------------------------

        private async Task<(NodeState Su, PackageLoadingState Loading, NodeState Device)>
            CreateDeviceAsync(string deviceName)
        {
            IDeviceBuilder<DeviceState> builder = await m_fixture.Manager
                .CreateDeviceAsync(new QualifiedName(
                    deviceName, m_fixture.Manager.DiNamespaceIndex))
                .ConfigureAwait(false);

            builder.WithSoftwareUpdate(m_store);

            ushort diNs = m_fixture.Manager.DiNamespaceIndex;
            ISystemContext ctx = m_fixture.Manager.SystemContext;
            NodeState su = builder.Device.FindChild(
                ctx, new QualifiedName("SoftwareUpdate", diNs))!;
            var loading = (PackageLoadingState)su.FindChild(
                ctx, new QualifiedName("Loading", diNs))!;
            return (su, loading, builder.Device);
        }

        // ------------------------------------------------------------------
        // method invocation helpers
        // ------------------------------------------------------------------

        private (NodeId FileNodeId, uint Handle) InvokeGenerateFileForWrite(
            TemporaryFileTransferState fileTransfer, Variant generateOptions)
        {
            NodeId fileNodeId = NodeId.Null;
            uint handle = 0;
            ServiceResult result = fileTransfer.GenerateFileForWrite!.OnCall!(
                m_fixture.Manager.SystemContext,
                fileTransfer.GenerateFileForWrite,
                fileTransfer.NodeId,
                generateOptions,
                ref fileNodeId,
                ref handle);
            Assert.That(result, Is.EqualTo(ServiceResult.Good),
                $"GenerateFileForWrite returned {result}.");
            return (fileNodeId, handle);
        }

        private ServiceResult TryInvokeGenerateFileForWrite(
            TemporaryFileTransferState fileTransfer, Variant generateOptions)
        {
            NodeId fileNodeId = NodeId.Null;
            uint handle = 0;
            return fileTransfer.GenerateFileForWrite!.OnCall!(
                m_fixture.Manager.SystemContext,
                fileTransfer.GenerateFileForWrite,
                fileTransfer.NodeId,
                generateOptions,
                ref fileNodeId,
                ref handle);
        }

        private NodeId InvokeCloseAndCommit(
            TemporaryFileTransferState fileTransfer, uint commitHandle)
        {
            NodeId completion = NodeId.Null;
            ServiceResult result = fileTransfer.CloseAndCommit!.OnCall!(
                m_fixture.Manager.SystemContext,
                fileTransfer.CloseAndCommit,
                fileTransfer.NodeId,
                commitHandle,
                ref completion);
            Assert.That(result, Is.EqualTo(ServiceResult.Good),
                $"CloseAndCommit returned {result}.");
            return completion;
        }

        private ServiceResult TryInvokeCloseAndCommit(
            TemporaryFileTransferState fileTransfer, uint fileHandle)
        {
            NodeId completion = NodeId.Null;
            return fileTransfer.CloseAndCommit!.OnCall!(
                m_fixture.Manager.SystemContext,
                fileTransfer.CloseAndCommit,
                fileTransfer.NodeId,
                fileHandle,
                ref completion);
        }

        private uint InvokeOpen(NodeState fileObject, byte mode)
        {
            var file = (FileState)fileObject;
            uint handle = 0;
            ServiceResult result = file.Open!.OnCall!(
                m_fixture.Manager.SystemContext,
                file.Open,
                file.NodeId,
                mode,
                ref handle);
            Assert.That(result, Is.EqualTo(ServiceResult.Good),
                $"Open returned {result}.");
            return handle;
        }

        private ServiceResult TryInvokeOpen(NodeState fileObject, byte mode)
        {
            var file = (FileState)fileObject;
            uint handle = 0;
            return file.Open!.OnCall!(
                m_fixture.Manager.SystemContext,
                file.Open,
                file.NodeId,
                mode,
                ref handle);
        }

        private void InvokeWrite(NodeState fileObject, uint fileHandle, byte[] data)
        {
            var file = (FileState)fileObject;
            ServiceResult result = file.Write!.OnCall!(
                m_fixture.Manager.SystemContext,
                file.Write,
                file.NodeId,
                fileHandle,
                ByteString.From(data));
            Assert.That(result, Is.EqualTo(ServiceResult.Good),
                $"Write returned {result}.");
        }

        private ServiceResult TryInvokeRead(
            NodeState fileObject, uint fileHandle, int length)
        {
            var file = (FileState)fileObject;
            ByteString data = default;
            return file.Read!.OnCall!(
                m_fixture.Manager.SystemContext,
                file.Read,
                file.NodeId,
                fileHandle,
                length,
                ref data);
        }

        private void InvokeClose(NodeState fileObject, uint fileHandle)
        {
            var file = (FileState)fileObject;
            ServiceResult result = file.Close!.OnCall!(
                m_fixture.Manager.SystemContext,
                file.Close,
                file.NodeId,
                fileHandle);
            Assert.That(result, Is.EqualTo(ServiceResult.Good),
                $"Close returned {result}.");
        }

        // ------------------------------------------------------------------
        // node lookup helpers
        // ------------------------------------------------------------------

        private NodeState ResolveNode(NodeId nodeId)
        {
            NodeState? node = TryFindPredefinedNode(nodeId);
            Assert.That(node, Is.Not.Null,
                $"Expected node {nodeId} to be registered with the manager.");
            return node!;
        }

        private NodeState? TryFindPredefinedNode(NodeId nodeId)
        {
            return m_fixture.Manager.FindPredefinedNode<NodeState>(nodeId);
        }

        // ------------------------------------------------------------------
        // payload helpers
        // ------------------------------------------------------------------

        private static byte[] MakeSequentialPayload(int length)
        {
            var bytes = new byte[length];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = (byte)(i & 0xFF);
            }
            return bytes;
        }
    }

    /// <summary>
    /// Test-only sugar around the <c>ISoftwarePackageStore</c> async
    /// surface so tests can read packages back as a byte buffer.
    /// </summary>
    internal static class PackageStoreTestExtensions
    {
        public static async Task<List<SoftwarePackage>> ToListAsync(
            this IAsyncEnumerable<SoftwarePackage> source)
        {
            var list = new List<SoftwarePackage>();
            await foreach (SoftwarePackage p in source.ConfigureAwait(false))
            {
                list.Add(p);
            }
            return list;
        }

        public static async Task<byte[]> ReadAllAsync(
            this ISoftwarePackageStore store, string packageId)
        {
            // 'using' (sync dispose) instead of 'await using' so the
            // assertion helper compiles on net48 where System.IO.Stream
            // does not implement IAsyncDisposable. The stream is in-
            // memory in tests so sync dispose is fine.
            using System.IO.Stream stream = await store
                .OpenReadAsync(packageId, CancellationToken.None)
                .ConfigureAwait(false);
            using var ms = new System.IO.MemoryStream();
            await stream.CopyToAsync(ms).ConfigureAwait(false);
            return ms.ToArray();
        }
    }
}
