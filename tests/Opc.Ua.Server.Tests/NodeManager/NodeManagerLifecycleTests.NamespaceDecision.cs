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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server.RuntimeNodeSet;
using static Opc.Ua.Server.Tests.NodeManager.LifecycleInspection;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class NodeManagerLifecycleTests
    {
        /// <summary>
        /// A candidate getter cannot synchronously re-enter serving namespace admission before the decision starts.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task NamespaceGetterRejectsServingReentryButKeepsStagedMutationsLocalAsync(bool unregister)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            TrackingLifecycleNodeManager? serving = null;
            LifecycleResult publicationResult1 = await CommitChangeAsync(
                m_server.NodeManagerLifecycle, LifecycleChange.Add(
                CreateTrackingNodeManagementFactory(
                kGeneration1Value, value => serving = value)), timeout.Token).ConfigureAwait(false);
            NodeManagerRegistration original = RequireRegistration(publicationResult1.Registrations[0]);
            AssertLifecycleValue(serving);
            IMasterNodeManager master = m_server.CurrentInstance.NodeManager;
            const string AliasUri = kModelNamespaceUri + ":GetterServingAlias";
            const string StagedAliasUri = kSecondModelNamespaceUri + ":GetterStagedAlias";
            master.RegisterNamespaceManager(AliasUri, serving);
            var candidate = CreateLifecycleNodeManager(kSecondModelNamespaceUri);
            bool committing = false;
            bool checkedReentry = false;
            ExecutionContext? publicationContext = null;
            candidate.SetupGet(value => value.NamespaceUris).Returns(() =>
            {
                if (committing)
                {
                    InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(() =>
                    {
                        if (unregister)
                        {
                            master.UnregisterNamespaceManager(AliasUri, serving);
                        }
                        else
                        {
                            master.RegisterNamespaceManager(AliasUri, serving);
                        }
                    });
                    Assert.That(failure!.Message, Does.Contain("lifecycle publication callback"));
                    master.RegisterNamespaceManager(StagedAliasUri, candidate.Object);
                    Assert.That(master.UnregisterNamespaceManager(StagedAliasUri, candidate.Object), Is.True);
                    checkedReentry = true;
                    publicationContext ??= ExecutionContext.Capture();
                }
                return new[] { kSecondModelNamespaceUri };
            });
            var factory = new CountingNodeManagerFactory(
                [kSecondModelNamespaceUri],
                (_, _, _) => new ValueTask<IAsyncNodeManager>(candidate.Object));
            NodeManagerRegistration? added = null;
            try
            {
                await using IPreparedChanges prepared =
                    await ((INodeManagerLifecycle)m_server.NodeManagerLifecycle).PrepareAsync(
                        [LifecycleChange.Add(factory)], timeout.Token).ConfigureAwait(false);
                committing = true;
                LifecycleResult result = await prepared.CommitAsync(static _ => default, timeout.Token)
                    .ConfigureAwait(false);
                added = LifecycleInspection.Registration(result.Registrations[0]);
                Assert.That(result.CleanupFailure, Is.Null);
                Assert.That(checkedReentry, Is.True);
                committing = false;
                Assert.That(publicationContext, Is.Not.Null);
                ExecutionContext.Run(publicationContext!, _ =>
                {
                    master.RegisterNamespaceManager(AliasUri, serving);
                    Assert.That(master.UnregisterNamespaceManager(AliasUri, serving), Is.True);
                }, null);
            }
            finally
            {
                committing = false;
                master.UnregisterNamespaceManager(AliasUri, serving);
                if (added is not null)
                {
                    await RemoveAndRetireAsync(m_server.NodeManagerLifecycle, added, timeout.Token)
                        .ConfigureAwait(false);
                }
                await RemoveAndRetireAsync(m_server.NodeManagerLifecycle, original, timeout.Token)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Conflicting namespace registration waits for the durable decision.
        /// </summary>
        [TestCase(false, false, false)]
        [TestCase(true, false, false)]
        [TestCase(false, true, false)]
        [TestCase(true, true, false)]
        [TestCase(false, false, true)]
        [TestCase(true, false, true)]
        [TestCase(false, true, true)]
        [TestCase(true, true, true)]
        public Task NamespaceRegistrationWaitsForDurableDecisionAsync(
            bool namespaceAlreadyExists,
            bool unregister,
            bool decisionFails) =>
            AssertNamespaceRegistrationDuringDurableDecisionAsync(
                namespaceAlreadyExists, unregister, decisionFails, conflicting: true);

        /// <summary>
        /// Unrelated namespace registration takes effect without waiting for the durable decision.
        /// </summary>
        [TestCase(false, false, false)]
        [TestCase(true, false, false)]
        [TestCase(false, true, false)]
        [TestCase(true, true, false)]
        [TestCase(false, false, true)]
        [TestCase(true, false, true)]
        [TestCase(false, true, true)]
        [TestCase(true, true, true)]
        public Task UnrelatedNamespaceRegistrationProceedsDuringDurableDecisionAsync(
            bool namespaceAlreadyExists,
            bool unregister,
            bool decisionFails) =>
            AssertNamespaceRegistrationDuringDurableDecisionAsync(
                namespaceAlreadyExists, unregister, decisionFails, conflicting: false);

        /// <summary>
        /// Asynchronous unrelated aliases take effect while the durable decision remains pending.
        /// </summary>
        [TestCase(false, false, false)]
        [TestCase(true, false, false)]
        [TestCase(false, true, false)]
        [TestCase(true, true, false)]
        [TestCase(false, false, true)]
        [TestCase(true, false, true)]
        [TestCase(false, true, true)]
        [TestCase(true, true, true)]
        public Task AsyncUnrelatedNamespaceRegistrationProceedsDuringDurableDecisionAsync(
            bool namespaceAlreadyExists,
            bool unregister,
            bool decisionFails) =>
            AssertNamespaceRegistrationDuringDurableDecisionAsync(
                namespaceAlreadyExists, unregister, decisionFails, conflicting: false, asynchronous: true);

        /// <summary>
        /// Cancelling a decision preserves completed unrelated aliases and releases conflicting registration waiters.
        /// </summary>
        [TestCase(false, false, false)]
        [TestCase(true, false, false)]
        [TestCase(false, true, false)]
        [TestCase(true, true, false)]
        [TestCase(false, false, true)]
        [TestCase(true, false, true)]
        [TestCase(false, true, true)]
        [TestCase(true, true, true)]
        public Task NamespaceRegistrationSurvivesDecisionCancellationAsync(
            bool unregister,
            bool asynchronous,
            bool conflicting) =>
            AssertNamespaceRegistrationDuringDurableDecisionAsync(
                namespaceAlreadyExists: unregister, unregister, decisionFails: false,
                conflicting, asynchronous, decisionCancelled: true);

        /// <summary>
        /// An unrelated namespace change before reservation still invalidates an already prepared batch.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task NamespaceMutationBeforeReservationRejectsStalePreparedBatchAsync(bool asynchronous)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            TrackingLifecycleNodeManager? manager = null;
            RequireRegistration(
                (await CommitChangeAsync(
                m_server.NodeManagerLifecycle, LifecycleChange.Add(
                CreateTrackingNodeManagementFactory(
                kGeneration1Value, value => manager = value)), timeout.Token).ConfigureAwait(false)).Registrations[0]);
            AssertLifecycleValue(manager);
            IServerInternal server = m_server.CurrentInstance;
            IMasterNodeManager master = server.NodeManager;
            var host = (IDynamicNodeManagerBatchHost)master;
            const string AliasUri = kModelNamespaceUri + ":BeforeReservationAlias";
            await using IPreparedChanges prepared =
                await ((INodeManagerLifecycle)m_server.NodeManagerLifecycle).PrepareAsync(
                    [LifecycleChange.Add(new RuntimeNodeSetNodeManagerFactory(
                        CreateOptions(
                            kSecondModelNamespaceUri, kGeneration2Value)))], timeout.Token).ConfigureAwait(false);
            int decisions = 0;
            try
            {
                if (asynchronous)
                {
                    await master.RegisterNamespaceManagerAsync(AliasUri, manager, timeout.Token).ConfigureAwait(false);
                }
                else
                {
                    master.RegisterNamespaceManager(AliasUri, manager);
                }
                NodeManagerRoutingTable.RoutingSnapshot changed = host.RoutingRevision;
                await Assert.ThrowsAsync<NodeManagerRoutingConflictException>(async () =>
                {
                    await prepared.CommitAsync(_ =>
                    {
                        decisions++;
                        return default;
                    }, timeout.Token).ConfigureAwait(false);
                }).ConfigureAwait(false);
                Assert.That(decisions, Is.Zero);
                Assert.That(prepared.IsCommitted, Is.False);
                Assert.That(host.RoutingRevision, Is.SameAs(changed));
                Assert.That(host.RoutingRevision.NamespaceManagers[server.NamespaceUris.GetIndex(AliasUri)],
                    Does.Contain(manager));
                Assert.That(server.NamespaceUris.GetIndex(kSecondModelNamespaceUri), Is.LessThan(0));
            }
            finally
            {
                master.UnregisterNamespaceManager(AliasUri, manager);
            }
        }

        /// <summary>
        /// Success and rejection retain multiple completed alias mutations, their order and their namespace indices.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task UnrelatedNamespaceAliasMutationsPreserveOrderAcrossDecisionAsync(
            bool decisionFails,
            bool asynchronous)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            TrackingLifecycleNodeManager? first = null;
            TrackingLifecycleNodeManager? second = null;
            RequireRegistration(
                (await CommitChangeAsync(
                m_server.NodeManagerLifecycle, LifecycleChange.Add(
                CreateTrackingNodeManagementFactory(
                kGeneration1Value, value => first = value)), timeout.Token).ConfigureAwait(false)).Registrations[0]);
            RequireRegistration(
                (await CommitChangeAsync(
                m_server.NodeManagerLifecycle, LifecycleChange.Add(CreateTrackingNodeManagementFactory(
                    kGeneration2Value, value => second = value, kSecondModelNamespaceUri)), timeout.Token)
                        .ConfigureAwait(false)).Registrations[0]);
            AssertLifecycleValue(first);
            AssertLifecycleValue(second);
            IServerInternal server = m_server.CurrentInstance;
            IMasterNodeManager master = server.NodeManager;
            var host = (IDynamicNodeManagerBatchHost)master;
            const string SharedUri = kModelNamespaceUri + ":SharedDecisionAlias";
            const string ReverseUri = SharedUri + ":Reverse";
            const string FirstUri = SharedUri + ":First";
            const string SecondUri = SharedUri + ":Second";
            const string CandidateUri = SharedUri + ":Candidate";
            master.RegisterNamespaceManager(SharedUri, first);
            master.RegisterNamespaceManager(SharedUri, second);
            master.RegisterNamespaceManager(ReverseUri, second);
            master.RegisterNamespaceManager(ReverseUri, first);
            int sharedIndex = server.NamespaceUris.GetIndex(SharedUri);
            int reverseIndex = server.NamespaceUris.GetIndex(ReverseUri);
            int reservedIndex = server.NamespaceUris.Count;
            NodeManagerRoutingTable.RoutingSnapshot before = host.RoutingRevision;
            var decisionFailure = new InvalidOperationException("Reject the alias merge's candidate.");
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            await using IPreparedChanges prepared =
                await ((INodeManagerLifecycle)m_server.NodeManagerLifecycle).PrepareAsync(
                    [LifecycleChange.Add(new RuntimeNodeSetNodeManagerFactory(
                        CreateOptions(CandidateUri, kGeneration2Value)))], timeout.Token).ConfigureAwait(false);
            Task<LifecycleResult> commit = prepared.CommitAsync(async token =>
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(token).ConfigureAwait(false);
                if (decisionFails)
                {
                    throw decisionFailure;
                }
            }, timeout.Token).AsTask();
            Task? mutations = null;
            bool observed = false;
            int firstIndex = -1;
            int secondIndex = -1;
            try
            {
                try
                {
                    await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                    mutations = RunWithoutExecutionContext(async () =>
                    {
                        if (asynchronous)
                        {
                            Assert.That(await master.UnregisterNamespaceManagerAsync(SharedUri, first, timeout.Token)
                                .ConfigureAwait(false), Is.True);
                            await master.RegisterNamespaceManagerAsync(SharedUri, first, timeout.Token)
                                .ConfigureAwait(false);
                            Assert.That(await master.UnregisterNamespaceManagerAsync(ReverseUri, first, timeout.Token)
                                .ConfigureAwait(false), Is.True);
                            await master.RegisterNamespaceManagerAsync(ReverseUri, first, timeout.Token)
                                .ConfigureAwait(false);
                            await master.RegisterNamespaceManagerAsync(FirstUri, first, timeout.Token)
                                .ConfigureAwait(false);
                            await master.RegisterNamespaceManagerAsync(SecondUri, second, timeout.Token)
                                .ConfigureAwait(false);
                        }
                        else
                        {
                            Assert.That(master.UnregisterNamespaceManager(SharedUri, first), Is.True);
                            master.RegisterNamespaceManager(SharedUri, first);
                            Assert.That(master.UnregisterNamespaceManager(ReverseUri, first), Is.True);
                            master.RegisterNamespaceManager(ReverseUri, first);
                            master.RegisterNamespaceManager(FirstUri, first);
                            master.RegisterNamespaceManager(SecondUri, second);
                        }
                    });
                    await mutations.WaitAsync(timeout.Token).ConfigureAwait(false);
                    Assert.That(commit.IsCompleted, Is.False);
                    Assert.That(host.RoutingRevision.NamespaceManagers[sharedIndex],
                        Is.EqualTo(new[] { second, first }));
                    Assert.That(host.RoutingRevision.NamespaceManagers[reverseIndex],
                        Is.EqualTo(new[] { second, first }),
                        "Preparation must not create a conflict by sorting an unrelated existing alias route.");
                    Assert.That(before.NamespaceManagers[sharedIndex], Is.EqualTo(new[] { first, second }),
                        "An existing reader's route image must not change when unrelated aliases are published.");
                    firstIndex = server.NamespaceUris.GetIndex(FirstUri);
                    secondIndex = server.NamespaceUris.GetIndex(SecondUri);
                    Assert.That(firstIndex, Is.GreaterThan(reservedIndex));
                    Assert.That(secondIndex, Is.GreaterThan(firstIndex));
                    Assert.That(server.NamespaceUris.GetIndex(CandidateUri), Is.EqualTo(reservedIndex));
                    Assert.That(host.RoutingRevision.NamespaceManagers.ContainsKey(reservedIndex), Is.False);
                    Assert.Throws<InvalidOperationException>(() =>
                        server.NamespaceUris.GetIndexOrAppend(SharedUri + ":Unreserved"),
                        "Permitted alias mutation must not release the string-table publication reservation.");
                }
                finally
                {
                    release.TrySetResult(true);
                }
                try
                {
                    LifecycleResult result = await commit.WaitAsync(timeout.Token).ConfigureAwait(false);
                    Assert.That(decisionFails, Is.False);
                    Assert.That(result.CleanupFailure, Is.Null);
                    Assert.That(host.RoutingRevision.NamespaceManagers[reservedIndex],
                        Does.Contain(RequireRegistration(result.Registrations[0]).NodeManager));
                    DataValue value = await ReadValueAsync(new NodeId(kValueNodeId, (ushort)reservedIndex))
                        .ConfigureAwait(false);
                    Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(value.WrappedValue.GetInt32(), Is.EqualTo(kGeneration2Value));
                }
                catch (InvalidOperationException failure) when (ReferenceEquals(failure, decisionFailure))
                {
                    Assert.That(decisionFails, Is.True);
                    Assert.That(host.RoutingRevision.NamespaceManagers.ContainsKey(reservedIndex), Is.False);
                    Assert.That(host.RoutingRevision.NodeManagers, Is.EqualTo(before.NodeManagers));
                }
                finally
                {
                    observed = true;
                }
                Assert.That(host.RoutingRevision.NamespaceManagers[sharedIndex],
                    Is.EqualTo(new[] { second, first }));
                Assert.That(host.RoutingRevision.NamespaceManagers[reverseIndex],
                    Is.EqualTo(new[] { second, first }));
                Assert.That(host.RoutingRevision.NamespaceManagers[firstIndex], Is.EqualTo(new[] { first }));
                Assert.That(host.RoutingRevision.NamespaceManagers[secondIndex], Is.EqualTo(new[] { second }));
                Assert.That(server.NamespaceUris.GetIndex(FirstUri), Is.EqualTo(firstIndex));
                Assert.That(server.NamespaceUris.GetIndex(SecondUri), Is.EqualTo(secondIndex));
                Assert.That(server.NamespaceUris.GetIndex(CandidateUri), Is.EqualTo(reservedIndex),
                    "Namespace indices already assigned to unrelated aliases cannot be rolled back on rejection.");
            }
            finally
            {
                release.TrySetResult(true);
                if (!observed)
                {
                    try
                    {
                        await commit.WaitAsync(timeout.Token).ConfigureAwait(false);
                    }
                    catch (InvalidOperationException failure) when (ReferenceEquals(failure, decisionFailure))
                    {
                        // The injected rejection is expected while draining the decision.
                    }
                }
                if (mutations is not null)
                {
                    await mutations.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
                master.UnregisterNamespaceManager(SharedUri, first);
                master.UnregisterNamespaceManager(SharedUri, second);
                master.UnregisterNamespaceManager(ReverseUri, first);
                master.UnregisterNamespaceManager(ReverseUri, second);
                master.UnregisterNamespaceManager(FirstUri, first);
                master.UnregisterNamespaceManager(SecondUri, second);
            }
        }

        /// <summary>
        /// A new alias for a retiring owner waits, without blocking unrelated survivor aliases on the same host.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task NamespaceAliasForRetiringOwnerWaitsWhileSurvivorAliasProceedsAsync(bool asynchronous)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            TrackingLifecycleNodeManager? retiring = null;
            TrackingLifecycleNodeManager? survivor = null;
            LifecycleResult publicationResult2 = await CommitChangeAsync(
                m_server.NodeManagerLifecycle, LifecycleChange.Add(
                CreateTrackingNodeManagementFactory(
                kGeneration1Value, value => retiring = value)), timeout.Token).ConfigureAwait(false);
            NodeManagerRegistration original = RequireRegistration(publicationResult2.Registrations[0]);
            RequireRegistration(
                (await CommitChangeAsync(
                m_server.NodeManagerLifecycle, LifecycleChange.Add(CreateTrackingNodeManagementFactory(
                    kGeneration2Value, value => survivor = value, kSecondModelNamespaceUri)), timeout.Token)
                        .ConfigureAwait(false)).Registrations[0]);
            AssertLifecycleValue(retiring);
            AssertLifecycleValue(survivor);
            IServerInternal server = m_server.CurrentInstance;
            IMasterNodeManager master = server.NodeManager;
            var host = (IDynamicNodeManagerBatchHost)master;
            const string RetiringAlias = kModelNamespaceUri + ":RetiringDecisionAlias";
            const string SurvivorAlias = kSecondModelNamespaceUri + ":SurvivingDecisionAlias";
            var decisionFailure = new InvalidOperationException("Keep the retiring owner after decision rejection.");
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            await using IPreparedChanges prepared =
                await ((INodeManagerLifecycle)m_server.NodeManagerLifecycle).PrepareAsync(
                    [LifecycleChange.Replace(
                        original.Slot,
                        CreateTrackingNodeManagementFactory(kGeneration2Value, _ =>
                        {
                        }))], timeout.Token).ConfigureAwait(false);
            Task<LifecycleResult> commit = prepared.CommitAsync(async token =>
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(token).ConfigureAwait(false);
                throw decisionFailure;
            }, timeout.Token).AsTask();
            Task? blocked = null;
            try
            {
                try
                {
                    await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                    blocked = RunWithoutExecutionContext(async () =>
                    {
                        started.TrySetResult(true);
                        if (asynchronous)
                        {
                            await master.RegisterNamespaceManagerAsync(RetiringAlias, retiring, timeout.Token)
                                .ConfigureAwait(false);
                        }
                        else
                        {
                            master.RegisterNamespaceManager(RetiringAlias, retiring);
                        }
                    });
                    await started.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                    await RunWithoutExecutionContext(async () =>
                    {
                        await master.RegisterNamespaceManagerAsync(SurvivorAlias, survivor, timeout.Token)
                            .ConfigureAwait(false);
                    }).WaitAsync(timeout.Token).ConfigureAwait(false);
                    Assert.That(blocked.IsCompleted, Is.False);
                    Assert.That(commit.IsCompleted, Is.False);
                    Assert.That(server.NamespaceUris.GetIndex(RetiringAlias), Is.LessThan(0));
                    Assert.That(host.RoutingRevision.NamespaceManagers[server.NamespaceUris.GetIndex(SurvivorAlias)],
                        Does.Contain(survivor));
                }
                finally
                {
                    release.TrySetResult(true);
                }
                InvalidOperationException? failure = await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await commit.WaitAsync(timeout.Token).ConfigureAwait(false)).ConfigureAwait(false);
                Assert.That(failure, Is.SameAs(decisionFailure));
                AssertLifecycleValue(blocked);
                await blocked.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(host.RoutingRevision.NamespaceManagers[server.NamespaceUris.GetIndex(RetiringAlias)],
                    Does.Contain(retiring));
                Assert.That(host.RoutingRevision.NamespaceManagers[server.NamespaceUris.GetIndex(SurvivorAlias)],
                    Does.Contain(survivor));
                Assert.That(m_server.NodeManagerLifecycle.Registrations.ToList(), Does.Contain(original));
            }
            finally
            {
                release.TrySetResult(true);
                try
                {
                    await commit.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (InvalidOperationException failure) when (ReferenceEquals(failure, decisionFailure))
                {
                    // The injected rejection is expected while draining the decision.
                }
                if (blocked is not null)
                {
                    await blocked.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
                master.UnregisterNamespaceManager(RetiringAlias, retiring);
                master.UnregisterNamespaceManager(SurvivorAlias, survivor);
            }
        }

        /// <summary>
        /// Cancelling one asynchronous alias wait neither releases the batch reservation nor cancels other waiters.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task CancellingConflictingNamespaceWaitKeepsDecisionReservedAsync(bool unregister)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
            TrackingLifecycleNodeManager? manager = null;
            RequireRegistration(
                (await CommitChangeAsync(
                m_server.NodeManagerLifecycle, LifecycleChange.Add(
                CreateTrackingNodeManagementFactory(
                kGeneration1Value, value => manager = value)), timeout.Token).ConfigureAwait(false)).Registrations[0]);
            AssertLifecycleValue(manager);
            IServerInternal server = m_server.CurrentInstance;
            IMasterNodeManager master = server.NodeManager;
            var host = (IDynamicNodeManagerBatchHost)master;
            const string AliasUri = kSecondModelNamespaceUri;
            const string UnrelatedUri = kModelNamespaceUri + ":CancelledWaitAlias";
            if (unregister)
            {
                master.RegisterNamespaceManager(AliasUri, manager);
            }
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            await using IPreparedChanges prepared =
                await ((INodeManagerLifecycle)m_server.NodeManagerLifecycle).PrepareAsync(
                    [LifecycleChange.Add(new RuntimeNodeSetNodeManagerFactory(
                        CreateOptions(
                            kSecondModelNamespaceUri, kGeneration2Value)))], timeout.Token).ConfigureAwait(false);
            Task<LifecycleResult> commit = prepared.CommitAsync(async token =>
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(token).ConfigureAwait(false);
            }, timeout.Token).AsTask();
            Task? cancelledWait = null;
            Task? otherWait = null;
            try
            {
                try
                {
                    await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                    cancelledWait = unregister
                        ? master.UnregisterNamespaceManagerAsync(AliasUri, manager, waitCancellation.Token).AsTask()
                        : master.RegisterNamespaceManagerAsync(AliasUri, manager, waitCancellation.Token).AsTask();
                    Assert.That(cancelledWait.IsCompleted, Is.False);
                    await waitCancellation.CancelAsync().ConfigureAwait(false);
                    await Assert.ThatAsync(
                        () => cancelledWait.WaitAsync(timeout.Token), Throws.InstanceOf<OperationCanceledException>())
                        .ConfigureAwait(false);
                    otherWait = unregister
                        ? master.UnregisterNamespaceManagerAsync(AliasUri, manager, timeout.Token).AsTask()
                        : master.RegisterNamespaceManagerAsync(AliasUri, manager, timeout.Token).AsTask();
                    Assert.That(otherWait.IsCompleted, Is.False);
                    await master.RegisterNamespaceManagerAsync(UnrelatedUri, manager, timeout.Token)
                        .ConfigureAwait(false);
                    Assert.That(commit.IsCompleted, Is.False);
                    Assert.That(otherWait.IsCompleted, Is.False);
                    Assert.That(host.RoutingRevision.NamespaceManagers[server.NamespaceUris.GetIndex(UnrelatedUri)],
                        Does.Contain(manager));
                }
                finally
                {
                    release.TrySetResult(true);
                }
                LifecycleResult result = await commit.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(result.CleanupFailure, Is.Null);
                AssertLifecycleValue(otherWait);
                await otherWait.WaitAsync(timeout.Token).ConfigureAwait(false);
                int namespaceIndex = server.NamespaceUris.GetIndex(AliasUri);
                Assert.That(host.RoutingRevision.NamespaceManagers[namespaceIndex],
                    Does.Contain(RequireRegistration(result.Registrations[0]).NodeManager));
                Assert.That(host.RoutingRevision.NamespaceManagers[namespaceIndex].Contains(manager),
                    Is.EqualTo(!unregister));
            }
            finally
            {
                release.TrySetResult(true);
                await commit.WaitAsync(timeout.Token).ConfigureAwait(false);
                if (cancelledWait is not null)
                {
                    try
                    {
                        await cancelledWait.WaitAsync(timeout.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (waitCancellation.IsCancellationRequested)
                    {
                        // The first wait was cancelled independently of the decision.
                    }
                }
                if (otherWait is not null)
                {
                    await otherWait.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
                master.UnregisterNamespaceManager(AliasUri, manager);
                master.UnregisterNamespaceManager(UnrelatedUri, manager);
            }
        }

        /// <summary>
        /// Namespace mutation from a decision rejects its own reservation instead of blocking the decision.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task NamespaceMutationFromDurableDecisionIsRejectedAsync(bool unregister)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            TrackingLifecycleNodeManager? manager = null;
            RequireRegistration(
                (await CommitChangeAsync(
                m_server.NodeManagerLifecycle, LifecycleChange.Add(
                CreateTrackingNodeManagementFactory(
                kGeneration1Value, value => manager = value)), timeout.Token).ConfigureAwait(false)).Registrations[0]);
            AssertLifecycleValue(manager);
            IMasterNodeManager master = m_server.CurrentInstance.NodeManager;
            const string AliasUri = kModelNamespaceUri + ":OwnDecisionAlias";
            await using IPreparedChanges prepared =
                await ((INodeManagerLifecycle)m_server.NodeManagerLifecycle).PrepareAsync(
                    [LifecycleChange.Add(new RuntimeNodeSetNodeManagerFactory(
                        CreateOptions(kSecondModelNamespaceUri, kGeneration2Value)))],
                    timeout.Token).ConfigureAwait(false);
            LifecycleResult result = await prepared.CommitAsync(_ =>
            {
                InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(() =>
                {
                    if (unregister)
                    {
                        master.UnregisterNamespaceManager(AliasUri, manager);
                    }
                    else
                    {
                        master.RegisterNamespaceManager(AliasUri, manager);
                    }
                });
                Assert.That(failure!.Message, Does.Contain("durable decision callback"));
                return default;
            }, timeout.Token).ConfigureAwait(false);
            Assert.That(result.CleanupFailure, Is.Null);
            master.RegisterNamespaceManager(AliasUri, manager);
            Assert.That(master.UnregisterNamespaceManager(AliasUri, manager), Is.True);
        }

        /// <summary>
        /// A durable decision cannot add or remove staged aliases after the candidate's routes were reserved.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task StagedNamespaceMutationFromDurableDecisionIsRejectedAsync(
            bool unregister,
            bool asynchronous)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            IMasterNodeManager master = m_server.CurrentInstance.NodeManager;
            const string AliasUri = kSecondModelNamespaceUri + ":StagedDecisionAlias";
            var candidate = CreateLifecycleNodeManager(kSecondModelNamespaceUri);
            var factory = new CountingNodeManagerFactory(
                [kSecondModelNamespaceUri],
                (_, _, _) => new ValueTask<IAsyncNodeManager>(candidate.Object));
            await using IPreparedChanges prepared =
                await ((INodeManagerLifecycle)m_server.NodeManagerLifecycle).PrepareAsync(
                    [LifecycleChange.Add(factory)], timeout.Token).ConfigureAwait(false);
            LifecycleResult result = await prepared.CommitAsync(async _ =>
            {
                InvalidOperationException? failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                {
                    if (asynchronous && unregister)
                    {
                        await master.UnregisterNamespaceManagerAsync(AliasUri, candidate.Object, timeout.Token)
                            .ConfigureAwait(false);
                    }
                    else if (asynchronous)
                    {
                        await master.RegisterNamespaceManagerAsync(AliasUri, candidate.Object, timeout.Token)
                            .ConfigureAwait(false);
                    }
                    else if (unregister)
                    {
                        master.UnregisterNamespaceManager(AliasUri, candidate.Object);
                    }
                    else
                    {
                        master.RegisterNamespaceManager(AliasUri, candidate.Object);
                    }
                }).ConfigureAwait(false);
                Assert.That(failure!.Message, Does.Contain("durable decision callback"));
            }, timeout.Token).ConfigureAwait(false);
            Assert.That(result.CleanupFailure, Is.Null);
            Assert.That(m_server.CurrentInstance.NamespaceUris.GetIndex(AliasUri), Is.LessThan(0));
            Assert.That(RequireRegistration(result.Registrations[0]).NodeManager, Is.SameAs(candidate.Object));
        }

        /// <summary>
        /// Node-management writes reject their own durable decision instead of waiting for that decision to finish.
        /// </summary>
        [TestCase(RequestType.AddNodes)]
        [TestCase(RequestType.DeleteNodes)]
        [TestCase(RequestType.AddReferences)]
        [TestCase(RequestType.DeleteReferences)]
        public async Task NodeManagementFromDurableDecisionIsRejectedAsync(RequestType requestType)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            IMasterNodeManager master = m_server.CurrentInstance.NodeManager;
            using var context = new OperationContext(
                new RequestHeader(),
                null,
                requestType,
                RequestLifetime.None);
            await using IPreparedChanges prepared =
                await ((INodeManagerLifecycle)m_server.NodeManagerLifecycle).PrepareAsync(
                    [LifecycleChange.Add(new RuntimeNodeSetNodeManagerFactory(
                        CreateOptions(kSecondModelNamespaceUri, kGeneration2Value)))],
                    timeout.Token).ConfigureAwait(false);

            ExecutionContext? decisionContext = null;
            LifecycleResult result = await prepared.CommitAsync(async _ =>
            {
                decisionContext = ExecutionContext.Capture();
                Task nodeManagement = requestType switch
                {
                    RequestType.AddNodes => master.AddNodesAsync(
                        context, [], CancellationToken.None).AsTask(),
                    RequestType.DeleteNodes => master.DeleteNodesAsync(
                        context, [], CancellationToken.None).AsTask(),
                    RequestType.AddReferences => master.AddReferencesAsync(
                        context, [], CancellationToken.None).AsTask(),
                    RequestType.DeleteReferences => master.DeleteReferencesAsync(
                        context, [], CancellationToken.None).AsTask(),
                    _ => throw new AssertionException($"Unexpected request type {requestType}.")
                };
                InvalidOperationException? failure = await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await nodeManagement.ConfigureAwait(false)).ConfigureAwait(false);
                Assert.That(failure!.Message, Does.Contain("durable decision callback"));
            }, timeout.Token).ConfigureAwait(false);

            Assert.That(result.CleanupFailure, Is.Null);
            Assert.That(decisionContext, Is.Not.Null);
            Task? resumed = null;
            ExecutionContext.Run(decisionContext!, _ =>
            {
                resumed = requestType switch
                {
                    RequestType.AddNodes => master.AddNodesAsync(context, [], CancellationToken.None).AsTask(),
                    RequestType.DeleteNodes => master.DeleteNodesAsync(context, [], CancellationToken.None).AsTask(),
                    RequestType.AddReferences => master.AddReferencesAsync(context, [], CancellationToken.None)
                        .AsTask(),
                    RequestType.DeleteReferences => master.DeleteReferencesAsync(context, [], CancellationToken.None)
                        .AsTask(),
                    _ => throw new AssertionException($"Unexpected request type {requestType}.")
                };
            }, null);
            Assert.That(resumed, Is.Not.Null);
            await resumed!.WaitAsync(timeout.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// Checks namespace admission and routing before and after a held durable decision.
        /// </summary>
        private async Task AssertNamespaceRegistrationDuringDurableDecisionAsync(
            bool namespaceAlreadyExists,
            bool unregister,
            bool decisionFails,
            bool conflicting,
            bool asynchronous = false,
            bool decisionCancelled = false)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var decisionCancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
            TrackingLifecycleNodeManager? manager = null;
            RequireRegistration(
                (await CommitChangeAsync(
                m_server.NodeManagerLifecycle, LifecycleChange.Add(
                CreateTrackingNodeManagementFactory(
                kGeneration1Value, value => manager = value)), timeout.Token).ConfigureAwait(false)).Registrations[0]);
            AssertLifecycleValue(manager);
            IServerInternal server = m_server.CurrentInstance;
            IMasterNodeManager master = server.NodeManager;
            var host = (IDynamicNodeManagerBatchHost)master;
            string aliasUri = conflicting ? kSecondModelNamespaceUri : kModelNamespaceUri + ":DecisionAlias";
            if (namespaceAlreadyExists)
            {
                server.NamespaceUris.GetIndexOrAppend(aliasUri);
                if (unregister)
                {
                    master.RegisterNamespaceManager(aliasUri, manager);
                }
            }
            else
            {
                Assert.That(server.NamespaceUris.GetIndex(aliasUri), Is.LessThan(0));
            }
            NodeManagerRoutingTable.RoutingSnapshot before = host.RoutingRevision;
            int reservedNamespaceIndex = server.NamespaceUris.GetIndex(kSecondModelNamespaceUri);
            bool candidateNamespaceExists = reservedNamespaceIndex >= 0;
            if (!candidateNamespaceExists)
            {
                reservedNamespaceIndex = server.NamespaceUris.Count;
            }

            var decisionFailure = new InvalidOperationException("The durable decision was rejected.");
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            await using IPreparedChanges prepared =
                await ((INodeManagerLifecycle)m_server.NodeManagerLifecycle).PrepareAsync(
                    [LifecycleChange.Add(new RuntimeNodeSetNodeManagerFactory(
                        CreateOptions(kSecondModelNamespaceUri, kGeneration2Value)))],
                    timeout.Token).ConfigureAwait(false);
            Task<LifecycleResult> commit = prepared.CommitAsync(async token =>
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(token).ConfigureAwait(false);
                if (decisionFails)
                {
                    throw decisionFailure;
                }
            }, decisionCancellation.Token).AsTask();

            Task<InvalidOperationException?>? registration = null;
            NodeManagerRegistration? published = null;
            bool unregistered = false;
            bool completedBeforeRelease = false;
            bool decisionWasPending = false;
            bool decisionObserved = false;
            bool earlyRouteContainsManager = false;
            int earlyNamespaceIndex = -1;

            void AssertRegistrationState()
            {
                int namespaceIndex = server.NamespaceUris.GetIndex(aliasUri);
                if (unregister)
                {
                    Assert.That(unregistered, Is.EqualTo(namespaceAlreadyExists));
                    Assert.That(namespaceIndex < 0 ||
                        !host.RoutingRevision.NamespaceManagers.TryGetValue(namespaceIndex, out var managers) ||
                        !managers.Contains(manager), Is.True);
                }
                else
                {
                    Assert.That(namespaceIndex, Is.GreaterThanOrEqualTo(0));
                    Assert.That(host.RoutingRevision.NamespaceManagers[namespaceIndex], Does.Contain(manager));
                }
            }

            try
            {
                try
                {
                    await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                    registration = RunWithoutExecutionContext(async () =>
                    {
                        started.TrySetResult(true);
                        try
                        {
                            if (unregister)
                            {
                                unregistered = asynchronous
                                    ? await master.UnregisterNamespaceManagerAsync(aliasUri, manager, timeout.Token)
                                        .ConfigureAwait(false)
                                    : master.UnregisterNamespaceManager(aliasUri, manager);
                            }
                            else if (asynchronous)
                            {
                                await master.RegisterNamespaceManagerAsync(aliasUri, manager, timeout.Token)
                                    .ConfigureAwait(false);
                            }
                            else
                            {
                                master.RegisterNamespaceManager(aliasUri, manager);
                            }
                            return (InvalidOperationException?)null;
                        }
                        catch (InvalidOperationException conflict)
                        {
                            return conflict;
                        }
                    });
                    await started.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                    await Task.WhenAny(registration, Task.Delay(TimeSpan.FromSeconds(1), timeout.Token))
                        .ConfigureAwait(false);
                    completedBeforeRelease = registration.IsCompleted;
                    decisionWasPending = !commit.IsCompleted;
                    if (!conflicting && completedBeforeRelease)
                    {
                        earlyNamespaceIndex = server.NamespaceUris.GetIndex(aliasUri);
                        earlyRouteContainsManager = earlyNamespaceIndex >= 0 &&
                            host.RoutingRevision.NamespaceManagers.TryGetValue(earlyNamespaceIndex, out var managers) &&
                            managers.Contains(manager);
                    }
                    Assert.That(host.RoutingRevision.NodeManagers, Is.EqualTo(before.NodeManagers),
                        "An alias mutation must not partially publish candidate managers.");
                    Assert.That(host.RoutingRevision.NamespaceManagers.TryGetValue(
                        reservedNamespaceIndex, out var candidateRoutes) &&
                        candidateRoutes.Any(owner =>
                            !ReferenceEquals(owner, manager)), Is.False,
                        "The durable decision must finish before candidate routes become visible.");
                    bool materializedSuffix = !conflicting && !unregister && !namespaceAlreadyExists;
                    Assert.That(server.NamespaceUris.GetIndex(kSecondModelNamespaceUri),
                        Is.EqualTo(candidateNamespaceExists || materializedSuffix ? reservedNamespaceIndex : -1));
                    if (decisionCancelled)
                    {
                        await decisionCancellation.CancelAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    release.TrySetResult(true);
                }

                try
                {
                    LifecycleResult result = await commit.WaitAsync(timeout.Token).ConfigureAwait(false);
                    Assert.That(decisionFails, Is.False);
                    Assert.That(decisionCancelled, Is.False);
                    Assert.That(result.CleanupFailure, Is.Null);
                    published = LifecycleInspection.Registration(result.Registrations[0]);
                }
                catch (InvalidOperationException decisionError) when (ReferenceEquals(decisionError, decisionFailure))
                {
                    Assert.That(decisionFails, Is.True);
                }
                catch (OperationCanceledException cancelled) when (decisionCancelled)
                {
                    Assert.That(cancelled.CancellationToken, Is.EqualTo(decisionCancellation.Token));
                    Assert.That(prepared.IsCommitted, Is.False);
                }
                finally
                {
                    decisionObserved = true;
                }
                AssertLifecycleValue(registration);
                InvalidOperationException? failure = await registration.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(decisionWasPending, Is.True);
                Assert.That(completedBeforeRelease, Is.EqualTo(!conflicting),
                    "Only a conflicting namespace registration should wait for the durable decision.");
                Assert.That(failure, Is.Null, "Registration must not fail with a pending-publication exception.");
                AssertRegistrationState();
                if (!conflicting)
                {
                    Assert.That(earlyRouteContainsManager, Is.EqualTo(!unregister));
                    Assert.That(server.NamespaceUris.GetIndex(aliasUri), Is.EqualTo(earlyNamespaceIndex));
                }
                if (!decisionFails && !decisionCancelled)
                {
                    AssertLifecycleValue(published);
                    int publishedNamespaceIndex = server.NamespaceUris.GetIndex(kSecondModelNamespaceUri);
                    Assert.That(publishedNamespaceIndex, Is.EqualTo(reservedNamespaceIndex));
                    Assert.That(host.RoutingRevision.NamespaceManagers[publishedNamespaceIndex],
                        Does.Contain(published.NodeManager));
                    DataValue value = await ReadValueAsync(new NodeId(kValueNodeId, (ushort)publishedNamespaceIndex))
                        .ConfigureAwait(false);
                    Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(value.WrappedValue.GetInt32(), Is.EqualTo(kGeneration2Value));
                }
                else
                {
                    Assert.That(host.RoutingRevision.NodeManagers, Is.EqualTo(before.NodeManagers));
                }
            }
            finally
            {
                release.TrySetResult(true);
                using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                try
                {
                    if (!decisionObserved || !commit.IsCompleted)
                    {
                        try
                        {
                            await commit.WaitAsync(cleanupTimeout.Token).ConfigureAwait(false);
                        }
                        catch (InvalidOperationException decisionError)
                            when (ReferenceEquals(decisionError, decisionFailure))
                        {
                            // The injected rejection is expected while draining the decision.
                        }
                        catch (OperationCanceledException) when (decisionCancelled)
                        {
                            // The injected cancellation is expected while draining the decision.
                        }
                    }
                }
                finally
                {
                    if (registration is not null)
                    {
                        InvalidOperationException? cleanupFailure =
                            await registration.WaitAsync(cleanupTimeout.Token).ConfigureAwait(false);
                        if (!unregister && cleanupFailure is null)
                        {
                            Assert.That(master.UnregisterNamespaceManager(aliasUri, manager), Is.True);
                        }
                    }
                }
            }
        }
    }
}
