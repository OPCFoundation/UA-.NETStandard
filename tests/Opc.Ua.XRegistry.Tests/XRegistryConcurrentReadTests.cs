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

using System;
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
    /// Verifies that a file handle's cursor stays consistent when two Reads on the same handle
    /// overlap, including short reads and failed storage operations.
    /// </summary>
    [TestFixture]
    [Category("XRegistry")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class XRegistryConcurrentReadTests
    {
        [Test]
        public async Task ConcurrentReadsOnOneHandleReturnDisjointSlicesAsync()
        {
            var store = new GatedResourceStore(blockFirstRead: true);
            using XRegistryRegistrationNodeManager nm = await CreateAddressSpaceAsync(store)
                .ConfigureAwait(false);
            (ResourceState resource, uint handle) = await OpenForReadAsync(nm, s_document)
                .ConfigureAwait(false);

            Task<ReadMethodStateResult> first = ReadAsync(nm, resource, handle, 4).AsTask();
            await store.FirstReadEntered.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Task<ReadMethodStateResult> second = ReadAsync(nm, resource, handle, 4).AsTask();
            store.ReleaseFirstRead();
            ReadMethodStateResult[] results = await Task.WhenAll(first, second).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(results[0].Data, Is.EqualTo(ByteString.From([1, 2, 3, 4])));
                Assert.That(results[1].Data, Is.EqualTo(ByteString.From([5, 6, 7, 8])));
            });
        }

        [Test]
        public async Task FailedConcurrentReadDoesNotSkipUnreadBytesAsync()
        {
            var store = new GatedResourceStore(blockFirstRead: true, failFirstRead: true);
            using XRegistryRegistrationNodeManager nm = await CreateAddressSpaceAsync(store)
                .ConfigureAwait(false);
            (ResourceState resource, uint handle) = await OpenForReadAsync(nm, s_document)
                .ConfigureAwait(false);

            Task<ReadMethodStateResult> first = ReadAsync(nm, resource, handle, 4).AsTask();
            await store.FirstReadEntered.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Task<ReadMethodStateResult> second = ReadAsync(nm, resource, handle, 4).AsTask();
            store.ReleaseFirstRead();

            Assert.That(() => first, Throws.TypeOf<ServiceResultException>());
            ReadMethodStateResult recovered = await second.ConfigureAwait(false);
            ReadMethodStateResult remainder = await ReadAsync(nm, resource, handle, 4).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(recovered.Data, Is.EqualTo(ByteString.From([1, 2, 3, 4])));
                Assert.That(remainder.Data, Is.EqualTo(ByteString.From([5, 6, 7, 8])));
            });
        }

        [Test]
        public async Task ConcurrentShortReadsCoverEveryByteInOrderAsync()
        {
            var store = new GatedResourceStore(blockFirstRead: true, maxReadLength: 2);
            using XRegistryRegistrationNodeManager nm = await CreateAddressSpaceAsync(store)
                .ConfigureAwait(false);
            (ResourceState resource, uint handle) = await OpenForReadAsync(nm, s_document)
                .ConfigureAwait(false);

            Task<ReadMethodStateResult> first = ReadAsync(nm, resource, handle, 8).AsTask();
            await store.FirstReadEntered.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Task<ReadMethodStateResult> second = ReadAsync(nm, resource, handle, 8).AsTask();
            store.ReleaseFirstRead();
            ReadMethodStateResult[] results = await Task.WhenAll(first, second).ConfigureAwait(false);
            ReadMethodStateResult third = await ReadAsync(nm, resource, handle, 8).ConfigureAwait(false);
            ReadMethodStateResult fourth = await ReadAsync(nm, resource, handle, 8).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(results[0].Data, Is.EqualTo(ByteString.From([1, 2])));
                Assert.That(results[1].Data, Is.EqualTo(ByteString.From([3, 4])));
                Assert.That(third.Data, Is.EqualTo(ByteString.From([5, 6])));
                Assert.That(fourth.Data, Is.EqualTo(ByteString.From([7, 8])));
            });
        }

        [Test]
        public async Task CancellingAQueuedReadDoesNotConsumeTheFollowingRangeAsync()
        {
            var store = new GatedResourceStore(blockFirstRead: true);
            using XRegistryRegistrationNodeManager nm = await CreateAddressSpaceAsync(store)
                .ConfigureAwait(false);
            (ResourceState resource, uint handle) = await OpenForReadAsync(nm, s_document)
                .ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();

            Task<ReadMethodStateResult> first = ReadAsync(nm, resource, handle, 4).AsTask();
            await store.FirstReadEntered.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Task<ReadMethodStateResult> cancelled = ReadAsync(nm, resource, handle, 4, cancellation.Token).AsTask();
            try
            {
                cancellation.Cancel();
                Assert.That(() => cancelled, Throws.InstanceOf<OperationCanceledException>());
            }
            finally
            {
                store.ReleaseFirstRead();
            }
            ReadMethodStateResult completed = await first.ConfigureAwait(false);
            ReadMethodStateResult remainder = await ReadAsync(nm, resource, handle, 4).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(completed.Data, Is.EqualTo(ByteString.From([1, 2, 3, 4])));
                Assert.That(remainder.Data, Is.EqualTo(ByteString.From([5, 6, 7, 8])));
            });
        }

        [Test]
        public async Task MissingCommittedContentIsNotReportedAsEndOfFileAsync()
        {
            var store = new GatedResourceStore();
            using XRegistryRegistrationNodeManager nm = await CreateAddressSpaceAsync(store)
                .ConfigureAwait(false);
            (ResourceState resource, uint handle) = await OpenForReadAsync(nm, s_document)
                .ConfigureAwait(false);
            store.MissingContent = true;

            ReadMethodStateResult missing = await ReadAsync(nm, resource, handle, 4).ConfigureAwait(false);
            GetPositionMethodStateResult position = await resource.GetPosition!.OnCallAsync!(
                nm.SystemContext, resource.GetPosition, resource.NodeId, handle, CancellationToken.None)
                .ConfigureAwait(false);
            store.MissingContent = false;
            ReadMethodStateResult recovered = await ReadAsync(nm, resource, handle, 4).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(missing.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.BadNotFound));
                Assert.That(position.Position, Is.Zero);
                Assert.That(recovered.Data, Is.EqualTo(ByteString.From([1, 2, 3, 4])));
            });
        }

        [Test]
        public async Task ReadWriteOpenPreservesTheWholeBaselineAfterShortStorageReadsAsync()
        {
            var store = new GatedResourceStore(maxReadLength: 2);
            using XRegistryRegistrationNodeManager nm = await CreateAddressSpaceAsync(store)
                .ConfigureAwait(false);
            (ResourceState resource, uint reader) = await OpenForReadAsync(nm, s_document)
                .ConfigureAwait(false);
            await resource.Close!.OnCallAsync!(
                nm.SystemContext, resource.Close, resource.NodeId, reader, CancellationToken.None)
                .ConfigureAwait(false);

            OpenMethodStateResult writer = await resource.Open!.OnCallAsync!(
                nm.SystemContext, resource.Open, resource.NodeId, 3, CancellationToken.None)
                .ConfigureAwait(false);
            await resource.Write!.OnCallAsync!(
                nm.SystemContext, resource.Write, resource.NodeId, writer.FileHandle,
                ByteString.From([9]), CancellationToken.None).ConfigureAwait(false);
            await resource.SetPosition!.OnCallAsync!(
                nm.SystemContext, resource.SetPosition, resource.NodeId, writer.FileHandle, 0,
                CancellationToken.None).ConfigureAwait(false);
            ReadMethodStateResult staged = await ReadAsync(nm, resource, writer.FileHandle, 8)
                .ConfigureAwait(false);

            Assert.That(staged.Data, Is.EqualTo(ByteString.From([9, 2, 3, 4, 5, 6, 7, 8])));
        }

        [Test]
        public async Task SequentialReadsAdvanceTheCursorExactlyAsync()
        {
            using XRegistryRegistrationNodeManager nm = await CreateAddressSpaceAsync(
                new GatedResourceStore()).ConfigureAwait(false);
            (ResourceState resource, uint handle) = await OpenForReadAsync(nm, s_document)
                .ConfigureAwait(false);

            ReadMethodStateResult first = await ReadAsync(nm, resource, handle, 3).ConfigureAwait(false);
            ReadMethodStateResult second = await ReadAsync(nm, resource, handle, 3).ConfigureAwait(false);
            ReadMethodStateResult third = await ReadAsync(nm, resource, handle, 3).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(first.Data.Span.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
                Assert.That(second.Data.Span.ToArray(), Is.EqualTo(new byte[] { 4, 5, 6 }));
                Assert.That(third.Data.Span.ToArray(), Is.EqualTo(new byte[] { 7, 8 }),
                    "A short read at the end must pull the reserved cursor back to the real end.");
            });
        }

        [Test]
        public async Task ReadingPastTheEndAfterAShortReadStaysEmptyAsync()
        {
            using XRegistryRegistrationNodeManager nm = await CreateAddressSpaceAsync(
                new GatedResourceStore()).ConfigureAwait(false);
            (ResourceState resource, uint handle) = await OpenForReadAsync(nm, s_document)
                .ConfigureAwait(false);

            await ReadAsync(nm, resource, handle, 100).ConfigureAwait(false);
            ReadMethodStateResult past = await ReadAsync(nm, resource, handle, 4).ConfigureAwait(false);

            Assert.That(past.Data.Span.Length, Is.Zero,
                "The cursor sits at the end, so a further read yields nothing.");
        }

        private static ValueTask<ReadMethodStateResult> ReadAsync(
            XRegistryRegistrationNodeManager nm,
            ResourceState resource,
            uint handle,
            int length,
            CancellationToken cancellationToken = default)
        {
            return resource.Read!.OnCallAsync!(
                nm.SystemContext, resource.Read, resource.NodeId, handle, length,
                cancellationToken);
        }

        /// <summary>
        /// Creates a resource, commits <paramref name="document"/> and reopens it for reading.
        /// </summary>
        private static async Task<(ResourceState Resource, uint Handle)> OpenForReadAsync(
            XRegistryRegistrationNodeManager nm,
            byte[] document)
        {
            CreateGroupMethodStateResult group = await nm.OnCreateGroupAsync(
                nm.SystemContext, null!, NodeId.Null, "schemas", CancellationToken.None)
                .ConfigureAwait(false);
            CreateResourceMethodStateResult created = await nm.OnCreateResourceAsync(
                nm.SystemContext, null!, group.GroupNodeId, "urn:doc", "1", true,
                CancellationToken.None).ConfigureAwait(false);
            var resource = (ResourceState)nm.Find(created.ResourceNodeId)!;

            await resource.Write!.OnCallAsync!(
                nm.SystemContext, resource.Write, resource.NodeId, created.FileHandle,
                ByteString.From(document), CancellationToken.None).ConfigureAwait(false);
            await resource.Close!.OnCallAsync!(
                nm.SystemContext, resource.Close, resource.NodeId, created.FileHandle,
                CancellationToken.None).ConfigureAwait(false);

            OpenMethodStateResult opened = await resource.Open!.OnCallAsync!(
                nm.SystemContext, resource.Open, resource.NodeId, kReadMode, CancellationToken.None)
                .ConfigureAwait(false);
            return (resource, opened.FileHandle);
        }

        private static async Task<XRegistryRegistrationNodeManager> CreateAddressSpaceAsync(
            IXRegistryResourceStore store)
        {
            var options = new XRegistryServerOptions
            {
                ContentIdProvider = new XRegistryServerTestHarness.FakeContentIdProvider(),
                ResourceStore = store
            };
            Mock<IServerInternal> server =
                XRegistryServerTestHarness.CreateServer(options.RegistryNamespaceUri);
            var nm = new XRegistryRegistrationNodeManager(server.Object, null!, options);
            await nm.CreateAddressSpaceAsync(
                new Dictionary<NodeId, IList<IReference>>(),
                CancellationToken.None).ConfigureAwait(false);
            return nm;
        }

        /// <summary>
        /// Holds the first storage read so a second FileType request can overlap deterministically.
        /// </summary>
        private sealed class GatedResourceStore(
            bool blockFirstRead = false,
            bool failFirstRead = false,
            int maxReadLength = int.MaxValue) : IXRegistryAtomicResourceStore
        {
            public Task FirstReadEntered => m_entered.Task;

            public bool MissingContent { get; set; }

            public void ReleaseFirstRead()
            {
                m_release.TrySetResult(true);
            }

            public async ValueTask<ByteString> ReadAsync(
                string resourceKey,
                long offset,
                int count,
                CancellationToken ct = default)
            {
                if (MissingContent)
                {
                    return default;
                }
                if (Interlocked.Increment(ref m_reads) == 1 && blockFirstRead)
                {
                    m_entered.TrySetResult(true);
                    await m_release.Task.WaitAsync(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                    if (failFirstRead)
                    {
                        throw new ServiceResultException(StatusCodes.BadResourceUnavailable);
                    }
                }
                return await m_inner.ReadAsync(resourceKey, offset, Math.Min(count, maxReadLength), ct)
                    .ConfigureAwait(false);
            }

            public ValueTask ReplaceAsync(string resourceKey, ByteString document, CancellationToken ct = default)
            {
                return m_inner.ReplaceAsync(resourceKey, document, ct);
            }

            public ValueTask WriteAsync(
                string resourceKey,
                long offset,
                ByteString data,
                CancellationToken ct = default)
            {
                return m_inner.WriteAsync(resourceKey, offset, data, ct);
            }

            public ValueTask<long> GetLengthAsync(string resourceKey, CancellationToken ct = default)
            {
                return m_inner.GetLengthAsync(resourceKey, ct);
            }

            public ValueTask<bool> DeleteAsync(string resourceKey, CancellationToken ct = default)
            {
                return m_inner.DeleteAsync(resourceKey, ct);
            }

            private readonly InMemoryResourceStore m_inner = new();

            private readonly TaskCompletionSource<bool> m_entered =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            private readonly TaskCompletionSource<bool> m_release =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            private int m_reads;
        }

        private const byte kReadMode = 1;
        private static readonly byte[] s_document = [1, 2, 3, 4, 5, 6, 7, 8];
    }
}
