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

        [Test]
        public void AddInitialThrowsOnNullNodeManager()
        {
            var table = new NodeManagerRoutingTable();

            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => table.AddInitial(null!))!;
            Assert.That(ex.ParamName, Is.EqualTo("nodeManager"));
        }

        [Test]
        public void InitializePublishesNamespaceRoutesFromDictionary()
        {
            var table = new NodeManagerRoutingTable();
            IAsyncNodeManager manager = CreateManager();

            ArgumentNullException nullEx = Assert.Throws<ArgumentNullException>(
                () => table.Initialize(null!))!;
            Assert.That(nullEx.ParamName, Is.EqualTo("namespaceManagers"));

            table.Initialize(
                new Dictionary<int, List<IAsyncNodeManager>> { [5] = [manager] });

            AssertSingleManagerRoute(table.NamespaceManagers, 5, manager);
        }

        [Test]
        public void AddThrowsOnNullNodeManager()
        {
            NodeManagerRoutingTable table = CreateTable(out _, out _);

            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => table.Add(null!, AddNamespaceIndexes))!;
            Assert.That(ex.ParamName, Is.EqualTo("nodeManager"));
        }

        [Test]
        public void AddThrowsOnNullNamespaceIndexes()
        {
            NodeManagerRoutingTable table = CreateTable(out _, out _);

            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => table.Add(CreateManager(), null!))!;
            Assert.That(ex.ParamName, Is.EqualTo("namespaceIndexes"));
        }

        [Test]
        public void AddThrowsWhenNodeManagerAlreadyRegistered()
        {
            NodeManagerRoutingTable table = CreateTable(out IAsyncNodeManager firstPermanent, out _);

            Assert.Throws<InvalidOperationException>(
                () => table.Add(firstPermanent, AddNamespaceIndexes));
        }

        [Test]
        public void AddWithVisibleFalseHidesManagerFromEnumerationButNotFromIndexer()
        {
            NodeManagerRoutingTable table = CreateTable(
                out IAsyncNodeManager firstPermanent,
                out IAsyncNodeManager secondPermanent);
            IAsyncNodeManager hiddenManager = CreateManager();

            table.Add(hiddenManager, AddNamespaceIndexes, visible: false);

            Assert.That(table, Has.Count.EqualTo(3));
            Assert.That(table[2], Is.SameAs(hiddenManager));
            Assert.That(table.IsVisible(hiddenManager), Is.False);
            using IEnumerator<IAsyncNodeManager> visibleSnapshot = table.GetEnumerator();
            AssertManagerSnapshot(visibleSnapshot, firstPermanent, secondPermanent);
        }

        [Test]
        public void ReplaceThrowsOnNullArguments()
        {
            NodeManagerRoutingTable table = CreateTable(out _, out _);
            IAsyncNodeManager original = CreateManager();
            table.Add(original, InitialNamespaceIndexes);

            Assert.That(
                Assert.Throws<ArgumentNullException>(
                    () => table.Replace(null!, CreateManager(), ReplaceNamespaceIndexes))!.ParamName,
                Is.EqualTo("current"));
            Assert.That(
                Assert.Throws<ArgumentNullException>(
                    () => table.Replace(original, null!, ReplaceNamespaceIndexes))!.ParamName,
                Is.EqualTo("replacement"));
            Assert.That(
                Assert.Throws<ArgumentNullException>(
                    () => table.Replace(original, CreateManager(), null!))!.ParamName,
                Is.EqualTo("replacementNamespaceIndexes"));
        }

        [Test]
        public void ReplaceThrowsWhenCurrentIsNotLifecycleManaged()
        {
            NodeManagerRoutingTable table = CreateTable(
                out IAsyncNodeManager firstPermanent,
                out _);

            Assert.Throws<InvalidOperationException>(
                () => table.Replace(firstPermanent, CreateManager(), ReplaceNamespaceIndexes));
        }

        [Test]
        public void ReplaceThrowsWhenReplacementAlreadyRegistered()
        {
            NodeManagerRoutingTable table = CreateTable(out _, out _);
            IAsyncNodeManager original = CreateManager();
            IAsyncNodeManager other = CreateManager();
            table.Add(original, InitialNamespaceIndexes);
            table.Add(other, ReplaceNamespaceIndexes);

            Assert.Throws<InvalidOperationException>(
                () => table.Replace(original, other, ReplaceNamespaceIndexes));
        }

        [Test]
        public void RemoveThrowsOnNullNodeManager()
        {
            NodeManagerRoutingTable table = CreateTable(out _, out _);

            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => table.Remove(null!))!;
            Assert.That(ex.ParamName, Is.EqualTo("nodeManager"));
        }

        [Test]
        public void RemoveThrowsWhenNodeManagerIsNotLifecycleManaged()
        {
            NodeManagerRoutingTable table = CreateTable(out IAsyncNodeManager firstPermanent, out _);

            Assert.Throws<InvalidOperationException>(() => table.Remove(firstPermanent));
        }

        [Test]
        public void RegisterNamespaceIsIdempotentForTheSameManager()
        {
            var table = new NodeManagerRoutingTable();
            IAsyncNodeManager manager = CreateManager();

            table.RegisterNamespace(7, manager);
            table.RegisterNamespace(7, manager);

            AssertSingleManagerRoute(table.NamespaceManagers, 7, manager);
        }

        [Test]
        public void RegisterNamespaceAppendsToExistingRouteForDifferentManager()
        {
            var table = new NodeManagerRoutingTable();
            IAsyncNodeManager first = CreateManager();
            IAsyncNodeManager second = CreateManager();

            table.RegisterNamespace(7, first);
            table.RegisterNamespace(7, second);

            Assert.That(table.NamespaceManagers[7], Has.Count.EqualTo(2));
            Assert.That(table.NamespaceManagers[7], Does.Contain(first));
            Assert.That(table.NamespaceManagers[7], Does.Contain(second));
        }

        [Test]
        public void RegisterNamespaceWithVisibleFalseHidesManager()
        {
            var table = new NodeManagerRoutingTable();
            IAsyncNodeManager manager = CreateManager();
            table.AddInitial(manager);

            table.RegisterNamespace(7, manager, visible: false);

            Assert.That(table.IsVisible(manager), Is.False);
        }

        [Test]
        public void UnregisterNamespaceReturnsFalseWhenNamespaceNotFound()
        {
            var table = new NodeManagerRoutingTable();

            Assert.That(table.UnregisterNamespace(1, CreateManager(), null), Is.False);
        }

        [Test]
        public void UnregisterNamespaceReturnsFalseWhenManagerNotInRoute()
        {
            var table = new NodeManagerRoutingTable();
            table.RegisterNamespace(7, CreateManager());

            Assert.That(table.UnregisterNamespace(7, CreateManager(), null), Is.False);
        }

        [Test]
        public void UnregisterNamespaceRemovesRouteEntryWhenLastManagerRemoved()
        {
            var table = new NodeManagerRoutingTable();
            IAsyncNodeManager manager = CreateManager();
            table.RegisterNamespace(7, manager);

            bool removed = table.UnregisterNamespace(7, manager, null);

            Assert.That(removed, Is.True);
            Assert.That(table.NamespaceManagers.ContainsKey(7), Is.False);
        }

        [Test]
        public void UnregisterNamespaceKeepsRouteWhenOtherManagersRemain()
        {
            var table = new NodeManagerRoutingTable();
            IAsyncNodeManager first = CreateManager();
            IAsyncNodeManager second = CreateManager();
            table.RegisterNamespace(7, first);
            table.RegisterNamespace(7, second);

            bool removed = table.UnregisterNamespace(7, first, null);

            Assert.That(removed, Is.True);
            AssertSingleManagerRoute(table.NamespaceManagers, 7, second);
        }

        [Test]
        public void UnregisterNamespaceMatchesBySyncNodeManagerWhenAsyncNodeManagerIsNull()
        {
            var table = new NodeManagerRoutingTable();
            INodeManager syncManager = new Mock<INodeManager>().Object;
            IAsyncNodeManager manager = CreateManagerWithSync(syncManager);
            table.RegisterNamespace(7, manager);

            bool removed = table.UnregisterNamespace(7, null, syncManager);

            Assert.That(removed, Is.True);
            Assert.That(table.NamespaceManagers.ContainsKey(7), Is.False);
        }

        [Test]
        public void RemoveNamespaceManagerThrowsOnNullNodeManager()
        {
            var table = new NodeManagerRoutingTable();

            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => table.RemoveNamespaceManager(null!))!;
            Assert.That(ex.ParamName, Is.EqualTo("nodeManager"));
        }

        [Test]
        public void RemoveNamespaceManagerRemovesAcrossAllNamespacesAndMatchesBySyncNodeManager()
        {
            var table = new NodeManagerRoutingTable();
            INodeManager syncManager = new Mock<INodeManager>().Object;
            IAsyncNodeManager registeredManager = CreateManagerWithSync(syncManager);
            IAsyncNodeManager lookupManager = CreateManagerWithSync(syncManager);
            IAsyncNodeManager unrelated = CreateManager();
            table.RegisterNamespace(2, registeredManager);
            table.RegisterNamespace(3, registeredManager);
            table.RegisterNamespace(3, unrelated);

            table.RemoveNamespaceManager(lookupManager);

            Assert.That(table.NamespaceManagers.ContainsKey(2), Is.False);
            AssertSingleManagerRoute(table.NamespaceManagers, 3, unrelated);
        }

        [Test]
        public void IsVisibleReturnsFalseForUnregisteredManager()
        {
            NodeManagerRoutingTable table = CreateTable(out _, out _);

            Assert.That(table.IsVisible(CreateManager()), Is.False);
        }

        [Test]
        public void IsVisibleReturnsTrueForRegisteredVisibleManager()
        {
            NodeManagerRoutingTable table = CreateTable(out IAsyncNodeManager firstPermanent, out _);

            Assert.That(table.IsVisible(firstPermanent), Is.True);
        }

        [Test]
        public void SetVisibleThrowsOnNullNodeManager()
        {
            NodeManagerRoutingTable table = CreateTable(out _, out _);

            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => table.SetVisible(null!, false))!;
            Assert.That(ex.ParamName, Is.EqualTo("nodeManager"));
        }

        [Test]
        public void SetVisibleThrowsWhenManagerNotRegistered()
        {
            NodeManagerRoutingTable table = CreateTable(out _, out _);

            Assert.Throws<InvalidOperationException>(
                () => table.SetVisible(CreateManager(), false));
        }

        [Test]
        public void SetVisibleTogglesVisibilityAffectingEnumerationAndIsVisible()
        {
            NodeManagerRoutingTable table = CreateTable(
                out IAsyncNodeManager firstPermanent,
                out IAsyncNodeManager secondPermanent);

            table.SetVisible(firstPermanent, false);

            Assert.That(table.IsVisible(firstPermanent), Is.False);
            using (IEnumerator<IAsyncNodeManager> hiddenSnapshot = table.GetEnumerator())
            {
                AssertManagerSnapshot(hiddenSnapshot, secondPermanent);
            }

            table.SetVisible(firstPermanent, true);

            Assert.That(table.IsVisible(firstPermanent), Is.True);
            using IEnumerator<IAsyncNodeManager> restoredSnapshot = table.GetEnumerator();
            AssertManagerSnapshot(restoredSnapshot, firstPermanent, secondPermanent);
        }

        [Test]
        public void ClearResetsAllState()
        {
            NodeManagerRoutingTable table = CreateTable(out _, out _);
            table.Add(CreateManager(), InitialNamespaceIndexes);

            table.Clear();

            Assert.That(table, Has.Count.EqualTo(0));
            Assert.That(table.NamespaceManagers, Is.Empty);
        }

        [Test]
        public void IndexerReturnsManagerAtPosition()
        {
            NodeManagerRoutingTable table = CreateTable(
                out IAsyncNodeManager firstPermanent,
                out IAsyncNodeManager secondPermanent);

            Assert.That(table[0], Is.SameAs(firstPermanent));
            Assert.That(table[1], Is.SameAs(secondPermanent));
        }

        [Test]
        public void ExplicitEnumerableGetEnumeratorReturnsSameManagersAsGenericEnumerator()
        {
            NodeManagerRoutingTable table = CreateTable(
                out IAsyncNodeManager firstPermanent,
                out IAsyncNodeManager secondPermanent);

            var managers = new List<IAsyncNodeManager>();
            System.Collections.IEnumerable untyped = table;
            System.Collections.IEnumerator enumerator = untyped.GetEnumerator();
            while (enumerator.MoveNext())
            {
                managers.Add((IAsyncNodeManager)enumerator.Current!);
            }

            Assert.That(managers, Is.EqualTo([firstPermanent, secondPermanent]));
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

        private static IAsyncNodeManager CreateManagerWithSync(INodeManager syncNodeManager)
        {
            var manager = new Mock<IAsyncNodeManager>();
            manager.Setup(m => m.SyncNodeManager).Returns(syncNodeManager);
            return manager.Object;
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
