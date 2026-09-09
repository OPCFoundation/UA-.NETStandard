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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using ClientSession = Opc.Ua.Client.ISession;

namespace Opc.Ua.Redundancy.Server.Tests.Identity
{
    /// <summary>
    /// Exercises replica identity through real UA-TCP clients and independent server lifetimes.
    /// </summary>
    [TestFixture]
    [Category("ReplicaNodeIdentity")]
    [Category("Integration")]
    [NonParallelizable]
    public sealed class ReplicaNodeIdentityIntegrationTests
    {
        /// <summary>
        /// Preserves browsed wire identities, reference targets, reads and monitored items across replicas and restart.
        /// </summary>
        /// <remarks>
        /// This exercises client endpoint failover, not leader election or transfer of an existing subscription.
        /// After the initial comparison, clients use only the NodeIds saved from the first server.
        /// </remarks>
        /// <exception cref="InvalidOperationException"></exception>
        [TestCase(NodeIdAssignmentMode.Numeric, IdType.Numeric)]
        [TestCase(NodeIdAssignmentMode.String, IdType.String)]
        [TestCase(NodeIdAssignmentMode.Guid, IdType.Guid)]
        [TestCase(NodeIdAssignmentMode.Opaque, IdType.Opaque)]
        public async Task SavedWireNodeIdsSurviveReplicaFailoverAndRestartAsync(
            NodeIdAssignmentMode mode,
            IdType expectedIdType)
        {
            await using var first = new ReplicaServer(FirstApplicationUri, mode, false, 1);
            await using var second = new ReplicaServer(SecondApplicationUri, mode, true, 7);
            await using var client = new ReplicaClient();
            await first.StartAsync().ConfigureAwait(false);
            await second.StartAsync().ConfigureAwait(false);
            await client.InitializeAsync().ConfigureAwait(false);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            CancellationToken ct = timeout.Token;

            Dictionary<string, BrowsedNode> saved;
            await using (ClientSession firstSession = await client.ConnectAsync(first.Endpoint, ct)
                .ConfigureAwait(false))
            await using (ClientSession secondSession = await client.ConnectAsync(second.Endpoint, ct)
                .ConfigureAwait(false))
            {
                await AssertNamespaceArrayAsync(firstSession, FirstApplicationUri, ct).ConfigureAwait(false);
                await AssertNamespaceArrayAsync(secondSession, SecondApplicationUri, ct).ConfigureAwait(false);
                saved = await BrowseSharedGraphAsync(firstSession, 1, ct).ConfigureAwait(false);
                Dictionary<string, BrowsedNode> other = await BrowseSharedGraphAsync(secondSession, 7, ct)
                    .ConfigureAwait(false);

                AssertSharedGraphShape(saved, expectedIdType);
                AssertSharedGraphShape(other, expectedIdType);
                AssertSameWireGraph(saved, other);
                Assert.That(first.CreatedManagerCount, Is.EqualTo(3));
                Assert.That(second.CreatedManagerCount, Is.EqualTo(3));
                await AssertSavedReadsAsync(firstSession, saved, ct).ConfigureAwait(false);
                await AssertSavedReadsAsync(secondSession, saved, ct).ConfigureAwait(false);
                await firstSession.CloseAsync(ct).ConfigureAwait(false);
                await secondSession.CloseAsync(ct).ConfigureAwait(false);
            }

            int originalPort = first.Port;
            string originalEndpointUrl = first.Endpoint.EndpointUrl ??
                throw new InvalidOperationException("The first replica has no endpoint URL.");
            NodeId savedReadingId = saved[PlantRootName + "/Pump/Reading"].NodeId;
            await first.StopAsync().ConfigureAwait(false);

            await using (ClientSession survivor = await client.ConnectAsync(second.Endpoint, ct).ConfigureAwait(false))
            {
                await AssertSavedReadsAsync(survivor, saved, ct).ConfigureAwait(false);
                await AssertSavedSubscriptionAsync(survivor, savedReadingId, 73, ct).ConfigureAwait(false);
                await survivor.CloseAsync(ct).ConfigureAwait(false);
            }

            await using var restarted = new ReplicaServer(FirstApplicationUri, mode, true, 11);
            await restarted.StartAsync(originalPort).ConfigureAwait(false);
            Assert.That(restarted.Endpoint.EndpointUrl, Is.EqualTo(originalEndpointUrl));
            await using ClientSession session = await client.ConnectAsync(restarted.Endpoint, ct)
                .ConfigureAwait(false);
            await AssertNamespaceArrayAsync(session, FirstApplicationUri, ct).ConfigureAwait(false);
            await AssertSavedReadsAsync(session, saved, ct).ConfigureAwait(false);
            await AssertSavedSubscriptionAsync(session, savedReadingId, 91, ct).ConfigureAwait(false);
            await session.CloseAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Rejects a shared namespace occupying the replica-local slot before any application node manager is created.
        /// </summary>
        [Test]
        public async Task StartupRejectsSharedNamespaceInReplicaLocalSlotAsync()
        {
            await using var replica = new ReplicaServer(
                ModelNamespaceUri,
                NodeIdAssignmentMode.Numeric,
                false,
                1);

            await Assert.ThatAsync(
                async () => await replica.StartAsync().ConfigureAwait(false),
                Throws.InstanceOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadConfigurationError)).ConfigureAwait(false);

            Assert.That(replica.CreatedManagerCount, Is.Zero);
        }

