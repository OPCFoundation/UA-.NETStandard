/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.XRegistry.Tests
{
    /// <summary>
    /// Verifies the <c>FileType</c> <c>Open</c> mode bits (OPC 10000-5 §C): Read = 1, Write = 2,
    /// EraseExisting = 4, Append = 8. The combinations the standard rejects have to be rejected, and
    /// a write that does not erase must start from the document already stored rather than from an
    /// empty buffer — otherwise a partial rewrite silently truncates the rest of it.
    /// </summary>
    [TestFixture]
    [Category("XRegistry")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class XRegistryFileModeTests
    {
        [TestCase((byte)0, TestName = "NeitherReadNorWrite")]
        [TestCase((byte)4, TestName = "EraseExistingWithoutWrite")]
        [TestCase((byte)8, TestName = "AppendWithoutReadOrWrite")]
        [TestCase((byte)5, TestName = "EraseExistingWithReadOnly")]
        [TestCase((byte)17, TestName = "ReservedModeBit")]
        public async Task InvalidOpenModesAreRejectedAsync(byte mode)
        {
            using XRegistryRegistrationNodeManager nm = await CreateAddressSpaceAsync()
                .ConfigureAwait(false);
            ResourceState resource = await CreateResourceAsync(nm).ConfigureAwait(false);

            OpenMethodStateResult opened = await resource.Open!.OnCallAsync!(
                nm.SystemContext, resource.Open, resource.NodeId, mode, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(opened.ServiceResult.StatusCode.Code,
                Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public async Task ReadWriteModeSharesTheCursorAndReadsStagedBytesAsync()
        {
            using XRegistryRegistrationNodeManager nm = await CreateAddressSpaceAsync()
                .ConfigureAwait(false);
            ResourceState resource = await CreateResourceAsync(nm).ConfigureAwait(false);
            await CommitAsync(nm, resource, [1, 2, 3, 4]).ConfigureAwait(false);

            OpenMethodStateResult opened = await resource.Open!.OnCallAsync!(
                nm.SystemContext, resource.Open, resource.NodeId, kReadMode | kWriteMode,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(opened.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.Good));

            WriteMethodStateResult written = await resource.Write!.OnCallAsync!(
                nm.SystemContext, resource.Write, resource.NodeId, opened.FileHandle,
                ByteString.From([9, 8]), CancellationToken.None).ConfigureAwait(false);
            GetPositionMethodStateResult position = await resource.GetPosition!.OnCallAsync!(
                nm.SystemContext, resource.GetPosition, resource.NodeId, opened.FileHandle,
                CancellationToken.None).ConfigureAwait(false);
            SetPositionMethodStateResult rewound = await resource.SetPosition!.OnCallAsync!(
                nm.SystemContext, resource.SetPosition, resource.NodeId, opened.FileHandle, 0,
                CancellationToken.None).ConfigureAwait(false);
            ReadMethodStateResult read = await resource.Read!.OnCallAsync!(
                nm.SystemContext, resource.Read, resource.NodeId, opened.FileHandle, 4,
                CancellationToken.None).ConfigureAwait(false);
            CloseMethodStateResult closed = await resource.Close!.OnCallAsync!(
                nm.SystemContext, resource.Close, resource.NodeId, opened.FileHandle,
                CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(written.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.Good));
                Assert.That(position.Position, Is.EqualTo(2UL));
                Assert.That(rewound.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.Good));
                Assert.That(read.Data.Span.ToArray(), Is.EqualTo(new byte[] { 9, 8, 3, 4 }));
                Assert.That(closed.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.Good));
            });
        }

        [TestCase(0)]
        [TestCase(-1)]
        public async Task NonpositiveReadLengthIsRejectedWithoutMovingTheCursorAsync(int length)
        {
            using XRegistryRegistrationNodeManager nm = await CreateAddressSpaceAsync()
                .ConfigureAwait(false);
            ResourceState resource = await CreateResourceAsync(nm).ConfigureAwait(false);
            await CommitAsync(nm, resource, [1, 2, 3]).ConfigureAwait(false);
            OpenMethodStateResult opened = await resource.Open!.OnCallAsync!(
                nm.SystemContext, resource.Open, resource.NodeId, kReadMode, CancellationToken.None)
                .ConfigureAwait(false);

            ReadMethodStateResult invalid = await resource.Read!.OnCallAsync!(
                nm.SystemContext, resource.Read, resource.NodeId, opened.FileHandle, length,
                CancellationToken.None).ConfigureAwait(false);
            ReadMethodStateResult valid = await resource.Read.OnCallAsync!(
                nm.SystemContext, resource.Read, resource.NodeId, opened.FileHandle, 3,
                CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(invalid.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidArgument));
                Assert.That(valid.Data, Is.EqualTo(ByteString.From([1, 2, 3])));
            });
        }

        [TestCase(false, false, TestName = "UnknownCursorHandleDoesNotAffectTheOwnerAsync")]
        [TestCase(true, false, TestName = "ClosedCursorHandleDoesNotAffectTheOwnerAsync")]
        [TestCase(false, true, TestName = "ForeignSessionCursorHandleDoesNotAffectTheOwnerAsync")]
        public async Task InvalidCursorHandlesReturnBadInvalidArgumentWithoutAffectingTheOwnerAsync(
            bool closeHandle,
            bool foreignSession)
        {
            using XRegistryRegistrationNodeManager nm = await CreateAddressSpaceAsync()
                .ConfigureAwait(false);
            ResourceState resource = await CreateResourceAsync(nm).ConfigureAwait(false);
            await CommitAsync(nm, resource, [1, 2, 3, 4]).ConfigureAwait(false);
            var ownerContext = (ServerSystemContext)nm.SystemContext.Copy();
            ownerContext.SessionId = new NodeId("cursor-owner", 1);
            var callerContext = (ServerSystemContext)ownerContext.Copy();
            if (foreignSession)
            {
                callerContext.SessionId = new NodeId("other-cursor-session", 1);
            }

            OpenMethodStateResult owner = await resource.Open!.OnCallAsync!(
                ownerContext, resource.Open, resource.NodeId, kReadMode, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(owner.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.Good));
            SetPositionMethodStateResult positioned = await resource.SetPosition!.OnCallAsync!(
                ownerContext, resource.SetPosition, resource.NodeId, owner.FileHandle, 1,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(positioned.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.Good));

            uint invalidHandle = foreignSession ? owner.FileHandle : uint.MaxValue;
            if (closeHandle)
            {
                OpenMethodStateResult retiring = await resource.Open.OnCallAsync!(
                    ownerContext, resource.Open, resource.NodeId, kReadMode, CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(retiring.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.Good));
                CloseMethodStateResult retired = await resource.Close!.OnCallAsync!(
                    ownerContext, resource.Close, resource.NodeId, retiring.FileHandle,
                    CancellationToken.None).ConfigureAwait(false);
                Assert.That(retired.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.Good));
                invalidHandle = retiring.FileHandle;
            }

            GetPositionMethodStateResult invalidGet = await resource.GetPosition!.OnCallAsync!(
                callerContext, resource.GetPosition, resource.NodeId, invalidHandle,
                CancellationToken.None).ConfigureAwait(false);
            SetPositionMethodStateResult invalidSet = await resource.SetPosition.OnCallAsync!(
                callerContext, resource.SetPosition, resource.NodeId, invalidHandle, 3,
                CancellationToken.None).ConfigureAwait(false);
            GetPositionMethodStateResult ownerPosition = await resource.GetPosition.OnCallAsync!(
                ownerContext, resource.GetPosition, resource.NodeId, owner.FileHandle,
                CancellationToken.None).ConfigureAwait(false);
            ReadMethodStateResult read = await resource.Read!.OnCallAsync!(
                ownerContext, resource.Read, resource.NodeId, owner.FileHandle, 3,
                CancellationToken.None).ConfigureAwait(false);
            ushort openCount = resource.OpenCount!.Value;
            CloseMethodStateResult closed = await resource.Close!.OnCallAsync!(
                ownerContext, resource.Close, resource.NodeId, owner.FileHandle,
                CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(invalidGet.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidArgument));
                Assert.That(invalidSet.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidArgument));
                Assert.That(ownerPosition.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.Good));
                Assert.That(ownerPosition.Position, Is.EqualTo(1UL));
                Assert.That(read.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.Good));
                Assert.That(read.Data, Is.EqualTo(ByteString.From([2, 3, 4])));
                Assert.That(openCount, Is.EqualTo((ushort)1));
                Assert.That(closed.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.Good));
                Assert.That(resource.OpenCount.Value, Is.Zero);
            });
        }

        [TestCase((byte)1, 0UL)]
        [TestCase((byte)2, 0UL)]
        [TestCase((byte)3, 0UL)]
        [TestCase((byte)6, 0UL)]
        [TestCase((byte)7, 0UL)]
        [TestCase((byte)9, 4UL)]
        [TestCase((byte)10, 4UL)]
        [TestCase((byte)11, 4UL)]
        [TestCase((byte)14, 0UL)]
        [TestCase((byte)15, 0UL)]
        public async Task LegalModesUseTheSpecifiedInitialCursorAsync(byte mode, ulong expectedPosition)
        {
            using XRegistryRegistrationNodeManager nm = await CreateAddressSpaceAsync()
                .ConfigureAwait(false);
            ResourceState resource = await CreateResourceAsync(nm).ConfigureAwait(false);
            await CommitAsync(nm, resource, [1, 2, 3, 4]).ConfigureAwait(false);

            OpenMethodStateResult opened = await resource.Open!.OnCallAsync!(
                nm.SystemContext, resource.Open, resource.NodeId, mode, CancellationToken.None)
                .ConfigureAwait(false);
            GetPositionMethodStateResult position = await resource.GetPosition!.OnCallAsync!(
                nm.SystemContext, resource.GetPosition, resource.NodeId, opened.FileHandle,
                CancellationToken.None).ConfigureAwait(false);
            CloseMethodStateResult closed = await resource.Close!.OnCallAsync!(
                nm.SystemContext, resource.Close, resource.NodeId, opened.FileHandle,
                CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(opened.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.Good));
                Assert.That(position.Position, Is.EqualTo(expectedPosition));
                Assert.That(closed.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.Good));
            });
        }

        [TestCase((byte)1)]
        [TestCase((byte)3)]
        public async Task SeekingPastEofClampsAndRewindingReadsTheBeginningAsync(byte mode)
        {
            using XRegistryRegistrationNodeManager nm = await CreateAddressSpaceAsync()
                .ConfigureAwait(false);
            ResourceState resource = await CreateResourceAsync(nm).ConfigureAwait(false);
            await CommitAsync(nm, resource, [1, 2, 3, 4]).ConfigureAwait(false);
            OpenMethodStateResult opened = await resource.Open!.OnCallAsync!(
                nm.SystemContext, resource.Open, resource.NodeId, mode, CancellationToken.None)
                .ConfigureAwait(false);

            SetPositionMethodStateResult sought = await resource.SetPosition!.OnCallAsync!(
                nm.SystemContext, resource.SetPosition, resource.NodeId, opened.FileHandle, ulong.MaxValue,
                CancellationToken.None).ConfigureAwait(false);
            GetPositionMethodStateResult position = await resource.GetPosition!.OnCallAsync!(
                nm.SystemContext, resource.GetPosition, resource.NodeId, opened.FileHandle,
                CancellationToken.None).ConfigureAwait(false);
            ReadMethodStateResult eof = await resource.Read!.OnCallAsync!(
                nm.SystemContext, resource.Read, resource.NodeId, opened.FileHandle, 4,
                CancellationToken.None).ConfigureAwait(false);
            await resource.SetPosition.OnCallAsync!(
                nm.SystemContext, resource.SetPosition, resource.NodeId, opened.FileHandle, 0,
                CancellationToken.None).ConfigureAwait(false);
            ReadMethodStateResult beginning = await resource.Read.OnCallAsync!(
                nm.SystemContext, resource.Read, resource.NodeId, opened.FileHandle, 2,
                CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(sought.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.Good));
                Assert.That(position.Position, Is.EqualTo(4UL));
                Assert.That(eof.Data, Is.EqualTo(ByteString.Empty));
                Assert.That(beginning.Data, Is.EqualTo(ByteString.From([1, 2])));
            });
        }

        [Test]
        public async Task WriterCannotOpenWhileAReaderOwnsTheFileAsync()
        {
            using XRegistryRegistrationNodeManager nm = await CreateAddressSpaceAsync()
                .ConfigureAwait(false);
            ResourceState resource = await CreateResourceAsync(nm).ConfigureAwait(false);
            await CommitAsync(nm, resource, [1, 2, 3]).ConfigureAwait(false);

            OpenMethodStateResult reader = await resource.Open!.OnCallAsync!(
                nm.SystemContext, resource.Open, resource.NodeId, kReadMode, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(reader.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.Good));

            OpenMethodStateResult writer = await resource.Open.OnCallAsync!(
                nm.SystemContext, resource.Open, resource.NodeId,
                kWriteMode | kEraseExistingMode, CancellationToken.None).ConfigureAwait(false);

            Assert.That(writer.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.BadNotWritable));

            OpenMethodStateResult secondReader = await resource.Open.OnCallAsync!(
                nm.SystemContext, resource.Open, resource.NodeId, kReadMode, CancellationToken.None)
                .ConfigureAwait(false);
            await resource.Close!.OnCallAsync!(
                nm.SystemContext, resource.Close, resource.NodeId, reader.FileHandle,
                CancellationToken.None).ConfigureAwait(false);
            OpenMethodStateResult stillBlocked = await resource.Open.OnCallAsync!(
                nm.SystemContext, resource.Open, resource.NodeId, kWriteMode, CancellationToken.None)
                .ConfigureAwait(false);
            await resource.Close.OnCallAsync!(
                nm.SystemContext, resource.Close, resource.NodeId, secondReader.FileHandle,
                CancellationToken.None).ConfigureAwait(false);
            OpenMethodStateResult available = await resource.Open.OnCallAsync!(
                nm.SystemContext, resource.Open, resource.NodeId, kWriteMode, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(secondReader.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.Good));
                Assert.That(stillBlocked.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.BadNotWritable));
                Assert.That(available.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.Good));
            });
        }

        [Test]
        public async Task WriteWithoutEraseExistingPreservesTheRestOfTheDocumentAsync()
        {
            using XRegistryRegistrationNodeManager nm = await CreateAddressSpaceAsync()
                .ConfigureAwait(false);
            ResourceState resource = await CreateResourceAsync(nm).ConfigureAwait(false);
            await CommitAsync(nm, resource, [1, 2, 3, 4, 5, 6]).ConfigureAwait(false);

            // Open for writing without EraseExisting and overwrite only the first two bytes.
            OpenMethodStateResult opened = await resource.Open!.OnCallAsync!(
                nm.SystemContext, resource.Open, resource.NodeId, kWriteMode, CancellationToken.None)
                .ConfigureAwait(false);
            await resource.Write!.OnCallAsync!(
                nm.SystemContext, resource.Write, resource.NodeId, opened.FileHandle,
                ByteString.From([9, 9]), CancellationToken.None).ConfigureAwait(false);
            await resource.Close!.OnCallAsync!(
                nm.SystemContext, resource.Close, resource.NodeId, opened.FileHandle,
                CancellationToken.None).ConfigureAwait(false);

            byte[] document = await ReadWholeDocumentAsync(nm, resource).ConfigureAwait(false);
            Assert.That(document, Is.EqualTo(new byte[] { 9, 9, 3, 4, 5, 6 }),
                "A write that does not erase replaces only the bytes it covers.");
        }

        [Test]
        public async Task WriteWithEraseExistingReplacesTheDocumentAsync()
        {
            using XRegistryRegistrationNodeManager nm = await CreateAddressSpaceAsync()
                .ConfigureAwait(false);
            ResourceState resource = await CreateResourceAsync(nm).ConfigureAwait(false);
            await CommitAsync(nm, resource, [1, 2, 3, 4, 5, 6]).ConfigureAwait(false);

            OpenMethodStateResult opened = await resource.Open!.OnCallAsync!(
                nm.SystemContext, resource.Open, resource.NodeId,
                kWriteMode | kEraseExistingMode, CancellationToken.None).ConfigureAwait(false);
            await resource.Write!.OnCallAsync!(
                nm.SystemContext, resource.Write, resource.NodeId, opened.FileHandle,
                ByteString.From([9, 9]), CancellationToken.None).ConfigureAwait(false);
            await resource.Close!.OnCallAsync!(
                nm.SystemContext, resource.Close, resource.NodeId, opened.FileHandle,
                CancellationToken.None).ConfigureAwait(false);

            byte[] document = await ReadWholeDocumentAsync(nm, resource).ConfigureAwait(false);
            Assert.That(document, Is.EqualTo("\t\t"u8.ToArray()));
        }

        [Test]
        public async Task FailedReplacementPreservesCommittedBytesAndEpochAsync()
        {
            using var files = new VirtualFileSystem();
            var fileSystem = new Mock<IFileSystem>();
            bool failWrite = false;
            fileSystem.Setup(f => f.Exists(It.IsAny<string>(), false))
                .Returns((string path, bool _) => files.Exists(path));
            fileSystem.Setup(f => f.OpenRead(It.IsAny<string>()))
                .Returns((string path) => files.OpenRead(path));
            fileSystem.Setup(f => f.OpenWrite(It.IsAny<string>()))
                .Returns((string path) => failWrite
                    ? throw new ServiceResultException(StatusCodes.BadResourceUnavailable)
                    : files.OpenWrite(path));
            fileSystem.Setup(f => f.Delete(It.IsAny<string>(), false))
                .Callback((string path, bool _) => files.Delete(path));
            fileSystem.Setup(f => f.GetLength(It.IsAny<string>()))
                .Returns((string path) => files.GetLength(path));
            fileSystem.Setup(f => f.Replace(It.IsAny<string>(), It.IsAny<string>()))
                .Callback((string source, string destination) => files.Replace(source, destination));
            using var store = new FileSystemResourceStore("resources", fileSystem.Object);
            using XRegistryRegistrationNodeManager nm = await CreateAddressSpaceAsync(store)
                .ConfigureAwait(false);
            ResourceState resource = await CreateResourceAsync(nm).ConfigureAwait(false);
            await CommitAsync(nm, resource, [1, 2, 3, 4]).ConfigureAwait(false);
            uint epoch = resource.Epoch!.Value;
            DateTimeUtc modified = resource.ModifiedAt!.Value;

            OpenMethodStateResult opened = await resource.Open!.OnCallAsync!(
                nm.SystemContext, resource.Open, resource.NodeId, kWriteMode | kEraseExistingMode,
                CancellationToken.None).ConfigureAwait(false);
            await resource.Write!.OnCallAsync!(
                nm.SystemContext, resource.Write, resource.NodeId, opened.FileHandle,
                ByteString.From([9, 8]), CancellationToken.None).ConfigureAwait(false);
            failWrite = true;

            Assert.That(() => resource.Close!.OnCallAsync!(
                nm.SystemContext, resource.Close, resource.NodeId, opened.FileHandle,
                CancellationToken.None).AsTask(), Throws.TypeOf<ServiceResultException>());
            byte[] document = await ReadWholeDocumentAsync(nm, resource).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(document, Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
                Assert.That(resource.Epoch.Value, Is.EqualTo(epoch));
                Assert.That(resource.ModifiedAt.Value, Is.EqualTo(modified));
                Assert.That(resource.Size!.Value, Is.EqualTo(4UL));
                Assert.That(resource.OpenCount!.Value, Is.Zero);
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task InitialUploadRetryAfterFailedWriteAndCleanupPublishesOnlyRetriedBytesAsync(
            bool retryDuringCleanupOutage)
        {
            var storage = new InMemoryResourceStore();
            Mock<IXRegistryResourceStore> store = CreateOffsetOnlyStore(storage);
            bool failWrite = true;
            bool cleanupUnavailable = false;
            store.Setup(s => s.WriteAsync(
                It.IsAny<string>(), It.IsAny<long>(), It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                .Returns(async (string key, long offset, ByteString data, CancellationToken ct) =>
                {
                    await storage.WriteAsync(key, offset, data, ct).ConfigureAwait(false);
                    if (failWrite)
                    {
                        cleanupUnavailable = true;
                        throw new ServiceResultException(StatusCodes.BadCommunicationError);
                    }
                });
            store.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string key, CancellationToken ct) => cleanupUnavailable
                    ? throw new ServiceResultException(StatusCodes.BadResourceUnavailable)
                    : storage.DeleteAsync(key, ct));
            using XRegistryRegistrationNodeManager nm = await CreateAddressSpaceAsync(store.Object)
                .ConfigureAwait(false);
            ResourceState resource = await CreateResourceAsync(nm).ConfigureAwait(false);
            string storeKey = resource.NodeId.ToString()!;
            string xid = resource.Xid!.Value;
            uint epoch = resource.Epoch!.Value;
            ushort ns = (ushort)nm.SystemContext.NamespaceUris.GetIndex(
                XRegistryWellKnown.XRegistryNamespaceUri);
            var failedContentId = new NodeId(ByteString.From([1, 2, 3, 4]), ns);
            var retriedContentId = new NodeId(ByteString.From([9, 8]), ns);

            await Assert.ThatAsync(async () =>
                await CommitAsync(nm, resource, [1, 2, 3, 4]).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadResourceUnavailable)).ConfigureAwait(false);
            ByteString residual = await store.Object.ReadAsync(storeKey, 0, int.MaxValue)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(residual, Is.EqualTo(ByteString.From([1, 2, 3, 4])));
                Assert.That(resource.Size!.Value, Is.Zero);
                Assert.That(resource.Epoch.Value, Is.EqualTo(epoch));
                Assert.That(resource.OpenCount!.Value, Is.Zero);
                Assert.That(nm.Find(failedContentId), Is.Null);
                Assert.That(nm.Find(retriedContentId), Is.Null);
            });

            failWrite = false;
            if (retryDuringCleanupOutage)
            {
                await Assert.ThatAsync(async () =>
                    await CommitAsync(nm, resource, [9, 8]).ConfigureAwait(false),
                    Throws.TypeOf<ServiceResultException>()
                        .With.Property(nameof(ServiceResultException.StatusCode))
                        .EqualTo(StatusCodes.BadResourceUnavailable)).ConfigureAwait(false);
                ByteString unchanged = await store.Object.ReadAsync(storeKey, 0, int.MaxValue)
                    .ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(unchanged, Is.EqualTo(ByteString.From([1, 2, 3, 4])));
                    Assert.That(resource.Size!.Value, Is.Zero);
                    Assert.That(resource.Epoch.Value, Is.EqualTo(epoch));
                    Assert.That(resource.OpenCount!.Value, Is.Zero);
                    Assert.That(nm.Find(failedContentId), Is.Null);
                    Assert.That(nm.Find(retriedContentId), Is.Null);
                });
            }
            cleanupUnavailable = false;
            OpenMethodStateResult opened = await resource.Open!.OnCallAsync!(
                nm.SystemContext, resource.Open, resource.NodeId, kWriteMode | kEraseExistingMode,
                CancellationToken.None).ConfigureAwait(false);
            WriteMethodStateResult written = await resource.Write!.OnCallAsync!(
                nm.SystemContext, resource.Write, resource.NodeId, opened.FileHandle,
                ByteString.From([9, 8]), CancellationToken.None).ConfigureAwait(false);
            CloseMethodStateResult closed = await resource.Close!.OnCallAsync!(
                nm.SystemContext, resource.Close, resource.NodeId, opened.FileHandle,
                CancellationToken.None).ConfigureAwait(false);
            ByteString stored = await store.Object.ReadAsync(storeKey, 0, int.MaxValue)
                .ConfigureAwait(false);
            long storedLength = await store.Object.GetLengthAsync(storeKey).ConfigureAwait(false);
            byte[] document = await ReadWholeDocumentAsync(nm, resource).ConfigureAwait(false);
            var fastPath = (BaseDataVariableState?)nm.Find(retriedContentId);

            Assert.That(fastPath, Is.Not.Null);
            Assert.That(fastPath!.Value.TryGetValue(out ByteString canonical), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(opened.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.Good));
                Assert.That(written.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.Good));
                Assert.That(closed.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.Good));
                Assert.That(stored, Is.EqualTo(ByteString.From([9, 8])));
                Assert.That(storedLength, Is.EqualTo(2L));
                Assert.That(document, Is.EqualTo(new byte[] { 9, 8 }));
                Assert.That(canonical, Is.EqualTo(ByteString.From([9, 8])));
                Assert.That(fastPath.NodeId, Is.EqualTo(retriedContentId));
                Assert.That(resource.Size!.Value, Is.EqualTo(2UL));
                Assert.That(resource.Epoch.Value, Is.EqualTo(epoch + 1));
                Assert.That(resource.Xid.Value, Is.EqualTo(xid));
                Assert.That(resource.OpenCount!.Value, Is.Zero);
                Assert.That(nm.Find(failedContentId), Is.Null);
            });
        }

        [Test]
        public async Task OffsetOnlyStoreCannotDeleteOrReplaceCommittedContentAsync()
        {
            var storage = new InMemoryResourceStore();
            Mock<IXRegistryResourceStore> store = CreateOffsetOnlyStore(storage);
            using XRegistryRegistrationNodeManager nm = await CreateAddressSpaceAsync(store.Object)
                .ConfigureAwait(false);
            ResourceState resource = await CreateResourceAsync(nm).ConfigureAwait(false);
            await CommitAsync(nm, resource, [1, 2, 3, 4]).ConfigureAwait(false);
            uint epoch = resource.Epoch!.Value;
            DateTimeUtc modified = resource.ModifiedAt!.Value;
            string xid = resource.Xid!.Value;
            ushort ns = (ushort)nm.SystemContext.NamespaceUris.GetIndex(
                XRegistryWellKnown.XRegistryNamespaceUri);
            store.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Throws(new ServiceResultException(StatusCodes.BadResourceUnavailable));

            OpenMethodStateResult opened = await resource.Open!.OnCallAsync!(
                nm.SystemContext, resource.Open, resource.NodeId, kWriteMode | kEraseExistingMode,
                CancellationToken.None).ConfigureAwait(false);
            WriteMethodStateResult written = await resource.Write!.OnCallAsync!(
                nm.SystemContext, resource.Write, resource.NodeId, opened.FileHandle,
                ByteString.From([9, 8]), CancellationToken.None).ConfigureAwait(false);
            CloseMethodStateResult closed = await resource.Close!.OnCallAsync!(
                nm.SystemContext, resource.Close, resource.NodeId, opened.FileHandle,
                CancellationToken.None).ConfigureAwait(false);
            byte[] document = await ReadWholeDocumentAsync(nm, resource).ConfigureAwait(false);
            ByteString stored = await store.Object.ReadAsync(resource.NodeId.ToString()!, 0, int.MaxValue)
                .ConfigureAwait(false);
            var fastPath = (BaseDataVariableState?)nm.Find(new NodeId(ByteString.From([1, 2, 3, 4]), ns));

            Assert.That(fastPath, Is.Not.Null);
            Assert.That(fastPath!.Value.TryGetValue(out ByteString canonical), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(opened.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.Good));
                Assert.That(written.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.Good));
                Assert.That(closed.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.BadNotSupported));
                Assert.That(document, Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
                Assert.That(stored, Is.EqualTo(ByteString.From([1, 2, 3, 4])));
                Assert.That(canonical, Is.EqualTo(ByteString.From([1, 2, 3, 4])));
                Assert.That(resource.Size!.Value, Is.EqualTo(4UL));
                Assert.That(resource.Epoch.Value, Is.EqualTo(epoch));
                Assert.That(resource.ModifiedAt.Value, Is.EqualTo(modified));
                Assert.That(resource.Xid.Value, Is.EqualTo(xid));
                Assert.That(resource.OpenCount!.Value, Is.Zero);
                Assert.That(nm.Find(new NodeId(ByteString.From([9, 8]), ns)), Is.Null);
            });
        }

        [Test]
        public async Task WriteWithAppendAddsToTheEndAsync()
        {
            using XRegistryRegistrationNodeManager nm = await CreateAddressSpaceAsync()
                .ConfigureAwait(false);
            ResourceState resource = await CreateResourceAsync(nm).ConfigureAwait(false);
            await CommitAsync(nm, resource, [1, 2, 3]).ConfigureAwait(false);

            OpenMethodStateResult opened = await resource.Open!.OnCallAsync!(
                nm.SystemContext, resource.Open, resource.NodeId,
                kWriteMode | kAppendMode, CancellationToken.None).ConfigureAwait(false);
            await resource.Write!.OnCallAsync!(
                nm.SystemContext, resource.Write, resource.NodeId, opened.FileHandle,
                ByteString.From([4, 5]), CancellationToken.None).ConfigureAwait(false);
            await resource.Close!.OnCallAsync!(
                nm.SystemContext, resource.Close, resource.NodeId, opened.FileHandle,
                CancellationToken.None).ConfigureAwait(false);

            byte[] document = await ReadWholeDocumentAsync(nm, resource).ConfigureAwait(false);
            Assert.That(document, Is.EqualTo(new byte[] { 1, 2, 3, 4, 5 }));
        }

        [Test]
        public async Task OpenCountAndSizeTrackTheFileAsync()
        {
            using XRegistryRegistrationNodeManager nm = await CreateAddressSpaceAsync()
                .ConfigureAwait(false);
            ResourceState resource = await CreateResourceAsync(nm).ConfigureAwait(false);
            await CommitAsync(nm, resource, [1, 2, 3, 4]).ConfigureAwait(false);

            ulong sizeAfterCommit = resource.Size!.Value;
            OpenMethodStateResult opened = await resource.Open!.OnCallAsync!(
                nm.SystemContext, resource.Open, resource.NodeId, kReadMode, CancellationToken.None)
                .ConfigureAwait(false);
            ushort openWhileOpen = resource.OpenCount!.Value;
            await resource.Close!.OnCallAsync!(
                nm.SystemContext, resource.Close, resource.NodeId, opened.FileHandle,
                CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(sizeAfterCommit, Is.EqualTo(4UL));
                Assert.That(openWhileOpen, Is.EqualTo((ushort)1));
                Assert.That(resource.OpenCount!.Value, Is.Zero);
            });
        }

        private static async Task<byte[]> ReadWholeDocumentAsync(
            XRegistryRegistrationNodeManager nm,
            ResourceState resource)
        {
            OpenMethodStateResult opened = await resource.Open!.OnCallAsync!(
                nm.SystemContext, resource.Open, resource.NodeId, kReadMode, CancellationToken.None)
                .ConfigureAwait(false);
            ReadMethodStateResult read = await resource.Read!.OnCallAsync!(
                nm.SystemContext, resource.Read, resource.NodeId, opened.FileHandle, int.MaxValue,
                CancellationToken.None).ConfigureAwait(false);
            await resource.Close!.OnCallAsync!(
                nm.SystemContext, resource.Close, resource.NodeId, opened.FileHandle,
                CancellationToken.None).ConfigureAwait(false);
            return read.Data.Span.ToArray();
        }

        /// <summary>
        /// Streams <paramref name="document"/> through the handle the create returned and commits it.
        /// </summary>
        private static async Task CommitAsync(
            XRegistryRegistrationNodeManager nm,
            ResourceState resource,
            byte[] document)
        {
            OpenMethodStateResult opened = await resource.Open!.OnCallAsync!(
                nm.SystemContext, resource.Open, resource.NodeId,
                kWriteMode | kEraseExistingMode, CancellationToken.None).ConfigureAwait(false);
            await resource.Write!.OnCallAsync!(
                nm.SystemContext, resource.Write, resource.NodeId, opened.FileHandle,
                ByteString.From(document), CancellationToken.None).ConfigureAwait(false);
            await resource.Close!.OnCallAsync!(
                nm.SystemContext, resource.Close, resource.NodeId, opened.FileHandle,
                CancellationToken.None).ConfigureAwait(false);
        }

        private static async Task<ResourceState> CreateResourceAsync(
            XRegistryRegistrationNodeManager nm)
        {
            CreateGroupMethodStateResult group = await nm.OnCreateGroupAsync(
                nm.SystemContext, null!, NodeId.Null, "schemas", CancellationToken.None)
                .ConfigureAwait(false);
            CreateResourceMethodStateResult created = await nm.OnCreateResourceAsync(
                nm.SystemContext, null!, group.GroupNodeId, "urn:doc", "1", false,
                CancellationToken.None).ConfigureAwait(false);
            return (ResourceState)nm.Find(created.ResourceNodeId)!;
        }

        private static Mock<IXRegistryResourceStore> CreateOffsetOnlyStore(InMemoryResourceStore storage)
        {
            var store = new Mock<IXRegistryResourceStore>();
            store.Setup(s => s.ReadAsync(
                It.IsAny<string>(), It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns((string key, long offset, int count, CancellationToken ct) =>
                    storage.ReadAsync(key, offset, count, ct));
            store.Setup(s => s.WriteAsync(
                It.IsAny<string>(), It.IsAny<long>(), It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                .Returns((string key, long offset, ByteString data, CancellationToken ct) =>
                    storage.WriteAsync(key, offset, data, ct));
            store.Setup(s => s.GetLengthAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string key, CancellationToken ct) => storage.GetLengthAsync(key, ct));
            store.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string key, CancellationToken ct) => storage.DeleteAsync(key, ct));
            return store;
        }

        private static async Task<XRegistryRegistrationNodeManager> CreateAddressSpaceAsync(
            IXRegistryResourceStore? store = null)
        {
            var options = new XRegistryServerOptions
            {
                ContentIdProvider = new XRegistryServerTestHarness.FakeContentIdProvider(),
                ResourceStore = store ?? new InMemoryResourceStore()
            };
            Mock<IServerInternal> server =
                XRegistryServerTestHarness.CreateServer(options.RegistryNamespaceUri);
            var nm = new XRegistryRegistrationNodeManager(server.Object, null!, options);
            await nm.CreateAddressSpaceAsync(
                new Dictionary<NodeId, IList<IReference>>(),
                CancellationToken.None).ConfigureAwait(false);
            return nm;
        }

        private const byte kReadMode = 1;
        private const byte kWriteMode = 2;
        private const byte kEraseExistingMode = 4;
        private const byte kAppendMode = 8;
    }
}
