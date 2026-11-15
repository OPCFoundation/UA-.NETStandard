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
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class NodeManagerRoutingTableTests
    {
        /// <summary>
        /// Retiring one manager must not remove another instance that compares equal.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void BatchRetirementKeepsDistinctEqualManager(bool removeFirst)
        {
            var table = new NodeManagerRoutingTable();
            IAsyncNodeManager first = CreateEqualRoutingManager();
            IAsyncNodeManager second = CreateEqualRoutingManager();
            table.Add(first, BatchNamespaceIndexes);
            table.Add(second, BatchNamespaceIndexes);
            NodeManagerGeneration firstGeneration = table.GetGeneration(first);
            NodeManagerGeneration secondGeneration = table.GetGeneration(second);
            IAsyncNodeManager removed = removeFirst ? first : second;
            IAsyncNodeManager retained = removeFirst ? second : first;
            NodeManagerGeneration removedGeneration = removeFirst ? firstGeneration : secondGeneration;
            NodeManagerGeneration retainedGeneration = removeFirst ? secondGeneration : firstGeneration;
            Assert.That(first, Is.EqualTo(second));

            using NodeManagerRoutingTable.PreparedRoutes routes = PrepareExactOwnerBatch(table, [], [removed]);
            routes.Reserve();
            routes.Publish();

            Assert.That(table.Revision.NodeManagers, Has.Length.EqualTo(1));
            Assert.That(table.Revision.NodeManagers[0], Is.SameAs(retained));
            AssertSingleManagerRoute(table.NamespaceManagers, 2, retained);
            Assert.That(table.Contains(removed), Is.False);
            Assert.That(table.GetGeneration(retained), Is.SameAs(retainedGeneration));
            Assert.That(removedGeneration.Gate.State, Is.EqualTo(NodeManagerGenerationState.Retiring));
            Assert.That(retainedGeneration.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
        }

        /// <summary>
        /// A stale equal instance cannot retire the currently registered instance.
        /// </summary>
        [Test]
        public void BatchRejectsStaleEqualRetirement()
        {
            var table = new NodeManagerRoutingTable();
            IAsyncNodeManager registered = CreateEqualRoutingManager();
            IAsyncNodeManager stale = CreateEqualRoutingManager();
            table.Add(registered, BatchNamespaceIndexes);
            NodeManagerRoutingTable.RoutingSnapshot before = table.Revision;
            NodeManagerGeneration generation = table.GetGeneration(registered);
            Assert.That(registered, Is.EqualTo(stale));
            NodeManagerRoutingTable.PreparedRoutes? unexpected = null;
            try
            {
                Assert.Throws<InvalidOperationException>(() => unexpected = PrepareExactOwnerBatch(table, [], [stale]));
            }
            finally
            {
                unexpected?.Dispose();
            }

            Assert.That(table.Revision, Is.SameAs(before));
            AssertSingleManagerRoute(table.NamespaceManagers, 2, registered);
            Assert.That(generation.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
        }

        /// <summary>
        /// A transparent adapter resolves to the exact registered owner for retirement.
        /// </summary>
        [Test]
        public void BatchRetiresRegisteredStandardAdapterUsingAlias()
        {
            var table = new NodeManagerRoutingTable();
            INodeManager synchronous = new Mock<INodeManager>().Object;
            IAsyncNodeManager registered = new AsyncNodeManagerAdapter(synchronous);
            IAsyncNodeManager alias = new AsyncNodeManagerAdapter(synchronous);
            table.Add(registered, BatchNamespaceIndexes);
            NodeManagerGeneration generation = table.GetGeneration(registered);

            using NodeManagerRoutingTable.PreparedRoutes routes = PrepareExactOwnerBatch(table, [], [alias]);
            routes.Reserve();
            routes.Publish();

            Assert.That(table.Revision.NodeManagers, Is.Empty);
            Assert.That(table.NamespaceManagers, Is.Empty);
            Assert.That(generation.Gate.State, Is.EqualTo(NodeManagerGenerationState.Retiring));
        }

        /// <summary>
        /// An async manager and its transparent round-trip adapter identify the same registered owner.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void BatchRetiresAsyncOwnerUsingRoundTripAdapter(bool registerAdapter)
        {
            var table = new NodeManagerRoutingTable();
            var manager = new Mock<IAsyncNodeManager>();
            var synchronous = new SyncNodeManagerAdapter(manager.Object);
            manager.SetupGet(value => value.SyncNodeManager).Returns(synchronous);
            var adapter = new AsyncNodeManagerAdapter(synchronous);
            IAsyncNodeManager registered = registerAdapter ? adapter : manager.Object;
            IAsyncNodeManager alias = registerAdapter ? manager.Object : adapter;
            table.Add(registered, BatchNamespaceIndexes);
            NodeManagerGeneration generation = table.GetGeneration(registered);

            using NodeManagerRoutingTable.PreparedRoutes routes = PrepareExactOwnerBatch(table, [], [alias]);
            routes.Reserve();
            routes.Publish();

            Assert.That(table.Revision.NodeManagers, Is.Empty);
            Assert.That(table.NamespaceManagers, Is.Empty);
            Assert.That(generation.Gate.State, Is.EqualTo(NodeManagerGenerationState.Retiring));
        }

        /// <summary>
        /// A transparent predecessor adapter replaces the owner at its original routing position.
        /// </summary>
        [Test]
        public void BatchReplacesRegisteredStandardAdapterUsingAlias()
        {
            var table = new NodeManagerRoutingTable();
            INodeManager synchronous = new Mock<INodeManager>().Object;
            IAsyncNodeManager registered = new AsyncNodeManagerAdapter(synchronous);
            IAsyncNodeManager alias = new AsyncNodeManagerAdapter(synchronous);
            IAsyncNodeManager survivor = CreateEqualRoutingManager();
            IAsyncNodeManager replacement = CreateEqualRoutingManager();
            table.Add(registered, BatchNamespaceIndexes);
            table.Add(survivor, BatchNamespaceIndexes);
            NodeManagerGeneration oldGeneration = table.GetGeneration(registered);
            NodeManagerGeneration newGeneration = NodeManagerGenerationRegistry.CreateReplacement(
                oldGeneration, replacement);
            var prepared = new PreparedNodeManager(replacement, [], newGeneration, alias);

            using NodeManagerRoutingTable.PreparedRoutes routes = PrepareExactOwnerBatch(table, [prepared], []);
            routes.Reserve();
            routes.Publish();

            IReadOnlyList<IAsyncNodeManager> ordered = table.NamespaceManagers[2];
            Assert.That(ordered, Has.Count.EqualTo(2));
            Assert.That(ordered[0], Is.SameAs(replacement));
            Assert.That(ordered[1], Is.SameAs(survivor));
            Assert.That(table.GetGeneration(replacement), Is.SameAs(newGeneration));
            Assert.That(oldGeneration.Gate.State, Is.EqualTo(NodeManagerGenerationState.Retiring));
            Assert.That(table.GetGeneration(survivor).Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
        }

        /// <summary>
        /// A custom facade and a standard adapter over the same physical manager are not interchangeable owners.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void BatchRejectsDifferentFacadeOverSameResource(bool registerStandardAdapter)
        {
            var table = new NodeManagerRoutingTable();
            INodeManager synchronous = new Mock<INodeManager>().Object;
            IAsyncNodeManager standard = new AsyncNodeManagerAdapter(synchronous);
            IAsyncNodeManager custom = new EqualRoutingNodeManager(synchronous);
            IAsyncNodeManager registered = registerStandardAdapter ? standard : custom;
            IAsyncNodeManager other = registerStandardAdapter ? custom : standard;
            table.Add(registered, BatchNamespaceIndexes);
            NodeManagerGeneration generation = table.GetGeneration(registered);
            NodeManagerRoutingTable.RoutingSnapshot before = table.Revision;
            NodeManagerRoutingTable.PreparedRoutes? unexpected = null;
            try
            {
                Assert.Throws<InvalidOperationException>(() => unexpected = PrepareExactOwnerBatch(table, [], [other]));
            }
            finally
            {
                unexpected?.Dispose();
            }

            Assert.That(table.Revision, Is.SameAs(before));
            AssertSingleManagerRoute(table.NamespaceManagers, 2, registered);
            Assert.That(generation.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
        }

        /// <summary>
        /// Sharing a physical manager does not make two custom facades the same retirement owner.
        /// </summary>
        [Test]
        public void BatchRejectsDifferentCustomFacadeOverSameResource()
        {
            var table = new NodeManagerRoutingTable();
            INodeManager synchronous = new Mock<INodeManager>().Object;
            IAsyncNodeManager registered = new EqualRoutingNodeManager(synchronous);
            IAsyncNodeManager other = new EqualRoutingNodeManager(synchronous);
            table.Add(registered, BatchNamespaceIndexes);
            NodeManagerGeneration generation = table.GetGeneration(registered);
            NodeManagerRoutingTable.RoutingSnapshot before = table.Revision;
            NodeManagerRoutingTable.PreparedRoutes? unexpected = null;
            try
            {
                Assert.Throws<InvalidOperationException>(() => unexpected = PrepareExactOwnerBatch(table, [], [other]));
            }
            finally
            {
                unexpected?.Dispose();
            }

            Assert.That(table.Revision, Is.SameAs(before));
            AssertSingleManagerRoute(table.NamespaceManagers, 2, registered);
            Assert.That(generation.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
        }

        /// <summary>
        /// A distinct equal candidate remains eligible for publication.
        /// </summary>
        [Test]
        public void BatchAddsDistinctEqualCandidate()
        {
            var table = new NodeManagerRoutingTable();
            IAsyncNodeManager registered = CreateEqualRoutingManager();
            IAsyncNodeManager candidate = CreateEqualRoutingManager();
            table.Add(registered, BatchNamespaceIndexes);
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.CreateAddition(candidate);
            var prepared = new PreparedNodeManager(candidate, [], generation);
            Assert.That(registered, Is.EqualTo(candidate));

            using NodeManagerRoutingTable.PreparedRoutes routes = PrepareExactOwnerBatch(table, [prepared], []);
            routes.Reserve();
            routes.Publish();

            IReadOnlyList<IAsyncNodeManager> ordered = table.NamespaceManagers[2];
            Assert.That(ordered, Has.Count.EqualTo(2));
            Assert.That(ordered[0], Is.SameAs(registered));
            Assert.That(ordered[1], Is.SameAs(candidate));
            Assert.That(table.GetGeneration(candidate), Is.SameAs(generation));
            Assert.That(generation.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
        }

        /// <summary>
        /// Publishing and retiring one shared facade preserves every other exact owner and namespace route.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void BatchSharedResourceFacadesKeepIndependentRoutesAndRetirement(bool registerRawResource)
        {
            var table = new NodeManagerRoutingTable();
            var resource = new Mock<IAsyncNodeManager>();
            var synchronous = new SyncNodeManagerAdapter(resource.Object);
            resource.SetupGet(value => value.SyncNodeManager).Returns(synchronous);
            IAsyncNodeManager first = new EqualRoutingNodeManager(synchronous);
            IAsyncNodeManager second = new EqualRoutingNodeManager(synchronous);
            if (registerRawResource)
            {
                table.Add(resource.Object, BatchNamespaceIndexes);
            }
            table.Add(first, BatchNamespaceIndexes);
            NodeManagerGeneration original = table.GetGeneration(first);
            NodeManagerGeneration added = NodeManagerGenerationRegistry.CreateAddition(second);
            var prepared = new PreparedNodeManager(second, [], added);
            using (NodeManagerRoutingTable.PreparedRoutes publication = PrepareExactOwnerBatch(table, [prepared], []))
            {
                publication.Reserve();
                publication.Publish();
            }
            Assert.That(table.GetGeneration(first), Is.SameAs(original));
            Assert.That(table.GetGeneration(second), Is.SameAs(added));
            Assert.That(added, Is.Not.SameAs(original));
            Assert.That(table.NamespaceManagers[2], Has.Count.EqualTo(registerRawResource ? 3 : 2));
            Assert.That(table.Revision.TryGetSourceGeneration(resource.Object, out _), Is.False);

            using (NodeManagerRoutingTable.PreparedRoutes retirement = PrepareExactOwnerBatch(table, [], [first]))
            {
                retirement.Reserve();
                retirement.Publish();
            }
            Assert.That(table.Contains(first), Is.False);
            Assert.That(table.Contains(second), Is.True);
            Assert.That(table.NamespaceManagers[2], Has.Count.EqualTo(registerRawResource ? 2 : 1));
            Assert.That(table.NamespaceManagers[2][registerRawResource ? 1 : 0], Is.SameAs(second));
            Assert.That(original.Gate.State, Is.EqualTo(NodeManagerGenerationState.Retiring));
            Assert.That(added.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
            if (registerRawResource)
            {
                Assert.That(table.NamespaceManagers[2][0], Is.SameAs(resource.Object));
                Assert.That(table.GetGeneration(resource.Object).Gate.State,
                    Is.EqualTo(NodeManagerGenerationState.Live));
            }
        }

        /// <summary>
        /// A publication cannot contain the same prepared manager twice.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void BatchRejectsDuplicateExactCandidate(bool separatePreparedHandles)
        {
            var table = new NodeManagerRoutingTable();
            IAsyncNodeManager manager = CreateEqualRoutingManager();
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.CreateAddition(manager);
            var first = new PreparedNodeManager(manager, [], generation);
            PreparedNodeManager second = separatePreparedHandles
                ? new PreparedNodeManager(manager, [], generation)
                : first;
            NodeManagerRoutingTable.RoutingSnapshot before = table.Revision;
            NodeManagerRoutingTable.PreparedRoutes? unexpected = null;
            try
            {
                Assert.That(
                    () => unexpected = PrepareExactOwnerBatch(table, [first, second], []),
                    Throws.InvalidOperationException.With.Message.EqualTo(
                        "A batch candidate was supplied more than once."));
            }
            finally
            {
                unexpected?.Dispose();
            }

            Assert.That(table.Revision, Is.SameAs(before));
            Assert.That(generation.Gate.State, Is.EqualTo(NodeManagerGenerationState.Prepared));
            Assert.DoesNotThrow(first.ValidateForPublication);
            Assert.DoesNotThrow(second.ValidateForPublication);
        }

        /// <summary>
        /// Transparent aliases cannot publish the same prepared manager twice.
        /// </summary>
        [Test]
        public void BatchRejectsDuplicateStandardAdapterCandidates()
        {
            var table = new NodeManagerRoutingTable();
            INodeManager synchronous = new Mock<INodeManager>().Object;
            IAsyncNodeManager firstManager = new AsyncNodeManagerAdapter(synchronous);
            IAsyncNodeManager secondManager = new AsyncNodeManagerAdapter(synchronous);
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.CreateAddition(firstManager);
            var first = new PreparedNodeManager(firstManager, [], generation);
            var second = new PreparedNodeManager(secondManager, [], generation);
            NodeManagerRoutingTable.RoutingSnapshot before = table.Revision;
            NodeManagerRoutingTable.PreparedRoutes? unexpected = null;
            try
            {
                Assert.That(
                    () => unexpected = PrepareExactOwnerBatch(table, [first, second], []),
                    Throws.InvalidOperationException.With.Message.EqualTo(
                        "A batch candidate was supplied more than once."));
            }
            finally
            {
                unexpected?.Dispose();
            }

            Assert.That(table.Revision, Is.SameAs(before));
            Assert.That(generation.Gate.State, Is.EqualTo(NodeManagerGenerationState.Prepared));
            Assert.DoesNotThrow(first.ValidateForPublication);
            Assert.DoesNotThrow(second.ValidateForPublication);
        }

        /// <summary>
        /// One registered owner cannot be replaced by two candidates in the same publication.
        /// </summary>
        [Test]
        public void BatchRejectsDuplicateReplacementPredecessor()
        {
            var table = new NodeManagerRoutingTable();
            IAsyncNodeManager predecessor = CreateEqualRoutingManager();
            table.Add(predecessor, BatchNamespaceIndexes);
            NodeManagerGeneration current = table.GetGeneration(predecessor);
            IAsyncNodeManager firstManager = CreateEqualRoutingManager();
            IAsyncNodeManager secondManager = CreateEqualRoutingManager();
            NodeManagerGeneration firstGeneration = NodeManagerGenerationRegistry.CreateReplacement(current, firstManager);
            NodeManagerGeneration secondGeneration =
                NodeManagerGenerationRegistry.CreateReplacement(current, secondManager);
            var first = new PreparedNodeManager(firstManager, [], firstGeneration, predecessor);
            var second = new PreparedNodeManager(secondManager, [], secondGeneration, predecessor);
            NodeManagerRoutingTable.RoutingSnapshot before = table.Revision;
            NodeManagerRoutingTable.PreparedRoutes? unexpected = null;
            try
            {
                Assert.That(
                    () => unexpected = PrepareExactOwnerBatch(table, [first, second], []),
                    Throws.InvalidOperationException.With.Message.EqualTo(
                        "A batch predecessor was supplied more than once."));
            }
            finally
            {
                unexpected?.Dispose();
            }

            Assert.That(table.Revision, Is.SameAs(before));
            Assert.That(current.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
            Assert.That(firstGeneration.Gate.State, Is.EqualTo(NodeManagerGenerationState.Prepared));
            Assert.That(secondGeneration.Gate.State, Is.EqualTo(NodeManagerGenerationState.Prepared));
            AssertSingleManagerRoute(table.NamespaceManagers, 2, predecessor);
        }

        /// <summary>
        /// Replacements of distinct equal managers retain their exact routing positions.
        /// </summary>
        [Test]
        public void BatchReplacesDistinctEqualManagersInTheirOwnRoutePositions()
        {
            var table = new NodeManagerRoutingTable();
            IAsyncNodeManager first = CreateEqualRoutingManager();
            IAsyncNodeManager second = CreateEqualRoutingManager();
            IAsyncNodeManager firstReplacement = CreateEqualRoutingManager();
            IAsyncNodeManager secondReplacement = CreateEqualRoutingManager();
            table.Add(first, BatchNamespaceIndexes);
            table.Add(second, BatchNamespaceIndexes);
            NodeManagerGeneration firstGeneration = table.GetGeneration(first);
            NodeManagerGeneration secondGeneration = table.GetGeneration(second);
            NodeManagerGeneration nextFirst =
                NodeManagerGenerationRegistry.CreateReplacement(firstGeneration, firstReplacement);
            NodeManagerGeneration nextSecond =
                NodeManagerGenerationRegistry.CreateReplacement(secondGeneration, secondReplacement);
            var firstPrepared = new PreparedNodeManager(firstReplacement, [], nextFirst, first);
            var secondPrepared = new PreparedNodeManager(secondReplacement, [], nextSecond, second);

            using NodeManagerRoutingTable.PreparedRoutes routes =
                PrepareExactOwnerBatch(table, [firstPrepared, secondPrepared], []);
            routes.Reserve();
            routes.Publish();

            IReadOnlyList<IAsyncNodeManager> ordered = table.NamespaceManagers[2];
            Assert.That(ordered, Has.Count.EqualTo(2));
            Assert.That(ordered[0], Is.SameAs(firstReplacement));
            Assert.That(ordered[1], Is.SameAs(secondReplacement));
            Assert.That(table.GetGeneration(firstReplacement), Is.SameAs(nextFirst));
            Assert.That(table.GetGeneration(secondReplacement), Is.SameAs(nextSecond));
            Assert.That(firstGeneration.Gate.State, Is.EqualTo(NodeManagerGenerationState.Retiring));
            Assert.That(secondGeneration.Gate.State, Is.EqualTo(NodeManagerGenerationState.Retiring));
        }

        /// <summary>
        /// Replacing the first of several equal managers preserves its dispatch position.
        /// </summary>
        [Test]
        public void BatchOrdersReplacementBeforeDistinctEqualSurvivors()
        {
            var table = new NodeManagerRoutingTable();
            IAsyncNodeManager first = CreateEqualRoutingManager();
            IAsyncNodeManager second = CreateEqualRoutingManager();
            IAsyncNodeManager third = CreateEqualRoutingManager();
            IAsyncNodeManager replacement = CreateEqualRoutingManager();
            table.Add(first, BatchNamespaceIndexes);
            table.Add(second, BatchNamespaceIndexes);
            table.Add(third, BatchNamespaceIndexes);
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.CreateReplacement(
                table.GetGeneration(first), replacement);
            var prepared = new PreparedNodeManager(replacement, [], generation, first);

            using NodeManagerRoutingTable.PreparedRoutes routes = PrepareExactOwnerBatch(table, [prepared], []);
            routes.Reserve();
            routes.Publish();

            IReadOnlyList<IAsyncNodeManager> ordered = table.NamespaceManagers[2];
            Assert.That(ordered, Has.Count.EqualTo(3));
            Assert.That(ordered[0], Is.SameAs(replacement));
            Assert.That(ordered[1], Is.SameAs(second));
            Assert.That(ordered[2], Is.SameAs(third));
        }

        private static NodeManagerRoutingTable.PreparedRoutes PrepareExactOwnerBatch(
            NodeManagerRoutingTable table,
            ArrayOf<PreparedNodeManager> candidates,
            ArrayOf<IAsyncNodeManager> removed)
        {
            return table.PrepareBatch(
                candidates, removed, _ => BatchNamespaceIndexes,
                new TypeTable(new NamespaceTable()), (EncodeableFactory)EncodeableFactory.Create());
        }

        private static EqualRoutingNodeManager CreateEqualRoutingManager()
        {
            return new EqualRoutingNodeManager(new Mock<INodeManager>().Object);
        }

        /// <summary>
        /// Supplies equal-valued facades backed by distinct synchronous managers.
        /// </summary>
        private sealed class EqualRoutingNodeManager : AsyncNodeManagerAdapter
        {
            /// <summary>
            /// Creates an equal-valued facade for a synchronous manager.
            /// </summary>
            public EqualRoutingNodeManager(INodeManager manager)
                : base(manager)
            {
            }

            /// <inheritdoc/>
            public override bool Equals(object? other)
            {
                return other is EqualRoutingNodeManager;
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                return 1;
            }
        }
    }
}
