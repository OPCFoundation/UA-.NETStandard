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
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.RuntimeNodeSet;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class NodeManagerLifecycleTests
    {
        /// <summary>
        /// Private preparation owns a refusing generation until its sole publication or abort transition.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task CandidateGenerationRemainsPreparedUntilPublicationOrAbortAsync(bool publish)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            TrackingLifecycleNodeManager? manager = null;
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(CreateTrackingNodeManagementFactory(
                    kGeneration1Value, created => manager = created))], timeout.Token).ConfigureAwait(false);
            TrackingLifecycleNodeManager candidate = RequireLifecycleValue(manager);
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(candidate);
            Assert.That(generation.Gate.State, Is.EqualTo(NodeManagerGenerationState.Prepared));
            bool admitted = generation.Gate.TryEnter(out int stripe);
            if (admitted)
            {
                generation.Gate.Exit(stripe);
            }
            Assert.That(admitted, Is.False);
            Assert.That(generation.Slot.TryGetCurrent(out _), Is.False);
            if (publish)
            {
                NodeManagerBatchResult result = await prepared.CommitAsync(_ => default, timeout.Token)
                    .ConfigureAwait(false);
                Assert.That(result.CleanupFailure, Is.Null);
                Assert.That(result.Registrations[0].Identity, Is.SameAs(generation));
                Assert.That(generation.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
                admitted = generation.Gate.TryEnter(out stripe);
                if (admitted)
                {
                    generation.Gate.Exit(stripe);
                }
                Assert.That(admitted, Is.True);
            }
            else
            {
                await prepared.DisposeAsync().ConfigureAwait(false);
                Assert.That(generation.Gate.State, Is.EqualTo(NodeManagerGenerationState.Aborted));
                Assert.That(generation.Slot.TryGetCurrent(out _), Is.False);
                Assert.That(candidate.DeleteAddressSpaceCount, Is.EqualTo(1));
                Assert.That(candidate.DisposeCount, Is.EqualTo(1));
            }
        }

        /// <summary>
        /// Reference discovery permits independent publication and releases its owner on every failure path.
        /// </summary>
        [TestCase("Stale")]
        [TestCase("Retire")]
        [TestCase("Cancel")]
        [TestCase("Failure")]
        public async Task ReferenceDiscoveryDoesNotHoldWriterOrLeakOwnerAdmissionAsync(string outcome)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Mock<IAsyncNodeManager> owner = CreateLifecycleNodeManager(kModelNamespaceUri);
            var ownerFactory = new Mock<IAsyncNodeManagerFactory>();
            ownerFactory.Setup(value => value.CreateAsync(
                It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(owner.Object);
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            await using IPreparedNodeManagerBatch initial = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(ownerFactory.Object)], timeout.Token).ConfigureAwait(false);
            NodeManagerBatchResult original = await initial.CommitAsync(_ => default, timeout.Token)
                .ConfigureAwait(false);
            var source = new NodeId(9911, (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri));
            owner.Setup(value => value.GetManagerHandleAsync(source, It.IsAny<CancellationToken>()))
                .Returns((NodeId _, CancellationToken token) => ResolveAsync(token));
            Mock<IAsyncNodeManager> candidate = CreateLifecycleNodeManager(kSecondModelNamespaceUri);
            candidate.Setup(value => value.CreateAddressSpaceAsync(
                It.IsAny<IDictionary<NodeId, IList<IReference>>>(), It.IsAny<CancellationToken>()))
                .Callback((IDictionary<NodeId, IList<IReference>> references, CancellationToken _) =>
                    references.Add(source,
                        [new NodeStateReference(ReferenceTypeIds.HasComponent, false, new NodeId(9912))]))
                .Returns(default(ValueTask));
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(
                It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(candidate.Object);
            await using IPreparedNodeManagerBatch publication = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(factory.Object)], timeout.Token).ConfigureAwait(false);
            int decisions = 0;
            Task<NodeManagerBatchResult> pending = publication.CommitAsync(_ =>
            {
                decisions++;
                return default;
            }, cancellation.Token).AsTask();
            NodeManagerBatchResult? independentResult = null;
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                NodeManagerBatchChange change = outcome == "Stale"
                    ? NodeManagerBatchChange.Add(new RuntimeNodeSetNodeManagerFactory(
                        CreateOptions(kModelNamespaceUri + ":Independent", kSecondRegistrationValue)))
                    : NodeManagerBatchChange.Remove(original.Registrations[0]);
                await using IPreparedNodeManagerBatch independent =
                    await lifecycle.PrepareAsync([change], timeout.Token).ConfigureAwait(false);
                independentResult = await independent.CommitAsync(_ => default, timeout.Token).ConfigureAwait(false);
                Assert.That(release.Task.IsCompleted, Is.False);
                if (outcome != "Stale")
                {
                    Assert.That(independentResult.Retirement.IsCompleted, Is.False,
                        "Reference discovery must keep its admitted owner alive.");
                    owner.Verify(value => value.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()), Times.Never);
                }
            }
            finally
            {
                if (outcome == "Cancel")
                {
                    cancellation.Cancel();
                }
                release.TrySetResult(true);
                await Assert.ThatAsync(() => pending.WaitAsync(timeout.Token),
                    outcome == "Cancel" ? Throws.InstanceOf<OperationCanceledException>() :
                    outcome == "Failure" ? Throws.TypeOf<SentinelException>() :
                    Throws.InstanceOf<InvalidOperationException>()).ConfigureAwait(false);
            }
            NodeManagerBatchResult committed = RequireLifecycleValue(independentResult);
            await committed.Retirement.WaitAsync(timeout.Token).ConfigureAwait(false);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(decisions, Is.Zero);
                Assert.That(publication.IsCommitted, Is.False);
                Assert.That(lifecycle.Revision, Is.EqualTo(committed.Revision));
                Assert.That(m_server.CurrentInstance.NamespaceUris.GetIndex(kSecondModelNamespaceUri), Is.EqualTo(-1));
            }
            owner.Verify(value => value.GetManagerHandleAsync(source, It.IsAny<CancellationToken>()), Times.Once);

            async ValueTask<object> ResolveAsync(CancellationToken token)
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(token).ConfigureAwait(false);
                if (outcome == "Failure")
                {
                    throw new SentinelException();
                }
                return null!;
            }
        }

        /// <summary>
        /// Transition discovery runs without the writer and revalidates its view before a durable decision.
        /// </summary>
        [Test]
        public async Task MonitoredItemPreparationAllowsIndependentPublicationAndRejectsStaleCommitAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Mock<IAsyncNodeManager> original = CreateLifecycleNodeManager(kModelNamespaceUri);
            Mock<INodeManagerMonitoredItemLifecycle> items = original.As<INodeManagerMonitoredItemLifecycle>();
            original.As<INodeManagerReloadParticipant>()
                .Setup(value => value.PrepareReloadAsync(
                    It.IsAny<IAsyncNodeManager>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<LocalReference>>([]));
            Mock<IAsyncNodeManager> replacement = CreateLifecycleNodeManager(kModelNamespaceUri);
            Mock<INodeManagerMonitoredItemLifecycle> incoming = replacement.As<INodeManagerMonitoredItemLifecycle>();
            incoming.Setup(value => value.CanAttachMonitoredItemAsync(
                It.IsAny<IMonitoredItem>(), It.IsAny<CancellationToken>()))
                .Returns((IMonitoredItem _, CancellationToken token) => CheckCompatibilityAsync(token));
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(
                It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(original.Object);
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            await using IPreparedNodeManagerBatch addition = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(factory.Object)], timeout.Token).ConfigureAwait(false);
            NodeManagerBatchResult initial = await addition.CommitAsync(_ => default, timeout.Token)
                .ConfigureAwait(false);
            NodeManagerGeneration generation = initial.Registrations[0].Identity;
            var monitoredItem = new Mock<IMonitoredItem>();
            monitoredItem.SetupGet(value => value.NodeManager).Returns(original.Object);
            monitoredItem.SetupGet(value => value.MonitoredItemType).Returns(MonitoredItemTypeMask.DataChange);
            factory.Setup(value => value.CreateAsync(
                It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(replacement.Object);
            await using IPreparedNodeManagerBatch reload = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Replace(initial.Registrations[0], factory.Object)], timeout.Token)
                .ConfigureAwait(false);
            generation.MonitoredItems[monitoredItem.Object] = new Mock<ISubscription>().Object;
            int decisions = 0;
            Task<NodeManagerBatchResult> pending = reload.CommitAsync(_ =>
            {
                decisions++;
                return default;
            }, timeout.Token).AsTask();
            NodeManagerBatchResult? independentResult = null;
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                await using IPreparedNodeManagerBatch independent = await lifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Add(new RuntimeNodeSetNodeManagerFactory(
                        CreateOptions(kSecondModelNamespaceUri, kSecondRegistrationValue)))], timeout.Token)
                    .ConfigureAwait(false);
                independentResult = await independent.CommitAsync(_ => default, timeout.Token).ConfigureAwait(false);
                Assert.That(release.Task.IsCompleted, Is.False);
            }
            finally
            {
                generation.MonitoredItems.TryRemove(monitoredItem.Object, out _);
                release.TrySetResult(true);
                await Assert.ThatAsync(() => pending.WaitAsync(timeout.Token),
                    Throws.InstanceOf<InvalidOperationException>()).ConfigureAwait(false);
            }
            using (Assert.EnterMultipleScope())
            {
                Assert.That(decisions, Is.Zero);
                Assert.That(reload.IsCommitted, Is.False);
                Assert.That(lifecycle.Revision, Is.EqualTo(RequireLifecycleValue(independentResult).Revision));
                Assert.That(lifecycle.Registrations.ToList(), Does.Contain(initial.Registrations[0]));
            }
            items.Verify(value => value.GetMonitoredItemsSnapshotAsync(
                It.IsAny<IReadOnlyCollection<NodeId>?>(), It.IsAny<CancellationToken>()), Times.Never);
            incoming.Verify(value => value.CanAttachMonitoredItemAsync(
                monitoredItem.Object, It.IsAny<CancellationToken>()), Times.Once);

            async ValueTask<ServiceResult> CheckCompatibilityAsync(CancellationToken token)
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(token).ConfigureAwait(false);
                return ServiceResult.Good;
            }
        }

        /// <summary>
        /// A factory can await a nested publication; the resulting outer conflict never reruns application code.
        /// </summary>
        [Test]
        public async Task CandidateFactoryCanAwaitNestedPublicationBeforeStaleCommitIsRejectedAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            var originalFactory = new RuntimeNodeSetNodeManagerFactory(
                CreateOptions(kModelNamespaceUri, kFirstRegistrationValue));
            NodeManagerBatchResult? nestedResult = null;
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(
                It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .Returns((IServerInternal server, ApplicationConfiguration configuration, CancellationToken token) =>
                    CreateWithNestedPublicationAsync(server, configuration, token));
            await using IPreparedNodeManagerBatch outer = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(factory.Object)], timeout.Token).ConfigureAwait(false);
            NodeManagerBatchResult committed = RequireLifecycleValue(nestedResult);
            int decisions = 0;
            await Assert.ThatAsync(() => outer.CommitAsync(_ =>
            {
                decisions++;
                return default;
            }, timeout.Token).AsTask(), Throws.InstanceOf<InvalidOperationException>()).ConfigureAwait(false);
            await outer.DisposeAsync().ConfigureAwait(false);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(decisions, Is.Zero);
                Assert.That(outer.IsCommitted, Is.False);
                Assert.That(lifecycle.Revision, Is.EqualTo(committed.Revision));
                Assert.That(lifecycle.Registrations.ToList(), Does.Contain(committed.Registrations[0]));
                Assert.That(m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri), Is.EqualTo(-1));
            }
            factory.Verify(value => value.CreateAsync(
                It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()),
                Times.Once);
            await AssertReadinessValueAsync(kSecondModelNamespaceUri, kSecondRegistrationValue).ConfigureAwait(false);

            async ValueTask<IAsyncNodeManager> CreateWithNestedPublicationAsync(
                IServerInternal server, ApplicationConfiguration configuration, CancellationToken token)
            {
                await using IPreparedNodeManagerBatch nested = await lifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Add(new RuntimeNodeSetNodeManagerFactory(
                        CreateOptions(kSecondModelNamespaceUri, kSecondRegistrationValue)))], token)
                    .ConfigureAwait(false);
                nestedResult = await nested.CommitAsync(_ => default, token).ConfigureAwait(false);
                Assert.That(nestedResult.CleanupFailure, Is.Null);
                return await originalFactory.CreateAsync(server, configuration, token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Failed or cancelled private preparation releases its current-generation admission for physical retirement.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task FailedPrivatePreparationReleasesRetiringCurrentGenerationAsync(bool cancel)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var preparationCancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            NodeManagerRegistration current = await lifecycle.AddRuntimeNodeSetAsync(
                CreateGenerationOptions(generation: 1), null, timeout.Token).ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(
                It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .Returns((IServerInternal _, ApplicationConfiguration _, CancellationToken token) =>
                    FailPreparationAsync(token));
            Task<IPreparedNodeManagerBatch> pending = lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Replace(current, factory.Object)], preparationCancellation.Token).AsTask();
            NodeManagerBatchResult? removed = null;
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                await using IPreparedNodeManagerBatch removal = await lifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Remove(current)], timeout.Token).ConfigureAwait(false);
                removed = await removal.CommitAsync(_ => default, timeout.Token).ConfigureAwait(false);
                Assert.That(removed.CleanupFailure, Is.Null);
                Assert.That(removed.Retirement.IsCompleted, Is.False,
                    "The current participant must remain alive while its admitted preparation callback executes.");
            }
            finally
            {
                if (cancel)
                {
                    preparationCancellation.Cancel();
                }
                else
                {
                    release.TrySetResult(true);
                }
                await Assert.ThatAsync(() => pending.WaitAsync(timeout.Token),
                    cancel ? Throws.InstanceOf<OperationCanceledException>() : Throws.TypeOf<SentinelException>())
                    .ConfigureAwait(false);
            }
            await RequireLifecycleValue(removed).Retirement.WaitAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(lifecycle.Registrations.ToList(), Does.Not.Contain(current));

            async ValueTask<IAsyncNodeManager> FailPreparationAsync(CancellationToken token)
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(token).ConfigureAwait(false);
                throw new SentinelException();
            }
        }

        /// <summary>
        /// Candidate callbacks do not serialize unrelated publication, and a stale candidate is never rebased.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task PrivatePreparationAllowsIndependentPublicationAndRejectsStaleCommitAsync(
            bool blockAddressSpace)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var originalFactory = new RuntimeNodeSetNodeManagerFactory(
                CreateOptions(kModelNamespaceUri, kFirstRegistrationValue));
            Mock<IAsyncNodeManager> candidate = CreateLifecycleNodeManager(kModelNamespaceUri);
            candidate.Setup(value => value.CreateAddressSpaceAsync(
                It.IsAny<IDictionary<NodeId, IList<IReference>>>(), It.IsAny<CancellationToken>()))
                .Returns((IDictionary<NodeId, IList<IReference>> _, CancellationToken token) =>
                    WaitForReleaseAsync(token));
            var delayedFactory = new Mock<IAsyncNodeManagerFactory>();
            int factoryCalls = 0;
            delayedFactory.Setup(value => value.CreateAsync(
                It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .Returns((IServerInternal server, ApplicationConfiguration configuration, CancellationToken token) =>
                    CreateDelayedAsync(server, configuration, token));
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            Task<IPreparedNodeManagerBatch> pending = lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(delayedFactory.Object)], timeout.Token).AsTask();
            IPreparedNodeManagerBatch? first = null;
            NodeManagerBatchResult? published = null;
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                using var independentTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await using IPreparedNodeManagerBatch independent = await lifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Add(new RuntimeNodeSetNodeManagerFactory(
                        CreateOptions(kSecondModelNamespaceUri, kSecondRegistrationValue)))],
                    independentTimeout.Token).ConfigureAwait(false);
                published = await independent.CommitAsync(_ => default, independentTimeout.Token).ConfigureAwait(false);
                Assert.That(release.Task.IsCompleted, Is.False,
                    "The second publication must finish while the first factory remains blocked.");
                Assert.That(published.CleanupFailure, Is.Null);
            }
            finally
            {
                release.TrySetResult(true);
                first = await pending.WaitAsync(timeout.Token).ConfigureAwait(false);
                if (published is null)
                {
                    await first.DisposeAsync().ConfigureAwait(false);
                }
            }

            IPreparedNodeManagerBatch stale = RequireLifecycleValue(first);
            NodeManagerBatchResult committed = RequireLifecycleValue(published);
            await using (stale.ConfigureAwait(false))
            {
                int decisions = 0;
                await Assert.ThatAsync(() => stale.CommitAsync(_ =>
                {
                    decisions++;
                    return default;
                }, timeout.Token).AsTask(), Throws.InstanceOf<InvalidOperationException>()).ConfigureAwait(false);
                Assert.That(stale.IsCommitted, Is.False);
                Assert.That(decisions, Is.Zero, "A conflicting preparation must fail before any durable decision.");
            }
            using (Assert.EnterMultipleScope())
            {
                Assert.That(factoryCalls, Is.EqualTo(1), "Conflict detection must not silently rerun a factory.");
                Assert.That(lifecycle.Revision, Is.EqualTo(committed.Revision));
                Assert.That(lifecycle.Registrations.ToList(), Does.Contain(committed.Registrations[0]));
                Assert.That(m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri), Is.EqualTo(-1));
            }
            await AssertReadinessValueAsync(kSecondModelNamespaceUri, kSecondRegistrationValue).ConfigureAwait(false);

            async ValueTask<IAsyncNodeManager> CreateDelayedAsync(
                IServerInternal server, ApplicationConfiguration configuration, CancellationToken token)
            {
                factoryCalls++;
                if (blockAddressSpace)
                {
                    return candidate.Object;
                }
                await WaitForReleaseAsync(token).ConfigureAwait(false);
                return await originalFactory.CreateAsync(server, configuration, token).ConfigureAwait(false);
            }

            async ValueTask WaitForReleaseAsync(CancellationToken token)
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(token).ConfigureAwait(false);
            }
        }
    }
}
