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
using System.Threading.Tasks;
using Crdt;
using Crdt.Transport;
using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.Redundancy.Server.Tests.Identity
{
    /// <summary>
    /// Exercises exact identity preservation and admission over the real CRDT serialization and framing pipeline.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class ReplicaIdentityGossipTests
    {
        /// <summary>
        /// Preserves a generated subtree and NodeId-valued data while excluding replica-local nodes.
        /// </summary>
        [TestCase(NodeIdAssignmentMode.Numeric)]
        [TestCase(NodeIdAssignmentMode.String)]
        [TestCase(NodeIdAssignmentMode.Guid)]
        [TestCase(NodeIdAssignmentMode.Opaque)]
        public async Task GossipPreservesGeneratedSubtreeIdentitiesAsync(NodeIdAssignmentMode mode)
        {
            var identityA = new ReplicaNodeIdFactory("gossip-set", [SharedUri], mode);
            var identityB = new ReplicaNodeIdFactory("gossip-set", [SharedUri], mode);
            (IServiceMessageContext messagesA, SystemContext contextA) = CreateContext(identityA, "urn:replica:a");
            (IServiceMessageContext messagesB, SystemContext contextB) = CreateContext(identityB, "urn:replica:b");
            var spaceA = new DictionaryAddressSpace(contextA);
            var spaceB = new DictionaryAddressSpace(contextB);
            BaseObjectState root = CreateTree(identityA, contextA);
            await spaceA.AddOrUpdateNodeAsync(root).ConfigureAwait(false);
            var local = new BaseObjectState(null)
            {
                NodeId = new NodeId("diagnostic", 1),
                BrowseName = new QualifiedName("Diagnostic", 1)
            };
            await spaceA.AddOrUpdateNodeAsync(local).ConfigureAwait(false);
            await using var network = new InMemoryNetwork();
            await using var syncA = new ReplicatedAddressSpaceSynchronizer(
                spaceA, messagesA, ReplicaId.FromUInt64(1), network.CreateTransport(),
                TimeProvider.System, CrdtReaderOptions.Default, identityA);
            await using var syncB = new ReplicatedAddressSpaceSynchronizer(
                spaceB, messagesB, ReplicaId.FromUInt64(2), network.CreateTransport(),
                TimeProvider.System, CrdtReaderOptions.Default, identityB);
            var hydrated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            syncA.InboundRejected += exception => hydrated.TrySetException(exception);
            syncB.InboundRejected += exception => hydrated.TrySetException(exception);
            syncB.InboundApplied += () =>
            {
                if (spaceB.TryGetNode(root.NodeId, out _))
                {
                    hydrated.TrySetResult(true);
                }
            };

            await syncA.SeedOrHydrateAsync().ConfigureAwait(false);
            await syncB.SeedOrHydrateAsync().ConfigureAwait(false);
            syncA.Start();
            syncB.Start();
            await hydrated.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);

            Assert.That(spaceB.TryGetNode(root.NodeId, out NodeState? remote), Is.True);
            Assert.That(remote!.NodeId, Is.EqualTo(root.NodeId));
            var originalChildren = new List<BaseInstanceState>();
            var remoteChildren = new List<BaseInstanceState>();
            root.GetChildren(contextA, originalChildren);
            remote.GetChildren(contextB, remoteChildren);
            Assert.That(remoteChildren, Has.Count.EqualTo(2));
            for (int i = 0; i < originalChildren.Count; i++)
            {
                Assert.That(remoteChildren[i].NodeId, Is.EqualTo(originalChildren[i].NodeId));
                Assert.That(remoteChildren[i].BrowseName, Is.EqualTo(originalChildren[i].BrowseName));
            }
            Assert.That(((BaseVariableState)remoteChildren[1]).Value,
                Is.EqualTo(Variant.From(originalChildren[0].NodeId)));
            Assert.That(spaceB.TryGetNode(local.NodeId, out _), Is.False);
            Assert.That(contextB.NamespaceUris.GetString(1), Is.EqualTo("urn:replica:b"));
            Assert.That(identityA.Descriptor, Is.EqualTo(identityB.Descriptor));
        }

        /// <summary>
        /// Rejects an incompatible peer before merging, changing the graph, or advancing successful apply state.
        /// </summary>
        [TestCase("other-set", SharedUri, NodeIdAssignmentMode.Numeric)]
        [TestCase("gossip-set", "urn:gossip:other", NodeIdAssignmentMode.Numeric)]
        [TestCase("gossip-set", SharedUri, NodeIdAssignmentMode.Guid)]
        public async Task IncompatiblePeerIsRejectedBeforeApplyAsync(
            string peerSet,
            string peerNamespace,
            NodeIdAssignmentMode peerMode)
        {
            var identityA = new ReplicaNodeIdFactory("gossip-set", [SharedUri]);
            var identityB = new ReplicaNodeIdFactory(peerSet, [peerNamespace], peerMode);
            (IServiceMessageContext messagesA, SystemContext contextA) = CreateContext(identityA, "urn:replica:a");
            (IServiceMessageContext messagesB, SystemContext contextB) = CreateContext(identityB, "urn:replica:b");
            var spaceA = new DictionaryAddressSpace(contextA);
            var spaceB = new DictionaryAddressSpace(contextB);
            BaseObjectState retained = CreateTree(identityA, contextA);
            await spaceA.AddOrUpdateNodeAsync(retained).ConfigureAwait(false);
            BaseObjectState peerRoot = CreateTree(identityB, contextB);
            await spaceB.AddOrUpdateNodeAsync(peerRoot).ConfigureAwait(false);
            await using var network = new InMemoryNetwork();
            await using var syncA = new ReplicatedAddressSpaceSynchronizer(
                spaceA, messagesA, ReplicaId.FromUInt64(1), network.CreateTransport(),
                TimeProvider.System, CrdtReaderOptions.Default, identityA);
            await using var syncB = new ReplicatedAddressSpaceSynchronizer(
                spaceB, messagesB, ReplicaId.FromUInt64(2), network.CreateTransport(),
                TimeProvider.System, CrdtReaderOptions.Default, identityB);
            var rejected = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
            syncA.InboundRejected += exception => rejected.TrySetResult(exception);
            await syncA.SeedOrHydrateAsync().ConfigureAwait(false);
            await syncB.SeedOrHydrateAsync().ConfigureAwait(false);
            syncA.Start();
            syncB.Start();
            Exception failure = await rejected.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);

            Assert.That(failure, Is.TypeOf<ServiceResultException>()
                .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadConfigurationError));
            Assert.That(identityA.IsCompatible, Is.False);
            Assert.That(syncA.InboundApplyCount, Is.Zero);
            Assert.That(spaceA.TryGetNode(retained.NodeId, out NodeState? unchanged), Is.True);
            Assert.That(unchanged, Is.SameAs(retained));
            Assert.That(spaceA.Nodes, Has.Exactly(1).Items);
        }

        private static BaseObjectState CreateTree(ReplicaNodeIdFactory identity, ISystemContext context)
        {
            IRebasableNodeIdFactory factory = identity.WithDefaultNamespaceIndex(2);
            var root = new BaseObjectState(null)
            {
                BrowseName = new QualifiedName("Machine", 2),
                DisplayName = new LocalizedText("Machine"),
                TypeDefinitionId = ObjectTypeIds.BaseObjectType
            };
            root.NodeId = factory.New(context, root);
            var value = new BaseDataVariableState(root)
            {
                BrowseName = new QualifiedName("Value", 2),
                DisplayName = new LocalizedText("Value"),
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.Scalar,
                Value = Variant.From(42)
            };
            value.NodeId = factory.New(context, value);
            root.AddChild(value);
            var target = new BaseDataVariableState(root)
            {
                BrowseName = new QualifiedName("Target", 2),
                DisplayName = new LocalizedText("Target"),
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                DataType = DataTypeIds.NodeId,
                ValueRank = ValueRanks.Scalar,
                Value = Variant.From(value.NodeId)
            };
            target.NodeId = factory.New(context, target);
            root.AddChild(target);
            identity.ValidateRegistration(context, root);
            return root;
        }

        private static (IServiceMessageContext Messages, SystemContext Context) CreateContext(
            ReplicaNodeIdFactory identity,
            string applicationUri)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var messages = ServiceMessageContext.CreateEmpty(telemetry);
            messages.NamespaceUris.Append(applicationUri);
            identity.PrepareNamespaces(messages.NamespaceUris);
            return (messages, new SystemContext(telemetry)
            {
                NamespaceUris = messages.NamespaceUris,
                ServerUris = messages.ServerUris,
                EncodeableFactory = messages.Factory
            });
        }

        private const string SharedUri = "urn:gossip:shared";
    }
}
