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

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

#nullable enable

using System;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server.Redundancy;
using Opc.Ua.Tests;
using Opc.Ua.Redundancy;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Integration tests for <see cref="AddressSpaceSynchronizer"/> running
    /// two replicas (writer + reader) over one shared store.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public class AddressSpaceSynchronizerTests
    {
        private const ushort NamespaceIndex = 1;
        private IServiceMessageContext m_messageContext = null!;
        private SystemContext m_systemContext = null!;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ServiceMessageContext messageContext = ServiceMessageContext.CreateEmpty(telemetry);
            messageContext.NamespaceUris.GetIndexOrAppend("urn:test:sync");
            m_messageContext = messageContext;
            m_systemContext = new SystemContext(telemetry)
            {
                NamespaceUris = messageContext.NamespaceUris,
                ServerUris = messageContext.ServerUris,
                EncodeableFactory = messageContext.Factory
            };
        }

        [Test]
        public async Task WriterSeedsEmptyStoreAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            var store = new InMemoryNodeStateStore(kv, m_messageContext);
            var writerSpace = new DictionaryAddressSpace(m_systemContext);
            await writerSpace.AddOrUpdateNodeAsync(NewVariable("seed", 1.0));

            await using var writer = new AddressSpaceSynchronizer(store, writerSpace, () => true);
            await writer.SeedOrHydrateAsync();

            IStoredNode? stored = await store.TryGetNodeAsync(new NodeId("seed", NamespaceIndex));
            (bool found, _) = await store.TryReadValueAsync(new NodeId("seed", NamespaceIndex));
            Assert.That(stored, Is.Not.Null);
            Assert.That(found, Is.True);
        }

        [Test]
        public async Task ReaderDoesNotSeedEmptyStoreAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            var store = new InMemoryNodeStateStore(kv, m_messageContext);
            var readerSpace = new DictionaryAddressSpace(m_systemContext);
            await readerSpace.AddOrUpdateNodeAsync(NewVariable("local", 1.0));

            await using var reader = new AddressSpaceSynchronizer(store, readerSpace, () => false);
            await reader.SeedOrHydrateAsync();

            IStoredNode? stored = await store.TryGetNodeAsync(new NodeId("local", NamespaceIndex));
            Assert.That(reader.IsWriter, Is.False);
            Assert.That(stored, Is.Null, "a reader must never write to the shared store");
        }

        [Test]
        public async Task WriterReplicatesTopologyAndValueToReaderAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            var writerStore = new InMemoryNodeStateStore(kv, m_messageContext);
            var readerStore = new InMemoryNodeStateStore(kv, m_messageContext);

            var writerSpace = new DictionaryAddressSpace(m_systemContext);
            var readerSpace = new DictionaryAddressSpace(m_systemContext);

            BaseDataVariableState nodeX = NewVariable("X", 1.0);
            await writerSpace.AddOrUpdateNodeAsync(nodeX);

            await using var writer = new AddressSpaceSynchronizer(writerStore, writerSpace, () => true);
            await using var reader = new AddressSpaceSynchronizer(readerStore, readerSpace, () => false);

            await writer.SeedOrHydrateAsync();
            writer.Start();

            await reader.SeedOrHydrateAsync();
            Assert.That(readerSpace.TryGetNode(nodeX.NodeId, out _), Is.True, "reader hydrated X from the store");
            reader.Start();

            // Value change on the writer propagates to the reader.
            Task<bool> valueApplied = WaitForInboundAsync(
                reader, c => c.Kind == NodeStateChangeKind.Value && c.NodeId == nodeX.NodeId);
            nodeX.Value = new Variant(42.0);
            nodeX.ClearChangeMasks(m_systemContext, false);
            await AwaitWithTimeoutAsync(valueApplied);

            Assert.That(readerSpace.TryGetNode(nodeX.NodeId, out NodeState? rx), Is.True);
            Assert.That(((BaseDataVariableState)rx!).Value, Is.EqualTo(new Variant(42.0)));

            // Adding a node on the writer propagates to the reader.
            BaseDataVariableState nodeY = NewVariable("Y", 7.0);
            Task<bool> addApplied = WaitForInboundAsync(
                reader, c => c.Kind == NodeStateChangeKind.Upsert && c.NodeId == nodeY.NodeId);
            await writerSpace.AddOrUpdateNodeAsync(nodeY);
            await AwaitWithTimeoutAsync(addApplied);

            Assert.That(readerSpace.TryGetNode(nodeY.NodeId, out _), Is.True, "reader received added node Y");

            // Removing a node on the writer propagates to the reader.
            Task<bool> deleteApplied = WaitForInboundAsync(
                reader, c => c.Kind == NodeStateChangeKind.Delete && c.NodeId == nodeX.NodeId);
            await writerSpace.RemoveNodeAsync(nodeX.NodeId);
            await AwaitWithTimeoutAsync(deleteApplied);

            Assert.That(readerSpace.TryGetNode(nodeX.NodeId, out _), Is.False, "reader removed node X");
        }

        private BaseDataVariableState NewVariable(string id, double value)
        {
            return new BaseDataVariableState(null)
            {
                NodeId = new NodeId(id, NamespaceIndex),
                BrowseName = new QualifiedName(id, NamespaceIndex),
                DisplayName = new LocalizedText(id),
                DataType = DataTypeIds.Double,
                ValueRank = ValueRanks.Scalar,
                Value = new Variant(value)
            };
        }

        private static Task<bool> WaitForInboundAsync(
            AddressSpaceSynchronizer synchronizer,
            Func<NodeStateChange, bool> predicate)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            void Handler(NodeStateChange change)
            {
                if (predicate(change))
                {
                    synchronizer.InboundApplied -= Handler;
                    tcs.TrySetResult(true);
                }
            }

            synchronizer.InboundApplied += Handler;
            return tcs.Task;
        }

        private static async Task AwaitWithTimeoutAsync(Task task)
        {
            Task completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(10)));
            Assert.That(completed, Is.SameAs(task), "replication did not complete within the timeout");
            await task;
        }
    }
}
