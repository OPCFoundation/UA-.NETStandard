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
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class NodeManagerRoutingTableTests
    {
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
            var prepared = new PreparedNodeManager(candidate, [])
            {
                ReplacedNodeManager = replace ? original : null
            };
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
                var prepared = new PreparedNodeManager(manager.Object, []);
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
                    preparation.TypeTree, preparation.Factory, [prepared], [references],
                    () =>
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
