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
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Historian;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class NodeManagerLifecycleTests
    {
        private static readonly TimeSpan s_readinessTimeout = TimeSpan.FromSeconds(5);

        private const string kReadinessProbeNamespaceUri = kModelNamespaceUri + ":ReadinessProbe";

        /// <summary>
        /// Reconciliation permits a nested writer while its result retains the revision and slots it published.
        /// </summary>
        [Test]
        public async Task BindingReconciliationAwaitsNestedAddWithoutChangingPublishedResultAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            await lifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(kGeneration1Value, _ => { }), null, timeout.Token)
                .ConfigureAwait(false);
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            (uint subscriptionId, uint itemId) = await CreateSubscriptionAndEventMonitoredItemAsync(
                services, ObjectIds.Server).ConfigureAwait(false);
            TrackingLifecycleNodeManager? candidate = null;
            TrackingLifecycleNodeManager? child = null;
            NodeManagerRegistration? childRegistration = null;
            long revisionBeforeChild = 0;
            var childFactory = new CountingNodeManagerFactory(
                [kReadinessProbeNamespaceUri],
                CreateTrackingNodeManagementFactory(
                    304, value => child = value, kReadinessProbeNamespaceUri).CreateAsync);
            try
            {
                await using IPreparedNodeManagerBatch prepared =
                    await ((INodeManagerBatchLifecycle)lifecycle).PrepareAsync(
                        [NodeManagerBatchChange.Add(CreateTrackingNodeManagementFactory(
                            kSecondRegistrationValue,
                            value =>
                            {
                                candidate = value;
                                value.AllEventsCallback = async (item, unsubscribe, token) =>
                                {
                                    if (!unsubscribe && item.Id == itemId)
                                    {
                                        revisionBeforeChild = lifecycle.Revision;
                                        childRegistration = await lifecycle.AddAsync(
                                            childFactory, null, token).ConfigureAwait(false);
                                    }
                                };
                            },
                            kSecondModelNamespaceUri))],
                        null, timeout.Token).ConfigureAwait(false);

                NodeManagerBatchResult result = await prepared.CommitAsync(static _ => default, timeout.Token)
                    .AsTask().WaitAsync(timeout.Token).ConfigureAwait(false);

                AssertLifecycleValue(candidate);
                AssertLifecycleValue(child);
                AssertLifecycleValue(childRegistration);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(result.CleanupFailure, Is.Null);
                    Assert.That(result.Revision, Is.EqualTo(revisionBeforeChild));
                    Assert.That(lifecycle.Revision, Is.GreaterThan(result.Revision));
                    Assert.That(result.Slots.ToList(), Does.Contain(result.Registrations[0].Slot));
                    Assert.That(result.Slots.ToList(), Does.Not.Contain(childRegistration.Slot));
                    Assert.That(candidate.SubscribedAllEventIds.ToList(), Is.EqualTo(new[] { itemId }));
                    Assert.That(child.SubscribedAllEventIds.ToList(), Is.EqualTo(new[] { itemId }));
                    Assert.That(childFactory.CreateCount, Is.EqualTo(1));
                }
            }
            finally
            {
                if (candidate is not null)
                {
                    candidate.AllEventsCallback = (_, _, _) => default;
                }
                await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Readiness runs on the published manager, can register a real child, and remains
        /// part of the Add operation even when a synchronous factory exposes both adapters.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("NodeManagerReadiness")]
        public async Task AddAsyncReadinessPublishesAndAwaitsNestedRegistrationAsync(bool synchronousFactory)
        {
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            using var timeout = new CancellationTokenSource(s_readinessTimeout);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ReadinessLifecycleNodeManager? participant = null;
            TrackingLifecycleNodeManager? childManager = null;
            NodeManagerRegistration? observed = null;
            NodeManagerRegistration? child = null;

            Task<NodeManagerRegistration> addTask = StartReadinessChangeAsync(
                ReadinessChange.Add,
                null,
                synchronousFactory,
                kGeneration1Value,
                manager =>
                {
                    participant = manager;
                    manager.ReadinessCallback = async ct =>
                    {
                        Assert.That(ct.CanBeCanceled, Is.False);
                        observed = FindReadinessRegistration();
                        Assert.That(observed.Generation, Is.EqualTo(1));
                        AssertReadinessManager(observed, manager, synchronousFactory);
                        await AssertReadinessValueAsync(kModelNamespaceUri, kGeneration1Value)
                            .ConfigureAwait(false);

                        child = await lifecycle.AddAsync(
                            CreateTrackingNodeManagementFactory(
                                kSecondRegistrationValue,
                                created => childManager = created,
                                kSecondModelNamespaceUri,
                                ReadinessNodeId(kModelNamespaceUri, kRootNodeId)),
                            null,
                            ct).AsTask().WaitAsync(s_readinessTimeout, ct).ConfigureAwait(false);
                        Assert.That(child.Id, Is.Not.EqualTo(observed.Id));
                        Assert.That(child.Generation, Is.EqualTo(1));
                        await AssertReadinessValueAsync(kSecondModelNamespaceUri, kSecondRegistrationValue)
                            .ConfigureAwait(false);
                        entered.TrySetResult(true);
                        await release.Task.WaitAsync(ct).ConfigureAwait(false);
                    };
                },
                timeout.Token);

            NodeManagerRegistration? added = null;
            try
            {
                await entered.Task.WaitAsync(s_readinessTimeout).ConfigureAwait(false);
                AssertLifecycleValue(participant);
                Assert.That(addTask.IsCompleted, Is.False, "Add must await the participant's unfinished work.");
                Assert.That(GetBranchRegistrations(lifecycle), Has.Count.EqualTo(2));
                Assert.That(participant.ReadinessCount, Is.EqualTo(1));
                Assert.That(participant.ReadinessCompletedCount, Is.Zero);
                Assert.That(participant.DeleteAddressSpaceCount, Is.Zero);
            }
            finally
            {
                release.TrySetResult(true);
                added = await addTask.WaitAsync(s_readinessTimeout).ConfigureAwait(false);
            }

            AssertLifecycleValue(added);
            AssertLifecycleValue(participant);
            AssertLifecycleValue(child);
            AssertLifecycleValue(childManager);
            Assert.That(added, Is.SameAs(observed));
            Assert.That(FindReadinessRegistration(), Is.SameAs(added));
            Assert.That(participant.ReadinessCount, Is.EqualTo(1));
            Assert.That(participant.ReadinessCompletedCount, Is.EqualTo(1));
            await AssertReadinessValueAsync(kModelNamespaceUri, kGeneration1Value).ConfigureAwait(false);
            await RemoveReadinessRegistrationAsync(child).ConfigureAwait(false);
            await RemoveReadinessRegistrationAsync(added, participant, synchronousFactory).ConfigureAwait(false);
            Assert.That(childManager.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(childManager.DisposeCount, Is.EqualTo(1));
            Assert.That(participant.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(GetBranchRegistrations(lifecycle), Is.Empty);
            await AssertReadinessNodeUnknownAsync(kModelNamespaceUri).ConfigureAwait(false);
            await AssertReadinessNodeUnknownAsync(kSecondModelNamespaceUri).ConfigureAwait(false);
        }

        /// <summary>
        /// All replacement-factory overloads run readiness on committed gen2, not on a
        /// prepared candidate. The old manager is deliberately an async reload participant.
        /// </summary>
        [TestCase(ReadinessChange.Reload, false)]
        [TestCase(ReadinessChange.Reload, true)]
        [TestCase(ReadinessChange.ShadowReload, false)]
        [TestCase(ReadinessChange.ShadowReload, true)]
        [TestCase(ReadinessChange.ImmediateReload, false)]
        [TestCase(ReadinessChange.ImmediateReload, true)]
        [Category("NodeManagerReadiness")]
        public async Task ReloadReadinessObservesCommittedGenerationAndAwaitsNestedRegistrationAsync(
            ReadinessChange change,
            bool synchronousFactory)
        {
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            using var timeout = new CancellationTokenSource(s_readinessTimeout);
            TrackingLifecycleNodeManager? originalManager = null;
            NodeManagerRegistration original = await lifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(
                    kGeneration1Value,
                    manager => originalManager = manager),
                null,
                timeout.Token).AsTask().WaitAsync(s_readinessTimeout).ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ReadinessLifecycleNodeManager? participant = null;
            TrackingLifecycleNodeManager? childManager = null;
            NodeManagerRegistration? observed = null;
            NodeManagerRegistration? child = null;

            Task<NodeManagerRegistration> reloadTask = StartReadinessChangeAsync(
                change,
                original,
                synchronousFactory,
                kGeneration2Value,
                manager =>
                {
                    participant = manager;
                    manager.ReadinessCallback = async ct =>
                    {
                        Assert.That(ct.CanBeCanceled, Is.False);
                        observed = FindReadinessRegistration();
                        Assert.That(observed.Id, Is.EqualTo(original.Id));
                        Assert.That(observed.Generation, Is.EqualTo(2));
                        Assert.That(observed, Is.Not.SameAs(original));
                        AssertReadinessManager(observed, manager, synchronousFactory);
                        await AssertReadinessValueAsync(kModelNamespaceUri, kGeneration2Value)
                            .ConfigureAwait(false);
                        child = await lifecycle.AddAsync(
                            CreateTrackingNodeManagementFactory(
                                kSecondRegistrationValue,
                                created => childManager = created,
                                kSecondModelNamespaceUri,
                                ReadinessNodeId(kModelNamespaceUri, kRootNodeId)),
                            null,
                            ct).AsTask().WaitAsync(s_readinessTimeout, ct).ConfigureAwait(false);
                        Assert.That(child.Id, Is.Not.EqualTo(original.Id));
                        await AssertReadinessValueAsync(kSecondModelNamespaceUri, kSecondRegistrationValue)
                            .ConfigureAwait(false);
                        entered.TrySetResult(true);
                        await release.Task.WaitAsync(ct).ConfigureAwait(false);
                    };
                },
                timeout.Token);

            NodeManagerRegistration? next = null;
            try
            {
                await entered.Task.WaitAsync(s_readinessTimeout).ConfigureAwait(false);
                AssertLifecycleValue(participant);
                Assert.That(reloadTask.IsCompleted, Is.False, "Reload must await committed-generation readiness.");
                Assert.That(GetBranchRegistrations(lifecycle), Has.Count.EqualTo(2));
                Assert.That(participant.ReadinessCount, Is.EqualTo(1));
                Assert.That(participant.ReadinessCompletedCount, Is.Zero);
                Assert.That(participant.DeleteAddressSpaceCount, Is.Zero);
            }
            finally
            {
                release.TrySetResult(true);
                next = await reloadTask.WaitAsync(s_readinessTimeout).ConfigureAwait(false);
            }

            AssertLifecycleValue(next);
            AssertLifecycleValue(participant);
            AssertLifecycleValue(originalManager);
            AssertLifecycleValue(child);
            AssertLifecycleValue(childManager);
            Assert.That(next, Is.SameAs(observed));
            Assert.That(FindReadinessRegistration(), Is.SameAs(next));
            Assert.That(next.Id, Is.EqualTo(original.Id));
            Assert.That(next.Generation, Is.EqualTo(2));
            Assert.That(participant.ReadinessCount, Is.EqualTo(1));
            Assert.That(participant.ReadinessCompletedCount, Is.EqualTo(1));
            await originalManager.DisposalCompleted.WaitAsync(s_readinessTimeout).ConfigureAwait(false);
            Assert.That(originalManager.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(originalManager.DisposeCount, Is.EqualTo(1));
            await AssertReadinessValueAsync(kModelNamespaceUri, kGeneration2Value).ConfigureAwait(false);
            await RemoveReadinessRegistrationAsync(child).ConfigureAwait(false);
            await RemoveReadinessRegistrationAsync(next, participant, synchronousFactory).ConfigureAwait(false);
            Assert.That(childManager.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(childManager.DisposeCount, Is.EqualTo(1));
            Assert.That(participant.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(GetBranchRegistrations(lifecycle), Is.Empty);
            await AssertReadinessNodeUnknownAsync(kModelNamespaceUri).ConfigureAwait(false);
        }

        /// <summary>
        /// Adds the optional-participant invariant to the existing preparation rollback
        /// tests: a candidate with real, partly created nodes is never made ready.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("NodeManagerReadiness")]
        public async Task PreparationFailureNeverInvokesReadinessAsync(bool reload)
        {
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            using var timeout = new CancellationTokenSource(s_readinessTimeout);
            NodeManagerRegistration? original = null;
            TrackingLifecycleNodeManager? originalManager = null;
            if (reload)
            {
                original = await lifecycle.AddAsync(
                    CreateTrackingNodeManagementFactory(
                        kGeneration1Value,
                        manager => originalManager = manager),
                    null,
                    timeout.Token).AsTask().WaitAsync(s_readinessTimeout).ConfigureAwait(false);
            }

            var expected = new SentinelException("Readiness candidate preparation failed.");
            ReadinessLifecycleNodeManager? participant = null;
            Task<NodeManagerRegistration> operation = StartReadinessChangeAsync(
                reload ? ReadinessChange.Reload : ReadinessChange.Add,
                original,
                synchronousFactory: false,
                kGeneration2Value,
                manager =>
                {
                    participant = manager;
                    manager.PreparationFailure = expected;
                },
                timeout.Token);
            SentinelException failure = await ExpectReadinessExceptionAsync<SentinelException>(operation)
                .ConfigureAwait(false);
            AssertLifecycleValue(participant);

            Assert.That(failure, Is.SameAs(expected));
            Assert.That(participant.ReadinessCount, Is.Zero);
            Assert.That(participant.ReadinessCompletedCount, Is.Zero);
            Assert.That(participant.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(participant.DisposeCount, Is.EqualTo(1));
            NodeId candidateValueId = new(
                kValueNodeId, (ushort)participant.Server.NamespaceUris.GetIndex(kModelNamespaceUri));
            Assert.That(participant.Find(candidateValueId), Is.Null);
            if (reload)
            {
                AssertLifecycleValue(original);
                AssertLifecycleValue(originalManager);
                Assert.That(GetBranchRegistrations(lifecycle), Has.Count.EqualTo(1));
                Assert.That(FindReadinessRegistration(), Is.SameAs(original));
                Assert.That(originalManager.DeleteAddressSpaceCount, Is.Zero);
                Assert.That(originalManager.DisposeCount, Is.Zero);
                await AssertReadinessValueAsync(kModelNamespaceUri, kGeneration1Value).ConfigureAwait(false);
                await RemoveReadinessRegistrationAsync(original).ConfigureAwait(false);
            }
            else
            {
                Assert.That(m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri), Is.EqualTo(-1));
                DataValue missing = await ReadValueAsync(candidateValueId).ConfigureAwait(false);
                Assert.That(missing.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            }
            Assert.That(GetBranchRegistrations(lifecycle), Is.Empty);
        }

        /// <summary>
        /// An Add callback exception is postcommit and preserves the exact live handle
        /// for explicit cleanup and a later, independent registration.
        /// </summary>
        [Test]
        [Category("NodeManagerReadiness")]
        public async Task AddAsyncReadinessFailureRetainsRegistrationForCleanupAsync()
        {
            using var timeout = new CancellationTokenSource(s_readinessTimeout);
            var expected = new SentinelException("Published Add readiness failed.");
            ReadinessLifecycleNodeManager? participant = null;
            NodeManagerRegistration? observed = null;
            Task<NodeManagerRegistration> operation = StartReadinessChangeAsync(
                ReadinessChange.Add,
                null,
                synchronousFactory: false,
                kGeneration1Value,
                manager =>
                {
                    participant = manager;
                    manager.ReadinessCallback = async ct =>
                    {
                        Assert.That(ct.CanBeCanceled, Is.False);
                        observed = FindReadinessRegistration();
                        await AssertReadinessValueAsync(kModelNamespaceUri, kGeneration1Value)
                            .ConfigureAwait(false);
                        throw expected;
                    };
                },
                timeout.Token);
            NodeManagerAddCommittedException failure =
                await ExpectReadinessExceptionAsync<NodeManagerAddCommittedException>(
                operation).ConfigureAwait(false);
            AssertLifecycleValue(participant);
            AssertLifecycleValue(observed);

            Assert.That(failure.Message, Does.Contain("added").And.Contain("readiness"));
            Assert.That(failure.InnerException, Is.SameAs(expected));
            Assert.That(failure.Registration, Is.SameAs(observed));
            Assert.That(FindReadinessRegistration(), Is.SameAs(observed));
            Assert.That(observed.Generation, Is.EqualTo(1));
            Assert.That(observed.NodeManager, Is.SameAs(participant));
            Assert.That(participant.ReadinessCount, Is.EqualTo(1));
            Assert.That(participant.ReadinessCompletedCount, Is.Zero);
            Assert.That(participant.DeleteAddressSpaceCount, Is.Zero);
            Assert.That(participant.DisposeCount, Is.Zero);
            await AssertReadinessNodeUnknownAsync(kModelNamespaceUri).ConfigureAwait(false);
            await AssertReadinessCleanupAndRecoveryAsync(observed, participant).ConfigureAwait(false);
        }

        /// <summary>
        /// Candidate historian bindings stay private through a failed readiness attempt,
        /// then publish once after the exact registration succeeds on retry.
        /// </summary>
        [Test]
        [Category("NodeManagerReadiness")]
        public async Task StagedHistorianBindingsPublishOnlyAfterSuccessfulReadinessRetryAsync()
        {
            using var timeout = new CancellationTokenSource(s_readinessTimeout);
            var expected = new SentinelException("Historian readiness failed once.");
            int readinessAttempts = 0;
            ReadinessLifecycleNodeManager? participant = null;
            StagedHistorianState? staged = null;
            IHistorianProviderRegistry serving =
                ((IHistorianRegistryProvider)m_server.CurrentInstance).HistorianRegistry;

            Task<NodeManagerRegistration> operation = StartReadinessChangeAsync(
                ReadinessChange.Add,
                null,
                synchronousFactory: false,
                kGeneration1Value,
                manager =>
                {
                    participant = manager;
                    staged = CreateStagedHistorianState(manager, serving);
                    manager.ReadinessCallback = _ =>
                    {
                        AssertCandidateHistorianRegistrationsArePrivate(staged!, serving);
                        if (Interlocked.Increment(ref readinessAttempts) == 1)
                        {
                            throw expected;
                        }
                        return default;
                    };
                },
                timeout.Token);

            NodeManagerAddCommittedException failure =
                await ExpectReadinessExceptionAsync<NodeManagerAddCommittedException>(operation)
                    .ConfigureAwait(false);
            AssertLifecycleValue(participant);
            AssertLifecycleValue(staged);
            StagedHistorianState candidate = staged!;

            Assert.That(failure.InnerException, Is.SameAs(expected));
            Assert.That(failure.Registration, Is.SameAs(FindReadinessRegistration()));
            Assert.That(failure.Registration.NodeManager, Is.SameAs(participant));
            Assert.That(readinessAttempts, Is.EqualTo(1));
            AssertCandidateHistorianRegistrationsArePrivate(candidate, serving);
            Assert.That(
                ContainsStateChangedHandler(candidate.Variable, candidate.CaptureHandler),
                Is.True,
                "The candidate builder must retain its capture handler while the committed readiness result is retryable.");

            NodeManagerBatchResult readinessResult = failure.Result ??
                throw new AssertionException("The failed readiness operation did not retain its retry result.");
            await readinessResult.RetryReconciliationAsync(timeout.Token).ConfigureAwait(false);

            Assert.That(readinessAttempts, Is.EqualTo(2));
            Assert.That(serving.Resolve(candidate.ExactNode), Is.SameAs(candidate.NodeProvider));
            Assert.That(
                serving.Resolve(candidate.NamespaceNode),
                Is.SameAs(candidate.NamespaceProvider));
            Assert.That(serving.Resolve(candidate.DefaultNode), Is.SameAs(candidate.DefaultProvider));
            Assert.That(candidate.LocalRegistry.Resolve(candidate.ExactNode), Is.SameAs(candidate.NodeProvider));
            Assert.That(candidate.LocalRegistry.Resolve(candidate.NamespaceNode), Is.SameAs(candidate.NamespaceProvider));
            Assert.That(candidate.LocalRegistry.Resolve(candidate.DefaultNode), Is.SameAs(candidate.DefaultProvider));
            Assert.That(serving.Providers.Contains(candidate.NodeProvider), Is.True);
            Assert.That(serving.Providers.Contains(candidate.NamespaceProvider), Is.True);
            Assert.That(serving.Providers.Contains(candidate.DefaultProvider), Is.True);

            await readinessResult.RetryReconciliationAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(readinessAttempts, Is.EqualTo(2), "A completed retry must not republish or rerun readiness.");
            Assert.That(serving.Resolve(candidate.ExactNode), Is.SameAs(candidate.NodeProvider));

            ReadinessLifecycleNodeManager? replacementManager = null;
            StagedHistorianState? replacementState = null;
            NodeManagerRegistration replacement = await StartReadinessChangeAsync(
                ReadinessChange.Reload,
                failure.Registration,
                synchronousFactory: false,
                kGeneration2Value,
                manager =>
                {
                    replacementManager = manager;
                    replacementState = CreateStagedHistorianState(manager, serving);
                    manager.ReadinessCallback = _ =>
                    {
                        Assert.That(serving.Resolve(candidate.ExactNode), Is.SameAs(candidate.BaselineExactProvider));
                        Assert.That(
                            serving.Resolve(candidate.NamespaceNode),
                            Is.SameAs(candidate.BaselineNamespaceProvider));
                        Assert.That(
                            serving.Resolve(candidate.DefaultNode),
                            Is.SameAs(candidate.BaselineDefaultProvider));
                        Assert.That(candidate.LocalRegistry.Resolve(candidate.ExactNode), Is.SameAs(candidate.NodeProvider));
                        Assert.That(serving.Providers.Contains(candidate.NodeProvider), Is.False);
                        return default;
                    };
                },
                timeout.Token).ConfigureAwait(false);
            AssertLifecycleValue(replacementManager);
            AssertLifecycleValue(replacementState);
            StagedHistorianState replacementCandidate = replacementState!;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(serving.Resolve(replacementCandidate.ExactNode), Is.SameAs(replacementCandidate.NodeProvider));
                Assert.That(
                    serving.Resolve(replacementCandidate.NamespaceNode),
                    Is.SameAs(replacementCandidate.NamespaceProvider));
                Assert.That(
                    serving.Resolve(replacementCandidate.DefaultNode),
                    Is.SameAs(replacementCandidate.DefaultProvider));
                Assert.That(serving.Providers.Contains(candidate.NodeProvider), Is.False);
                Assert.That(serving.Providers.Contains(replacementCandidate.NodeProvider), Is.True);
            }

            await RemoveReadinessRegistrationAsync(replacement, replacementManager).ConfigureAwait(false);

            Assert.That(serving.Resolve(candidate.ExactNode), Is.SameAs(candidate.BaselineExactProvider));
            Assert.That(serving.Resolve(candidate.NamespaceNode), Is.SameAs(candidate.BaselineNamespaceProvider));
            Assert.That(serving.Resolve(candidate.DefaultNode), Is.SameAs(candidate.BaselineDefaultProvider));
            Assert.That(
                ContainsStateChangedHandler(candidate.Variable, candidate.CaptureHandler),
                Is.False,
                "Retiring the ready generation must detach its builder-owned capture handler.");
            Assert.That(
                ContainsStateChangedHandler(replacementCandidate.Variable, replacementCandidate.CaptureHandler),
                Is.False,
                "Retiring the replacement generation must detach its builder-owned capture handler.");
            Assert.That(candidate.NodeProvider.DisposeCount, Is.Zero,
                "The serving registry retains disposal responsibility for a published provider.");
            Assert.That(candidate.NamespaceProvider.DisposeCount, Is.Zero);
            Assert.That(candidate.DefaultProvider.DisposeCount, Is.Zero);
            Assert.That(replacementCandidate.NodeProvider.DisposeCount, Is.Zero);
            Assert.That(replacementCandidate.NamespaceProvider.DisposeCount, Is.Zero);
            Assert.That(replacementCandidate.DefaultProvider.DisposeCount, Is.Zero);
        }

        /// <summary>
        /// Aborting preparation withdraws only candidate state and disposes its staged
        /// providers and capture resources without publishing any bindings.
        /// </summary>
        [Test]
        [Category("NodeManagerReadiness")]
        public async Task AbortedHistorianCandidateDoesNotPublishAndCleansBuilderResourcesAsync()
        {
            using var timeout = new CancellationTokenSource(s_readinessTimeout);
            var expected = new SentinelException("Historian candidate preparation failed.");
            ReadinessLifecycleNodeManager? participant = null;
            StagedHistorianState? staged = null;
            IHistorianProviderRegistry serving =
                ((IHistorianRegistryProvider)m_server.CurrentInstance).HistorianRegistry;
            var legacyProvider = new LifecycleHistorianProvider();
            var otherOwnerProvider = new LifecycleHistorianProvider();
            serving.RegisterDefault(legacyProvider);
            using var otherOwner = new StagedHistorianProviderRegistry(
                m_server.CurrentInstance.NamespaceUris,
                serving,
                new object());
            var otherOwnerNode = new NodeId("s9-other-owner", 0);
            otherOwner.RegisterForNode(otherOwnerNode, otherOwnerProvider);
            otherOwner.Publish();

            Task<NodeManagerRegistration> operation = StartReadinessChangeAsync(
                ReadinessChange.Add,
                null,
                synchronousFactory: false,
                kGeneration1Value,
                manager =>
                {
                    participant = manager;
                    staged = CreateStagedHistorianState(manager, serving);
                    manager.PreparationFailure = expected;
                },
                timeout.Token);

            SentinelException failure =
                await ExpectReadinessExceptionAsync<SentinelException>(operation).ConfigureAwait(false);
            AssertLifecycleValue(participant);
            AssertLifecycleValue(staged);
            StagedHistorianState candidate = staged!;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(failure, Is.SameAs(expected));
                Assert.That(participant.ReadinessCount, Is.Zero);
                Assert.That(participant.DeleteAddressSpaceCount, Is.EqualTo(1));
                Assert.That(participant.DisposeCount, Is.EqualTo(1));
                Assert.That(serving.Resolve(candidate.ExactNode), Is.SameAs(legacyProvider));
                Assert.That(serving.Resolve(candidate.NamespaceNode), Is.SameAs(legacyProvider));
                Assert.That(serving.Resolve(candidate.DefaultNode), Is.SameAs(legacyProvider));
                Assert.That(serving.Resolve(otherOwnerNode), Is.SameAs(otherOwnerProvider));
                Assert.That(serving.Providers.Contains(otherOwnerProvider), Is.True);
                Assert.That(legacyProvider.DisposeCount, Is.Zero);
                Assert.That(otherOwnerProvider.DisposeCount, Is.Zero);
                Assert.That(ContainsStateChangedHandler(candidate.Variable, candidate.CaptureHandler), Is.False);
                Assert.That(candidate.NodeProvider.DisposeCount, Is.EqualTo(1));
                Assert.That(candidate.NamespaceProvider.DisposeCount, Is.EqualTo(1));
                Assert.That(candidate.DefaultProvider.DisposeCount, Is.EqualTo(1));
            }
        }

        /// <summary>
        /// Rejects candidate historian registrations when the manager cannot complete readiness.
        /// </summary>
        [Test]
        [Category("NodeManagerReadiness")]
        public async Task HistorianRegistrationRequiresReadinessParticipantAsync()
        {
            using var timeout = new CancellationTokenSource(s_readinessTimeout);
            var provider = new LifecycleHistorianProvider();
            IHistorianProviderRegistry serving =
                ((IHistorianRegistryProvider)m_server.CurrentInstance).HistorianRegistry;
            NodeId defaultNode = new("s9-no-readiness", 0);
            IHistorianProvider? baselineProvider = serving.Resolve(defaultNode);
            TrackingLifecycleNodeManager? manager = null;

            ServiceResultException? failure = await Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                await m_server.NodeManagerLifecycle.AddAsync(
                    CreateTrackingNodeManagementFactory(
                        kGeneration1Value,
                        created =>
                        {
                            manager = created;
                            new HistorianBuilder(created.SystemContext.Server)
                                .UseProvider(provider)
                                .RegisterAsDefault();
                        }),
                    null,
                    timeout.Token).ConfigureAwait(false);
            }).ConfigureAwait(false);

            AssertLifecycleValue(manager);
            TrackingLifecycleNodeManager actualManager = manager ??
                throw new AssertionException("The historian candidate factory did not create its manager.");
            ServiceResultException actualFailure = failure ??
                throw new AssertionException("Historian registration did not require a readiness participant.");
            using (Assert.EnterMultipleScope())
            {
                Assert.That(actualFailure.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
                Assert.That(actualFailure.Message, Does.Contain("requires").And.Contain("OnServerReadyAsync"));
                Assert.That(actualManager.DisposeCount, Is.EqualTo(1));
                Assert.That(actualManager.DeleteAddressSpaceCount, Is.Zero);
                Assert.That(serving.Resolve(defaultNode), Is.SameAs(baselineProvider));
                Assert.That(serving.Providers.Contains(provider), Is.False);
                Assert.That(provider.DisposeCount, Is.EqualTo(1));
            }
        }

        /// <summary>
        /// A readiness retry owns its pending registration until the callback attempt completes.
        /// </summary>
        [Test]
        [Category("NodeManagerReadiness")]
        public async Task ReadinessRetryRejectsRemovalWhileCallbackIsActiveAsync()
        {
            using var timeout = new CancellationTokenSource(s_readinessTimeout);
            var expected = new SentinelException("Published Add readiness failed before retry.");
            var retryEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseRetry = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int attempts = 0;
            ReadinessLifecycleNodeManager? participant = null;
            Task<NodeManagerRegistration> operation = StartReadinessChangeAsync(
                ReadinessChange.Add,
                null,
                synchronousFactory: false,
                kGeneration1Value,
                manager =>
                {
                    participant = manager;
                    manager.ReadinessCallback = async ct =>
                    {
                        if (Interlocked.Increment(ref attempts) == 1)
                        {
                            throw expected;
                        }
                        retryEntered.TrySetResult(true);
                        await releaseRetry.Task.WaitAsync(ct).ConfigureAwait(false);
                    };
                },
                timeout.Token);
            NodeManagerAddCommittedException failure =
                await ExpectReadinessExceptionAsync<NodeManagerAddCommittedException>(operation)
                    .ConfigureAwait(false);
            NodeManagerBatchResult result = failure.Result ??
                throw new AssertionException("The committed Add result was not retained.");

            Task retry = result.RetryReconciliationAsync(timeout.Token).AsTask();
            try
            {
                await retryEntered.Task.WaitAsync(s_readinessTimeout).ConfigureAwait(false);
                InvalidOperationException removeFailure =
                    await ExpectReadinessExceptionAsync<InvalidOperationException>(
                        m_server.NodeManagerLifecycle.RemoveAsync(
                            failure.Registration, null, timeout.Token).AsTask()).ConfigureAwait(false);
                Assert.That(removeFailure.Message, Does.Contain("completing readiness"));
            }
            finally
            {
                releaseRetry.TrySetResult(true);
            }

            await retry.WaitAsync(s_readinessTimeout).ConfigureAwait(false);
            Assert.That(result.Issues.ToArray(), Is.Empty);
            await RemoveReadinessRegistrationAsync(failure.Registration, participant).ConfigureAwait(false);
            Assert.That(participant!.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(participant.DisposeCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Replacing a service-level owner revokes its writer before the replacement readiness callback claims it.
        /// </summary>
        [Test]
        public async Task ReplacingServiceLevelOwnerRevokesWriterForNextGenerationAsync()
        {
            using var timeout = new CancellationTokenSource(s_readinessTimeout);
            Action<byte>? retiredWriter = null;
            Action<byte>? replacementWriter = null;
            ReadinessLifecycleNodeManager? retiredManager = null;
            ReadinessLifecycleNodeManager? replacementManager = null;
            NodeManagerRegistration registration = await StartReadinessChangeAsync(
                ReadinessChange.Add,
                null,
                synchronousFactory: false,
                kGeneration1Value,
                manager =>
                {
                    retiredManager = manager;
                    manager.ReadinessCallback = _ =>
                    {
                        retiredWriter = ((IServerServiceLevelControl)manager.SystemContext.Server)
                            .ClaimServiceLevelControl();
                        return default;
                    };
                },
                timeout.Token).ConfigureAwait(false);

            NodeManagerRegistration replacement = await StartReadinessChangeAsync(
                ReadinessChange.Reload,
                registration,
                synchronousFactory: false,
                kGeneration2Value,
                manager =>
                {
                    replacementManager = manager;
                    manager.ReadinessCallback = _ =>
                    {
                        replacementWriter = ((IServerServiceLevelControl)manager.SystemContext.Server)
                            .ClaimServiceLevelControl();
                        return default;
                    };
                },
                timeout.Token).ConfigureAwait(false);

            AssertLifecycleValue(retiredManager);
            AssertLifecycleValue(replacementManager);
            Assert.That(replacement.Generation, Is.EqualTo(registration.Generation + 1));
            Assert.That(retiredWriter, Is.Not.Null);
            Assert.That(replacementWriter, Is.Not.Null);
            Assert.That(() => retiredWriter!(10), Throws.TypeOf<InvalidOperationException>());
            replacementWriter!(20);
            await RemoveReadinessRegistrationAsync(replacement).ConfigureAwait(false);
        }

        /// <summary>
        /// Caller cancellation cannot abort committed readiness. Independent participant cancellation
        /// is reported with the authoritative committed identity.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("NodeManagerReadiness")]
        public async Task AddAsyncReadinessCancellationPreservesCommittedOutcomeAsync(bool observeCancellation)
        {
            using var caller = new CancellationTokenSource(s_readinessTimeout);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ReadinessLifecycleNodeManager? participant = null;
            NodeManagerRegistration? observed = null;
            CancellationToken receivedToken = default;
            OperationCanceledException? callbackCancellation = null;
            Task<NodeManagerRegistration> operation = StartReadinessChangeAsync(
                ReadinessChange.Add,
                null,
                synchronousFactory: false,
                kGeneration1Value,
                manager =>
                {
                    participant = manager;
                    manager.ReadinessCallback = async ct =>
                    {
                        receivedToken = ct;
                        observed = FindReadinessRegistration();
                        await AssertReadinessValueAsync(kModelNamespaceUri, kGeneration1Value)
                            .ConfigureAwait(false);
                        entered.TrySetResult(true);
                        await release.Task.WaitAsync(s_readinessTimeout, CancellationToken.None).ConfigureAwait(false);
                        if (observeCancellation)
                        {
                            try
                            {
                                caller.Token.ThrowIfCancellationRequested();
                            }
                            catch (OperationCanceledException exception)
                            {
                                callbackCancellation = exception;
                                throw;
                            }
                        }
                    };
                },
                caller.Token);

            NodeManagerAddCommittedException? failure = null;
            NodeManagerRegistration? added = null;
            try
            {
                await entered.Task.WaitAsync(s_readinessTimeout).ConfigureAwait(false);
                Assert.That(receivedToken.CanBeCanceled, Is.False);
                Assert.That(operation.IsCompleted, Is.False);
                caller.Cancel();
            }
            finally
            {
                release.TrySetResult(true);
                if (observeCancellation)
                {
                    failure = await ExpectReadinessExceptionAsync<NodeManagerAddCommittedException>(operation)
                        .ConfigureAwait(false);
                }
                else
                {
                    added = await operation.WaitAsync(s_readinessTimeout).ConfigureAwait(false);
                }
            }

            AssertLifecycleValue(participant);
            AssertLifecycleValue(observed);
            if (observeCancellation)
            {
                AssertLifecycleValue(failure);
                Assert.That(failure.Message, Does.Contain("added").And.Contain("readiness"));
                Assert.That(failure.InnerException, Is.InstanceOf<OperationCanceledException>());
                Assert.That(
                    RequireLifecycleValue(failure.InnerException as OperationCanceledException).CancellationToken,
                    Is.EqualTo(caller.Token));
                Assert.That(failure.InnerException, Is.SameAs(callbackCancellation));
                Assert.That(failure.Registration, Is.SameAs(observed));
                Assert.That(failure.Result!.Registrations[0], Is.SameAs(observed));
            }
            else
            {
                Assert.That(added, Is.SameAs(observed));
            }
            Assert.That(FindReadinessRegistration(), Is.SameAs(observed));
            Assert.That(observed.Generation, Is.EqualTo(1));
            Assert.That(observed.NodeManager, Is.SameAs(participant));
            Assert.That(participant.ReadinessCount, Is.EqualTo(1));
            Assert.That(participant.ReadinessCompletedCount, Is.EqualTo(observeCancellation ? 0 : 1));
            Assert.That(participant.DeleteAddressSpaceCount, Is.Zero);
            Assert.That(participant.DisposeCount, Is.Zero);
            if (observeCancellation)
            {
                await AssertReadinessNodeUnknownAsync(kModelNamespaceUri).ConfigureAwait(false);
            }
            else
            {
                await AssertReadinessValueAsync(kModelNamespaceUri, kGeneration1Value).ConfigureAwait(false);
            }
            await AssertReadinessCleanupAndRecoveryAsync(observed, participant).ConfigureAwait(false);
        }

        /// <summary>
        /// Callback failures in every reload mode report the authoritative next handle,
        /// including cancellation, and release the readiness claim for explicit cleanup.
        /// </summary>
        [TestCase(ReadinessChange.Reload, false)]
        [TestCase(ReadinessChange.Reload, true)]
        [TestCase(ReadinessChange.ShadowReload, false)]
        [TestCase(ReadinessChange.ShadowReload, true)]
        [TestCase(ReadinessChange.ImmediateReload, false)]
        [TestCase(ReadinessChange.ImmediateReload, true)]
        [Category("NodeManagerReadiness")]
        public async Task ReloadReadinessFailureReportsExactCommittedRegistrationAsync(
            ReadinessChange change,
            bool cancel)
        {
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            using var caller = new CancellationTokenSource(s_readinessTimeout);
            TrackingLifecycleNodeManager? originalManager = null;
            NodeManagerRegistration original = await lifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(
                    kGeneration1Value,
                    manager => originalManager = manager),
                null,
                caller.Token).AsTask().WaitAsync(s_readinessTimeout).ConfigureAwait(false);
            Exception expected = cancel
                ? new OperationCanceledException("Committed reload readiness cancelled.", caller.Token)
                : new SentinelException("Committed reload readiness failed.");
            ReadinessLifecycleNodeManager? participant = null;
            NodeManagerRegistration? observed = null;
            Task<NodeManagerRegistration> operation = StartReadinessChangeAsync(
                change,
                original,
                synchronousFactory: false,
                kGeneration2Value,
                manager =>
                {
                    participant = manager;
                    manager.ReadinessCallback = async ct =>
                    {
                        Assert.That(ct.CanBeCanceled, Is.False);
                        observed = FindReadinessRegistration();
                        Assert.That(observed.Id, Is.EqualTo(original.Id));
                        Assert.That(observed.Generation, Is.EqualTo(2));
                        await AssertReadinessValueAsync(kModelNamespaceUri, kGeneration2Value)
                            .ConfigureAwait(false);
                        if (cancel)
                        {
                            caller.Cancel();
                            Assert.That(ct.IsCancellationRequested, Is.False);
                        }
                        throw expected;
                    };
                },
                caller.Token);
            NodeManagerReloadCommittedException failure =
                await ExpectReadinessExceptionAsync<NodeManagerReloadCommittedException>(operation)
                    .ConfigureAwait(false);
            AssertLifecycleValue(participant);
            AssertLifecycleValue(originalManager);

            Assert.That(failure.Message, Does.Contain("replacement NodeManager is live"));
            Assert.That(failure.InnerException, Is.SameAs(expected));
            Assert.That(failure.Registration, Is.SameAs(observed));
            Assert.That(FindReadinessRegistration(), Is.SameAs(failure.Registration));
            Assert.That(failure.Registration.Id, Is.EqualTo(original.Id));
            Assert.That(failure.Registration.Generation, Is.EqualTo(2));
            Assert.That(failure.Registration.NodeManager, Is.SameAs(participant));
            Assert.That(participant.ReadinessCount, Is.EqualTo(1));
            Assert.That(participant.ReadinessCompletedCount, Is.Zero);
            Assert.That(participant.DeleteAddressSpaceCount, Is.Zero);
            Assert.That(participant.DisposeCount, Is.Zero);
            await originalManager.DisposalCompleted.WaitAsync(s_readinessTimeout).ConfigureAwait(false);
            Assert.That(originalManager.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(originalManager.DisposeCount, Is.EqualTo(1));
            // The replacement is live but never completed readiness, so external reads stay hidden.
            await AssertReadinessNodeUnknownAsync(kModelNamespaceUri).ConfigureAwait(false);
            TrackingLifecycleNodeManager? rejectedManager = null;
            using var rejectTimeout = new CancellationTokenSource(s_readinessTimeout);
            Assert.That(
                () => lifecycle.ReloadAsync(
                    failure.Registration,
                    CreateTrackingNodeManagementFactory(
                        kFirstRegistrationValue,
                        manager => rejectedManager = manager),
                    rejectTimeout.Token).AsTask().WaitAsync(s_readinessTimeout),
                Throws.InvalidOperationException);
            Assert.That(rejectedManager, Is.Null);
            await AssertReadinessCleanupAndRecoveryAsync(failure.Registration, participant).ConfigureAwait(false);
        }

        /// <summary>
        /// Readiness reserves only its generation. Conflicts must reject before invoking
        /// factories or deletion, not wait for readiness while retaining the global gate.
        /// </summary>
        [TestCase(ReadinessChange.Add)]
        [TestCase(ReadinessChange.Reload)]
        [TestCase(ReadinessChange.ShadowReload)]
        [TestCase(ReadinessChange.ImmediateReload)]
        [Category("NodeManagerReadiness")]
        public async Task ReadinessRejectsSameHandleChangesWithoutBlockingIndependentRegistrationAsync(
            ReadinessChange change)
        {
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            using var timeout = new CancellationTokenSource(s_readinessTimeout);
            NodeManagerRegistration? original = null;
            if (change != ReadinessChange.Add)
            {
                original = await lifecycle.AddAsync(
                    CreateNodeManagementFactory(kGeneration1Value, includeEuRange: false),
                    null,
                    timeout.Token).AsTask().WaitAsync(s_readinessTimeout).ConfigureAwait(false);
            }

            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ReadinessLifecycleNodeManager? participant = null;
            NodeManagerRegistration? observed = null;
            Task<NodeManagerRegistration> operation = StartReadinessChangeAsync(
                change,
                original,
                synchronousFactory: false,
                kGeneration2Value,
                manager =>
                {
                    participant = manager;
                    manager.ReadinessCallback = async ct =>
                    {
                        observed = FindReadinessRegistration();
                        entered.TrySetResult(true);
                        await release.Task.WaitAsync(ct).ConfigureAwait(false);
                    };
                },
                timeout.Token);
            var asyncReplacement = new Mock<IAsyncNodeManagerFactory>(MockBehavior.Strict);
            var syncReplacement = new Mock<INodeManagerFactory>(MockBehavior.Strict);
            NodeManagerRegistration? completed = null;

            try
            {
                await entered.Task.WaitAsync(s_readinessTimeout).ConfigureAwait(false);
                AssertLifecycleValue(observed);
                AssertLifecycleValue(participant);
                Assert.That(operation.IsCompleted, Is.False);
                InvalidOperationException removeFailure =
                    await ExpectReadinessExceptionAsync<InvalidOperationException>(
                        lifecycle.RemoveAsync(observed, null, timeout.Token).AsTask()).ConfigureAwait(false);
                Assert.That(removeFailure.Message, Does.Contain("completing readiness"));

                asyncReplacement.SetupGet(factory => factory.NamespacesUris).Returns([]);
                syncReplacement.SetupGet(factory => factory.NamespacesUris).Returns([]);
                ReadinessChange[] reloads =
                [
                    ReadinessChange.Reload,
                    ReadinessChange.ShadowReload,
                    ReadinessChange.ImmediateReload
                ];
                foreach (ReadinessChange reload in reloads)
                {
                    InvalidOperationException reloadFailure =
                        await ExpectReadinessExceptionAsync<InvalidOperationException>(
                            InvokeReadinessChangeAsync(
                                lifecycle,
                                reload,
                                observed,
                                asyncReplacement.Object,
                                timeout.Token).AsTask()).ConfigureAwait(false);
                    Assert.That(reloadFailure.Message, Does.Contain("completing readiness"));

                    // One held Add covers the sync competitor overloads too; the other
                    // cases focus on ownership of each distinct reload operation.
                    if (change == ReadinessChange.Add)
                    {
                        InvalidOperationException syncFailure =
                            await ExpectReadinessExceptionAsync<InvalidOperationException>(
                                InvokeReadinessChangeAsync(
                                    lifecycle,
                                    reload,
                                    observed,
                                    syncReplacement.Object,
                                    timeout.Token).AsTask()).ConfigureAwait(false);
                        Assert.That(syncFailure.Message, Does.Contain("completing readiness"));
                    }
                }

                asyncReplacement.Verify(
                    factory => factory.CreateAsync(
                        It.IsAny<IServerInternal>(),
                        It.IsAny<ApplicationConfiguration>(),
                        It.IsAny<CancellationToken>()),
                    Times.Never);
                syncReplacement.Verify(
                    factory => factory.Create(
                        It.IsAny<IServerInternal>(),
                        It.IsAny<ApplicationConfiguration>()),
                    Times.Never);
                Assert.That(participant.DeleteAddressSpaceCount, Is.Zero);
                Assert.That(participant.DisposeCount, Is.Zero);
                Assert.That(FindReadinessRegistration(), Is.SameAs(observed));

                TrackingLifecycleNodeManager? independentManager = null;
                NodeManagerRegistration independent = await lifecycle.AddAsync(
                    CreateTrackingNodeManagementFactory(
                        kSecondRegistrationValue,
                        manager => independentManager = manager,
                        kSecondModelNamespaceUri),
                    null,
                    timeout.Token).AsTask().WaitAsync(s_readinessTimeout).ConfigureAwait(false);
                Assert.That(independent.Id, Is.Not.EqualTo(observed.Id));
                await AssertReadinessValueAsync(kSecondModelNamespaceUri, kSecondRegistrationValue)
                    .ConfigureAwait(false);
                await RemoveReadinessRegistrationAsync(independent).ConfigureAwait(false);
                AssertLifecycleValue(independentManager);
                Assert.That(independentManager.DeleteAddressSpaceCount, Is.EqualTo(1));
                Assert.That(independentManager.DisposeCount, Is.EqualTo(1));
                await AssertReadinessNodeUnknownAsync(kSecondModelNamespaceUri).ConfigureAwait(false);
                await AssertReadinessNodeUnknownAsync(kModelNamespaceUri).ConfigureAwait(false);
                Assert.That(operation.IsCompleted, Is.False);
                Assert.That(participant.ReadinessCompletedCount, Is.Zero);
                Assert.That(participant.DeleteAddressSpaceCount, Is.Zero);
                Assert.That(participant.DisposeCount, Is.Zero);
                Assert.That(GetBranchRegistrations(lifecycle), Has.Count.EqualTo(1));
            }
            finally
            {
                release.TrySetResult(true);
                completed = await operation.WaitAsync(s_readinessTimeout).ConfigureAwait(false);
            }

            AssertLifecycleValue(completed);
            AssertLifecycleValue(participant);
            Assert.That(completed, Is.SameAs(observed));
            Assert.That(participant.ReadinessCount, Is.EqualTo(1));
            Assert.That(participant.ReadinessCompletedCount, Is.EqualTo(1));
            await AssertReadinessValueAsync(kModelNamespaceUri, kGeneration2Value).ConfigureAwait(false);
            await RemoveReadinessRegistrationAsync(completed).ConfigureAwait(false);
            Assert.That(participant.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(participant.DisposeCount, Is.EqualTo(1));
            Assert.That(GetBranchRegistrations(lifecycle), Is.Empty);
            await AssertReadinessNodeUnknownAsync(kModelNamespaceUri).ConfigureAwait(false);
        }

        /// <summary>
        /// Detached manager destruction may remove its real child through the lifecycle.
        /// Removal and retired-drain claims still prevent duplicate destruction while that
        /// callback is suspended, including a shadow generation with a real monitored item.
        /// </summary>
        [TestCase(ReadinessCleanup.Remove)]
        [TestCase(ReadinessCleanup.Reload)]
        [TestCase(ReadinessCleanup.ShadowDrain)]
        [Category("NodeManagerReadiness")]
        public async Task DetachedDeletionRemovesRealDependentRegistrationWithSingleCleanupAsync(
            ReadinessCleanup cleanup)
        {
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            using var timeout = new CancellationTokenSource(s_readinessTimeout);
            var deleteEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseDelete = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ReadinessLifecycleNodeManager? parentManager = null;
            TrackingLifecycleNodeManager? childManager = null;
            TrackingLifecycleNodeManager? replacementManager = null;
            NodeManagerRegistration? child = null;
            NodeManagerRegistration original = await StartReadinessChangeAsync(
                ReadinessChange.Add,
                null,
                synchronousFactory: false,
                kGeneration1Value,
                manager =>
                {
                    parentManager = manager;
                    manager.ReadinessCallback = async ct =>
                    {
                        child = await lifecycle.AddAsync(
                            CreateTrackingNodeManagementFactory(
                                kSecondRegistrationValue,
                                created => childManager = created,
                                kSecondModelNamespaceUri,
                                ReadinessNodeId(kModelNamespaceUri, kRootNodeId)),
                            null,
                            ct).AsTask().WaitAsync(s_readinessTimeout, ct).ConfigureAwait(false);
                    };
                    manager.DeleteCallback = async ct =>
                    {
                        // If an earlier assertion fails, the existing ordered fixture
                        // shutdown owns all remaining registrations, including the child.
                        if (lifecycle.IsShuttingDown)
                        {
                            return;
                        }
                        deleteEntered.TrySetResult(true);
                        await releaseDelete.Task.WaitAsync(s_readinessTimeout, ct).ConfigureAwait(false);
                        using var nestedTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        nestedTimeout.CancelAfter(s_readinessTimeout);
                        await RemoveAndRetireAsync(lifecycle, RequireLifecycleValue(child), nestedTimeout.Token)
                            .WaitAsync(s_readinessTimeout, ct).ConfigureAwait(false);
                    };
                },
                timeout.Token).WaitAsync(s_readinessTimeout).ConfigureAwait(false);
            AssertLifecycleValue(parentManager);
            AssertLifecycleValue(childManager);
            AssertLifecycleValue(child);

            var services = new ServerTestServices(m_server, m_secureChannelContext);
            uint subscriptionId = 0;
            Task? cleanupTask = null;
            Task<NodeManagerRegistration>? reloadTask = null;
            NodeManagerRegistration? replacement = null;
            try
            {
                if (cleanup == ReadinessCleanup.ShadowDrain)
                {
                    // The monitored item migrates at commit; the test separately awaits physical retirement.
                    (subscriptionId, _) = await CreateSubscriptionWithMonitoredItemAsync(
                        services,
                        ReadinessNodeId(kModelNamespaceUri, kValueNodeId))
                        .WaitAsync(s_readinessTimeout).ConfigureAwait(false);
                }
                reloadTask = RunWithoutExecutionContext(async () =>
                {
                    NodeManagerBatchChange change = cleanup == ReadinessCleanup.Remove
                        ? NodeManagerBatchChange.Remove(original)
                        : NodeManagerBatchChange.Replace(original, CreateTrackingNodeManagementFactory(
                            kGeneration2Value, manager => replacementManager = manager));
                    await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                        [change], timeout.Token).ConfigureAwait(false);
                    NodeManagerBatchResult result = await prepared.CommitAsync(_ => default, timeout.Token)
                        .ConfigureAwait(false);
                    Assert.That(result.CleanupFailure, Is.Null);
                    await result.Retirement.WaitAsync(s_readinessTimeout, timeout.Token).ConfigureAwait(false);
                    return cleanup == ReadinessCleanup.Remove ? original : result.Registrations[0];
                });
                cleanupTask = reloadTask;

                await deleteEntered.Task.WaitAsync(s_readinessTimeout).ConfigureAwait(false);
                Assert.That(cleanupTask.IsCompleted, Is.False);
                Assert.That(parentManager.DeleteAddressSpaceCount, Is.EqualTo(1));
                Assert.That(parentManager.DisposeCount, Is.Zero);
                Assert.That(childManager.DeleteAddressSpaceCount, Is.Zero);
                Assert.That(childManager.DisposeCount, Is.Zero);
                Assert.That(lifecycle.Registrations.Find(candidate => candidate.Id == child.Id), Is.SameAs(child));
                await AssertReadinessValueAsync(kSecondModelNamespaceUri, kSecondRegistrationValue)
                    .ConfigureAwait(false);

                InvalidOperationException competingRemove =
                    await ExpectReadinessExceptionAsync<InvalidOperationException>(
                        lifecycle.RemoveAsync(original, null, timeout.Token).AsTask()).ConfigureAwait(false);
                Assert.That(competingRemove.Message, Does.Contain("stale").Or.Contain("not owned"));

                // These real operations also give retired cleanup another opportunity
                // to run while the first destruction callback owns its drain claim.
                TrackingLifecycleNodeManager? probeManager = null;
                NodeManagerRegistration probe = await lifecycle.AddAsync(
                    CreateTrackingNodeManagementFactory(
                        kFirstRegistrationValue,
                        manager => probeManager = manager,
                        kReadinessProbeNamespaceUri),
                    null,
                    timeout.Token).AsTask().WaitAsync(s_readinessTimeout).ConfigureAwait(false);
                await AssertReadinessValueAsync(kReadinessProbeNamespaceUri, kFirstRegistrationValue)
                    .ConfigureAwait(false);
                await RemoveReadinessRegistrationAsync(probe).ConfigureAwait(false);
                AssertLifecycleValue(probeManager);
                Assert.That(probeManager.DeleteAddressSpaceCount, Is.EqualTo(1));
                Assert.That(probeManager.DisposeCount, Is.EqualTo(1));
                Assert.That(parentManager.DeleteAddressSpaceCount, Is.EqualTo(1));
                Assert.That(parentManager.DisposeCount, Is.Zero);
                Assert.That(childManager.DeleteAddressSpaceCount, Is.Zero);
                Assert.That(cleanupTask.IsCompleted, Is.False);
                if (cleanup == ReadinessCleanup.Remove)
                {
                    await AssertReadinessNodeUnknownAsync(kModelNamespaceUri).ConfigureAwait(false);
                }
                else
                {
                    AssertLifecycleValue(replacementManager);
                    Assert.That(FindReadinessRegistration().Generation, Is.EqualTo(2));
                    await AssertReadinessValueAsync(kModelNamespaceUri, kGeneration2Value).ConfigureAwait(false);
                    Assert.That(replacementManager.DeleteAddressSpaceCount, Is.Zero);
                    Assert.That(replacementManager.DisposeCount, Is.Zero);
                }
            }
            finally
            {
                releaseDelete.TrySetResult(true);
                try
                {
                    if (cleanupTask is not null)
                    {
                        await cleanupTask.WaitAsync(s_readinessTimeout).ConfigureAwait(false);
                    }
                }
                finally
                {
                    if (subscriptionId != 0)
                    {
                        await DeleteSubscriptionAsync(services, subscriptionId)
                            .WaitAsync(s_readinessTimeout).ConfigureAwait(false);
                    }
                }
            }

            if (reloadTask is not null && cleanup != ReadinessCleanup.Remove)
            {
                replacement = await reloadTask.WaitAsync(s_readinessTimeout).ConfigureAwait(false);
            }
            Assert.That(parentManager.ReadinessCount, Is.EqualTo(1));
            Assert.That(parentManager.ReadinessCompletedCount, Is.EqualTo(1));
            Assert.That(parentManager.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(parentManager.DisposeCount, Is.EqualTo(1));
            Assert.That(parentManager.Find(ReadinessNodeId(kModelNamespaceUri, kValueNodeId)), Is.Null);
            Assert.That(childManager.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(childManager.DisposeCount, Is.EqualTo(1));
            Assert.That(lifecycle.Registrations.Find(candidate => candidate.Id == child.Id), Is.Null);
            await AssertReadinessNodeUnknownAsync(kSecondModelNamespaceUri).ConfigureAwait(false);
            await AssertReadinessNodeUnknownAsync(kReadinessProbeNamespaceUri).ConfigureAwait(false);
            if (replacement is not null)
            {
                AssertLifecycleValue(replacementManager);
                Assert.That(FindReadinessRegistration(), Is.SameAs(replacement));
                Assert.That(replacement.Id, Is.EqualTo(original.Id));
                Assert.That(replacement.Generation, Is.EqualTo(2));
                await AssertReadinessValueAsync(kModelNamespaceUri, kGeneration2Value).ConfigureAwait(false);
                await RemoveReadinessRegistrationAsync(replacement).ConfigureAwait(false);
                Assert.That(replacementManager.DeleteAddressSpaceCount, Is.EqualTo(1));
                Assert.That(replacementManager.DisposeCount, Is.EqualTo(1));
            }
            Assert.That(GetBranchRegistrations(lifecycle), Is.Empty);
            await AssertReadinessNodeUnknownAsync(kModelNamespaceUri).ConfigureAwait(false);
        }

        private Task<NodeManagerRegistration> StartReadinessChangeAsync(
            ReadinessChange change,
            NodeManagerRegistration? original,
            bool synchronousFactory,
            int value,
            Action<ReadinessLifecycleNodeManager> configure,
            CancellationToken cancellationToken)
        {
            ReadinessLifecycleNodeManager CreateManager(
                IServerInternal server,
                ApplicationConfiguration configuration)
            {
                var manager = new ReadinessLifecycleNodeManager(
                    server,
                    configuration,
                    m_logger,
                    value);
                configure(manager);
                return manager;
            }

            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            if (synchronousFactory)
            {
                var factory = new Mock<INodeManagerFactory>();
                factory.Setup(candidate => candidate.Create(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>()))
                    .Returns((IServerInternal server, ApplicationConfiguration configuration) =>
                        new SyncNodeManagerAdapter(CreateManager(server, configuration)));
                return RunWithoutExecutionContext(() => InvokeReadinessChangeAsync(
                    lifecycle, change, original, factory.Object, cancellationToken).AsTask());
            }
            else
            {
                var factory = new Mock<IAsyncNodeManagerFactory>();
                factory.Setup(candidate => candidate.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                    .Returns((
                        IServerInternal server,
                        ApplicationConfiguration configuration,
                        CancellationToken _) => new ValueTask<IAsyncNodeManager>(
                            CreateManager(server, configuration)));
                return RunWithoutExecutionContext(() => InvokeReadinessChangeAsync(
                    lifecycle, change, original, factory.Object, cancellationToken).AsTask());
            }
        }

        private static ValueTask<NodeManagerRegistration> InvokeReadinessChangeAsync(
            INodeManagerLifecycle lifecycle,
            ReadinessChange change,
            NodeManagerRegistration? original,
            IAsyncNodeManagerFactory factory,
            CancellationToken cancellationToken)
        {
            return change switch
            {
                ReadinessChange.Add => lifecycle.AddAsync(factory, null, cancellationToken),
                ReadinessChange.Reload => lifecycle.ReloadAsync(
                    RequireLifecycleValue(original), factory, null, cancellationToken),
                ReadinessChange.ShadowReload => lifecycle.ShadowReloadAsync(
                    RequireLifecycleValue(original), factory, cancellationToken),
                ReadinessChange.ImmediateReload => lifecycle.ImmediateReloadAsync(
                    RequireLifecycleValue(original), factory, cancellationToken),
                _ => throw new ArgumentOutOfRangeException(nameof(change))
            };
        }

        private static ValueTask<NodeManagerRegistration> InvokeReadinessChangeAsync(
            INodeManagerLifecycle lifecycle,
            ReadinessChange change,
            NodeManagerRegistration? original,
            INodeManagerFactory factory,
            CancellationToken cancellationToken)
        {
            return change switch
            {
                ReadinessChange.Add => lifecycle.AddAsync(factory, null, cancellationToken),
                ReadinessChange.Reload => lifecycle.ReloadAsync(
                    RequireLifecycleValue(original), factory, null, cancellationToken),
                ReadinessChange.ShadowReload => lifecycle.ShadowReloadAsync(
                    RequireLifecycleValue(original), factory, cancellationToken),
                ReadinessChange.ImmediateReload => lifecycle.ImmediateReloadAsync(
                    RequireLifecycleValue(original), factory, cancellationToken),
                _ => throw new ArgumentOutOfRangeException(nameof(change))
            };
        }

        private NodeManagerRegistration FindReadinessRegistration()
        {
            NodeManagerRegistration? registration = m_server.NodeManagerLifecycle.Registrations.Find(
                candidate => candidate.NamespaceUris.Contains(kModelNamespaceUri));
            Assert.That(registration, Is.Not.Null, "Readiness requires an already committed registration.");
            return RequireLifecycleValue(registration);
        }

        private NodeId ReadinessNodeId(string namespaceUri, uint identifier)
        {
            int namespaceIndex = m_server.CurrentInstance.NamespaceUris.GetIndex(namespaceUri);
            Assert.That(namespaceIndex, Is.GreaterThanOrEqualTo(0));
            return new NodeId(identifier, (ushort)namespaceIndex);
        }

        private async Task AssertReadinessValueAsync(string namespaceUri, int expected)
        {
            DataValue value = await ReadValueAsync(ReadinessNodeId(namespaceUri, kValueNodeId))
                .WaitAsync(s_readinessTimeout).ConfigureAwait(false);
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(value.WrappedValue.GetInt32(), Is.EqualTo(expected));
        }

        private async Task AssertReadinessNodeUnknownAsync(string namespaceUri)
        {
            DataValue value = await ReadValueAsync(ReadinessNodeId(namespaceUri, kValueNodeId))
                .WaitAsync(s_readinessTimeout).ConfigureAwait(false);
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        private static StagedHistorianState CreateStagedHistorianState(
            ReadinessLifecycleNodeManager manager,
            IHistorianProviderRegistry serving)
        {
            IServerInternal stagingServer = manager.SystemContext.Server;
            var localRegistry =
                ((IHistorianRegistryProvider)stagingServer).HistorianRegistry;
            ushort namespaceIndex = manager.NamespaceIndexes[0];
            var exactNode = new NodeId("s9-historian-exact", namespaceIndex);
            var namespaceNode = new NodeId("s9-historian-namespace", namespaceIndex);
            var defaultNode = new NodeId("s9-historian-default", 0);
            var nodeProvider = new LifecycleHistorianProvider();
            var namespaceProvider = new LifecycleHistorianProvider();
            var defaultProvider = new LifecycleHistorianProvider();
            var variable = new BaseDataVariableState(parent: null)
            {
                NodeId = exactNode,
                BrowseName = new QualifiedName("S9HistorianCapture", namespaceIndex),
                DisplayName = new LocalizedText("S9HistorianCapture"),
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.Scalar
            };

            var nodeBuilder = new HistorianBuilder(stagingServer).UseProvider(nodeProvider);
            nodeBuilder.RegisterForNode(exactNode);
            Delegate[] initialHandlers = GetStateChangedHandlers(variable);
            nodeBuilder.Historize(
                variable,
                systemContext: manager.SystemContext,
                captureOptions: new HistorianCaptureOptions
                {
                    BatchTarget = 1,
                    BatchWindow = TimeSpan.FromSeconds(1)
                });
            new HistorianBuilder(stagingServer)
                .UseProvider(namespaceProvider)
                .RegisterForNamespace(kModelNamespaceUri);
            new HistorianBuilder(stagingServer)
                .UseProvider(defaultProvider)
                .RegisterAsDefault();

            Delegate? captureHandler = null;
            foreach (Delegate handler in GetStateChangedHandlers(variable))
            {
                if (!initialHandlers.Any(initial => ReferenceEquals(initial, handler)))
                {
                    Assert.That(captureHandler, Is.Null,
                        "Historize must install exactly one capture handler on this variable.");
                    captureHandler = handler;
                }
            }
            Assert.That(captureHandler, Is.Not.Null,
                "Historize must install the candidate builder's value-capture handler.");

            return new StagedHistorianState(
                localRegistry,
                exactNode,
                namespaceNode,
                defaultNode,
                serving.Resolve(exactNode),
                serving.Resolve(namespaceNode),
                serving.Resolve(defaultNode),
                nodeProvider,
                namespaceProvider,
                defaultProvider,
                variable,
                captureHandler!);
        }

        private static void AssertCandidateHistorianRegistrationsArePrivate(
            StagedHistorianState staged,
            IHistorianProviderRegistry serving)
        {
            Assert.That(staged.LocalRegistry.Resolve(staged.ExactNode), Is.SameAs(staged.NodeProvider));
            Assert.That(staged.LocalRegistry.Resolve(staged.NamespaceNode), Is.SameAs(staged.NamespaceProvider));
            Assert.That(staged.LocalRegistry.Resolve(staged.DefaultNode), Is.SameAs(staged.DefaultProvider));
            Assert.That(serving.Resolve(staged.ExactNode), Is.SameAs(staged.BaselineExactProvider));
            Assert.That(serving.Resolve(staged.NamespaceNode), Is.SameAs(staged.BaselineNamespaceProvider));
            Assert.That(serving.Resolve(staged.DefaultNode), Is.SameAs(staged.BaselineDefaultProvider));
            Assert.That(serving.Providers.Contains(staged.NodeProvider), Is.False);
            Assert.That(serving.Providers.Contains(staged.NamespaceProvider), Is.False);
            Assert.That(serving.Providers.Contains(staged.DefaultProvider), Is.False);
        }

        private static Delegate[] GetStateChangedHandlers(BaseVariableState variable)
        {
            var handlersField = typeof(NodeState).GetField(
                nameof(NodeState.StateChanged),
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(handlersField, Is.Not.Null,
                "The StateChanged backing delegate is required to verify builder cleanup.");
            return ((Delegate?)handlersField!.GetValue(variable))?.GetInvocationList() ?? [];
        }

        private static bool ContainsStateChangedHandler(BaseVariableState variable, Delegate expected)
        {
            foreach (Delegate handler in GetStateChangedHandlers(variable))
            {
                if (ReferenceEquals(handler, expected))
                {
                    return true;
                }
            }
            return false;
        }

        private static void AssertReadinessManager(
            NodeManagerRegistration registration,
            ReadinessLifecycleNodeManager participant,
            bool synchronousFactory)
        {
            if (synchronousFactory)
            {
                Assert.That(registration.NodeManager, Is.TypeOf<AsyncNodeManagerAdapter>());
                Assert.That(registration.NodeManager.SyncNodeManager, Is.TypeOf<SyncNodeManagerAdapter>());
            }
            else
            {
                Assert.That(registration.NodeManager, Is.SameAs(participant));
            }
        }

        private static async Task<TException> ExpectReadinessExceptionAsync<TException>(Task operation)
            where TException : Exception
        {
            TException? failure = null;
            try
            {
                await operation.WaitAsync(s_readinessTimeout).ConfigureAwait(false);
            }
            catch (TException exception)
            {
                failure = exception;
            }
            Assert.That(failure, Is.TypeOf<TException>());
            return RequireLifecycleValue(failure);
        }

        private async Task RemoveReadinessRegistrationAsync(
            NodeManagerRegistration registration,
            ReadinessLifecycleNodeManager? participant = null,
            bool synchronousFactory = false)
        {
            using var timeout = new CancellationTokenSource(s_readinessTimeout);
            await RemoveAndRetireAsync(m_server.NodeManagerLifecycle, registration, timeout.Token)
                .WaitAsync(s_readinessTimeout).ConfigureAwait(false);
            if (synchronousFactory)
            {
                // The legacy sync facade does not own disposal of its underlying async
                // manager. Release that test-owned resource only after real removal.
                RequireLifecycleValue(participant).Dispose();
            }
        }

        private async Task AssertReadinessCleanupAndRecoveryAsync(
            NodeManagerRegistration registration,
            ReadinessLifecycleNodeManager participant)
        {
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            Assert.That(GetBranchRegistrations(lifecycle), Has.Count.EqualTo(1));
            await RemoveReadinessRegistrationAsync(registration).ConfigureAwait(false);
            Assert.That(GetBranchRegistrations(lifecycle), Is.Empty);
            Assert.That(participant.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(participant.DisposeCount, Is.EqualTo(1));
            await AssertReadinessNodeUnknownAsync(kModelNamespaceUri).ConfigureAwait(false);

            using var timeout = new CancellationTokenSource(s_readinessTimeout);
            TrackingLifecycleNodeManager? laterManager = null;
            NodeManagerRegistration later = await lifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(
                    kFirstRegistrationValue,
                    manager => laterManager = manager),
                null,
                timeout.Token).AsTask().WaitAsync(s_readinessTimeout).ConfigureAwait(false);
            Assert.That(later.Id, Is.Not.EqualTo(registration.Id));
            Assert.That(later.Generation, Is.EqualTo(1));
            Assert.That(later.NodeManager, Is.SameAs(laterManager));
            await AssertReadinessValueAsync(kModelNamespaceUri, kFirstRegistrationValue).ConfigureAwait(false);
            await RemoveReadinessRegistrationAsync(later).ConfigureAwait(false);
            AssertLifecycleValue(laterManager);
            Assert.That(laterManager.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(laterManager.DisposeCount, Is.EqualTo(1));
            Assert.That(participant.ReadinessCount, Is.EqualTo(1));
            Assert.That(GetBranchRegistrations(lifecycle), Is.Empty);
            await AssertReadinessNodeUnknownAsync(kModelNamespaceUri).ConfigureAwait(false);
        }

        private sealed class ReadinessLifecycleNodeManager :
            NodeManagementLifecycleNodeManager,
            INodeManagerReadinessNotification
        {
            public ReadinessLifecycleNodeManager(
                IServerInternal server,
                ApplicationConfiguration configuration,
                ILogger logger,
                int value)
                : base(server, configuration, logger, kModelNamespaceUri, value)
            {
            }

            public Func<CancellationToken, ValueTask>? ReadinessCallback { get; set; }

            public Func<CancellationToken, ValueTask>? DeleteCallback { get; set; }

            public Exception? PreparationFailure { get; set; }

            public int ReadinessCount => Volatile.Read(ref m_readinessCount);

            public int ReadinessCompletedCount => Volatile.Read(ref m_readinessCompletedCount);

            public int DeleteAddressSpaceCount => Volatile.Read(ref m_deleteAddressSpaceCount);

            public int DisposeCount => Volatile.Read(ref m_disposeCount);

            public int HistoryUpdateCount => Volatile.Read(ref m_historyUpdateCount);

            public Task DisposalCompleted => m_disposed.Task;

            public ValueTask ReportRootEventAsync(string message, CancellationToken cancellationToken)
            {
                NodeState root = RequireLifecycleValue(Find(new NodeId(kRootNodeId, NamespaceIndexes[0])));
                var occurrence = new BaseEventState(null);
                occurrence.Initialize(SystemContext, root, EventSeverity.Medium, new LocalizedText(message));
                return OnReportEventAsync(SystemContext, root, occurrence, cancellationToken);
            }

            public override ValueTask HistoryUpdateAsync(
                OperationContext context,
                Type detailsType,
                ArrayOf<HistoryUpdateDetails> nodesToUpdate,
                IList<HistoryUpdateResult> results,
                IList<ServiceResult> errors,
                CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref m_historyUpdateCount);
                return base.HistoryUpdateAsync(
                    context, detailsType, nodesToUpdate, results, errors, cancellationToken);
            }

            public async ValueTask OnServerReadyAsync(CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref m_readinessCount);
                if (ReadinessCallback is not null)
                {
                    await ReadinessCallback(cancellationToken).ConfigureAwait(false);
                }
                Interlocked.Increment(ref m_readinessCompletedCount);
            }

            public override async ValueTask CreateAddressSpaceAsync(
                IDictionary<NodeId, IList<IReference>> externalReferences,
                CancellationToken cancellationToken = default)
            {
                await base.CreateAddressSpaceAsync(externalReferences, cancellationToken).ConfigureAwait(false);
                if (PreparationFailure is not null)
                {
                    throw PreparationFailure;
                }
            }

            public override async ValueTask DeleteAddressSpaceAsync(CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref m_deleteAddressSpaceCount);
                if (DeleteCallback is not null)
                {
                    await DeleteCallback(cancellationToken).ConfigureAwait(false);
                }
                await base.DeleteAddressSpaceAsync(cancellationToken).ConfigureAwait(false);
            }

            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);
                if (disposing)
                {
                    Interlocked.Increment(ref m_disposeCount);
                    m_disposed.TrySetResult(true);
                }
            }

            private readonly TaskCompletionSource<bool> m_disposed =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private int m_readinessCount;
            private int m_readinessCompletedCount;
            private int m_deleteAddressSpaceCount;
            private int m_disposeCount;
            private int m_historyUpdateCount;
        }

        private sealed record StagedHistorianState(
            IHistorianProviderRegistry LocalRegistry,
            NodeId ExactNode,
            NodeId NamespaceNode,
            NodeId DefaultNode,
            IHistorianProvider? BaselineExactProvider,
            IHistorianProvider? BaselineNamespaceProvider,
            IHistorianProvider? BaselineDefaultProvider,
            LifecycleHistorianProvider NodeProvider,
            LifecycleHistorianProvider NamespaceProvider,
            LifecycleHistorianProvider DefaultProvider,
            BaseDataVariableState Variable,
            Delegate CaptureHandler);

        private sealed class LifecycleHistorianProvider :
            HistorianProviderBase,
            IHistorianBulkInsertProvider,
            IDisposable
        {
            public int DisposeCount { get; private set; }

            public ValueTask<ArrayOf<HistorianUpdateOutcome<DataValue>>> InsertBatchAsync(
                HistorianOperationContext context,
                ArrayOf<HistorianDataBatch> batch,
                CancellationToken cancellationToken)
            {
                return new ValueTask<ArrayOf<HistorianUpdateOutcome<DataValue>>>(
                    Array.Empty<HistorianUpdateOutcome<DataValue>>());
            }

            public void Dispose()
            {
                DisposeCount++;
            }
        }

        /// <summary>
        /// Public lifecycle entry points that own a newly ready generation.
        /// </summary>
        public enum ReadinessChange
        {
            Add,
            Reload,
            ShadowReload,
            ImmediateReload
        }

        /// <summary>
        /// Distinct removal and retired-generation destruction paths.
        /// </summary>
        public enum ReadinessCleanup
        {
            Remove,
            Reload,
            ShadowDrain
        }
    }
}
