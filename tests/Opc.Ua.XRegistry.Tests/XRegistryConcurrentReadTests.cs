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
    /// overlap. The store call is awaited, so the cursor must be reserved under the lock rather than
    /// read outside it; otherwise both callers start at the same offset, both get the same bytes and
    /// the cursor then skips a slice.
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
            // The store holds both readers inside ReadAsync until each has taken its offset, which
            // is exactly the window where an unsynchronized cursor read goes wrong.
            var store = new GatedResourceStore(participants: 2);
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace(store);
            (ResourceState resource, uint handle) = await OpenForReadAsync(nm, s_document)
                .ConfigureAwait(false);

            Task<ReadMethodStateResult> first = ReadAsync(nm, resource, handle, 4).AsTask();
            Task<ReadMethodStateResult> second = ReadAsync(nm, resource, handle, 4).AsTask();
            ReadMethodStateResult[] results = await Task.WhenAll(first, second).ConfigureAwait(false);

            var combined = new List<byte>();
            foreach (ReadMethodStateResult result in results)
            {
                combined.AddRange(result.Data.Span.ToArray());
            }
            combined.Sort();

            Assert.Multiple(() =>
            {
                Assert.That(store.MaxConcurrency, Is.EqualTo(2),
                    "Precondition: the two reads really did overlap.");
                Assert.That(combined, Is.EqualTo(s_document),
                    "Between them the two reads must cover the document exactly once — no slice " +
                    "returned twice and none skipped.");
                Assert.That(store.Offsets, Is.Unique,
                    "Each read must start from its own offset.");
            });
        }

        [Test]
        public async Task SequentialReadsAdvanceTheCursorExactlyAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace(
                new GatedResourceStore(participants: 1));
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
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace(
                new GatedResourceStore(participants: 1));
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
            int length)
        {
            return resource.Read!.OnCallAsync!(
                nm.SystemContext, resource.Read, resource.NodeId, handle, length,
                CancellationToken.None);
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
        /// In-memory store that holds every reader inside <c>ReadAsync</c> until the expected number
        /// of them has arrived, so overlapping reads are deterministic rather than timing-dependent.
        /// </summary>
        private sealed class GatedResourceStore(int participants) : IXRegistryResourceStore
        {
            public int MaxConcurrency { get; private set; }

            public List<long> Offsets { get; } = [];

            public async ValueTask<ByteString> ReadAsync(
                string resourceKey,
                long offset,
                int count,
                CancellationToken ct = default)
            {
                lock (m_lock)
                {
                    Offsets.Add(offset);
                    m_arrived++;
                    MaxConcurrency = Math.Max(MaxConcurrency, m_arrived);
                    if (m_arrived >= participants)
                    {
                        m_gate.TrySetResult(true);
                    }
                }

                await m_gate.Task.ConfigureAwait(false);

                ByteString chunk = await m_inner.ReadAsync(resourceKey, offset, count, ct)
                    .ConfigureAwait(false);

                lock (m_lock)
                {
                    m_arrived--;
                }
                return chunk;
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
            private readonly TaskCompletionSource<bool> m_gate =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly Lock m_lock = new();
            private int m_arrived;
        }

        private const byte kReadMode = 1;
        private static readonly byte[] s_document = [1, 2, 3, 4, 5, 6, 7, 8];
    }
}
