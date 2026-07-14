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

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    [TestFixture]
    [Category("NodeManagerLifecycle")]
    [Parallelizable(ParallelScope.All)]
    public sealed class NodeManagerRoutingTableTests
    {
        private static readonly int[] AddNamespaceIndexes = [2, 2, 3];
        private static readonly int[] ReplaceNamespaceIndexes = [3, 4];
        private static readonly int[] InitialNamespaceIndexes = [2, 3];
        private static readonly int[] ConcurrentNamespaceIndexes = [9];

        [Test]
        public void AddPublishesManagerAndNamespaceRoutesWithoutMutatingCapturedSnapshots()
        {
            NodeManagerRoutingTable table = CreateTable(
                out IAsyncNodeManager firstPermanent,
                out IAsyncNodeManager secondPermanent);
            IAsyncNodeManager lifecycleManager = CreateManager();
            using IEnumerator<IAsyncNodeManager> managerSnapshot = table.GetEnumerator();
            IReadOnlyDictionary<int, IReadOnlyList<IAsyncNodeManager>>
                namespaceRouteSnapshot = table.NamespaceManagers;

            table.Add(lifecycleManager, AddNamespaceIndexes);

            IReadOnlyDictionary<int, IReadOnlyList<IAsyncNodeManager>> currentRoutes =
                table.NamespaceManagers;
            Assert.That(table, Has.Count.EqualTo(3));
            Assert.That(table[0], Is.SameAs(firstPermanent));
            Assert.That(table[1], Is.SameAs(secondPermanent));
            Assert.That(table[2], Is.SameAs(lifecycleManager));
            Assert.That(currentRoutes, Is.Not.SameAs(namespaceRouteSnapshot));
            Assert.That(currentRoutes, Has.Count.EqualTo(2));
            Assert.That(currentRoutes.Keys, Is.EquivalentTo(InitialNamespaceIndexes));
            AssertSingleManagerRoute(currentRoutes, 2, lifecycleManager);
            AssertSingleManagerRoute(currentRoutes, 3, lifecycleManager);

            AssertManagerSnapshot(
                managerSnapshot,
                firstPermanent,
                secondPermanent);
            Assert.That(namespaceRouteSnapshot, Is.Empty);
        }

        [Test]
        public void ReplacePublishesReplacementRoutesWithoutMutatingCapturedSnapshots()
        {
            NodeManagerRoutingTable table = CreateTable(
                out IAsyncNodeManager firstPermanent,
                out IAsyncNodeManager secondPermanent);
            IAsyncNodeManager original = CreateManager();
            IAsyncNodeManager replacement = CreateManager();
            table.Add(original, InitialNamespaceIndexes);
            using IEnumerator<IAsyncNodeManager> managerSnapshot = table.GetEnumerator();
            IReadOnlyDictionary<int, IReadOnlyList<IAsyncNodeManager>>
                namespaceRouteSnapshot = table.NamespaceManagers;
            IReadOnlyList<IAsyncNodeManager> namespaceTwoSnapshot =
                namespaceRouteSnapshot[2];
            IReadOnlyList<IAsyncNodeManager> namespaceThreeSnapshot =
                namespaceRouteSnapshot[3];

            table.Replace(original, replacement, ReplaceNamespaceIndexes);

            IReadOnlyDictionary<int, IReadOnlyList<IAsyncNodeManager>> currentRoutes =
                table.NamespaceManagers;
            Assert.That(table, Has.Count.EqualTo(3));
            Assert.That(table[0], Is.SameAs(firstPermanent));
            Assert.That(table[1], Is.SameAs(secondPermanent));
            Assert.That(table[2], Is.SameAs(replacement));
            Assert.That(table[2], Is.Not.SameAs(original));
            Assert.That(currentRoutes, Is.Not.SameAs(namespaceRouteSnapshot));
            Assert.That(currentRoutes, Has.Count.EqualTo(2));
            Assert.That(currentRoutes.Keys, Is.EquivalentTo(ReplaceNamespaceIndexes));
            Assert.That(currentRoutes.ContainsKey(2), Is.False);
            AssertSingleManagerRoute(currentRoutes, 3, replacement);
            AssertSingleManagerRoute(currentRoutes, 4, replacement);
            Assert.That(currentRoutes[3][0], Is.Not.SameAs(original));
            Assert.That(currentRoutes[4][0], Is.Not.SameAs(original));

            AssertManagerSnapshot(
                managerSnapshot,
                firstPermanent,
                secondPermanent,
                original);
            Assert.That(namespaceRouteSnapshot, Has.Count.EqualTo(2));
            Assert.That(namespaceRouteSnapshot.Keys, Is.EquivalentTo(InitialNamespaceIndexes));
            Assert.That(namespaceRouteSnapshot[2], Is.SameAs(namespaceTwoSnapshot));
            Assert.That(namespaceRouteSnapshot[3], Is.SameAs(namespaceThreeSnapshot));
            AssertSingleManagerRoute(namespaceRouteSnapshot, 2, original);
            AssertSingleManagerRoute(namespaceRouteSnapshot, 3, original);
        }

        [Test]
        public void RemoveUnpublishesManagerWithoutMutatingCapturedSnapshots()
        {
            NodeManagerRoutingTable table = CreateTable(
                out IAsyncNodeManager firstPermanent,
                out IAsyncNodeManager secondPermanent);
            IAsyncNodeManager lifecycleManager = CreateManager();
            table.Add(lifecycleManager, InitialNamespaceIndexes);
            using IEnumerator<IAsyncNodeManager> managerSnapshot = table.GetEnumerator();
            IReadOnlyDictionary<int, IReadOnlyList<IAsyncNodeManager>>
                namespaceRouteSnapshot = table.NamespaceManagers;
            IReadOnlyList<IAsyncNodeManager> namespaceTwoSnapshot =
                namespaceRouteSnapshot[2];
            IReadOnlyList<IAsyncNodeManager> namespaceThreeSnapshot =
                namespaceRouteSnapshot[3];

            table.Remove(lifecycleManager);

            IReadOnlyDictionary<int, IReadOnlyList<IAsyncNodeManager>> currentRoutes =
                table.NamespaceManagers;
            Assert.That(table, Has.Count.EqualTo(2));
            Assert.That(table[0], Is.SameAs(firstPermanent));
            Assert.That(table[1], Is.SameAs(secondPermanent));
            Assert.That(currentRoutes, Is.Not.SameAs(namespaceRouteSnapshot));
            Assert.That(currentRoutes, Is.Empty);

            AssertManagerSnapshot(
                managerSnapshot,
                firstPermanent,
                secondPermanent,
                lifecycleManager);
            Assert.That(namespaceRouteSnapshot, Has.Count.EqualTo(2));
            Assert.That(namespaceRouteSnapshot.Keys, Is.EquivalentTo(InitialNamespaceIndexes));
            Assert.That(namespaceRouteSnapshot[2], Is.SameAs(namespaceTwoSnapshot));
            Assert.That(namespaceRouteSnapshot[3], Is.SameAs(namespaceThreeSnapshot));
            AssertSingleManagerRoute(namespaceRouteSnapshot, 2, lifecycleManager);
            AssertSingleManagerRoute(namespaceRouteSnapshot, 3, lifecycleManager);
        }

        /// <summary>
        /// While one writer alternates replacing the single lifecycle-managed slot between
        /// two manager instances (~200 times) and several readers loop concurrently, every
        /// individually captured manager-list snapshot (via <see cref="NodeManagerRoutingTable.GetEnumerator"/>)
        /// must remain structurally complete - three managers, the first two always the
        /// same permanent instances, and the third always exactly one of the two lifecycle
        /// generations, never anything else - and every individually captured
        /// <see cref="NodeManagerRoutingTable.NamespaceManagers"/> snapshot must show exactly
        /// one non-null route to the same namespace index, pointing at exactly one valid
        /// lifecycle generation. The manager list and the namespace-route dictionary are
        /// each read via a single, separate, atomic snapshot access; a legitimate concurrent
        /// write can therefore be interleaved between the two reads, so the two are
        /// deliberately never cross-correlated against each other.
        /// </summary>
        [Test]
        public async Task ConcurrentReadersObserveOnlyCompleteRoutingSnapshotsAsync()
        {
            const int ReaderIterations = 100;
            const int ReaderCount = 4;

            NodeManagerRoutingTable table = CreateTable(
                out IAsyncNodeManager firstPermanent,
                out IAsyncNodeManager secondPermanent);
            IAsyncNodeManager generationA = CreateManager();
            IAsyncNodeManager generationB = CreateManager();
            table.Add(generationA, ConcurrentNamespaceIndexes);

            var managerSnapshotFailures = new ConcurrentBag<string>();
            var namespaceSnapshotFailures = new ConcurrentBag<string>();
            using var startBarrier = new Barrier(ReaderCount + 1);
            using var writerStarted = new ManualResetEventSlim();
            using var readersReadyForConcurrentWrite = new CountdownEvent(ReaderCount);
            using var concurrentWriteCompleted = new ManualResetEventSlim();
            using var stopWriter = new CancellationTokenSource();
            int writerIterations = 0;

            var writerTask = Task.Run(() =>
            {
                IAsyncNodeManager current = generationA;
                startBarrier.SignalAndWait();

                ReplaceCurrent();
                writerStarted.Set();
                readersReadyForConcurrentWrite.Wait();
                ReplaceCurrent();
                concurrentWriteCompleted.Set();

                while (!stopWriter.IsCancellationRequested)
                {
                    ReplaceCurrent();
                    Thread.Yield();
                }

                void ReplaceCurrent()
                {
                    IAsyncNodeManager next = ReferenceEquals(current, generationA)
                            ? generationB
                            : generationA;
                    table.Replace(current, next, ConcurrentNamespaceIndexes);
                    current = next;
                    Interlocked.Increment(ref writerIterations);
                }
            });

            Task[] readerTasks = [.. Enumerable.Range(0, ReaderCount).Select(_ => Task.Run(() =>
            {
                startBarrier.SignalAndWait();
                writerStarted.Wait();
                readersReadyForConcurrentWrite.Signal();
                concurrentWriteCompleted.Wait();

                for (int iteration = 0; iteration < ReaderIterations; iteration++)
                {
                    using IEnumerator<IAsyncNodeManager> managerSnapshot = table.GetEnumerator();
                    var managers = new List<IAsyncNodeManager>();
                    while (managerSnapshot.MoveNext())
                    {
                        managers.Add(managerSnapshot.Current);
                    }

                    if (managers.Count != 3 ||
                        !ReferenceEquals(managers[0], firstPermanent) ||
                        !ReferenceEquals(managers[1], secondPermanent) ||
                        (!ReferenceEquals(managers[2], generationA) &&
                            !ReferenceEquals(managers[2], generationB)))
                    {
                        managerSnapshotFailures.Add(
                            $"iteration {iteration}: manager count {managers.Count}");
                    }

                    IReadOnlyDictionary<int, IReadOnlyList<IAsyncNodeManager>> namespaceRoutes =
                        table.NamespaceManagers;
                    int namespaceIndex = ConcurrentNamespaceIndexes[0];
                    bool hasRoute = namespaceRoutes.TryGetValue(
                        namespaceIndex,
                        out IReadOnlyList<IAsyncNodeManager> route);
                    if (!hasRoute ||
                        route.Count != 1 ||
                        (!ReferenceEquals(route[0], generationA) &&
                            !ReferenceEquals(route[0], generationB)))
                    {
                        namespaceSnapshotFailures.Add(
                            $"iteration {iteration}: hasRoute {hasRoute}, " +
                            $"count {(hasRoute ? route.Count : -1)}");
                    }

                    Thread.Yield();
                }
            }))];

            try
            {
                await Task.WhenAll(readerTasks).ConfigureAwait(false);
            }
            finally
            {
                stopWriter.Cancel();
                await writerTask.ConfigureAwait(false);
            }

            Assert.That(writerIterations, Is.GreaterThan(2));
            Assert.That(managerSnapshotFailures, Is.Empty);
            Assert.That(namespaceSnapshotFailures, Is.Empty);
        }

        private static NodeManagerRoutingTable CreateTable(
            out IAsyncNodeManager firstPermanent,
            out IAsyncNodeManager secondPermanent)
        {
            firstPermanent = CreateManager();
            secondPermanent = CreateManager();
            var table = new NodeManagerRoutingTable();
            table.AddInitial(firstPermanent);
            table.AddInitial(secondPermanent);
            return table;
        }

        private static IAsyncNodeManager CreateManager()
        {
            return new Mock<IAsyncNodeManager>().Object;
        }

        private static void AssertManagerSnapshot(
            IEnumerator<IAsyncNodeManager> snapshot,
            params IAsyncNodeManager[] expected)
        {
            foreach (IAsyncNodeManager expectedManager in expected)
            {
                Assert.That(snapshot.MoveNext(), Is.True);
                Assert.That(snapshot.Current, Is.SameAs(expectedManager));
            }
            Assert.That(snapshot.MoveNext(), Is.False);
        }

        private static void AssertSingleManagerRoute(
            IReadOnlyDictionary<int, IReadOnlyList<IAsyncNodeManager>> routes,
            int namespaceIndex,
            IAsyncNodeManager expected)
        {
            Assert.That(routes.ContainsKey(namespaceIndex), Is.True);
            Assert.That(routes[namespaceIndex], Has.Count.EqualTo(1));
            Assert.That(routes[namespaceIndex][0], Is.SameAs(expected));
        }
    }
}
