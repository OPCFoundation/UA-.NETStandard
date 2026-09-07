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
    /// Verifies what happens when a <c>Close</c> that commits a document races the <c>Delete</c> of
    /// the same resource. Close awaits the resource store outside the lock, so a Delete can complete
    /// its own cleanup in that window; Close must then leave nothing behind.
    /// </summary>
    [TestFixture]
    [Category("XRegistry")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class XRegistryCloseDeleteRaceTests
    {
        [Test]
        public async Task ADeleteDuringTheCommitLeavesNoOrphanedBytesAsync()
        {
            // The store blocks inside the commit's Write, which is exactly the window in which the
            // resource can be deleted, making the race deterministic instead of timing dependent.
            var store = new BlockingOnWriteResourceStore();
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace(store);

            CreateGroupMethodStateResult group = await nm.OnCreateGroupAsync(
                nm.SystemContext, null!, NodeId.Null, "schemas", CancellationToken.None)
                .ConfigureAwait(false);
            CreateResourceMethodStateResult created = await nm.OnCreateResourceAsync(
                nm.SystemContext, null!, group.GroupNodeId, "urn:doc", "1", true,
                CancellationToken.None).ConfigureAwait(false);
            var resource = (ResourceState)nm.Find(created.ResourceNodeId)!;
            string storeKey = created.ResourceNodeId.ToString()!;

            await resource.Write!.OnCallAsync!(
                nm.SystemContext, resource.Write, resource.NodeId, created.FileHandle,
                ByteString.From([1, 2, 3, 4]), CancellationToken.None).ConfigureAwait(false);

            // Start the commit; it parks inside the store write.
            Task<CloseMethodStateResult> closing = resource.Close!.OnCallAsync!(
                nm.SystemContext, resource.Close, resource.NodeId, created.FileHandle,
                CancellationToken.None).AsTask();
            await store.WriteEntered.ConfigureAwait(false);

            // Delete the resource while the commit is parked, then let the commit finish.
            DeleteMethodStateResult deleted = await nm
                .OnDeleteResourceAsync(resource, resource.Epoch!.Value).ConfigureAwait(false);
            store.Release();
            CloseMethodStateResult closed = await closing.ConfigureAwait(false);

            ByteString orphaned = await store.ReadAsync(storeKey, 0, int.MaxValue)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(deleted.ServiceResult), Is.True);
                Assert.That(closed.ServiceResult.StatusCode.Code,
                    Is.EqualTo(StatusCodes.BadInvalidState),
                    "The commit cannot succeed against a resource that no longer exists.");
                Assert.That(orphaned.IsNull, Is.True,
                    "The bytes the commit wrote after the delete's cleanup must be compensated — " +
                    "store keys are the resource NodeId and instance ids never repeat, so nothing " +
                    "else would ever collect them.");
                Assert.That(nm.Find(created.ResourceNodeId), Is.Null);
            });
        }

        [Test]
        public async Task ACommitThatIsNotRacedStillStoresTheDocumentAsync()
        {
            var store = new BlockingOnWriteResourceStore();
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace(store);
            store.Release();

            CreateGroupMethodStateResult group = await nm.OnCreateGroupAsync(
                nm.SystemContext, null!, NodeId.Null, "schemas", CancellationToken.None)
                .ConfigureAwait(false);
            CreateResourceMethodStateResult created = await nm.OnCreateResourceAsync(
                nm.SystemContext, null!, group.GroupNodeId, "urn:doc", "1", true,
                CancellationToken.None).ConfigureAwait(false);
            var resource = (ResourceState)nm.Find(created.ResourceNodeId)!;

            byte[] document = [1, 2, 3, 4];
            await resource.Write!.OnCallAsync!(
                nm.SystemContext, resource.Write, resource.NodeId, created.FileHandle,
                ByteString.From(document), CancellationToken.None).ConfigureAwait(false);
            CloseMethodStateResult closed = await resource.Close!.OnCallAsync!(
                nm.SystemContext, resource.Close, resource.NodeId, created.FileHandle,
                CancellationToken.None).ConfigureAwait(false);

            ByteString stored = await store
                .ReadAsync(created.ResourceNodeId.ToString()!, 0, int.MaxValue)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(closed.ServiceResult), Is.True);
                Assert.That(stored.Span.ToArray(), Is.EqualTo(document),
                    "The compensation must not fire when the resource is still there.");
            });
        }

        private static XRegistryRegistrationNodeManager CreateAddressSpace(
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
            nm.CreateAddressSpace(new Dictionary<NodeId, IList<IReference>>());
            return nm;
        }

        /// <summary>
        /// In-memory store that parks the first non-empty <c>WriteAsync</c> until it is released,
        /// so a test can interleave another operation with a commit deterministically.
        /// </summary>
        private sealed class BlockingOnWriteResourceStore : IXRegistryResourceStore
        {
            /// <summary>
            /// Completes once a commit has entered <see cref="WriteAsync"/>.
            /// </summary>
            public Task WriteEntered => m_entered.Task;

            /// <summary>
            /// Lets a parked write proceed.
            /// </summary>
            public void Release()
            {
                m_release.TrySetResult(true);
            }

            public ValueTask<ByteString> ReadAsync(
                string resourceKey,
                long offset,
                int count,
                CancellationToken ct = default)
            {
                return m_inner.ReadAsync(resourceKey, offset, count, ct);
            }

            public async ValueTask WriteAsync(
                string resourceKey,
                long offset,
                ByteString data,
                CancellationToken ct = default)
            {
                if (!data.IsNull && data.Length > 0)
                {
                    m_entered.TrySetResult(true);
                    await m_release.Task.ConfigureAwait(false);
                }
                await m_inner.WriteAsync(resourceKey, offset, data, ct).ConfigureAwait(false);
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
        }
    }
}
