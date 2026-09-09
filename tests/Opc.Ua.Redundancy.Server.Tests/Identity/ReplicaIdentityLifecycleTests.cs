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

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.Redundancy.Server.Tests.Identity
{
    /// <summary>
    /// Verifies pre-publication hydration and continued capture after replacing an owning node manager.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class ReplicaIdentityLifecycleTests
    {
        /// <summary>
        /// Stages retained runtime IDs before the replacement is visible, then follows its new address-space source.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ReplacementHydratesBeforePublicationAndRebindsCaptureAsync(bool activeActive)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var messages = ServiceMessageContext.CreateEmpty(telemetry);
            messages.NamespaceUris.Append("urn:lifecycle:replica");
            var identity = new ReplicaNodeIdFactory("lifecycle-set", [SharedUri]);
            identity.PrepareNamespaces(messages.NamespaceUris);
            var server = new Mock<IServerInternal>();
            server.As<INodeIdFactoryProvider>().Setup(s => s.NodeIdFactory).Returns(identity);
            server.Setup(s => s.Telemetry).Returns(telemetry);
            server.Setup(s => s.MessageContext).Returns(messages);
            server.Setup(s => s.NamespaceUris).Returns(messages.NamespaceUris);
            server.Setup(s => s.ServerUris).Returns(messages.ServerUris);
            server.Setup(s => s.Factory).Returns(messages.Factory);
            var context = new ServerSystemContext(server.Object);
            server.Setup(s => s.DefaultSystemContext).Returns(context);
            var original = new DictionaryAddressSpace(context);
            var replacement = new DictionaryAddressSpace(context);
            BaseDataVariableState oldValue = Variable("Existing", 42);
            BaseDataVariableState newValue = Variable("Existing", 0);
            BaseDataVariableState retained = Variable("Runtime", 17);
            await original.AddOrUpdateNodeAsync(oldValue).ConfigureAwait(false);
            await original.AddOrUpdateNodeAsync(retained).ConfigureAwait(false);
            await replacement.AddOrUpdateNodeAsync(newValue).ConfigureAwait(false);
            Mock<IAsyncNodeManager> oldManager = Manager(original);
            Mock<IAsyncNodeManager> newManager = Manager(replacement);
            ILocalAddressSpaceSource[] current = [oldManager.As<ILocalAddressSpaceSource>().Object];
            server.Setup(s => s.FindNodeManagers<ILocalAddressSpaceSource>()).Returns(() => current);
            using var store = new InMemorySharedKeyValueStore();
            IAsyncDisposable startup;
            if (activeActive)
            {
                var task = new ReplicatedAddressSpaceStartupTask(
                    Mock.Of<IServiceProvider>(), new ReplicatedAddressSpaceOptions());
                startup = task;
                await task.OnServerStartedAsync(server.Object).ConfigureAwait(false);
            }
            else
            {
                var task = new DistributedAddressSpaceStartupTask(store, new StaticLeaderElection(true));
                startup = task;
                await task.OnServerStartedAsync(server.Object).ConfigureAwait(false);
            }
            await using (startup.ConfigureAwait(false))
            {
                await identity.PrepareNodeManagerAsync(server.Object, newManager.Object, oldManager.Object)
                    .ConfigureAwait(false);
                Assert.That(replacement.TryGetNode(retained.NodeId, out NodeState? hydrated), Is.True,
                    "the cached runtime ID must exist before the replacement is published");
                Assert.That(hydrated!.NodeId, Is.EqualTo(retained.NodeId));
                Assert.That(replacement.TryGetNode(oldValue.NodeId, out NodeState? actual), Is.True);
                Assert.That(actual, Is.SameAs(newValue), "the replacement's callback-owning instance is retained");
                Assert.That(newValue.Value, Is.EqualTo(Variant.From(42)));
                Assert.That(current[0], Is.SameAs(oldManager.As<ILocalAddressSpaceSource>().Object));

                current = [newManager.As<ILocalAddressSpaceSource>().Object];
                await identity.OnNodeManagersChangedAsync(server.Object).ConfigureAwait(false);
                BaseDataVariableState added = Variable("AfterReload", 99);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                if (activeActive)
                {
                    await replacement.AddOrUpdateNodeAsync(added).ConfigureAwait(false);
                    var later = new DictionaryAddressSpace(context);
                    Mock<IAsyncNodeManager> laterManager = Manager(later);
                    await identity.PrepareNodeManagerAsync(
                        server.Object, laterManager.Object, newManager.Object, timeout.Token).ConfigureAwait(false);
                    Assert.That(later.TryGetNode(added.NodeId, out NodeState? captured), Is.True,
                        "the existing CRDT map must capture the new manager rather than its retired source");
                    Assert.That(((BaseVariableState)captured!).Value, Is.EqualTo(Variant.From(99)));
                }
                else
                {
                    await using IAsyncEnumerator<KeyValueChange> changes = store.WatchAsync("n/" + added.NodeId, timeout.Token)
                        .GetAsyncEnumerator(timeout.Token);
                    Task<bool> changed = changes.MoveNextAsync().AsTask();
                    await replacement.AddOrUpdateNodeAsync(added).ConfigureAwait(false);
                    Assert.That(await changed.ConfigureAwait(false), Is.True);
                    Assert.That(changes.Current.Key, Is.EqualTo("n/" + added.NodeId));
                }
            }
        }

        private static Mock<IAsyncNodeManager> Manager(DictionaryAddressSpace space)
        {
            var manager = new Mock<IAsyncNodeManager>();
            manager.Setup(s => s.NamespaceUris).Returns([SharedUri]);
            manager.As<ILocalAddressSpaceSource>().Setup(s => s.CreateLocalAddressSpace()).Returns(space);
            return manager;
        }

        /// <summary>
        /// A writer reconciling an older topology must not publish its temporary child removals as new deletions.
        /// </summary>
        [Test]
        public async Task WriterHydrationDoesNotRecapturePreparedChildRemovalAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var messages = ServiceMessageContext.CreateEmpty(telemetry);
            messages.NamespaceUris.Append("urn:lifecycle:replica");
            messages.NamespaceUris.Append(SharedUri);
            var context = new SystemContext(telemetry)
            {
                NamespaceUris = messages.NamespaceUris,
                ServerUris = messages.ServerUris,
                EncodeableFactory = messages.Factory
            };
            using var backend = new InMemorySharedKeyValueStore();
            using var store = new InMemoryNodeStateStore(backend, messages);
            var persisted = new BaseObjectState(null)
            {
                NodeId = new NodeId("Root", 2),
                BrowseName = new QualifiedName("Root", 2)
            };
            await store.UpsertNodeAsync(new StoredNode(
                persisted.NodeId, NodeStateSerializer.Serialize(context, persisted))).ConfigureAwait(false);
            var local = new BaseObjectState(null)
            {
                NodeId = persisted.NodeId,
                BrowseName = persisted.BrowseName
            };
            BaseDataVariableState child = Variable("Runtime", 99);
            local.AddChild(child);
            var space = new DictionaryAddressSpace(context);
            await space.AddOrUpdateNodeAsync(local).ConfigureAwait(false);
            await space.AddOrUpdateNodeAsync(child).ConfigureAwait(false);
            await using (var sync = new AddressSpaceSynchronizer(store, space, static () => true))
            {
                sync.StartBeforeHydration();
                await sync.SeedOrHydrateAsync().ConfigureAwait(false);
                await sync.CompleteHydrationAsync().ConfigureAwait(false);
            }
            await foreach (NodeStateChange change in store.ReadDeltaLogAsync(0).ConfigureAwait(false))
            {
                Assert.That(change.Kind, Is.Not.EqualTo(NodeStateChangeKind.Delete),
                    "reconciliation is inbound application, not a new writer mutation");
            }
        }

        private static BaseDataVariableState Variable(string name, int value)
        {
            return new BaseDataVariableState(null)
            {
                NodeId = new NodeId(name, 2),
                BrowseName = new QualifiedName(name, 2),
                TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.Scalar,
                Value = Variant.From(value)
            };
        }

        private const string SharedUri = "urn:lifecycle:shared";
    }
}
