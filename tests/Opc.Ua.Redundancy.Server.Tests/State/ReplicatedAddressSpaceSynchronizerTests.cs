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

// CA2007: tests run without a SynchronizationContext.
#pragma warning disable CA2007

using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Crdt;
using Crdt.Transport;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.Redundancy.Server.Tests
{
    /// <summary>
    /// Active/active convergence tests for <see cref="ReplicatedAddressSpaceSynchronizer"/>
    /// running two replicas over a deterministic in-memory gossip network.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    public sealed class ReplicatedAddressSpaceSynchronizerTests
    {
        private const ushort NamespaceIndex = 1;
        private IServiceMessageContext m_messageContext = null!;
        private SystemContext m_systemContext = null!;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var messageContext = ServiceMessageContext.CreateEmpty(telemetry);
            messageContext.NamespaceUris.GetIndexOrAppend("urn:test:crdt");
            m_messageContext = messageContext;
            m_systemContext = new SystemContext(telemetry)
            {
                NamespaceUris = messageContext.NamespaceUris,
                ServerUris = messageContext.ServerUris,
                EncodeableFactory = messageContext.Factory
            };
        }

        [Test]
        public async Task ReplicatesAddedNodeToPeerAsync()
        {
            await using TwoReplicaFixture fixture = await TwoReplicaFixture.CreateAsync(this).ConfigureAwait(false);

            BaseDataVariableState node = NewVariable("added", 1.0);
            await fixture.SpaceA.AddOrUpdateNodeAsync(node).ConfigureAwait(false);

            await AssertEventuallyAsync(
                () => fixture.SpaceB.TryGetNode(node.NodeId, out _),
                "node added on A should replicate to B").ConfigureAwait(false);
        }

        /// <summary>
        /// Exercises actual service authorization on both peers after shared-store or active/active replication.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("SecurityRegression")]
        public async Task SecurityF07ReplicaCannotDropNodeOrChildPolicyAsync(bool sharedStore)
        {
            await using var serverA = new AuthorizationServer();
            await using var serverB = new AuthorizationServer();
            BaseDataVariableState root = NewPolicyTree();
            await serverA.Space.AddOrUpdateNodeAsync(root).ConfigureAwait(false);

            if (sharedStore)
            {
                using var kv = new InMemorySharedKeyValueStore();
                using var store = new InMemoryNodeStateStore(kv, m_messageContext);
                await using var writer = new AddressSpaceSynchronizer(store, serverA.Space, () => true);
                await using var reader = new AddressSpaceSynchronizer(store, serverB.Space, () => false);
                await writer.SeedOrHydrateAsync().ConfigureAwait(false);
                await reader.SeedOrHydrateAsync().ConfigureAwait(false);
            }
            else
            {
                await using var replicaA = new ControlledReplica(this, 1, serverA.Space);
                await using var replicaB = new ControlledReplica(this, 2, serverB.Space);
                await replicaA.StartAsync().ConfigureAwait(false);
                await replicaB.StartAsync().ConfigureAwait(false);
                await replicaA.SendToAsync(replicaB).ConfigureAwait(false);
            }

            Assert.That(serverB.Space.TryGetNode(root.NodeId, out NodeState? remote), Is.True);
            Assert.That(remote, Is.Not.SameAs(root));
            await AssertEffectivePolicyAsync(serverA).ConfigureAwait(false);
            await AssertEffectivePolicyAsync(serverB).ConfigureAwait(false);
        }

        /// <summary>
        /// Tightening policy on existing roots and descendants changes service access without replacing runtime state.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF07ExistingReplicaAppliesAuthorizationChangesAsync()
        {
            await using var serverA = new AuthorizationServer();
            await using var serverB = new AuthorizationServer();
            await using var replicaA = new ControlledReplica(this, 1, serverA.Space);
            await using var replicaB = new ControlledReplica(this, 2, serverB.Space);
            await replicaA.StartAsync().ConfigureAwait(false);
            await replicaB.StartAsync().ConfigureAwait(false);
            await replicaA.SendToAsync(replicaB).ConfigureAwait(false);
            await replicaB.SendToAsync(replicaA).ConfigureAwait(false);

            BaseDataVariableState root = NewPolicyTree(restricted: false);
            await serverA.Space.AddOrUpdateNodeAsync(root).ConfigureAwait(false);
            await replicaA.SendToAsync(replicaB).ConfigureAwait(false);
            BaseVariableState existingRoot = replicaB.GetVariable(root.NodeId);
            BaseVariableState existingChild = replicaB.GetVariable(new NodeId("secure-child", NamespaceIndex));
            var runtimeHandle = new object();
            existingChild.Handle = runtimeHandle;
            int callbackCount = 0;
            ServiceResult callback(
ISystemContext context,
NodeState node,
NumericRange indexRange,
QualifiedName dataEncoding,
                ref Variant value,
                ref StatusCode statusCode,
                ref DateTimeUtc timestamp)
            {
                callbackCount++;
                return ServiceResult.Good;
            }
            existingChild.OnReadValue = callback;
            DataValue before = await serverB.ReadAsync(
                existingChild.NodeId, ObjectIds.WellKnownRole_AuthenticatedUser).ConfigureAwait(false);
            Assert.That(before.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(before.WrappedValue, Is.EqualTo(new Variant(42.0)));
            int beforeDenial = callbackCount;

            root.RolePermissions = AdminPermissions(PermissionType.Read | PermissionType.Write);
            root.AccessRestrictions = AccessRestrictionType.EncryptionRequired | AccessRestrictionType.SessionRequired;
            await root.ClearChangeMasksAsync(serverA.Space.Context, false).ConfigureAwait(false);
            await replicaA.SendToAsync(replicaB).ConfigureAwait(false);
            DataValue rootDenied = await serverB.ReadAsync(
                root.NodeId, ObjectIds.WellKnownRole_AuthenticatedUser).ConfigureAwait(false);
            Assert.That(rootDenied.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));

            var child = (BaseVariableState)root.FindChild(
                serverA.Space.Context, new QualifiedName("secure-child", NamespaceIndex))!;
            SetChildPolicy(child);
            await child.ClearChangeMasksAsync(serverA.Space.Context, false).ConfigureAwait(false);
            await replicaA.SendToAsync(replicaB).ConfigureAwait(false);

            Assert.That(replicaB.GetVariable(root.NodeId), Is.SameAs(existingRoot));
            Assert.That(replicaB.GetVariable(child.NodeId), Is.SameAs(existingChild));
            Assert.That(existingChild.Handle, Is.SameAs(runtimeHandle));
            Assert.That(existingChild.OnReadValue, Is.EqualTo((NodeValueEventHandler)callback));
            DataValue denied = await serverB.ReadAsync(
                child.NodeId, ObjectIds.WellKnownRole_AuthenticatedUser).ConfigureAwait(false);
            Assert.That(denied.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(denied.WrappedValue.IsNull, Is.True);
            Assert.That(callbackCount, Is.EqualTo(beforeDenial), "denied reads must not invoke the value callback");
            Assert.That(
                await serverB.WriteAsync(child.NodeId, ObjectIds.WellKnownRole_SecurityAdmin, 99.0)
                    .ConfigureAwait(false),
                Is.EqualTo(StatusCodes.BadUserAccessDenied), "user permissions must intersect the general role mask");
            DataValue authorized = await serverB.ReadAsync(
                child.NodeId, ObjectIds.WellKnownRole_SecurityAdmin).ConfigureAwait(false);
            Assert.That(authorized.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(authorized.WrappedValue, Is.EqualTo(new Variant(42.0)));
            Assert.That(callbackCount, Is.EqualTo(beforeDenial + 1));
            DataValue insecure = await serverB.ReadAsync(
                child.NodeId, ObjectIds.WellKnownRole_SecurityAdmin, MessageSecurityMode.Sign).ConfigureAwait(false);
            Assert.That(insecure.StatusCode, Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));

            child.RolePermissions = default;
            child.UserRolePermissions = default;
            child.AccessRestrictions = null;
            await child.ClearChangeMasksAsync(serverA.Space.Context, false).ConfigureAwait(false);
            await replicaA.SendToAsync(replicaB).ConfigureAwait(false);
            DataValue reopened = await serverB.ReadAsync(
                child.NodeId,
                ObjectIds.WellKnownRole_AuthenticatedUser,
                MessageSecurityMode.None).ConfigureAwait(false);
            Assert.That(reopened.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(reopened.WrappedValue, Is.EqualTo(new Variant(42.0)));
        }

        /// <summary>
        /// Store hydration replaces existing node policy and restores policy on newly initialized descendants.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF07StoredPolicyReplacesExistingReplicaPolicyAsync()
        {
            await using var serverA = new AuthorizationServer();
            await using var serverB = new AuthorizationServer();
            BaseDataVariableState source = NewPolicyTree();
            await serverA.Space.AddOrUpdateNodeAsync(source).ConfigureAwait(false);
            BaseDataVariableState existing = NewVariable("secure-root", 42.0);
            existing.AccessLevel = AccessLevels.CurrentReadOrWrite;
            existing.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            await serverB.Space.AddOrUpdateNodeAsync(existing).ConfigureAwait(false);
            DataValue before = await serverB.ReadAsync(
                existing.NodeId, ObjectIds.WellKnownRole_AuthenticatedUser).ConfigureAwait(false);
            Assert.That(before.StatusCode, Is.EqualTo(StatusCodes.Good));
            using var kv = new InMemorySharedKeyValueStore();
            using var store = new InMemoryNodeStateStore(kv, m_messageContext);
            await using var writer = new AddressSpaceSynchronizer(store, serverA.Space, () => true);
            await using var reader = new AddressSpaceSynchronizer(store, serverB.Space, () => false);

            await writer.SeedOrHydrateAsync().ConfigureAwait(false);
            await reader.SeedOrHydrateAsync().ConfigureAwait(false);

            Assert.That(serverB.Space.TryGetNode(source.NodeId, out NodeState? updated), Is.True);
            Assert.That(updated, Is.SameAs(existing));
            await AssertEffectivePolicyAsync(serverB).ConfigureAwait(false);
        }

        /// <summary>
        /// A truncated required policy cannot create a readable node during shared-store hydration.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF07IncompleteStoredPolicyIsNotRegisteredAsync()
        {
            await using var server = new AuthorizationServer();
            using var kv = new InMemorySharedKeyValueStore();
            using var store = new InMemoryNodeStateStore(kv, m_messageContext);
            BaseDataVariableState source = NewPolicyTree();
            byte[] bytes = NodeStateSerializer.Serialize(m_systemContext, source).ToArray();
            Array.Resize(ref bytes, bytes.Length - 1);
            await store.UpsertNodeAsync(new StoredNode(source.NodeId, new ByteString(bytes))).ConfigureAwait(false);
            await using var reader = new AddressSpaceSynchronizer(store, server.Space, () => false);

            await Assert.ThatAsync(
                async () => await reader.SeedOrHydrateAsync().ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadDecodingError))
                .ConfigureAwait(false);

            Assert.That(server.Space.TryGetNode(source.NodeId, out _), Is.False);
            Assert.That(server.Space.TryGetNode(new NodeId("secure-child", NamespaceIndex), out _), Is.False);
            DataValue denied = await server.ReadAsync(
                source.NodeId, ObjectIds.WellKnownRole_AuthenticatedUser).ConfigureAwait(false);
            Assert.That(denied.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        /// <summary>
        /// A rejected live policy withdraws previously open nodes and value-only updates cannot reopen them.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF07InvalidLivePolicyWithdrawsExistingTreeUntilRepairedAsync()
        {
            await using var serverA = new AuthorizationServer();
            await using var serverB = new AuthorizationServer();
            BaseDataVariableState source = NewPolicyTree(restricted: false);
            await serverA.Space.AddOrUpdateNodeAsync(source).ConfigureAwait(false);
            using var kv = new InMemorySharedKeyValueStore();
            using var store = new InMemoryNodeStateStore(kv, m_messageContext);
            await using var writer = new AddressSpaceSynchronizer(store, serverA.Space, () => true);
            await using var reader = new AddressSpaceSynchronizer(store, serverB.Space, () => false);
            await writer.SeedOrHydrateAsync().ConfigureAwait(false);
            await reader.SeedOrHydrateAsync().ConfigureAwait(false);
            reader.Start();
            var childId = new NodeId("secure-child", NamespaceIndex);
            DataValue before = await serverB.ReadAsync(
                childId, ObjectIds.WellKnownRole_AuthenticatedUser).ConfigureAwait(false);
            Assert.That(before.StatusCode, Is.EqualTo(StatusCodes.Good));

            BaseDataVariableState protectedSource = NewPolicyTree();
            await serverA.Space.AddOrUpdateNodeAsync(protectedSource).ConfigureAwait(false);
            ByteString complete = NodeStateSerializer.Serialize(serverA.Space.Context, protectedSource);
            byte[] bytes = complete.ToArray();
            Array.Resize(ref bytes, bytes.Length - 1);
            Task<bool> rejected = WaitForStoreApplyAsync(reader, NodeStateChangeKind.Upsert, source.NodeId);
            await store.UpsertNodeAsync(new StoredNode(source.NodeId, new ByteString(bytes))).ConfigureAwait(false);
            await AwaitWithTimeoutAsync(rejected).ConfigureAwait(false);

            foreach (string id in new[] { "secure-root", "secure-child", "secure-grandchild" })
            {
                var nodeId = new NodeId(id, NamespaceIndex);
                Assert.That(serverB.Space.TryGetNode(nodeId, out _), Is.False, id);
                DataValue denied = await serverB.ReadAsync(
                    nodeId, ObjectIds.WellKnownRole_AuthenticatedUser).ConfigureAwait(false);
                Assert.That(denied.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown), id);
            }

            Task<bool> valueApplied = WaitForStoreApplyAsync(reader, NodeStateChangeKind.Value, childId);
            await store.WriteValueAsync(childId, new DataValue(new Variant(99.0))).ConfigureAwait(false);
            await AwaitWithTimeoutAsync(valueApplied).ConfigureAwait(false);
            DataValue stillDenied = await serverB.ReadAsync(
                childId, ObjectIds.WellKnownRole_AuthenticatedUser).ConfigureAwait(false);
            Assert.That(stillDenied.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));

            Task<bool> repaired = WaitForStoreApplyAsync(reader, NodeStateChangeKind.Upsert, source.NodeId);
            await store.UpsertNodeAsync(new StoredNode(source.NodeId, complete)).ConfigureAwait(false);
            await AwaitWithTimeoutAsync(repaired).ConfigureAwait(false);

            DataValue restricted = await serverB.ReadAsync(
                childId, ObjectIds.WellKnownRole_AuthenticatedUser).ConfigureAwait(false);
            Assert.That(restricted.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            DataValue authorized = await serverB.ReadAsync(
                childId, ObjectIds.WellKnownRole_SecurityAdmin).ConfigureAwait(false);
            Assert.That(authorized.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(authorized.WrappedValue, Is.EqualTo(new Variant(99.0)));
        }

        /// <summary>
        /// Policy validation precedes CRDT merge even without replica-identity enforcement.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF07MalformedGossipPolicyRejectedBeforeMergeAsync()
        {
            await using var server = new AuthorizationServer();
            await using var replica = new ControlledReplica(this, 2, server.Space);
            await replica.StartAsync().ConfigureAwait(false);
            BaseDataVariableState source = NewPolicyTree();
            ByteString complete = NodeStateSerializer.Serialize(m_systemContext, source);
            byte[] bytes = complete.ToArray();
            Array.Resize(ref bytes, bytes.Length - 1);
            var clock = new HybridLogicalClock(ReplicaId.FromUInt64(1), TimeProvider.System);
            var remote = new LWWMap<string, ByteString>();
            BaseDataVariableState sibling = NewVariable("valid-sibling", 1.0);
            remote.Set("n|" + sibling.NodeId, NodeStateSerializer.Serialize(m_systemContext, sibling), clock);
            remote.Set("n|" + source.NodeId, new ByteString(bytes), clock);
            var rejected = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
            replica.Sync.InboundRejected += exception => rejected.TrySetResult(exception);

            replica.Transport.Receive(FrameCodec.Encode(
                MessageType.State, remote.ToByteArray(CrdtValues.String, ByteStringCrdtSerializer.Instance)));
            Exception failure = await rejected.Task.WaitAsync(GetReplicationTimeout()).ConfigureAwait(false);

            Assert.That(failure, Is.TypeOf<ServiceResultException>()
                .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadDecodingError));
            Assert.That(replica.Sync.InboundApplyCount, Is.Zero);
            Assert.That(server.Space.TryGetNode(source.NodeId, out _), Is.False);
            Assert.That(server.Space.TryGetNode(sibling.NodeId, out _), Is.False);

            remote.Set("n|" + source.NodeId, complete, clock);
            Task applied = replica.Sync.WaitForInboundApplyAfterAsync(0);
            replica.Transport.Receive(FrameCodec.Encode(
                MessageType.State, remote.ToByteArray(CrdtValues.String, ByteStringCrdtSerializer.Instance)));
            await AwaitWithTimeoutAsync(applied).ConfigureAwait(false);

            Assert.That(server.Space.TryGetNode(sibling.NodeId, out _), Is.True);
            await AssertEffectivePolicyAsync(server).ConfigureAwait(false);
        }

        [Test]
        public async Task ReplicatesValueUpdateAsync()
        {
            await using TwoReplicaFixture fixture = await TwoReplicaFixture.CreateAsync(this).ConfigureAwait(false);

            BaseDataVariableState node = NewVariable("value", 1.0);
            await fixture.SpaceA.AddOrUpdateNodeAsync(node).ConfigureAwait(false);
            await AssertEventuallyAsync(
                () => fixture.SpaceB.TryGetNode(node.NodeId, out _),
                "node should replicate before the value update").ConfigureAwait(false);

            node.Value = new Variant(42.0);
            node.ClearChangeMasks(m_systemContext, false);

            await AssertEventuallyAsync(
                () => fixture.SpaceB.TryGetNode(node.NodeId, out NodeState? remote) &&
                    remote is BaseVariableState variable &&
                    variable.Value.Equals(new Variant(42.0)),
                "value updated on A should replicate to B").ConfigureAwait(false);
        }

        [Test]
        public async Task ReplicatesRemovalAsync()
        {
            await using TwoReplicaFixture fixture = await TwoReplicaFixture.CreateAsync(this).ConfigureAwait(false);

            BaseDataVariableState node = NewVariable("removed", 1.0);
            await fixture.SpaceA.AddOrUpdateNodeAsync(node).ConfigureAwait(false);
            await AssertEventuallyAsync(
                () => fixture.SpaceB.TryGetNode(node.NodeId, out _),
                "node should replicate before removal").ConfigureAwait(false);

            await fixture.SpaceA.RemoveNodeAsync(node.NodeId).ConfigureAwait(false);

            await AssertEventuallyAsync(
                () => !fixture.SpaceB.TryGetNode(node.NodeId, out _),
                "removal on A should replicate to B").ConfigureAwait(false);
        }

        [Test]
        public async Task MultiWriterConvergesBothDirectionsAsync()
        {
            await using TwoReplicaFixture fixture = await TwoReplicaFixture.CreateAsync(this).ConfigureAwait(false);

            BaseDataVariableState onA = NewVariable("from-a", 1.0);
            BaseDataVariableState onB = NewVariable("from-b", 2.0);
            await fixture.SpaceA.AddOrUpdateNodeAsync(onA).ConfigureAwait(false);
            await fixture.SpaceB.AddOrUpdateNodeAsync(onB).ConfigureAwait(false);

            await AssertEventuallyAsync(
                () => fixture.SpaceA.TryGetNode(onB.NodeId, out _) &&
                    fixture.SpaceB.TryGetNode(onA.NodeId, out _),
                "each replica's write should converge on the other (no leader)").ConfigureAwait(false);
        }

        [Test]
        public async Task ConcurrentValueWritesConvergeAsync()
        {
            await using TwoReplicaFixture fixture = await TwoReplicaFixture.CreateAsync(this).ConfigureAwait(false);

            BaseDataVariableState seed = NewVariable("contended", 0.0);
            await fixture.SpaceA.AddOrUpdateNodeAsync(seed).ConfigureAwait(false);
            await AssertEventuallyAsync(
                () => fixture.SpaceB.TryGetNode(seed.NodeId, out _),
                "node should replicate before concurrent writes").ConfigureAwait(false);

            // Concurrent writes on both replicas; last-writer-wins must converge
            // both sides to the same value.
            seed.Value = new Variant(11.0);
            seed.ClearChangeMasks(m_systemContext, false);
            if (fixture.SpaceB.TryGetNode(seed.NodeId, out NodeState? onB) && onB is BaseVariableState bVar)
            {
                bVar.Value = new Variant(22.0);
                bVar.ClearChangeMasks(m_systemContext, false);
            }

            await AssertEventuallyAsync(
                () => fixture.SpaceA.TryGetNode(seed.NodeId, out NodeState? a) &&
                    fixture.SpaceB.TryGetNode(seed.NodeId, out NodeState? b) &&
                    a is BaseVariableState av &&
                    b is BaseVariableState bv &&
                    av.Value.Equals(bv.Value),
                "concurrent value writes must converge to the same value on both replicas").ConfigureAwait(false);
        }

        [Test]
        public async Task RemovedNodesAreDetachedFromTrackingAsync()
        {
            await using var network = new InMemoryNetwork();
            var space = new DictionaryAddressSpace(m_systemContext);
            await using var sync = new ReplicatedAddressSpaceSynchronizer(
                space,
                m_messageContext,
                ReplicaId.FromUInt64(1),
                network.CreateTransport(),
                TimeProvider.System,
                CrdtReaderOptions.Default);
            await sync.SeedOrHydrateAsync().ConfigureAwait(false);
            sync.Start();

            BaseDataVariableState node = NewVariable("tracked", 1.0);
            await space.AddOrUpdateNodeAsync(node).ConfigureAwait(false);
            await AssertEventuallyAsync(
                () => sync.TrackedNodeCount == 1,
                "the local node should be tracked while it exists").ConfigureAwait(false);

            await space.RemoveNodeAsync(node.NodeId).ConfigureAwait(false);
            await AssertEventuallyAsync(
                () => sync.TrackedNodeCount == 0,
                "removing a node should detach its StateChanged subscription").ConfigureAwait(false);

            node.Value = new Variant(9.0);
            node.ClearChangeMasks(m_systemContext, false);
            await Task.Delay(200).ConfigureAwait(false);

            Assert.That(sync.TrackedNodeCount, Is.Zero);
        }

        [Test]
        public async Task ReplicatesReferenceAddAndRemoveAsync()
        {
            await using TwoReplicaFixture fixture = await TwoReplicaFixture.CreateAsync(this).ConfigureAwait(false);

            BaseDataVariableState source = NewVariable("source", 1.0);
            BaseDataVariableState target = NewVariable("target", 2.0);
            await fixture.SpaceA.AddOrUpdateNodeAsync(source).ConfigureAwait(false);
            await fixture.SpaceA.AddOrUpdateNodeAsync(target).ConfigureAwait(false);
            await AssertEventuallyAsync(
                () => fixture.SpaceB.TryGetNode(source.NodeId, out _) &&
                    fixture.SpaceB.TryGetNode(target.NodeId, out _),
                "nodes should replicate before reference changes").ConfigureAwait(false);

            ExpandedNodeId targetId = new(target.NodeId);
            source.AddReference(ReferenceTypeIds.Organizes, false, targetId);
            source.ClearChangeMasks(m_systemContext, false);
            await AssertEventuallyAsync(
                () => fixture.SpaceB.TryGetNode(source.NodeId, out NodeState? mirrored) &&
                    mirrored!.ReferenceExists(ReferenceTypeIds.Organizes, false, targetId),
                "a reference added on A should replicate to B").ConfigureAwait(false);

            source.RemoveReference(ReferenceTypeIds.Organizes, false, targetId);
            source.ClearChangeMasks(m_systemContext, false);
            await AssertEventuallyAsync(
                () => fixture.SpaceB.TryGetNode(source.NodeId, out NodeState? mirrored) &&
                    !mirrored!.ReferenceExists(ReferenceTypeIds.Organizes, false, targetId),
                "a reference removed on A should be removed on B").ConfigureAwait(false);
        }

        [Test]
        public async Task ReconcileMaterializedValuesCorrectsStaleReplicaAndRebroadcastsAsync()
        {
            await using var replicaA = new ControlledReplica(this, 1);
            await using var replicaB = new ControlledReplica(this, 2);
            await replicaA.StartAsync().ConfigureAwait(false);
            await replicaB.StartAsync().ConfigureAwait(false);
            await replicaA.SendToAsync(replicaB).ConfigureAwait(false);
            await replicaB.SendToAsync(replicaA).ConfigureAwait(false);

            BaseDataVariableState source = NewVariable("reconcile", 1.0);
            await replicaA.Space.AddOrUpdateNodeAsync(source).ConfigureAwait(false);
            await replicaA.SendToAsync(replicaB).ConfigureAwait(false);
            await replicaB.SendToAsync(replicaA).ConfigureAwait(false);

            source.Value = new Variant(42.0);
            source.ClearChangeMasks(m_systemContext, false);
            var expected = new DataValue(source.Value, source.StatusCode, source.Timestamp);
            await replicaA.SendToAsync(replicaB).ConfigureAwait(false);
            await replicaB.SendToAsync(replicaA).ConfigureAwait(false);
            AssertValue(replicaB, source.NodeId, in expected);
            Assert.That(replicaA.Transport.HasSentFrame, Is.False);
            Assert.That(replicaB.Transport.HasSentFrame, Is.False);

            BaseVariableState staleVariable = replicaB.GetVariable(source.NodeId);
            staleVariable.Value = new Variant(11.0);
            staleVariable.StatusCode = StatusCodes.BadOutOfService;
            staleVariable.Timestamp = DateTimeUtc.Now;

            int topologyUpdates = 0;
            replicaB.Space.NodeAdded += _ => Interlocked.Increment(ref topologyUpdates);
            long inboundApplyCount = replicaA.Sync.InboundApplyCount;
            Task rebroadcastApplied = replicaA.Sync.WaitForInboundApplyAfterAsync(inboundApplyCount);
            await replicaA.Sync.SeedOrHydrateAsync().ConfigureAwait(false);
            await replicaA.SendToAsync(replicaB).ConfigureAwait(false);

            AssertValue(replicaB, source.NodeId, in expected);
            Assert.That(topologyUpdates, Is.Zero, "reconciliation must not depend on a topology diff");
            Assert.That(rebroadcastApplied.IsCompleted, Is.False, "the correcting frame has not reached A yet");
            await replicaB.SendToAsync(replicaA).ConfigureAwait(false);
            await AwaitWithTimeoutAsync(rebroadcastApplied).ConfigureAwait(false);
            AssertValue(replicaA, source.NodeId, in expected);
            Assert.That(replicaA.Sync.InboundApplyCount, Is.EqualTo(inboundApplyCount + 1));
            Assert.That(replicaA.Transport.HasSentFrame, Is.False);
            Assert.That(replicaB.Transport.HasSentFrame, Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ReconciliationRebroadcastCompletesBeforeReseedAsync(bool registerAfterApply)
        {
            await using var replicaA = new ControlledReplica(this, 1);
            await using var replicaB = new ControlledReplica(this, 2);
            await replicaA.StartAsync().ConfigureAwait(false);
            await replicaB.StartAsync().ConfigureAwait(false);
            await replicaA.SendToAsync(replicaB).ConfigureAwait(false);
            await replicaB.SendToAsync(replicaA).ConfigureAwait(false);

            BaseDataVariableState source = NewVariable("reconcile-before-reseed", 1.0);
            await replicaA.Space.AddOrUpdateNodeAsync(source).ConfigureAwait(false);
            await replicaA.SendToAsync(replicaB).ConfigureAwait(false);

            source.Value = new Variant(42.0);
            source.ClearChangeMasks(m_systemContext, false);
            var expected = new DataValue(source.Value, source.StatusCode, source.Timestamp);
            await replicaA.SendToAsync(replicaB).ConfigureAwait(false);
            AssertValue(replicaB, source.NodeId, in expected);
            BaseVariableState staleVariable = replicaB.GetVariable(source.NodeId);

            staleVariable.Value = new Variant(11.0);
            staleVariable.StatusCode = StatusCodes.Good;
            staleVariable.Timestamp = DateTimeUtc.Now;

            // The delayed echo predates A's write, so A sends its current state back to B.
            await replicaB.SendToAsync(replicaA).ConfigureAwait(false);
            await replicaA.SendToAsync(replicaB).ConfigureAwait(false);
            AssertValue(replicaB, source.NodeId, in expected);

            // Consume the earlier value echo separately from the reconciliation rebroadcast.
            await replicaB.SendToAsync(replicaA).ConfigureAwait(false);
            long correctionApplyCount = replicaA.Sync.InboundApplyCount;
            Task? rebroadcastApplied = registerAfterApply ?
                null :
                replicaA.Sync.WaitForInboundApplyAfterAsync(correctionApplyCount);
            await replicaB.SendToAsync(replicaA).ConfigureAwait(false);
            rebroadcastApplied ??= replicaA.Sync.WaitForInboundApplyAfterAsync(correctionApplyCount);
            await AwaitWithTimeoutAsync(rebroadcastApplied).ConfigureAwait(false);
            AssertValue(replicaA, source.NodeId, in expected);
            Assert.That(replicaA.Sync.InboundApplyCount, Is.EqualTo(correctionApplyCount + 1));
            Assert.That(replicaA.Transport.HasSentFrame, Is.False);
            Assert.That(replicaB.Transport.HasSentFrame, Is.False);

            long inboundApplyCount = replicaA.Sync.InboundApplyCount;
            await replicaA.Sync.SeedOrHydrateAsync().ConfigureAwait(false);
            await replicaA.SendToAsync(replicaB).ConfigureAwait(false);

            AssertValue(replicaB, source.NodeId, in expected);
            Assert.That(replicaA.Transport.HasSentFrame, Is.False);
            Assert.That(replicaB.Transport.HasSentFrame, Is.False);
            Assert.That(replicaA.Sync.InboundApplyCount, Is.EqualTo(inboundApplyCount),
                "a peer that was already reconciled must not echo the no-op reseed");
        }

        [Test]
        public void ConstructorRejectsNullAddressSpace()
        {
            Assert.That(
                () => new ReplicatedAddressSpaceSynchronizer(
                    null!, m_messageContext, ReplicaId.FromUInt64(1), null!,
                    TimeProvider.System, CrdtReaderOptions.Default),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("addressSpace"));
        }

        [Test]
        public void ConstructorRejectsNullMessageContext()
        {
            var space = new DictionaryAddressSpace(m_systemContext);
            Assert.That(
                () => new ReplicatedAddressSpaceSynchronizer(
                    space, null!, ReplicaId.FromUInt64(1), null!,
                    TimeProvider.System, CrdtReaderOptions.Default),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("messageContext"));
        }

        [Test]
        public void ConstructorRejectsNullTransport()
        {
            var space = new DictionaryAddressSpace(m_systemContext);
            Assert.That(
                () => new ReplicatedAddressSpaceSynchronizer(
                    space, m_messageContext, ReplicaId.FromUInt64(1), null!,
                    TimeProvider.System, CrdtReaderOptions.Default),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("transport"));
        }

        [Test]
        public async Task StartIsIdempotentAsync()
        {
            await using var network = new InMemoryNetwork();
            var space = new DictionaryAddressSpace(m_systemContext);
            await using var sync = new ReplicatedAddressSpaceSynchronizer(
                space, m_messageContext, ReplicaId.FromUInt64(1),
                network.CreateTransport(), TimeProvider.System, CrdtReaderOptions.Default);
            await sync.SeedOrHydrateAsync().ConfigureAwait(false);

            sync.Start();

            Assert.That(sync.Start, Throws.Nothing);
        }

        [Test]
        public async Task DisposeAsyncIsIdempotentAsync()
        {
            await using var network = new InMemoryNetwork();
            var space = new DictionaryAddressSpace(m_systemContext);
            await using var sync = new ReplicatedAddressSpaceSynchronizer(
                space, m_messageContext, ReplicaId.FromUInt64(1),
                network.CreateTransport(), TimeProvider.System, CrdtReaderOptions.Default);
            await sync.SeedOrHydrateAsync().ConfigureAwait(false);
            sync.Start();

            await sync.DisposeAsync().ConfigureAwait(false);

            Assert.That(
                async () => await sync.DisposeAsync().ConfigureAwait(false),
                Throws.Nothing);
        }

        private static BaseDataVariableState NewVariable(string id, double value, NodeState? parent = null)
        {
            return new BaseDataVariableState(parent)
            {
                NodeId = new NodeId(id, NamespaceIndex),
                BrowseName = new QualifiedName(id, NamespaceIndex),
                DisplayName = new LocalizedText(id),
                DataType = DataTypeIds.Double,
                ValueRank = ValueRanks.Scalar,
                Value = new Variant(value)
            };
        }

        /// <summary>
        /// Creates independently protected descendants with permissive ordinary read/write access bits.
        /// </summary>
        private static BaseDataVariableState NewPolicyTree(bool restricted = true)
        {
            BaseDataVariableState root = NewVariable("secure-root", 42.0);
            root.AccessLevel = AccessLevels.CurrentReadOrWrite;
            root.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            BaseDataVariableState child = NewVariable("secure-child", 42.0, root);
            child.AccessLevel = AccessLevels.CurrentReadOrWrite;
            child.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            BaseDataVariableState grandchild = NewVariable("secure-grandchild", 42.0, child);
            grandchild.AccessLevel = AccessLevels.CurrentReadOrWrite;
            grandchild.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            child.AddChild(grandchild);
            root.AddChild(child);
            if (restricted)
            {
                root.RolePermissions = AdminPermissions(PermissionType.Read | PermissionType.Write);
                root.AccessRestrictions =
                    AccessRestrictionType.EncryptionRequired | AccessRestrictionType.SessionRequired;
                SetChildPolicy(child);
                grandchild.UserRolePermissions = AdminPermissions(PermissionType.Read | PermissionType.Write);
                grandchild.AccessRestrictions = AccessRestrictionType.EncryptionRequired;
            }
            return root;
        }

        /// <summary>
        /// Grants a broad general mask but a narrower user mask, so loss of either mask is observable.
        /// </summary>
        private static void SetChildPolicy(BaseVariableState child)
        {
            child.RolePermissions =
            [
                new RolePermissionType
                {
                    RoleId = ObjectIds.WellKnownRole_SecurityAdmin,
                    Permissions = (uint)(PermissionType.Read | PermissionType.Write)
                },
                new RolePermissionType
                {
                    RoleId = ObjectIds.WellKnownRole_AuthenticatedUser,
                    Permissions = (uint)(PermissionType.Read | PermissionType.Write)
                }
            ];
            child.UserRolePermissions = AdminPermissions(PermissionType.Read);
            child.AccessRestrictions =
                AccessRestrictionType.SigningRequired | AccessRestrictionType.EncryptionRequired;
        }

        /// <summary>
        /// Creates a role mask for the privileged actor used by the actual service dispatcher.
        /// </summary>
        private static ArrayOf<RolePermissionType> AdminPermissions(PermissionType permissions)
        {
            return
            [
                new RolePermissionType
                {
                    RoleId = ObjectIds.WellKnownRole_SecurityAdmin,
                    Permissions = (uint)permissions
                }
            ];
        }

        /// <summary>
        /// Proves effective read, write, user-role intersection and channel restrictions on materialized peer state.
        /// </summary>
        private static async Task AssertEffectivePolicyAsync(AuthorizationServer server)
        {
            foreach (string id in new[] { "secure-root", "secure-child", "secure-grandchild" })
            {
                var nodeId = new NodeId(id, NamespaceIndex);
                DataValue denied = await server.ReadAsync(
                    nodeId, ObjectIds.WellKnownRole_AuthenticatedUser).ConfigureAwait(false);
                Assert.That(denied.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied), id);
                Assert.That(denied.WrappedValue.IsNull, Is.True, id);
                Assert.That(
                    await server.WriteAsync(nodeId, ObjectIds.WellKnownRole_AuthenticatedUser, 99.0)
                        .ConfigureAwait(false),
                    Is.EqualTo(StatusCodes.BadUserAccessDenied), id);
                DataValue allowed = await server.ReadAsync(
                    nodeId, ObjectIds.WellKnownRole_SecurityAdmin).ConfigureAwait(false);
                Assert.That(allowed.StatusCode, Is.EqualTo(StatusCodes.Good), id);
                Assert.That(allowed.WrappedValue, Is.EqualTo(new Variant(42.0)), id);
                DataValue insecure = await server.ReadAsync(
                    nodeId, ObjectIds.WellKnownRole_SecurityAdmin, MessageSecurityMode.Sign).ConfigureAwait(false);
                Assert.That(insecure.StatusCode, Is.EqualTo(StatusCodes.BadSecurityModeInsufficient), id);
            }
            Assert.That(
                await server.WriteAsync(
                    new NodeId("secure-child", NamespaceIndex), ObjectIds.WellKnownRole_SecurityAdmin, 99.0)
                    .ConfigureAwait(false),
                Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(
                await server.WriteAsync(
                    new NodeId("secure-root", NamespaceIndex), ObjectIds.WellKnownRole_SecurityAdmin, 43.0)
                    .ConfigureAwait(false),
                Is.EqualTo(StatusCodes.Good));
            DataValue changed = await server.ReadAsync(
                new NodeId("secure-root", NamespaceIndex),
                ObjectIds.WellKnownRole_SecurityAdmin).ConfigureAwait(false);
            Assert.That(changed.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(changed.WrappedValue, Is.EqualTo(new Variant(43.0)));
        }

        /// <summary>
        /// Waits for a particular store change to finish processing without polling or sleeping.
        /// </summary>
        private static Task<bool> WaitForStoreApplyAsync(
            AddressSpaceSynchronizer synchronizer,
            NodeStateChangeKind kind,
            NodeId nodeId)
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnApplied(NodeStateChange change)
            {
                if (change.Kind == kind && change.NodeId == nodeId)
                {
                    synchronizer.InboundApplied -= OnApplied;
                    completion.TrySetResult(true);
                }
            }
            synchronizer.InboundApplied += OnApplied;
            return completion.Task;
        }

        private static async Task AssertEventuallyAsync(Func<bool> condition, string message)
        {
            // CRDT convergence over the in-memory gossip network is normally sub-second,
            // but the background capture/broadcast loops can be starved of CPU on a heavily
            // loaded CI runner (the full test matrix runs dozens of jobs per runner). Allow a
            // generous deadline so the assertion measures convergence correctness, not runner load.
            DateTime deadline = DateTime.UtcNow + GetReplicationTimeout();
            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                {
                    return;
                }
                await Task.Delay(25).ConfigureAwait(false);
            }
            Assert.Fail(message);
        }

        private static async Task AwaitWithTimeoutAsync(Task task)
        {
            // Same rationale as AssertEventuallyAsync above: this waits on the very
            // same background capture/broadcast loops, so it needs the same allowance
            // for a CPU-starved runner. The assertion still requires the replication
            // to complete - only the patience is bounded, not the outcome.
            Task completed = await Task.WhenAny(task, Task.Delay(GetReplicationTimeout()))
                .ConfigureAwait(false);
            Assert.That(completed, Is.SameAs(task), "replication did not complete within the timeout");
            await task.ConfigureAwait(false);
        }

        private static TimeSpan GetReplicationTimeout()
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ?
                TimeSpan.FromSeconds(90) :
                TimeSpan.FromSeconds(30);
        }

        private static void AssertValue(ControlledReplica replica, NodeId nodeId, in DataValue expected)
        {
            BaseVariableState actual = replica.GetVariable(nodeId);
            Assert.That(actual.Value, Is.EqualTo(expected.WrappedValue));
            Assert.That(actual.StatusCode, Is.EqualTo(expected.StatusCode));
            Assert.That(actual.Timestamp, Is.EqualTo(expected.SourceTimestamp));
        }

        private sealed class ControlledReplica : IAsyncDisposable
        {
            /// <summary>
            /// Uses either the dictionary fixture or a production node-manager address-space adapter.
            /// </summary>
            public ControlledReplica(
                ReplicatedAddressSpaceSynchronizerTests test,
                ulong replicaId,
                ILocalAddressSpace? addressSpace = null)
            {
                Space = addressSpace ?? new DictionaryAddressSpace(test.m_systemContext);
                Transport = new ControlledTransport();
                Sync = new ReplicatedAddressSpaceSynchronizer(
                    Space, test.m_messageContext, ReplicaId.FromUInt64(replicaId),
                    Transport, TimeProvider.System, CrdtReaderOptions.Default);
            }

            /// <summary>
            /// The materialized address space owned by this replica.
            /// </summary>
            public ILocalAddressSpace Space { get; }

            public ControlledTransport Transport { get; }

            public ReplicatedAddressSpaceSynchronizer Sync { get; }

            public async Task StartAsync()
            {
                await Sync.SeedOrHydrateAsync().ConfigureAwait(false);
                Sync.Start();
            }

            public async Task SendToAsync(ControlledReplica peer)
            {
                long observedCount = peer.Sync.InboundApplyCount;
                Task applied = peer.Sync.WaitForInboundApplyAfterAsync(observedCount);
                peer.Transport.Receive(Transport.TakeSentFrame().ToArray());
                await AwaitWithTimeoutAsync(applied).ConfigureAwait(false);
                Assert.That(peer.Sync.InboundApplyCount, Is.EqualTo(observedCount + 1));
            }

            public BaseVariableState GetVariable(NodeId nodeId)
            {
                Assert.That(Space.TryGetNode(nodeId, out NodeState? node), Is.True);
                Assert.That(node, Is.InstanceOf<BaseVariableState>());
                return (BaseVariableState)node!;
            }

            public ValueTask DisposeAsync()
            {
                return Sync.DisposeAsync();
            }
        }

        private sealed class ControlledTransport : ITransport
        {
            public event Action<ReadOnlyMemory<byte>>? FrameReceived;

            public bool HasSentFrame => m_sent.Reader.TryPeek(out _);

            public ByteString TakeSentFrame()
            {
                Assert.That(m_sent.Reader.TryRead(out ByteString frame), Is.True,
                    "the completed operation must have sent a frame");
                return frame;
            }

            /// <summary>
            /// Delivers one complete wire frame without a listening socket or a scheduling delay.
            /// </summary>
            public void Receive(ReadOnlyMemory<byte> frame)
            {
                FrameReceived?.Invoke(frame);
            }

            public ValueTask StartAsync(CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                return default;
            }

            public ValueTask SendAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
            {
                Assert.That(FrameCodec.Decode(frame).MessageType, Is.EqualTo(MessageType.State));
                return m_sent.Writer.WriteAsync(new ByteString(frame.ToArray()), ct);
            }

            public ValueTask DisposeAsync()
            {
                m_sent.Writer.TryComplete();
                return default;
            }

            private readonly Channel<ByteString> m_sent = Channel.CreateUnbounded<ByteString>();
        }

        /// <summary>
        /// Hosts real node registration and service dispatch with no listener, PKI or namespace default ACL.
        /// </summary>
        private sealed class AuthorizationServer : IAsyncDisposable
        {
            /// <summary>
            /// Creates an isolated server and its real application node manager.
            /// </summary>
            public AuthorizationServer()
            {
                ITelemetryContext telemetry = NUnitTelemetryContext.Create();
                var namespaces = new NamespaceTable();
                namespaces.GetIndexOrAppend("urn:test:crdt");
                var server = new Mock<IServerInternal>();
                server.Setup(value => value.Telemetry).Returns(telemetry);
                server.Setup(value => value.NamespaceUris).Returns(namespaces);
                server.Setup(value => value.ServerUris).Returns(new StringTable());
                server.Setup(value => value.Factory).Returns(EncodeableFactory.Create());
                server.Setup(value => value.TypeTree).Returns(new TypeTable(namespaces));
                server.Setup(value => value.DefaultSystemContext).Returns(new ServerSystemContext(server.Object));
                var configurationManager = new Mock<IConfigurationNodeManager>();
                configurationManager.Setup(value => value.NamespaceUris).Returns(Array.Empty<string>());
                var coreManager = new Mock<ICoreNodeManager>();
                var factory = new Mock<IMainNodeManagerFactory>();
                factory.Setup(value => value.CreateConfigurationNodeManager()).Returns(configurationManager.Object);
                factory.Setup(value => value.CreateCoreNodeManager(It.IsAny<ushort>())).Returns(coreManager.Object);
                server.Setup(value => value.MainNodeManagerFactory).Returns(factory.Object);
                var nodeManager = new AuthorizationNodeManager(server.Object);
#pragma warning disable CS0618 // Standalone fixture; TODO: replace when the compatibility constructor is removed.
                m_dispatcher = new MasterNodeManager(
                    server.Object,
                    new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() },
                    null,
                    [nodeManager]);
#pragma warning restore CS0618
                server.Setup(value => value.NodeManager).Returns(m_dispatcher);
                Space = ((ILocalAddressSpaceSource)nodeManager).CreateLocalAddressSpace();
            }

            /// <summary>
            /// Exposes the production registration adapter used by the synchronizers.
            /// </summary>
            public ILocalAddressSpace Space { get; }

            /// <summary>
            /// Reads through the real master dispatcher as a distinct role-bearing actor.
            /// </summary>
            public async Task<DataValue> ReadAsync(
                NodeId nodeId,
                NodeId roleId,
                MessageSecurityMode securityMode = MessageSecurityMode.SignAndEncrypt)
            {
                using OperationContext context = CreateOperation(roleId, securityMode, RequestType.Read);
                (ArrayOf<DataValue> values, _) = await m_dispatcher.ReadAsync(
                    context,
                    0,
                    TimestampsToReturn.Neither,
                    [new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value }]).ConfigureAwait(false);
                return values[0];
            }

            /// <summary>
            /// Writes through the same production permission and access-level guards as a remote client.
            /// </summary>
            public async Task<StatusCode> WriteAsync(NodeId nodeId, NodeId roleId, double value)
            {
                using OperationContext context = CreateOperation(
                    roleId, MessageSecurityMode.SignAndEncrypt, RequestType.Write);
                (ArrayOf<StatusCode> results, _) = await m_dispatcher.WriteAsync(
                    context,
                    [
                        new WriteValue
                        {
                            NodeId = nodeId,
                            AttributeId = Attributes.Value,
                            Value = new DataValue(new Variant(value))
                        }
                    ]).ConfigureAwait(false);
                return results[0];
            }

            /// <inheritdoc/>
            public ValueTask DisposeAsync()
            {
                return m_dispatcher.DisposeAsync();
            }

            /// <summary>
            /// Models separate accepted sessions and channels without bypassing authorization.
            /// </summary>
            private static OperationContext CreateOperation(
                NodeId roleId,
                MessageSecurityMode securityMode,
                RequestType requestType)
            {
                var identity = new Mock<IUserIdentity>();
                identity.Setup(value => value.GrantedRoleIds).Returns([roleId]);
                var session = new Mock<ISession>();
                session.Setup(value => value.EffectiveIdentity).Returns(identity.Object);
                session.Setup(value => value.PreferredLocales).Returns([]);
                var channel = new SecureChannelContext(
                    "security-f07", new EndpointDescription { SecurityMode = securityMode }, RequestEncoding.Binary);
                return new OperationContext(
                    new RequestHeader(), channel, requestType, RequestLifetime.None, session.Object);
            }

            /// <summary>
            /// Dispatches real service operations to this server's application address space.
            /// </summary>
            private readonly MasterNodeManager m_dispatcher;
        }

        /// <summary>
        /// Uses the standard asynchronous predefined-node registration and service implementation.
        /// </summary>
        private sealed class AuthorizationNodeManager : AsyncCustomNodeManager
        {
            /// <summary>
            /// Binds the application namespace to an isolated server.
            /// </summary>
            public AuthorizationNodeManager(IServerInternal server)
                : base(server, "urn:test:crdt")
            {
            }
        }

        private sealed class TwoReplicaFixture : IAsyncDisposable
        {
            public DictionaryAddressSpace SpaceA { get; private set; } = null!;
            public DictionaryAddressSpace SpaceB { get; private set; } = null!;

            public static async Task<TwoReplicaFixture> CreateAsync(ReplicatedAddressSpaceSynchronizerTests test)
            {
                var fixture = new TwoReplicaFixture
                {
                    m_network = new InMemoryNetwork(),
                    SpaceA = new DictionaryAddressSpace(test.m_systemContext),
                    SpaceB = new DictionaryAddressSpace(test.m_systemContext)
                };

                fixture.m_syncA = new ReplicatedAddressSpaceSynchronizer(
                    fixture.SpaceA, test.m_messageContext, ReplicaId.FromUInt64(1),
                    fixture.m_network.CreateTransport(), TimeProvider.System, CrdtReaderOptions.Default);
                fixture.m_syncB = new ReplicatedAddressSpaceSynchronizer(
                    fixture.SpaceB, test.m_messageContext, ReplicaId.FromUInt64(2),
                    fixture.m_network.CreateTransport(), TimeProvider.System, CrdtReaderOptions.Default);

                // Start both transports first so neither misses the other's
                // broadcasts, then begin capturing/applying changes.
                await fixture.m_syncA.SeedOrHydrateAsync().ConfigureAwait(false);
                await fixture.m_syncB.SeedOrHydrateAsync().ConfigureAwait(false);
                fixture.m_syncA.Start();
                fixture.m_syncB.Start();
                return fixture;
            }

            public async ValueTask DisposeAsync()
            {
                if (m_syncA != null)
                {
                    await m_syncA.DisposeAsync().ConfigureAwait(false);
                }
                if (m_syncB != null)
                {
                    await m_syncB.DisposeAsync().ConfigureAwait(false);
                }
                if (m_network != null)
                {
                    await m_network.DisposeAsync().ConfigureAwait(false);
                }
            }

            private InMemoryNetwork? m_network;
            private ReplicatedAddressSpaceSynchronizer? m_syncA;
            private ReplicatedAddressSpaceSynchronizer? m_syncB;
        }
    }
}