        /// <summary>
        /// Keeps a service-created identifier readable across a real lifecycle replacement and preserves later capture.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ServiceCreatedNodeIdsSurviveLiveManagerReloadAsync(bool activeActive)
        {
            await using var replica = new ReplicaServer(
                FirstApplicationUri, NodeIdAssignmentMode.Numeric, false, 1);
            await using var client = new ReplicaClient();
            await replica.StartAsync().ConfigureAwait(false);
            await replica.EnableReplicationAsync(activeActive).ConfigureAwait(false);
            await client.InitializeAsync().ConfigureAwait(false);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            CancellationToken ct = timeout.Token;
            await using ClientSession session = await client.ConnectAsync(replica.Endpoint, ct).ConfigureAwait(false);
            Dictionary<string, BrowsedNode> saved = await BrowseSharedGraphAsync(session, 1, ct).ConfigureAwait(false);
            NodeId parent = saved[PlantRootName + "/Pump"].NodeId;
            NodeId dynamicId = await AddRuntimeNodeAsync(session, parent, "Runtime", ct).ConfigureAwait(false);
            await replica.FlushReplicationAsync(dynamicId, ct).ConfigureAwait(false);
            await replica.ReloadInstancesAsync(ct).ConfigureAwait(false);
            await ReadRuntimeNodeAsync(session, dynamicId, ct).ConfigureAwait(false);
            ArrayOf<ReferenceDescription> retainedReferences = await BrowseReferencesAsync(session, parent, ct)
                .ConfigureAwait(false);
            Assert.That(retainedReferences.ToList().Any(reference => reference.NodeId.InnerNodeId == dynamicId),
                Is.True, "The retained node must remain a child of its original parent after hydration.");
            await AssertSavedReadsAsync(session, saved, ct).ConfigureAwait(false);

            NodeId nextId = await AddRuntimeNodeAsync(session, parent, "AfterReload", ct).ConfigureAwait(false);
            await replica.FlushReplicationAsync(nextId, ct).ConfigureAwait(false);
            await replica.AssertStoredIdentityAsync(dynamicId, ct).ConfigureAwait(false);
            await replica.ReloadInstancesAsync(ct).ConfigureAwait(false);
            await ReadRuntimeNodeAsync(session, dynamicId, ct).ConfigureAwait(false);
            await ReadRuntimeNodeAsync(session, nextId, ct).ConfigureAwait(false);
            await session.CloseAsync(ct).ConfigureAwait(false);
        }

        private static async Task<NodeId> AddRuntimeNodeAsync(
            ClientSession session,
            NodeId parent,
            string name,
            CancellationToken ct)
        {
            AddNodesResponse response = await session.AddNodesAsync(null,
                [
                    new AddNodesItem
                    {
                        ParentNodeId = parent,
                        ReferenceTypeId = ReferenceTypeIds.HasComponent,
                        BrowseName = new QualifiedName(name, parent.NamespaceIndex),
                        NodeClass = NodeClass.Variable,
                        TypeDefinition = VariableTypeIds.BaseDataVariableType,
                        NodeAttributes = new ExtensionObject(new VariableAttributes
                        {
                            SpecifiedAttributes = (uint)(NodeAttributesMask.DisplayName |
                                NodeAttributesMask.Value |
                                NodeAttributesMask.DataType |
                                NodeAttributesMask.ValueRank |
                                NodeAttributesMask.AccessLevel |
                                NodeAttributesMask.UserAccessLevel),
                            DisplayName = new LocalizedText(name),
                            DataType = DataTypeIds.Int32,
                            ValueRank = ValueRanks.Scalar,
                            Value = Variant.From(123),
                            AccessLevel = AccessLevels.CurrentReadOrWrite,
                            UserAccessLevel = AccessLevels.CurrentReadOrWrite
                        })
                    }
                ], ct).ConfigureAwait(false);
            Assert.That(response.Results, Has.Count.EqualTo(1));
            Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(response.Results[0].AddedNodeId.IsNull, Is.False);
            return response.Results[0].AddedNodeId;
        }

        private static async Task ReadRuntimeNodeAsync(ClientSession session, NodeId nodeId, CancellationToken ct)
        {
            ReadResponse response = await session.ReadAsync(null, 0, TimestampsToReturn.Both,
                [new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value }], ct).ConfigureAwait(false);
            Assert.That(response.Results, Has.Count.EqualTo(1));
            Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(response.Results[0].WrappedValue, Is.EqualTo(Variant.From(123)));
        }

