// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server.RuntimeNodeSet;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Prepared changes on disjoint NodeManagers commit after each other; overlapping changes are rejected with a
    /// retryable <see cref="NodeManagerLifecycleConflictException"/> before the decision.
    /// </summary>
    public sealed partial class NodeManagerLifecycleTests
    {
        /// <summary>
        /// A reload prepared before a reload of another manager publishes is rebased and commits.
        /// </summary>
        [Test]
        public async Task PreparedReloadCommitsAfterDisjointReloadPublishesAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            NodeManagerSlot first = await AddRuntimeManagerAsync(
                lifecycle, kModelNamespaceUri, kFirstRegistrationValue, timeout.Token).ConfigureAwait(false);
            NodeManagerSlot second = await AddRuntimeManagerAsync(
                lifecycle, kSecondModelNamespaceUri, kSecondRegistrationValue, timeout.Token).ConfigureAwait(false);

            await using IPreparedChanges prepared = await lifecycle.PrepareAsync(
                [LifecycleChange.Reload(first, RuntimeFactory(kModelNamespaceUri, 303))], timeout.Token)
                .ConfigureAwait(false);
            LifecycleResult intervening = await lifecycle.ApplyAsync(
                [LifecycleChange.Reload(second, RuntimeFactory(kSecondModelNamespaceUri, 404))], timeout.Token)
                .ConfigureAwait(false);
            int decisions = 0;
            LifecycleResult committed = await prepared.CommitAsync(_ =>
            {
                decisions++;
                return default;
            }, timeout.Token).ConfigureAwait(false);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(decisions, Is.EqualTo(1));
                Assert.That(prepared.IsCommitted, Is.True);
                Assert.That(committed.Revision, Is.GreaterThan(intervening.Revision));
                Assert.That(first.TryGetCurrent(out NodeManagerSlotSnapshot current), Is.True);
                Assert.That(current.Generation, Is.EqualTo(2));
                Assert.That(second.TryGetCurrent(out NodeManagerSlotSnapshot other), Is.True);
                Assert.That(other.Generation, Is.EqualTo(2));
            }
            await AssertReadinessValueAsync(kModelNamespaceUri, 303).ConfigureAwait(false);
            await AssertReadinessValueAsync(kSecondModelNamespaceUri, 404).ConfigureAwait(false);
        }

        /// <summary>
        /// An addition of a new namespace prepared before an unrelated reload publishes is rebased and commits.
        /// </summary>
        [Test]
        public async Task PreparedAdditionCommitsAfterDisjointReloadPublishesAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            NodeManagerSlot first = await AddRuntimeManagerAsync(
                lifecycle, kModelNamespaceUri, kFirstRegistrationValue, timeout.Token).ConfigureAwait(false);

            await using IPreparedChanges prepared = await lifecycle.PrepareAsync(
                [LifecycleChange.Add(RuntimeFactory(kSecondModelNamespaceUri, kSecondRegistrationValue))],
                timeout.Token).ConfigureAwait(false);
            await lifecycle.ApplyAsync(
                [LifecycleChange.Reload(first, RuntimeFactory(kModelNamespaceUri, 303))], timeout.Token)
                .ConfigureAwait(false);
            LifecycleResult committed = await prepared.CommitAsync(cancellationToken: timeout.Token)
                .ConfigureAwait(false);

            Assert.That(committed.Registrations.Count, Is.EqualTo(1));
            await AssertReadinessValueAsync(kModelNamespaceUri, 303).ConfigureAwait(false);
            await AssertReadinessValueAsync(kSecondModelNamespaceUri, kSecondRegistrationValue).ConfigureAwait(false);
        }

        /// <summary>
        /// Two reloads prepared for the same slot overlap: the second commit is a retryable conflict that publishes
        /// nothing, and preparing it again succeeds.
        /// </summary>
        [Test]
        public async Task OverlappingReloadOfSameSlotIsRetryableConflictAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            NodeManagerSlot slot = await AddRuntimeManagerAsync(
                lifecycle, kModelNamespaceUri, kFirstRegistrationValue, timeout.Token).ConfigureAwait(false);

            await using IPreparedChanges stale = await lifecycle.PrepareAsync(
                [LifecycleChange.Reload(slot, RuntimeFactory(kModelNamespaceUri, 303))], timeout.Token)
                .ConfigureAwait(false);
            await lifecycle.ApplyAsync(
                [LifecycleChange.Reload(slot, RuntimeFactory(kModelNamespaceUri, 505))], timeout.Token)
                .ConfigureAwait(false);
            int decisions = 0;
            await Assert.ThatAsync(async () => await stale.CommitAsync(_ =>
            {
                decisions++;
                return default;
            }, timeout.Token).ConfigureAwait(false), Throws.InstanceOf<NodeManagerLifecycleConflictException>())
                .ConfigureAwait(false);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(decisions, Is.Zero);
                Assert.That(stale.IsCommitted, Is.False);
            }
            await AssertReadinessValueAsync(kModelNamespaceUri, 505).ConfigureAwait(false);

            await lifecycle.ApplyAsync(
                [LifecycleChange.Reload(slot, RuntimeFactory(kModelNamespaceUri, 303))], timeout.Token)
                .ConfigureAwait(false);
            await AssertReadinessValueAsync(kModelNamespaceUri, 303).ConfigureAwait(false);
        }

        /// <summary>
        /// Two additions that append different namespaces at the same index overlap: the second commit is a
        /// retryable conflict that publishes nothing, and preparing it again succeeds.
        /// </summary>
        [Test]
        public async Task OverlappingNamespaceAdditionIsRetryableConflictAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            int namespaceCount = m_server.CurrentInstance.NamespaceUris.Count;

            await using IPreparedChanges stale = await lifecycle.PrepareAsync(
                [LifecycleChange.Add(RuntimeFactory(kModelNamespaceUri, kFirstRegistrationValue))], timeout.Token)
                .ConfigureAwait(false);
            await AddRuntimeManagerAsync(
                lifecycle, kSecondModelNamespaceUri, kSecondRegistrationValue, timeout.Token).ConfigureAwait(false);
            await Assert.ThatAsync(
                async () => await stale.CommitAsync(cancellationToken: timeout.Token).ConfigureAwait(false),
                Throws.InstanceOf<NodeManagerLifecycleConflictException>()).ConfigureAwait(false);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(stale.IsCommitted, Is.False);
                Assert.That(m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri), Is.LessThan(0));
                Assert.That(
                    m_server.CurrentInstance.NamespaceUris.GetIndex(kSecondModelNamespaceUri),
                    Is.EqualTo(namespaceCount));
            }

            await AddRuntimeManagerAsync(
                lifecycle, kModelNamespaceUri, kFirstRegistrationValue, timeout.Token).ConfigureAwait(false);
            await AssertReadinessValueAsync(kModelNamespaceUri, kFirstRegistrationValue).ConfigureAwait(false);
            await AssertReadinessValueAsync(kSecondModelNamespaceUri, kSecondRegistrationValue).ConfigureAwait(false);
        }

        /// <summary>
        /// Concurrent reloads of disjoint managers all commit.
        /// </summary>
        [Test]
        public async Task ConcurrentReloadsOfDisjointManagersAllCommitAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            NodeManagerSlot first = await AddRuntimeManagerAsync(
                lifecycle, kModelNamespaceUri, kFirstRegistrationValue, timeout.Token).ConfigureAwait(false);
            NodeManagerSlot second = await AddRuntimeManagerAsync(
                lifecycle, kSecondModelNamespaceUri, kSecondRegistrationValue, timeout.Token).ConfigureAwait(false);
            const int rounds = 8;

            await Task.WhenAll(
                ReloadAsync(first, kModelNamespaceUri, 1000),
                ReloadAsync(second, kSecondModelNamespaceUri, 2000)).ConfigureAwait(false);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(first.TryGetCurrent(out NodeManagerSlotSnapshot current), Is.True);
                Assert.That(current.Generation, Is.EqualTo(rounds + 1));
                Assert.That(second.TryGetCurrent(out NodeManagerSlotSnapshot other), Is.True);
                Assert.That(other.Generation, Is.EqualTo(rounds + 1));
            }
            await AssertReadinessValueAsync(kModelNamespaceUri, 1000 + rounds - 1).ConfigureAwait(false);
            await AssertReadinessValueAsync(kSecondModelNamespaceUri, 2000 + rounds - 1).ConfigureAwait(false);

            async Task ReloadAsync(NodeManagerSlot slot, string namespaceUri, int baseValue)
            {
                for (int round = 0; round < rounds; round++)
                {
                    await using IPreparedChanges prepared = await lifecycle.PrepareAsync(
                        [LifecycleChange.Reload(slot, RuntimeFactory(namespaceUri, baseValue + round))],
                        timeout.Token).ConfigureAwait(false);
                    await Task.Yield();
                    await prepared.CommitAsync(cancellationToken: timeout.Token).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// A conflict raised by the durable decision callback itself is not reported as the host's retryable
        /// conflict: the callback already ran, so it reaches the caller unchanged.
        /// </summary>
        [Test]
        public async Task DecisionCallbackConflictIsNotReportedAsRetryableAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            await using IPreparedChanges prepared = await lifecycle.PrepareAsync(
                [LifecycleChange.Add(RuntimeFactory(kModelNamespaceUri, kFirstRegistrationValue))], timeout.Token)
                .ConfigureAwait(false);
            var failure = new OptimisticPublicationConflictException("Raised by the decision callback.");

            await Assert.ThatAsync(
                async () => await prepared.CommitAsync(_ => throw failure, timeout.Token).ConfigureAwait(false),
                Throws.Exception.SameAs(failure)).ConfigureAwait(false);
            Assert.That(prepared.IsCommitted, Is.False);
        }

        private static RuntimeNodeSetNodeManagerFactory RuntimeFactory(string namespaceUri, int value)
        {
            return new RuntimeNodeSetNodeManagerFactory(CreateOptions(namespaceUri, value));
        }

        private static async Task<NodeManagerSlot> AddRuntimeManagerAsync(
            INodeManagerLifecycle lifecycle,
            string namespaceUri,
            int value,
            CancellationToken cancellationToken)
        {
            LifecycleResult added = await lifecycle.ApplyAsync(
                [LifecycleChange.Add(RuntimeFactory(namespaceUri, value))], cancellationToken).ConfigureAwait(false);
            return added.Registrations[0].Slot;
        }
    }
}
