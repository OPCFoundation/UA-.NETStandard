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
using System.Xml;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class NodeManagerRoutingTableTests
    {
        /// <summary>
        /// Published routing images retain type and encoding registrations across publication or abort.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void TypeRegistrationsAreIsolatedByRoutingPublication(bool publish)
        {
            var namespaceUris = new NamespaceTable();
            var servingTypes = new TypeTable(namespaceUris);
            var parentType = new NodeId(9700);
            var stagedType = new NodeId(9701);
            var unassignedNamespaceType = new NodeId(9702, 1);
            const string lateNamespaceUri = "urn:opcua:test:late-namespace";
            servingTypes.AddSubtype(parentType, NodeId.Null);
            servingTypes.AddSubtype(unassignedNamespaceType, NodeId.Null);
            var factory = (EncodeableFactory)EncodeableFactory.Create();
            const string typeNamespace = "urn:opcua:test:staged-encodeable";
            ExpandedNodeId stagedEncodingId = new(9704, typeNamespace);
            var stagedEncodeable = new Mock<IEncodeableType>();
            stagedEncodeable.SetupGet(value => value.XmlName)
                .Returns(new XmlQualifiedName("StagedType", typeNamespace));
            var table = new NodeManagerRoutingTable();
            table.Initialize(new Dictionary<int, List<IAsyncNodeManager>>(), servingTypes, factory);
            NodeManagerRoutingTable.RoutingSnapshot previous = table.Revision;
            namespaceUris.Append(lateNamespaceUri);
            var expandedType = new ExpandedNodeId(9702, lateNamespaceUri);

            TypeTable candidate = servingTypes.CaptureSnapshot(out TypeTable source, out long revision);
            candidate.AddSubtype(stagedType, parentType);
            TypeTable merged = servingTypes.PrepareAdditiveMerge(candidate, source, revision);
            EncodeableFactory candidateFactory =
                factory.CaptureSnapshot(out EncodeableFactory factorySource, out long factoryRevision);
            candidateFactory.Builder.AddEncodeableType(stagedEncodingId, stagedEncodeable.Object).Commit();
            EncodeableFactory mergedFactory = factory.PrepareAdditiveMerge(
                candidateFactory, factorySource, factoryRevision, namespaceUris);
            using NodeManagerRoutingTable.PreparedRoutes routes = table.PrepareBatch(
                [], [], static _ => [], merged, mergedFactory);
            routes.Reserve();

            using (TypeTable.Publication types = servingTypes.BeginPublication(source, revision))
            using (EncodeableFactory.Publication registrations =
                factory.BeginPublication(factorySource, factoryRevision))
            {
                if (publish)
                {
                    types.Complete(merged);
                    registrations.Complete(mergedFactory);
                    routes.Publish();
                }
            }
            if (!publish)
            {
                routes.Dispose();
            }

            using (Assert.EnterMultipleScope())
            {
                Assert.That(previous.TypeTree!.IsKnown(stagedType), Is.False);
                Assert.That(previous.TypeTree.IsKnown(expandedType), Is.False);
                Assert.That(table.Revision.TypeTree!.IsKnown(stagedType), Is.EqualTo(publish));
                Assert.That(table.Revision.TypeTree!.IsKnown(expandedType), Is.EqualTo(publish));
                Assert.That(servingTypes.IsKnown(stagedType), Is.EqualTo(publish));
                Assert.That(servingTypes.IsKnown(expandedType), Is.True);
                Assert.That(merged.IsKnown(stagedType), Is.True);
                Assert.That(previous.Factory!.TryGetEncodeableType(stagedEncodingId, out _), Is.False);
                Assert.That(
                    table.Revision.Factory!.TryGetEncodeableType(stagedEncodingId, out _),
                    Is.EqualTo(publish));
                Assert.That(factory.TryGetEncodeableType(stagedEncodingId, out _), Is.EqualTo(publish));
                Assert.That(mergedFactory.TryGetEncodeableType(stagedEncodingId, out _), Is.True);
            }
            if (publish)
            {
                var laterType = new NodeId(9703);
                servingTypes.AddSubtype(laterType, parentType);
                Assert.That(table.Revision.TypeTree!.IsKnown(laterType), Is.False);
                ExpandedNodeId laterEncodingId = new(9705, typeNamespace);
                factory.Builder.AddEncodeableType(laterEncodingId, stagedEncodeable.Object).Commit();
                Assert.That(table.Revision.Factory!.TryGetEncodeableType(laterEncodingId, out _), Is.False);
            }
        }

        /// <summary>
        /// A retired provider cannot be revived after producer shutdown has started, even before destruction.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ReplacementRejectsGenerationWhoseProducerShutdownStartedAsync(bool completed)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var original = new Mock<IAsyncNodeManager>();
            original.As<INodeManagerShutdown>().Setup(value => value.PrepareForShutdownAsync()).Returns(async () =>
            {
                entered.TrySetResult(true);
                await release.Task.ConfigureAwait(false);
            });
            NodeManagerRoutingTable table = CreateTable(out _, out _);
            table.Add(original.Object, BatchNamespaceIndexes);
            NodeManagerGeneration generation = table.GetGeneration(original.Object);
            IAsyncNodeManager replacement = CreateManager();
            table.Replace(original.Object, replacement, BatchNamespaceIndexes);
            Task stopping = generation.PrepareForShutdownAsync();
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                if (completed)
                {
                    release.TrySetResult(true);
                    await stopping.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
                Assert.That(() => table.Replace(replacement, original.Object, BatchNamespaceIndexes),
                    Throws.InvalidOperationException.With.Message.Contains("retired"));
                Assert.That(table.Revision.VisibleNodeManagers, Does.Contain(replacement));
                Assert.That(table.Revision.VisibleNodeManagers, Does.Not.Contain(original.Object));
                Assert.That(generation.Gate.State, Is.EqualTo(NodeManagerGenerationState.Retiring));
                original.As<INodeManagerShutdown>().Verify(value => value.PrepareForShutdownAsync(), Times.Once);
            }
            finally
            {
                release.TrySetResult(true);
                await stopping.WaitAsync(timeout.Token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Publishing reveals only its candidate and never invokes a hidden manager's custom equality.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void PublicationTransitionsHiddenGenerationsWithoutCallingManagerEquality(bool replace)
        {
            NodeManagerRoutingTable table = CreateTable(out _, out _);
            IAsyncNodeManager original = CreateManager();
            if (replace)
            {
                table.Add(original, BatchNamespaceIndexes);
            }
            IAsyncNodeManager candidate = CreateManager();
            bool decided = false;
            var hidden = new Mock<IAsyncNodeManager>();
            hidden.Setup(value => value.Equals(It.IsAny<object>())).Returns((object other) =>
            {
                if (decided)
                {
                    throw new InvalidOperationException("Manager equality after the durable decision.");
                }
                return ReferenceEquals(other, hidden.Object);
            });
            table.RegisterNamespace(2, candidate, visible: false);
            table.Add(hidden.Object, [3], visible: false);
            NodeManagerGeneration hiddenGeneration = table.GetGeneration(hidden.Object);
            NodeManagerGeneration? originalGeneration = replace ? table.GetGeneration(original) : null;
            NodeManagerGeneration generation = replace
                ? NodeManagerGenerationRegistry.CreateReplacement(table.GetGeneration(original), candidate)
                : NodeManagerGenerationRegistry.CreateAddition(candidate);
            var prepared = new PreparedNodeManager(candidate, [], generation, replace ? original : null);
            using NodeManagerRoutingTable.PreparedRoutes routes = table.PrepareBatch(
                [prepared], [], _ => BatchNamespaceIndexes,
                new TypeTable(new NamespaceTable()), (EncodeableFactory)EncodeableFactory.Create());
            routes.Reserve();
            NodeManagerGeneration candidateGeneration = NodeManagerGenerationRegistry.GetOrCreate(candidate);

            decided = true;
            routes.Publish();

            Assert.That(table.IsVisible(candidate), Is.True);
            Assert.That(table.IsVisible(hidden.Object), Is.False);
            Assert.That(candidateGeneration.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
            Assert.That(hiddenGeneration.Gate.State, Is.EqualTo(NodeManagerGenerationState.Prepared));
            if (originalGeneration is not null)
            {
                Assert.That(originalGeneration.Gate.State, Is.EqualTo(NodeManagerGenerationState.Retiring));
            }
        }

        /// <summary>
        /// Candidate handle lookup runs before the decision, and a failing post-swap callback cannot prevent
        /// already-prepared reference additions from completing against the committed view.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void PublisherCapturesCandidateHandlesBeforeDecisionAndCompletesCommittedReferences(bool callbackFails)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                var preparation = new ChangePreparer(server.Object);
                NodeManagerRoutingTable table = CreateTable(out _, out _);
                bool decisionReturned = false;
                int candidateLookups = 0;
                var manager = new Mock<IAsyncNodeManager>();
                manager.SetupGet(value => value.SyncNodeManager).Returns(() =>
                {
                    candidateLookups++;
                    if (decisionReturned)
                    {
                        throw new InvalidOperationException("Candidate lookup after the durable decision.");
                    }
                    return null!;
                });
                var prepared = new PreparedNodeManager(manager.Object, [],
                    NodeManagerGenerationRegistry.CreateAddition(manager.Object));
                using NodeManagerRoutingTable.PreparedRoutes routes = table.PrepareBatch(
                    [prepared], [], _ => BatchNamespaceIndexes,
                    preparation.OriginalTypes, preparation.OriginalFactory);
                routes.Reserve();
                using TypeTable.Publication types = preparation.OriginalTypes.BeginPublication(
                    preparation.OriginalTypes, preparation.TypeRevision);
                using EncodeableFactory.Publication factory = preparation.OriginalFactory.BeginPublication(
                    preparation.OriginalFactory, preparation.FactoryRevision);
                using StringTable.Publication namespaces = server.Object.NamespaceUris.BeginPublication(
                    preparation.NamespaceUris, preparation.NamespaceRevision);
                using StringTable.Publication serverUris = server.Object.ServerUris.BeginPublication(
                    preparation.ServerUris, preparation.ServerRevision);
                var node = new BaseObjectState(null);
                var droppedTarget = new NodeId("Dropped", 2);
                var newTarget = new NodeId("Added", 2);
                node.AddReference(ReferenceTypeIds.Organizes, false, droppedTarget);
                using NodeState.ReferenceUpdate references = node.PrepareReferences(
                    [new NodeStateReference(ReferenceTypeIds.Organizes, false, newTarget)],
                    [new NodeStateReference(ReferenceTypeIds.Organizes, false, droppedTarget)]);
                references.Reserve();
                NodeManagerRoutingTable.RoutingSnapshot before = table.Revision;
                bool callbackObservedCommittedView = false;
                bool callbackObservedOrderedRemoval = false;
                var publisher = new AddressSpacePublisher(
                    routes, namespaces, serverUris, types, factory,
                    preparation.TypeTree, preparation.Factory, [prepared],
                    new Dictionary<NodeState.ReferenceUpdate, NodeManagerGeneration>
                    {
                        [references] = routes.Next.GetGeneration(manager.Object)
                    },
                    historianRegistrationsChanged =>
                    {
                        callbackObservedCommittedView = !ReferenceEquals(table.Revision, before) &&
                            table.Revision.TryGetGeneration(manager.Object, out _);
                        callbackObservedOrderedRemoval =
                            !node.ReferenceExists(ReferenceTypeIds.Organizes, false, droppedTarget) &&
                            !node.ReferenceExists(ReferenceTypeIds.Organizes, false, newTarget);
                        if (callbackFails)
                        {
                            throw new InvalidOperationException("Post-commit application state failed.");
                        }
                    });
                Assert.That(candidateLookups, Is.EqualTo(1));

                decisionReturned = true;
                if (callbackFails)
                {
                    Assert.That(publisher.Publish,
                        Throws.TypeOf<InvalidOperationException>()
                            .With.Message.EqualTo("Post-commit application state failed."));
                }
                else
                {
                    publisher.Publish();
                }

                Assert.That(candidateLookups, Is.EqualTo(1));
                Assert.That(callbackObservedCommittedView, Is.True);
                Assert.That(callbackObservedOrderedRemoval, Is.True);
                Assert.That(table.Revision.Revision, Is.GreaterThan(before.Revision));
                Assert.That(node.ReferenceExists(ReferenceTypeIds.Organizes, false, droppedTarget), Is.False);
                Assert.That(node.ReferenceExists(ReferenceTypeIds.Organizes, false, newTarget), Is.True);
            }
        }
    }
}