        private static async Task AssertNamespaceArrayAsync(
            ClientSession session,
            string applicationUri,
            CancellationToken ct)
        {
            ReadResponse response = await session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Neither,
                [new ReadValueId { NodeId = VariableIds.Server_NamespaceArray, AttributeId = Attributes.Value }],
                ct).ConfigureAwait(false);
            ServerFixtureUtils.ValidateResponse(response.ResponseHeader);
            Assert.That(response.Results, Has.Count.EqualTo(1));
            Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(response.Results[0].WrappedValue.TryGetValue(out ArrayOf<string> namespaces), Is.True);
            Assert.That(namespaces.Count, Is.GreaterThanOrEqualTo(4));
            Assert.That(namespaces[0], Is.EqualTo(Namespaces.OpcUa));
            Assert.That(namespaces[1], Is.EqualTo(applicationUri));
            Assert.That(namespaces[2], Is.EqualTo(ModelNamespaceUri));
            Assert.That(namespaces[3], Is.EqualTo(InstanceNamespaceUri));
        }

        private static async Task<Dictionary<string, BrowsedNode>> BrowseSharedGraphAsync(
            ClientSession session,
            int expectedLocalDiagnosticCount,
            CancellationToken ct)
        {
            var objects = (await BrowseReferencesAsync(session, ObjectIds.ObjectsFolder, ct)
                .ConfigureAwait(false)).ToList();
            ReferenceDescription local = objects.Single(reference =>
                reference.IsForward && reference.BrowseName.Name == LocalRootName);
            Assert.That(local.NodeId.NamespaceIndex, Is.EqualTo(1));
            ArrayOf<ReferenceDescription> localReferences = await BrowseReferencesAsync(
                session,
                GetWireNodeId(local.NodeId),
                ct).ConfigureAwait(false);
            var diagnostics = localReferences
                .ToList()
                .Where(reference => reference.IsForward && reference.ReferenceTypeId == ReferenceTypeIds.HasComponent)
                .ToArrayOf();
            Assert.That(diagnostics, Has.Count.EqualTo(expectedLocalDiagnosticCount));
            foreach (ReferenceDescription diagnostic in diagnostics)
            {
                NodeId id = GetWireNodeId(diagnostic.NodeId);
                Assert.That(id.NamespaceIndex, Is.EqualTo(1));
                Assert.That(id.IdType, Is.EqualTo(IdType.Numeric));
            }

            var pending = new Queue<(string Path, ReferenceDescription Reference)>();
            foreach (string rootName in s_sharedRootNames)
            {
                ReferenceDescription root = objects.Single(reference =>
                    reference.IsForward && reference.BrowseName.Name == rootName);
                Assert.That(root.ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.Organizes));
                pending.Enqueue((rootName, root));
            }

            var nodes = new Dictionary<string, BrowsedNode>(StringComparer.Ordinal);
            var visited = new HashSet<NodeId>();
            while (pending.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                (string path, ReferenceDescription description) = pending.Dequeue();
                NodeId nodeId = GetWireNodeId(description.NodeId);
                Assert.That(visited.Add(nodeId), Is.True, $"Distinct application paths share NodeId {nodeId}.");
                ArrayOf<ReferenceDescription> references = await BrowseReferencesAsync(session, nodeId, ct)
                    .ConfigureAwait(false);
                foreach (ReferenceDescription reference in references)
                {
                    _ = GetWireNodeId(reference.NodeId);
                    Assert.That(reference.TypeDefinition.IsAbsolute, Is.False);
                    if (reference.IsForward &&
                        (reference.ReferenceTypeId == ReferenceTypeIds.Organizes ||
                            reference.ReferenceTypeId == ReferenceTypeIds.HasComponent ||
                            reference.ReferenceTypeId == ReferenceTypeIds.HasProperty))
                    {
                        Assert.That(reference.BrowseName.Name, Is.Not.Null.And.Not.Empty);
                        pending.Enqueue((path + "/" + reference.BrowseName.Name, reference));
                    }
                }
                nodes.Add(path, new BrowsedNode(
                    nodeId,
                    description.BrowseName,
                    description.NodeClass,
                    references.ToArrayOf(static reference => new WireReference(
                        reference.ReferenceTypeId,
                        reference.IsForward,
                        reference.NodeId,
                        reference.BrowseName,
                        reference.NodeClass,
                        reference.TypeDefinition))));
            }
            return nodes;
        }

        private static async Task<ArrayOf<ReferenceDescription>> BrowseReferencesAsync(
            ClientSession session,
            NodeId nodeId,
            CancellationToken ct)
        {
            BrowseResponse response = await session.BrowseAsync(
                null,
                null,
                2,
                [
                    new BrowseDescription
                    {
                        NodeId = nodeId,
                        BrowseDirection = BrowseDirection.Both,
                        ReferenceTypeId = ReferenceTypeIds.References,
                        IncludeSubtypes = true,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                ],
                ct).ConfigureAwait(false);
            ServerFixtureUtils.ValidateResponse(response.ResponseHeader);
            Assert.That(response.Results, Has.Count.EqualTo(1));
            BrowseResult result = response.Results[0];
            var references = new List<ReferenceDescription>();
            while (true)
            {
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good), $"Browse failed for NodeId {nodeId}.");
                references.AddRange(result.References);
                if (result.ContinuationPoint.IsEmpty)
                {
                    return references.ToArrayOf();
                }
                BrowseNextResponse next = await session.BrowseNextAsync(
                    null,
                    false,
                    [result.ContinuationPoint],
                    ct).ConfigureAwait(false);
                ServerFixtureUtils.ValidateResponse(next.ResponseHeader);
                Assert.That(next.Results, Has.Count.EqualTo(1));
                result = next.Results[0];
            }
        }

        private static NodeId GetWireNodeId(ExpandedNodeId nodeId)
        {
            Assert.That(nodeId.IsNull, Is.False);
            Assert.That(nodeId.ServerIndex, Is.Zero);
            Assert.That(nodeId.NamespaceUri, Is.Null.Or.Empty);
            return nodeId.InnerNodeId;
        }

        private static void AssertSharedGraphShape(Dictionary<string, BrowsedNode> nodes, IdType expectedIdType)
        {
            string[] expectedPaths = [.. s_sharedRootNames
                .ToList()
                .SelectMany(root => s_sharedPathSuffixes.ToList().Select(suffix => root + suffix))];
            Assert.That(nodes.Keys, Is.EquivalentTo(expectedPaths));
            foreach (KeyValuePair<string, BrowsedNode> entry in nodes)
            {
                int namespaceIndex = entry.Key.StartsWith(ModelRootName, StringComparison.Ordinal) ? 2 : 3;
                Assert.That(entry.Value.NodeId.NamespaceIndex, Is.EqualTo(namespaceIndex), entry.Key);
                Assert.That(entry.Value.NodeId.IdType, Is.EqualTo(expectedIdType), entry.Key);
                Assert.That(entry.Value.BrowseName.NamespaceIndex, Is.EqualTo(namespaceIndex), entry.Key);
                Assert.That(entry.Value.References.ToList(), Is.Not.Empty, entry.Key);
                bool isReading = entry.Key.EndsWith("/Reading", StringComparison.Ordinal);
                bool isProperty = entry.Key.EndsWith("/Units", StringComparison.Ordinal);
                Assert.That(entry.Value.NodeClass,
                    Is.EqualTo(isReading || isProperty ? NodeClass.Variable : NodeClass.Object), entry.Key);
                int separator = entry.Key.LastIndexOf('/');
                NodeId parentId = separator < 0 ? ObjectIds.ObjectsFolder : nodes[entry.Key[..separator]].NodeId;
                NodeId referenceTypeId = isProperty
                    ? ReferenceTypeIds.HasProperty
                    : isReading ? ReferenceTypeIds.HasComponent : ReferenceTypeIds.Organizes;
                Assert.That(
                    entry.Value.References.ToList().Any(reference =>
                        !reference.IsForward &&
                        reference.ReferenceTypeId == referenceTypeId &&
                        reference.Target.InnerNodeId == parentId),
                    Is.True,
                    $"The inverse parent reference is missing for {entry.Key}.");
            }
        }

        private static void AssertSameWireGraph(
            Dictionary<string, BrowsedNode> expected,
            Dictionary<string, BrowsedNode> actual)
        {
            Assert.That(actual.Keys, Is.EquivalentTo(expected.Keys));
            foreach (KeyValuePair<string, BrowsedNode> entry in expected)
            {
                BrowsedNode other = actual[entry.Key];
                Assert.That(other.NodeId.NamespaceIndex, Is.EqualTo(entry.Value.NodeId.NamespaceIndex), entry.Key);
                Assert.That(other.NodeId.IdType, Is.EqualTo(entry.Value.NodeId.IdType), entry.Key);
                Assert.That(other.NodeId, Is.EqualTo(entry.Value.NodeId), $"Full wire NodeId differs for {entry.Key}.");
                Assert.That(other.BrowseName, Is.EqualTo(entry.Value.BrowseName), entry.Key);
                Assert.That(other.NodeClass, Is.EqualTo(entry.Value.NodeClass), entry.Key);
                Assert.That(other.References.ToList(), Is.EquivalentTo(entry.Value.References.ToList()), entry.Key);
            }
        }

        private static async Task AssertSavedReadsAsync(
            ClientSession session,
            Dictionary<string, BrowsedNode> saved,
            CancellationToken ct)
        {
            var reads = new List<(ReadValueId Request, Variant Expected)>();
            foreach (KeyValuePair<string, BrowsedNode> entry in saved)
            {
                BrowsedNode node = entry.Value;
                reads.Add((
                    new ReadValueId { NodeId = node.NodeId, AttributeId = Attributes.NodeId },
                    new Variant(node.NodeId)));
                reads.Add((
                    new ReadValueId { NodeId = node.NodeId, AttributeId = Attributes.BrowseName },
                    new Variant(node.BrowseName)));
                if (node.NodeClass == NodeClass.Variable)
                {
                    Variant expected = entry.Key.EndsWith("/Units", StringComparison.Ordinal)
                        ? new Variant("rpm")
                        : new Variant(entry.Key.EndsWith("/Pump/Reading", StringComparison.Ordinal) ? 10 : 20);
                    reads.Add((
                        new ReadValueId { NodeId = node.NodeId, AttributeId = Attributes.Value },
                        expected));
                }
            }
            ReadResponse response = await session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Neither,
                reads.ToArrayOf(static read => read.Request),
                ct).ConfigureAwait(false);
            ServerFixtureUtils.ValidateResponse(response.ResponseHeader);
            Assert.That(response.Results, Has.Count.EqualTo(reads.Count));
            for (int index = 0; index < reads.Count; index++)
            {
                Assert.That(response.Results[index].StatusCode, Is.EqualTo(StatusCodes.Good),
                    $"Read failed for saved NodeId {reads[index].Request.NodeId}.");
                Assert.That(response.Results[index].WrappedValue, Is.EqualTo(reads[index].Expected),
                    $"Attribute {reads[index].Request.AttributeId} differs at {reads[index].Request.NodeId}.");
            }
        }

        private static async Task AssertSavedSubscriptionAsync(
            ClientSession session,
            NodeId savedNodeId,
            int updatedValue,
            CancellationToken ct)
        {
            CreateSubscriptionResponse subscription = await session.CreateSubscriptionAsync(
                null,
                100,
                600,
                10,
                0,
                true,
                0,
                ct).ConfigureAwait(false);
            ServerFixtureUtils.ValidateResponse(subscription.ResponseHeader);
            Assert.That(subscription.SubscriptionId, Is.Not.Zero);
            try
            {
                CreateMonitoredItemsResponse monitored = await session.CreateMonitoredItemsAsync(
                    null,
                    subscription.SubscriptionId,
                    TimestampsToReturn.Neither,
                    [
                        new MonitoredItemCreateRequest
                        {
                            ItemToMonitor = new ReadValueId
                            {
                                NodeId = savedNodeId,
                                AttributeId = Attributes.Value
                            },
                            MonitoringMode = MonitoringMode.Reporting,
                            RequestedParameters = new MonitoringParameters
                            {
                                ClientHandle = MonitoredItemHandle,
                                SamplingInterval = 0,
                                QueueSize = 10,
                                DiscardOldest = true
                            }
                        }
                    ],
                    ct).ConfigureAwait(false);
                ServerFixtureUtils.ValidateResponse(monitored.ResponseHeader);
                Assert.That(monitored.Results, Has.Count.EqualTo(1));
                Assert.That(monitored.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(monitored.Results[0].MonitoredItemId, Is.Not.Zero);

                uint sequence = await AssertPublishedValueAsync(
                    session,
                    subscription.SubscriptionId,
                    0,
                    10,
                    ct).ConfigureAwait(false);
                WriteResponse written = await session.WriteAsync(
                    null,
                    [
                        new WriteValue
                        {
                            NodeId = savedNodeId,
                            AttributeId = Attributes.Value,
                            Value = new DataValue(new Variant(updatedValue))
                        }
                    ],
                    ct).ConfigureAwait(false);
                ServerFixtureUtils.ValidateResponse(written.ResponseHeader);
                Assert.That(written.Results, Has.Count.EqualTo(1));
                Assert.That(written.Results[0], Is.EqualTo(StatusCodes.Good));
                _ = await AssertPublishedValueAsync(
                    session,
                    subscription.SubscriptionId,
                    sequence,
                    updatedValue,
                    ct).ConfigureAwait(false);
            }
            finally
            {
                DeleteSubscriptionsResponse deleted = await session.DeleteSubscriptionsAsync(
                    null,
                    [subscription.SubscriptionId],
                    CancellationToken.None).ConfigureAwait(false);
                ServerFixtureUtils.ValidateResponse(deleted.ResponseHeader);
                Assert.That(deleted.Results, Has.Count.EqualTo(1));
                Assert.That(deleted.Results[0], Is.EqualTo(StatusCodes.Good));
            }
        }

        private static async Task<uint> AssertPublishedValueAsync(
            ClientSession session,
            uint subscriptionId,
            uint acknowledgeSequence,
            int expectedValue,
            CancellationToken ct)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            ArrayOf<SubscriptionAcknowledgement> acknowledgements = acknowledgeSequence == 0
                ? []
                :
                [
                    new SubscriptionAcknowledgement
                    {
                        SubscriptionId = subscriptionId,
                        SequenceNumber = acknowledgeSequence
                    }
                ];
            while (true)
            {
                PublishResponse response = await session.PublishAsync(null, acknowledgements, timeout.Token)
                    .ConfigureAwait(false);
                ServerFixtureUtils.ValidateResponse(response.ResponseHeader);
                Assert.That(response.SubscriptionId, Is.EqualTo(subscriptionId));
                Assert.That(response.Results, Has.Count.EqualTo(acknowledgements.Count));
                foreach (StatusCode result in response.Results)
                {
                    Assert.That(result, Is.EqualTo(StatusCodes.Good));
                }
                acknowledgements = [];
                ArrayOf<ExtensionObject> data = response.NotificationMessage.NotificationData;
                if (data.Count == 0)
                {
                    continue;
                }
                Assert.That(data, Has.Count.EqualTo(1));
                Assert.That(data[0].TryGetValue(out DataChangeNotification? change), Is.True);
                Assert.That(change, Is.Not.Null);
                Assert.That(change!.MonitoredItems, Has.Count.EqualTo(1));
                MonitoredItemNotification item = change.MonitoredItems[0];
                Assert.That(item.ClientHandle, Is.EqualTo(MonitoredItemHandle));
                Assert.That(item.Value.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(item.Value.WrappedValue.TryGetValue(out int value), Is.True);
                Assert.That(value, Is.EqualTo(expectedValue));
                Assert.That(response.NotificationMessage.SequenceNumber, Is.Not.Zero);
                return response.NotificationMessage.SequenceNumber;
            }
        }

        private sealed record BrowsedNode(
            NodeId NodeId,
            QualifiedName BrowseName,
            NodeClass NodeClass,
            ArrayOf<WireReference> References);

        private sealed record WireReference(
            NodeId ReferenceTypeId,
            bool IsForward,
            ExpandedNodeId Target,
            QualifiedName BrowseName,
            NodeClass NodeClass,
            ExpandedNodeId TypeDefinition);

        private sealed class ReplicaServer : IAsyncDisposable
        {
            /// <summary>
            /// Configures an independent server with a fresh identity factory and reordered application managers.
            /// </summary>
            public ReplicaServer(
                string applicationUri,
                NodeIdAssignmentMode mode,
                bool reverseOrder,
                int localDiagnosticCount)
            {
                m_applicationUri = applicationUri;
                m_mode = mode;
                m_factories =
                [
                    new ReplicaNodeManagerFactory(applicationUri, LocalRootName, reverseOrder, localDiagnosticCount),
                    new ReplicaNodeManagerFactory(ModelNamespaceUri, ModelRootName, reverseOrder, 0),
                    new ReplicaNodeManagerFactory(InstanceNamespaceUri, PlantRootName, reverseOrder, 0)
                ];
                if (reverseOrder)
                {
                    m_factories = Enumerable.Reverse(m_factories.ToList()).ToArrayOf();
                }
                m_fixture = new ServerFixture<StandardServer>(CreateServer)
                {
                    SecurityNone = true,
                    AutoAccept = true
                };
            }

            /// <summary>
            /// Gets the live unsecure UA-TCP endpoint used by real clients.
            /// </summary>
            /// <exception cref="InvalidOperationException"></exception>
            public EndpointDescription Endpoint => m_fixture.Server.GetEndpoints().Find(endpoint =>
                endpoint.SecurityMode == MessageSecurityMode.None &&
                endpoint.TransportProfileUri == Profiles.UaTcpTransport)
                ?? throw new InvalidOperationException("The replica did not publish a UA-TCP endpoint.");

            /// <summary>
            /// Gets the bound listener port for a fresh restart on the same endpoint.
            /// </summary>
            public int Port => m_fixture.Port;

            /// <summary>
            /// Gets how many application managers the normal startup pipeline constructed.
            /// </summary>
            public int CreatedManagerCount => m_factories.ToList().Sum(factory => factory.CreationCount);

            /// <summary>
            /// Starts the configured server without preparing or rewriting its namespace table in the test.
            /// </summary>
            public async Task StartAsync(int port = 0)
            {
                await m_fixture.LoadConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
                m_fixture.Config.ApplicationUri = m_applicationUri;
                await m_fixture.StartAsync(m_pkiRoot, port).ConfigureAwait(false);
            }

            /// <summary>
            /// Stops the server and releases the listener before the replacement is started.
            /// </summary>
            public Task StopAsync()
            {
                return m_fixture.StopAsync();
            }

            public async Task EnableReplicationAsync(bool activeActive)
            {
                IServerStartupTask startup = activeActive
                    ? new ReplicatedAddressSpaceStartupTask(Mock.Of<IServiceProvider>(), new ReplicatedAddressSpaceOptions())
                    : new DistributedAddressSpaceStartupTask(m_store, new StaticLeaderElection(true));
                m_replication = (IAsyncDisposable)startup;
                await startup.OnServerStartedAsync(m_fixture.Server.CurrentInstance).ConfigureAwait(false);
                m_activeActive = activeActive;
            }

            public async Task FlushReplicationAsync(NodeId nodeId, CancellationToken ct)
            {
                if (m_activeActive)
                {
                    return;
                }
                using var nodes = new InMemoryNodeStateStore(m_store, m_fixture.Server.CurrentInstance.MessageContext);
                while (true)
                {
                    await foreach (IStoredNode stored in nodes.EnumerateAsync(ct).ConfigureAwait(false))
                    {
                        NodeState root = NodeStateSerializer.Deserialize(
                            m_fixture.Server.CurrentInstance.DefaultSystemContext, stored.Payload);
                        if (root.NodeId == nodeId ||
                            NodeStateSerializer.GetDescendantNodeIds(
                                m_fixture.Server.CurrentInstance.DefaultSystemContext, root).Contains(nodeId))
                        {
                            return;
                        }
                    }
                    await Task.Delay(10, ct).ConfigureAwait(false);
                }
            }

            public async Task ReloadInstancesAsync(CancellationToken ct)
            {
                INodeManagerLifecycle lifecycle = m_fixture.Server.NodeManagerLifecycle;
                NodeManagerRegistration registration = lifecycle.Registrations.ToList().Single(value =>
                    value.NamespaceUris.ToList().Contains(InstanceNamespaceUri));
                await lifecycle.ReloadAsync(
                    registration,
                    new ReplicaNodeManagerFactory(InstanceNamespaceUri, PlantRootName, true, 0),
                    callerContext: null,
                    ct).ConfigureAwait(false);
            }

            public async Task AssertStoredIdentityAsync(NodeId nodeId, CancellationToken ct)
            {
                if (m_activeActive)
                {
                    return;
                }
                using var nodes = new InMemoryNodeStateStore(m_store, m_fixture.Server.CurrentInstance.MessageContext);
                bool found = false;
                await foreach (IStoredNode stored in nodes.EnumerateAsync(ct).ConfigureAwait(false))
                {
                    NodeState root = NodeStateSerializer.Deserialize(
                        m_fixture.Server.CurrentInstance.DefaultSystemContext, stored.Payload);
                    found |= root.NodeId == nodeId ||
                        NodeStateSerializer.GetDescendantNodeIds(
                            m_fixture.Server.CurrentInstance.DefaultSystemContext, root).Contains(nodeId);
                }
                Assert.That(found, Is.True, "A later addition must not erase a retained shared identifier from storage.");
            }

            /// <inheritdoc/>
            public async ValueTask DisposeAsync()
            {
                if (m_replication != null)
                {
                    await m_replication.DisposeAsync().ConfigureAwait(false);
                }
                await m_fixture.StopAsync().ConfigureAwait(false);
                m_store.Dispose();
                if (Directory.Exists(m_pkiRoot))
                {
                    Directory.Delete(m_pkiRoot, recursive: true);
                }
            }

            private StandardServer CreateServer(ITelemetryContext telemetry)
            {
                var server = new StandardServer(telemetry)
                {
                    NodeIdFactory = new ReplicaNodeIdFactory(
                        ReplicaSetId,
                        [ModelNamespaceUri, InstanceNamespaceUri],
                        m_mode)
                };
                foreach (ReplicaNodeManagerFactory factory in m_factories)
                {
                    server.AddNodeManager(factory);
                }
                return server;
            }

            private readonly string m_applicationUri;
            private readonly NodeIdAssignmentMode m_mode;
            private readonly ArrayOf<ReplicaNodeManagerFactory> m_factories;
            private readonly ServerFixture<StandardServer> m_fixture;
            private readonly InMemorySharedKeyValueStore m_store = new();
            private IAsyncDisposable? m_replication;
            private bool m_activeActive;

            private readonly string m_pkiRoot = Path.Combine(
                Path.GetTempPath(),
                nameof(ReplicaNodeIdentityIntegrationTests),
                Guid.NewGuid().ToString("N"));
        }

        private sealed class ReplicaClient : IAsyncDisposable
        {
            /// <summary>
            /// Creates an isolated client application whose sessions use the actual UA-TCP transport.
            /// </summary>
            public ReplicaClient()
            {
                m_application = new ApplicationInstance(m_telemetry)
                {
                    ApplicationName = nameof(ReplicaNodeIdentityIntegrationTests),
                    ApplicationType = ApplicationType.Client
                };
            }

            /// <summary>
            /// Initializes the client through the standard application and certificate-store configuration.
            /// </summary>
            public async Task InitializeAsync()
            {
                ArrayOf<CertificateIdentifier> certificates =
                    ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                        "CN=ReplicaIdentityClient, DC=localhost",
                        CertificateStoreType.Directory,
                        m_pkiRoot);
                m_configuration = await m_application
                    .Build(
                        "urn:localhost:replica-identity:client",
                        "urn:opcfoundation.org:replica-identity:client")
                    .SetOperationTimeout(10000)
                    .AsClient()
                    .AddSecurityConfiguration(certificates, m_pkiRoot)
                    .SetAutoAcceptUntrustedCertificates(true)
                    .CreateAsync()
                    .ConfigureAwait(false);
                Assert.That(
                    await m_application.CheckApplicationInstanceCertificatesAsync(true).ConfigureAwait(false),
                    Is.True);
            }

            /// <summary>
            /// Opens a raw client session, without a namespace-remapping or automatic failover layer.
            /// </summary>
            /// <exception cref="InvalidOperationException"></exception>
            public Task<ClientSession> ConnectAsync(EndpointDescription endpoint, CancellationToken ct)
            {
                ApplicationConfiguration configuration = m_configuration
                    ?? throw new InvalidOperationException("The client must be initialized before connecting.");
                var configured = new ConfiguredEndpoint(null, endpoint, EndpointConfiguration.Create(configuration));
                var factory = new DefaultSessionFactory(m_telemetry);
                return factory.CreateAsync(
                    configuration,
                    configured,
                    false,
                    false,
                    nameof(ReplicaNodeIdentityIntegrationTests),
                    60000,
                    new UserIdentity(),
                    [],
                    ct);
            }

            /// <inheritdoc/>
            public async ValueTask DisposeAsync()
            {
                await m_application.DisposeAsync().ConfigureAwait(false);
                if (Directory.Exists(m_pkiRoot))
                {
                    Directory.Delete(m_pkiRoot, recursive: true);
                }
            }

            private readonly ITelemetryContext m_telemetry = NUnitTelemetryContext.Create();
            private readonly ApplicationInstance m_application;

            private readonly string m_pkiRoot = Path.Combine(
                Path.GetTempPath(),
                nameof(ReplicaNodeIdentityIntegrationTests),
                Guid.NewGuid().ToString("N"));

            private ApplicationConfiguration? m_configuration;
        }

        private sealed class ReplicaNodeManagerFactory : IAsyncNodeManagerFactory
        {
            /// <summary>
            /// Describes one application namespace and its normal node-manager construction.
            /// </summary>
            public ReplicaNodeManagerFactory(
                string namespaceUri,
                string rootName,
                bool reverseOrder,
                int localDiagnosticCount)
            {
                NamespacesUris = [namespaceUri];
                m_rootName = rootName;
                m_reverseOrder = reverseOrder;
                m_localDiagnosticCount = localDiagnosticCount;
            }

            /// <inheritdoc/>
            public ArrayOf<string> NamespacesUris { get; }

            /// <summary>
            /// Gets the number of constructions requested by server startup.
            /// </summary>
            public int CreationCount => Volatile.Read(ref m_creationCount);

            /// <inheritdoc/>
            public ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server,
                ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Interlocked.Increment(ref m_creationCount);
                return new ValueTask<IAsyncNodeManager>(new ReplicaNodeManager(
                    server,
                    configuration,
                    NamespacesUris[0],
                    m_rootName,
                    m_reverseOrder,
                    m_localDiagnosticCount));
            }

            private readonly string m_rootName;
            private readonly bool m_reverseOrder;
            private readonly int m_localDiagnosticCount;
            private int m_creationCount;
        }

        private sealed class ReplicaNodeManager : AsyncCustomNodeManager, INodeManagerReloadParticipant
        {
            /// <summary>
            /// Uses the server's policy-preserving factory for shared nodes and replica-local diagnostics.
            /// </summary>
            public ReplicaNodeManager(
                IServerInternal server,
                ApplicationConfiguration configuration,
                string namespaceUri,
                string rootName,
                bool reverseOrder,
                int localDiagnosticCount)
                : base(server, configuration, namespaceUri)
            {
                m_rootName = rootName;
                m_reverseOrder = reverseOrder;
                m_localDiagnosticCount = localDiagnosticCount;
                NodeIdFactory = NodeIdFactory.WithMode(
                    localDiagnosticCount > 0 ? NodeIdAssignmentMode.Counter : NodeIdFactory.Mode);
            }

            /// <inheritdoc/>
            public override bool AllowNodeManagement => true;

            /// <inheritdoc/>
            public ValueTask<ArrayOf<LocalReference>> PrepareReloadAsync(
                IAsyncNodeManager replacement,
                CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                if (replacement is not ReplicaNodeManager next || next.m_rootName != m_rootName)
                {
                    throw new NotSupportedException("The test model requires a matching replacement manager.");
                }
                return new ValueTask<ArrayOf<LocalReference>>([]);
            }

            /// <inheritdoc/>
            protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
                ISystemContext context,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var root = new FolderState(null)
                {
                    BrowseName = new QualifiedName(m_rootName, NodeIdFactory.DefaultNamespaceIndex),
                    DisplayName = new LocalizedText(m_rootName),
                    TypeDefinitionId = ObjectTypeIds.FolderType
                };
                root.NodeId = New(context, root);
                root.AddReference(ReferenceTypeIds.Organizes, true, ObjectIds.ObjectsFolder);
                if (m_localDiagnosticCount > 0)
                {
                    for (int index = 0; index < m_localDiagnosticCount; index++)
                    {
                        AddReading(context, root, "Diagnostic" + index.ToString(CultureInfo.InvariantCulture), index);
                    }
                }
                else
                {
                    foreach (string name in (ArrayOf<string>)(m_reverseOrder ? ["Valve", "Pump"] : ["Pump", "Valve"]))
                    {
                        var asset = new BaseObjectState(root)
                        {
                            BrowseName = new QualifiedName(name, NodeIdFactory.DefaultNamespaceIndex),
                            DisplayName = new LocalizedText(name),
                            ReferenceTypeId = ReferenceTypeIds.Organizes,
                            TypeDefinitionId = ObjectTypeIds.BaseObjectType
                        };
                        asset.NodeId = New(context, asset);
                        root.AddChild(asset);
                        BaseDataVariableState<int> reading = AddReading(
                            context,
                            asset,
                            "Reading",
                            name == "Pump" ? 10 : 20);
                        var units = PropertyState<string>.With<VariantBuilder>(reading, "rpm");
                        units.BrowseName = new QualifiedName("Units", NodeIdFactory.DefaultNamespaceIndex);
                        units.DisplayName = new LocalizedText("Units");
                        units.ReferenceTypeId = ReferenceTypeIds.HasProperty;
                        units.TypeDefinitionId = VariableTypeIds.PropertyType;
                        units.DataType = DataTypeIds.String;
                        units.ValueRank = ValueRanks.Scalar;
                        units.AccessLevel = AccessLevels.CurrentRead;
                        units.UserAccessLevel = AccessLevels.CurrentRead;
                        units.StatusCode = StatusCodes.Good;
                        units.NodeId = New(context, units);
                        reading.AddChild(units);
                    }
                }
                return new ValueTask<NodeStateCollection>([root]);
            }

            private BaseDataVariableState<int> AddReading(
                ISystemContext context,
                NodeState parent,
                string name,
                int value)
            {
                var reading = BaseDataVariableState<int>.With<VariantBuilder>(parent);
                reading.BrowseName = new QualifiedName(name, NodeIdFactory.DefaultNamespaceIndex);
                reading.DisplayName = new LocalizedText(name);
                reading.ReferenceTypeId = ReferenceTypeIds.HasComponent;
                reading.TypeDefinitionId = VariableTypeIds.BaseDataVariableType;
                reading.DataType = DataTypeIds.Int32;
                reading.ValueRank = ValueRanks.Scalar;
                reading.AccessLevel = AccessLevels.CurrentReadOrWrite;
                reading.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
                reading.MinimumSamplingInterval = MinimumSamplingIntervals.Continuous;
                reading.Value = value;
                reading.StatusCode = StatusCodes.Good;
                reading.NodeId = New(context, reading);
                parent.AddChild(reading);
                return reading;
            }

            private readonly string m_rootName;
            private readonly bool m_reverseOrder;
            private readonly int m_localDiagnosticCount;
        }

        private const string ReplicaSetId = "replica-wire-integration";
        private const string ModelNamespaceUri = "urn:opcfoundation.org:replica-identity:model";
        private const string InstanceNamespaceUri = "urn:opcfoundation.org:replica-identity:instances";
        private const string FirstApplicationUri = "urn:localhost:replica-identity:first";
        private const string SecondApplicationUri = "urn:localhost:replica-identity:second";
        private const string ModelRootName = "SharedCatalog";
        private const string PlantRootName = "SharedPlant";
        private const string LocalRootName = "LocalDiagnostics";
        private const uint MonitoredItemHandle = 41;
        private static readonly ArrayOf<string> s_sharedRootNames = [ModelRootName, PlantRootName];

        private static readonly ArrayOf<string> s_sharedPathSuffixes =
        [
            string.Empty,
            "/Pump",
            "/Pump/Reading",
            "/Pump/Reading/Units",
            "/Valve",
            "/Valve/Reading",
            "/Valve/Reading/Units"
        ];
    }
}
