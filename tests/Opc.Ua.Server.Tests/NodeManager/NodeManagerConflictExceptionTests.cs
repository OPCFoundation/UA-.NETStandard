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
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Tests native conflict retention and no-replay semantics under the former compatibility case identities.
    /// </summary>
    [TestFixture]
    [Category("Server")]
    [Category("NodeManagerLifecycle")]
    [Parallelizable(ParallelScope.All)]
    public sealed class NodeManagerConflictExceptionTests
    {
        [Test]
        public async Task RoutingConflictReleasesBatchAndRoutingGraph()
        {
            (InvalidOperationException conflict, WeakReference<IPreparedChanges> batch,
                WeakReference<NodeManagerRoutingTable.RoutingSnapshot> snapshot,
                WeakReference<IAsyncNodeManager> manager) = await CreateRetentionControlAsync(false)
                .ConfigureAwait(false);
            await Task.Yield();
            CollectTargets();
            Assert.That(IsAlive(batch), Is.False);
            Assert.That(IsAlive(snapshot), Is.False);
            Assert.That(IsAlive(manager), Is.False);
            Assert.That(conflict, Is.TypeOf<NodeManagerRoutingConflictException>());
            Assert.That(((NodeManagerRoutingConflictException)conflict).RoutingRevision, Is.Null);
            GC.KeepAlive(conflict);
        }

        [Test]
        public async Task PreparedConflictReleasesBatch()
        {
            (InvalidOperationException conflict, WeakReference<IPreparedChanges> batch, _,
                WeakReference<IAsyncNodeManager> manager) = await CreateRetentionControlAsync(true)
                .ConfigureAwait(false);
            await Task.Yield();
            CollectTargets();
            Assert.That(IsAlive(batch), Is.False);
            Assert.That(IsAlive(manager), Is.False);
            Assert.That(conflict, Is.TypeOf<OptimisticPublicationConflictException>());
            Assert.That(conflict.Message, Does.Contain("Serving types changed"));
            GC.KeepAlive(conflict);
        }

        [Test]
        public void ConflictConstructorsRejectMissingIdentity()
        {
            Assert.That(() => new NodeManagerRoutingConflictException(
                "Routing view changed.", (NodeManagerRoutingTable.RoutingSnapshot)null!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("routingRevision"));
            Assert.That(typeof(NodeManagerRoutingConflictException)
                .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance),
                Has.All.Matches<ConstructorInfo>(constructor =>
                    constructor is not null &&
                    Array.TrueForAll(constructor.GetParameters(),
                        parameter => parameter.ParameterType != typeof(IPreparedChanges))));
            Assert.That(typeof(NodeManagerLifecycle).Assembly.GetType(
                "Opc.Ua.Server.NodeManagerPreparedConflictException"), Is.Null);
        }

        /// <summary>
        /// All former binding classifications now reject stale native work without an implicit retry.
        /// </summary>
        [TestCase("Routing", "Same")]
        [TestCase("Prepared", "Same")]
        [TestCase("Routing", "Different")]
        [TestCase("Prepared", "Different")]
        [TestCase("Routing", "Collected")]
        [TestCase("Prepared", "Collected")]
        [TestCase("Routing", "Unbound")]
        [TestCase("Prepared", "Unbound")]
        public async Task CompatibilityRetriesOnlyExactLiveOwnerAsync(string kind, string ownerState)
        {
            await using var harness = new NodeManagerLifecycleChangeSetCleanupTests.ChangeSetCleanupHarness();
            var factory = new EqualFactory("urn:test:native-conflict:" + ownerState);
            await using IPreparedChanges prepared = await harness.Lifecycle.PrepareAsync([LifecycleChange.Add(factory)])
                .ConfigureAwait(false);
            if (kind == "Prepared")
            {
                harness.Data.TypeTree.AddSubtype(
                    new NodeId("native-conflict:" + ownerState, 0), DataTypeIds.BaseDataType);
            }
            else
            {
                var competing = new EqualFactory("urn:test:native-conflict:competing:" + ownerState);
                LifecycleResult winner = await harness.Lifecycle.ApplyAsync([LifecycleChange.Add(competing)])
                    .ConfigureAwait(false);
                Assert.That(winner.Issues, Is.Empty);
                if (ownerState == "Collected")
                {
                    LifecycleResult removed = await harness.Lifecycle.ApplyAsync(
                        [LifecycleChange.Remove(winner.Registrations[0].Slot)]).ConfigureAwait(false);
                    await removed.Retired.ConfigureAwait(false);
                }
            }
            long revision = harness.Lifecycle.Revision;
            int decisions = 0;
            InvalidOperationException? first = await Assert.CatchAsync<InvalidOperationException>(
                async () => await prepared.CommitAsync(_ =>
                {
                    decisions++;
                    return default;
                }).ConfigureAwait(false)).ConfigureAwait(false);
            InvalidOperationException? second = await Assert.CatchAsync<InvalidOperationException>(
                async () => await prepared.CommitAsync(_ =>
                {
                    decisions++;
                    return default;
                }).ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(second, Is.SameAs(first));
            Assert.That(first, kind == "Prepared"
                ? Is.TypeOf<OptimisticPublicationConflictException>()
                : Is.TypeOf<NodeManagerRoutingConflictException>());
            Assert.That(decisions, Is.Zero);
            Assert.That(factory.CreateCount, Is.EqualTo(1));
            Assert.That(factory.DeleteCount, Is.EqualTo(1));
            Assert.That(factory.DisposeCount, Is.EqualTo(1));
            Assert.That(prepared.IsCommitted, Is.False);
            Assert.That(harness.Lifecycle.Revision, Is.EqualTo(revision));
        }

        [TestCase("Decision")]
        [TestCase("Committed")]
        public async Task CompatibilityDoesNotReplayPreparedConflictAfterDecisionOrCommitAsync(string stage)
        {
            await using var harness = new NodeManagerLifecycleChangeSetCleanupTests.ChangeSetCleanupHarness();
            var factory = new EqualFactory("urn:test:native-conflict:decision");
            await using IPreparedChanges prepared = await harness.Lifecycle.PrepareAsync([LifecycleChange.Add(factory)])
                .ConfigureAwait(false);
            var failure = new InvalidOperationException("The external decision failed.");
            int decisions = 0;
            if (stage == "Decision")
            {
                InvalidOperationException? first = await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await prepared.CommitAsync(_ =>
                    {
                        decisions++;
                        throw failure;
                    }).ConfigureAwait(false)).ConfigureAwait(false);
                InvalidOperationException? second = await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await prepared.CommitAsync(_ =>
                    {
                        decisions++;
                        return default;
                    }).ConfigureAwait(false)).ConfigureAwait(false);
                Assert.That(first, Is.SameAs(failure));
                Assert.That(second, Is.SameAs(failure));
                Assert.That(prepared.IsCommitted, Is.False);
                Assert.That(factory.DeleteCount, Is.EqualTo(1));
            }
            else
            {
                LifecycleResult first = await prepared.CommitAsync(_ =>
                {
                    decisions++;
                    return default;
                }).ConfigureAwait(false);
                long revision = harness.Lifecycle.Revision;
                LifecycleResult second = await prepared.CommitAsync(_ =>
                {
                    decisions++;
                    return default;
                }).ConfigureAwait(false);
                Assert.That(second, Is.SameAs(first));
                Assert.That(prepared.IsCommitted, Is.True);
                Assert.That(first.Issues, Is.Empty);
                Assert.That(harness.Lifecycle.Revision, Is.EqualTo(revision));
            }
            Assert.That(decisions, Is.EqualTo(1));
            Assert.That(factory.CreateCount, Is.EqualTo(1));
        }

        [TestCase("Routing")]
        [TestCase("Prepared")]
        public async Task CompatibilityDoesNotRetryValueEqualDistinctOwnerAsync(string kind)
        {
            await using var harness = new NodeManagerLifecycleChangeSetCleanupTests.ChangeSetCleanupHarness();
            var current = new EqualFactory("urn:test:native-conflict:equal:first");
            var other = new EqualFactory("urn:test:native-conflict:equal:second");
            Assert.That(current, Is.EqualTo(other));
            Assert.That(current, Is.Not.SameAs(other));
            await using IPreparedChanges prepared = await harness.Lifecycle.PrepareAsync([LifecycleChange.Add(current)])
                .ConfigureAwait(false);
            if (kind == "Prepared")
            {
                harness.Data.TypeTree.AddSubtype(new NodeId("equal-owner-conflict", 0), DataTypeIds.BaseDataType);
            }
            else
            {
                LifecycleResult winner = await harness.Lifecycle.ApplyAsync([LifecycleChange.Add(other)])
                    .ConfigureAwait(false);
                Assert.That(winner.Issues, Is.Empty);
            }
            int decisions = 0;
            InvalidOperationException? conflict = await Assert.CatchAsync<InvalidOperationException>(
                async () => await prepared.CommitAsync(_ =>
                {
                    decisions++;
                    return default;
                }).ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(conflict, kind == "Prepared"
                ? Is.TypeOf<OptimisticPublicationConflictException>()
                : Is.TypeOf<NodeManagerRoutingConflictException>());
            Assert.That(decisions, Is.Zero);
            Assert.That(current.CreateCount, Is.EqualTo(1));
            Assert.That(current.DeleteCount, Is.EqualTo(1));
            Assert.That(current.DisposeCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Returns only weak owner references and the original native failure after cleanup and recorder release.
        /// </summary>
        /// <exception cref="AssertionException">The native unit did not report its conflict.</exception>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static async Task<(InvalidOperationException Conflict, WeakReference<IPreparedChanges> Batch,
            WeakReference<NodeManagerRoutingTable.RoutingSnapshot> Snapshot,
            WeakReference<IAsyncNodeManager> Manager)> CreateRetentionControlAsync(bool metadata)
        {
            await using var harness = new NodeManagerLifecycleChangeSetCleanupTests.ChangeSetCleanupHarness();
            var factory = new EqualFactory("urn:test:native-conflict:retention");
            NodeManagerRoutingTable.RoutingSnapshot? routing =
                ((IDynamicNodeManagerBatchHost)harness.Master).RoutingRevision;
            var snapshot = new WeakReference<NodeManagerRoutingTable.RoutingSnapshot>(routing);
            IPreparedChanges? prepared = await harness.Lifecycle.PrepareAsync([LifecycleChange.Add(factory)])
                .ConfigureAwait(false);
            var owner = new WeakReference<IPreparedChanges>(prepared);
            WeakReference<IAsyncNodeManager> manager = factory.Candidate!;
            CollectTargets();
            Assert.That(IsAlive(owner), Is.True);
            Assert.That(IsAlive(manager), Is.True);
            Assert.That(IsAlive(snapshot), Is.True);
            if (metadata)
            {
                harness.Data.TypeTree.AddSubtype(new NodeId("retention-conflict", 0), DataTypeIds.BaseDataType);
            }
            else
            {
                LifecycleResult winner = await harness.Lifecycle.ApplyAsync(
                    [LifecycleChange.Add(new EqualFactory("urn:test:native-conflict:retention:winner"))])
                    .ConfigureAwait(false);
                Assert.That(winner.Issues, Is.Empty);
            }
            InvalidOperationException failure = await Assert.CatchAsync<InvalidOperationException>(
                async () => await prepared.CommitAsync().ConfigureAwait(false)).ConfigureAwait(false) ??
                throw new AssertionException("The native unit did not report its conflict.");
            Assert.That(failure, metadata
                ? Is.TypeOf<OptimisticPublicationConflictException>()
                : Is.TypeOf<NodeManagerRoutingConflictException>());
            await prepared.DisposeAsync().ConfigureAwait(false);
            Assert.That(factory.DeleteCount, Is.EqualTo(1));
            Assert.That(factory.DisposeCount, Is.EqualTo(1));
            harness.ReleaseHostRecorderReferences();
            prepared = null;
            routing = null;
            return (failure, owner, snapshot, manager);
        }

        /// <summary>
        /// Collects outside the no-inline producer and weak-reference inspection frames.
        /// </summary>
        private static void CollectTargets()
        {
            for (int index = 0; index < 3; index++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
        }

        /// <summary>
        /// Tests weak reachability without keeping a retrieved target in the caller's frame.
        /// </summary>
        /// <typeparam name="T">The weakly referenced target type.</typeparam>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool IsAlive<T>(WeakReference<T> reference)
            where T : class
        {
            return reference.TryGetTarget(out _);
        }

        /// <summary>
        /// Keeps cleanup counters and weak candidates while testing value-equal distinct factory identities.
        /// </summary>
        private sealed class EqualFactory(string uri) : IAsyncNodeManagerFactory
        {
            /// <inheritdoc/>
            public ArrayOf<string> NamespacesUris => [uri];

            /// <summary>
            /// Gets the number of candidate creations.
            /// </summary>
            public int CreateCount { get; private set; }

            /// <summary>
            /// Gets the number of address-space cleanup attempts.
            /// </summary>
            public int DeleteCount { get; private set; }

            /// <summary>
            /// Gets the number of physical disposal attempts.
            /// </summary>
            public int DisposeCount { get; private set; }

            /// <summary>
            /// Gets the non-owning candidate inspection reference.
            /// </summary>
            public WeakReference<IAsyncNodeManager>? Candidate { get; private set; }

            /// <inheritdoc/>
            public ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server, ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CreateCount++;
                var manager = new Mock<IAsyncNodeManager>();
                manager.SetupGet(value => value.NamespaceUris).Returns([uri]);
                manager.Setup(value => value.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()))
                    .Callback((CancellationToken _) => DeleteCount++)
                    .Returns(default(ValueTask));
                manager.As<IDisposable>().Setup(value => value.Dispose()).Callback(() => DisposeCount++);
                Candidate = new WeakReference<IAsyncNodeManager>(manager.Object);
                return new ValueTask<IAsyncNodeManager>(manager.Object);
            }

            /// <inheritdoc/>
            public override bool Equals(object? obj)
            {
                return obj is EqualFactory;
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                return typeof(EqualFactory).GetHashCode();
            }
        }
    }
}
