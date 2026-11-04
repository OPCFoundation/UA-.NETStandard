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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Server.RuntimeNodeSet;
using Opc.Ua.Tests;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    /// <summary>
    /// Contains tests for WoT atomic publication native.
    /// </summary>
    public sealed partial class WotAtomicPublicationNativeTests
    {
        /// <summary>
        /// Verifies that committed warnings publish before changed and retain the exact next owners.
        /// </summary>
        /// <param name="storeWarning">The store-warning flag for this test case.</param>
        /// <param name="observerWarning">The observer-warning flag for this test case.</param>
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        [Platform("Win")]
        public async Task CommittedWarningsPublishBeforeChangedAndRetainTheExactNextOwners(
            bool storeWarning, bool observerWarning)
        {
            const string requestId = "handoff-warning";
            const string observerMessage = "The handoff Changed observer failed.";
            HandoffProbe probe = ObserveHandoff();
            WotResource first = await AddAsync("first").ConfigureAwait(false);
            WotResource second = await AddAsync("second").ConfigureAwait(false);
            long before = m_registry.Current.Generation;
            uint observedGeneration = 0;
            WotCommittedPublicationState? observedPublication = null;
            WotRegistrySnapshot? observedRegistry = null;
            int notifications = 0;
            int remainingNotifications = 0;
            EventHandler<WotRegistryChangedEventArgs> handler = (_, change) =>
            {
                notifications++;
                observedGeneration = m_coordinator.Generation;
                observedPublication = m_coordinator.CommittedPublication;
                observedRegistry = change.Current;
                if (observerWarning)
                {
                    throw new InvalidOperationException(observerMessage);
                }
            };
            EventHandler<WotRegistryChangedEventArgs> remaining = (_, _) => remainingNotifications++;
            m_registry.Changed += handler;
            m_registry.Changed += remaining;
            m_committedWarning = storeWarning;
            WotRefreshResult result;
            try
            {
                result = await m_coordinator.RefreshAsync(HandoffRequest(requestId)).ConfigureAwait(false);
            }
            finally
            {
                m_registry.Changed -= handler;
                m_registry.Changed -= remaining;
                m_committedWarning = false;
            }

            Assert.That(notifications, Is.EqualTo(1));
            Assert.That(remainingNotifications, Is.EqualTo(1));
            Assert.That(observedGeneration, Is.EqualTo(1u));
            Assert.That(observedPublication, Is.SameAs(m_coordinator.CommittedPublication));
            Assert.That(observedPublication!.RegistrySnapshot, Is.SameAs(observedRegistry));
            Assert.That(observedRegistry, Is.SameAs(m_registry.Current));
            Assert.That(m_registry.Current.Generation, Is.EqualTo(before + 1));
            Assert.That(result.Summary.Outcome, Is.EqualTo(WoTOutcomeEnum.Warning));
            Assert.That(result.Summary.Total, Is.EqualTo(2u));
            Assert.That(result.Summary.Succeeded, Is.EqualTo(2u));
            Assert.That(result.Summary.Failed, Is.Zero);
            Assert.That(result.Summary.Retired, Is.Zero);
            Assert.That(result.Results, Has.Length.EqualTo(2));
            Assert.That(result.Results.All(row => row.Generation == 1 && row.VersionId == "v1" &&
                row.Outcome == WoTOutcomeEnum.Warning), Is.True);
            if (observerWarning)
            {
                Assert.That(result.Results.All(row => row.Message!.Contains(observerMessage, StringComparison.Ordinal)),
                    Is.True);
            }
            if (storeWarning)
            {
                Assert.That(result.Results.All(row => row.Message!.Contains(
                    "uncertain final durability", StringComparison.Ordinal)), Is.True);
            }
            AssertHandoffEvents(requestId, 1, first, second);
            Assert.That(probe.DecisionCount, Is.EqualTo(1));
            Assert.That(probe.AcceptedCount, Is.EqualTo(1));
            Assert.That(probe.PublicationCount, Is.EqualTo(1));
            Assert.That(probe.DisposalCount, Is.EqualTo(1));
            Assert.That(probe.RuntimeDisposedCount, Is.Zero);
            await AssertLiveImageAsync(1, first, second).ConfigureAwait(false);

            WotResource unchanged = m_registry.Current.FindResource(second.GroupId, second.ResourceId)!;
            WotRefreshRequest nextRequest = HandoffRequest("handoff-warning-next", 1, force: true);
            nextRequest.Selection =
            [
                new WoTResourceSelectorDataType
                {
                    GroupId = first.GroupId,
                    ResourceId = first.ResourceId,
                    VersionId = "v1",
                    Kind = first.Kind
                }
            ];
            WotRefreshResult next = await m_coordinator.RefreshAsync(nextRequest).ConfigureAwait(false);

            Assert.That(next.NewGeneration, Is.EqualTo(2u));
            Assert.That(m_registry.Current.Generation, Is.EqualTo(before + 2));
            Assert.That(probe.DecisionCount, Is.EqualTo(2));
            Assert.That(probe.AcceptedCount, Is.EqualTo(2));
            Assert.That(probe.PublicationCount, Is.EqualTo(2));
            Assert.That(probe.DisposalCount, Is.EqualTo(2));
            Assert.That(probe.Changes[1].Count, Is.EqualTo(1));
            foreach (WotProjectionChange change in probe.Changes[1])
            {
                WotProjectionHandle previous = probe.Publications[0].Projections.ToList().Single(
                    handle => handle.ClosureKey == change.Document!.ClosureKey);
                Assert.That(change.Current, Is.SameAs(previous));
            }
            Assert.That(next.Results, Has.Length.EqualTo(1));
            Assert.That(m_registry.Current.AllResources().Count(), Is.EqualTo(2));
            Assert.That(m_registry.Current.FindResource(second.GroupId, second.ResourceId), Is.SameAs(unchanged));
            Assert.That((await ReadNodeClassAsync(Root(second)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.Good));
            AssertHandoffEvents("handoff-warning-next", 2, first);
            await AssertLiveImageAsync(2, first).ConfigureAwait(false);
        }

        /// <summary>
        /// Verifies that confirmed noncommit retains the exact live owner and releases candidates.
        /// </summary>
        [Test]
        [Platform("Win")]
        public async Task ConfirmedNoncommitRetainsTheExactLiveOwnerAndReleasesCandidates()
        {
            HandoffProbe probe = ObserveHandoff();
            WotResource first = await AddAsync("first").ConfigureAwait(false);
            await m_coordinator.RefreshAsync(HandoffRequest("handoff-initial")).ConfigureAwait(false);
            await UpdateHandoffResourceAsync(first).ConfigureAwait(false);
            WotResource second = await AddAsync("second").ConfigureAwait(false);
            WotRegistrySnapshot before = m_registry.Current;
            WotCommittedPublicationState previous = m_coordinator.CommittedPublication;
            ArrayOf<NodeManagerRegistration> registrations = m_server.NodeManagerLifecycle.Registrations;
            m_events.Clear();
            m_failDecision = true;

            await Assert.ThatAsync(
                async () => await m_coordinator.RefreshAsync(HandoffRequest("handoff-noncommit", 1))
                    .ConfigureAwait(false),
                Throws.TypeOf<WotRegistryCommitNotCommittedException>()).ConfigureAwait(false);

            Assert.That(m_registry.Current, Is.SameAs(before));
            Assert.That(m_coordinator.CommittedPublication, Is.SameAs(previous));
            Assert.That(m_coordinator.Generation, Is.EqualTo(1u));
            Assert.That(m_server.NodeManagerLifecycle.Registrations, Is.EqualTo(registrations));
            Assert.That(probe.Publications[1].IsCommitted, Is.False);
            Assert.That(probe.DecisionCount, Is.EqualTo(2));
            Assert.That(probe.AcceptedCount, Is.EqualTo(1));
            Assert.That(probe.PublicationCount, Is.EqualTo(1));
            Assert.That(probe.RuntimeCreatedCount, Is.EqualTo(3));
            Assert.That(probe.RuntimeDisposedCount, Is.EqualTo(2));
            Assert.That(m_events, Is.Empty);
            Assert.That((await ReadNodeClassAsync(Root(first)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.Good));
            Assert.That((await ReadNodeClassAsync(NewHandoffNode(first)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            Assert.That((await ReadNodeClassAsync(Root(second)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            m_failDecision = false;

            WotRefreshResult retry = await m_coordinator.RefreshAsync(
                HandoffRequest("handoff-noncommit-retry", 1)).ConfigureAwait(false);

            Assert.That(retry.NewGeneration, Is.EqualTo(2u));
            Assert.That(probe.DecisionCount, Is.EqualTo(3));
            Assert.That(probe.AcceptedCount, Is.EqualTo(2));
            Assert.That(probe.PublicationCount, Is.EqualTo(2));
            Assert.That(probe.Changes[2].ToList().Single(change => change.Current is not null).Current,
                Is.SameAs(probe.Publications[0].Projections.ToList().Single()));
            Assert.That((await ReadNodeClassAsync(NewHandoffNode(first)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.Good));
            await AssertLiveImageAsync(2, first, second).ConfigureAwait(false);
        }

        /// <summary>
        /// Verifies that cancellation before decision disposes candidates and admits retry.
        /// </summary>
        [Test]
        [Platform("Win")]
        public async Task CancellationBeforeDecisionDisposesCandidatesAndAdmitsRetry()
        {
            HandoffProbe probe = ObserveHandoff();
            WotResource first = await AddAsync("first").ConfigureAwait(false);
            WotResource second = await AddAsync("second").ConfigureAwait(false);
            WotRegistrySnapshot before = m_registry.Current;
            WotCommittedPublicationState previous = m_coordinator.CommittedPublication;
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var resume = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var cancellation = new CancellationTokenSource();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            probe.BeforeDecisionAsync = async _ =>
            {
                entered.TrySetResult(true);
                await resume.Task.ConfigureAwait(false);
            };
            Task<WotRefreshResult> pending = m_coordinator.RefreshAsync(
                HandoffRequest("handoff-cancel-before"), cancellation.Token).AsTask();
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(m_registry.Current, Is.SameAs(before));
                Assert.That(m_coordinator.CommittedPublication, Is.SameAs(previous));
                Assert.That(probe.RuntimeCreatedCount, Is.EqualTo(2));
                Assert.That(probe.RuntimeDisposedCount, Is.Zero);
                cancellation.Cancel();
            }
            finally
            {
                resume.TrySetResult(true);
            }
            await Assert.ThatAsync(
                () => pending.WaitAsync(timeout.Token),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);

            Assert.That(m_registry.Current, Is.SameAs(before));
            Assert.That(m_coordinator.CommittedPublication, Is.SameAs(previous));
            Assert.That(probe.DecisionCount, Is.Zero);
            Assert.That(probe.AcceptedCount, Is.Zero);
            Assert.That(probe.PublicationCount, Is.Zero);
            Assert.That(probe.RuntimeDisposedCount, Is.EqualTo(2));
            Assert.That(probe.DisposalCount, Is.EqualTo(1));
            Assert.That(m_events, Is.Empty);
            Assert.That((await ReadNodeClassAsync(Root(first)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            Assert.That((await ReadNodeClassAsync(Root(second)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            probe.BeforeDecisionAsync = null;

            WotRefreshResult retry = await m_coordinator.RefreshAsync(HandoffRequest("handoff-cancel-retry"))
                .ConfigureAwait(false);

            Assert.That(retry.NewGeneration, Is.EqualTo(1u));
            Assert.That(m_registry.Current.Generation, Is.EqualTo(before.Generation + 1));
            Assert.That(probe.DecisionCount, Is.EqualTo(1));
            Assert.That(probe.PublicationCount, Is.EqualTo(1));
            await AssertLiveImageAsync(1, first, second).ConfigureAwait(false);
        }

        /// <summary>
        /// Verifies that cancellation after decision publishes once and admits the next generation.
        /// </summary>
        /// <param name="storeWarning">The store-warning flag for this test case.</param>
        [TestCase(false)]
        [TestCase(true)]
        [Platform("Win")]
        public async Task CancellationAfterDecisionPublishesOnceAndAdmitsTheNextGeneration(bool storeWarning)
        {
            const string requestId = "handoff-cancel-after";
            HandoffProbe probe = ObserveHandoff();
            WotResource first = await AddAsync("first").ConfigureAwait(false);
            WotResource second = await AddAsync("second").ConfigureAwait(false);
            WotRegistrySnapshot before = m_registry.Current;
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var resume = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var cancellation = new CancellationTokenSource();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            probe.AfterDecisionAsync = async _ =>
            {
                entered.TrySetResult(true);
                await resume.Task.ConfigureAwait(false);
            };
            m_committedWarning = storeWarning;
            Task<WotRefreshResult> pending = m_coordinator.RefreshAsync(
                HandoffRequest(requestId), cancellation.Token).AsTask();
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(probe.DecisionCount, Is.EqualTo(1));
                Assert.That(probe.AcceptedCount, Is.EqualTo(1));
                Assert.That(probe.PublicationCount, Is.Zero);
                Assert.That(m_registry.Current, Is.SameAs(before));
                Assert.That(m_coordinator.Generation, Is.Zero);
                Assert.That(m_events, Is.Empty);
                cancellation.Cancel();
            }
            finally
            {
                resume.TrySetResult(true);
            }
            WotRefreshResult result = await pending.WaitAsync(timeout.Token).ConfigureAwait(false);

            Assert.That(result.NewGeneration, Is.EqualTo(1u));
            Assert.That(result.Summary.Generation, Is.EqualTo(1u));
            Assert.That(m_registry.Current.Generation, Is.EqualTo(before.Generation + 1));
            Assert.That(probe.PublicationCount, Is.EqualTo(1));
            Assert.That(probe.DisposalCount, Is.EqualTo(1));
            Assert.That(probe.RuntimeDisposedCount, Is.Zero);
            if (storeWarning)
            {
                Assert.That(result.Summary.Outcome, Is.EqualTo(WoTOutcomeEnum.Warning));
            }
            AssertHandoffEvents(requestId, 1, first, second);
            await AssertLiveImageAsync(1, first, second).ConfigureAwait(false);
            probe.AfterDecisionAsync = null;
            m_committedWarning = false;

            WotRefreshResult next = await m_coordinator.RefreshAsync(
                HandoffRequest("handoff-cancel-after-next", 1, force: true)).ConfigureAwait(false);

            Assert.That(next.NewGeneration, Is.EqualTo(2u));
            Assert.That(probe.DecisionCount, Is.EqualTo(2));
            Assert.That(probe.AcceptedCount, Is.EqualTo(2));
            Assert.That(probe.PublicationCount, Is.EqualTo(2));
            AssertHandoffEvents("handoff-cancel-after-next", 2, first, second);
        }

        /// <summary>
        /// Verifies that indeterminate decision retains live owners and blocks the next decision.
        /// </summary>
        [Test]
        [Platform("Win")]
        public async Task IndeterminateDecisionRetainsLiveOwnersAndBlocksTheNextDecision()
        {
            HandoffProbe probe = ObserveHandoff();
            WotResource first = await AddAsync("first").ConfigureAwait(false);
            await m_coordinator.RefreshAsync(HandoffRequest("handoff-known")).ConfigureAwait(false);
            await UpdateHandoffResourceAsync(first).ConfigureAwait(false);
            WotRegistrySnapshot before = m_registry.Current;
            WotCommittedPublicationState previous = m_coordinator.CommittedPublication;
            ArrayOf<NodeManagerRegistration> registrations = m_server.NodeManagerLifecycle.Registrations;
            m_events.Clear();
            m_indeterminateDecision = true;

            await Assert.ThatAsync(
                async () => await m_coordinator.RefreshAsync(HandoffRequest("handoff-indeterminate", 1))
                    .ConfigureAwait(false),
                Throws.TypeOf<WotRegistryCommitIndeterminateException>()).ConfigureAwait(false);

            Assert.That(m_registry.Current, Is.SameAs(before));
            Assert.That(m_coordinator.CommittedPublication, Is.SameAs(previous));
            Assert.That(m_coordinator.Generation, Is.EqualTo(1u));
            Assert.That(m_server.NodeManagerLifecycle.Registrations, Is.EqualTo(registrations));
            Assert.That(probe.Publications[1].IsCommitted, Is.False);
            Assert.That(probe.AcceptedCount, Is.EqualTo(1));
            Assert.That(probe.PublicationCount, Is.EqualTo(1));
            Assert.That(probe.RuntimeDisposedCount, Is.EqualTo(1));
            Assert.That(m_events, Is.Empty);
            Assert.That(Directory.GetFiles(Path.Combine(m_root, "registry"), "manifest.json.tmp-*"),
                Has.Length.EqualTo(1));
            Assert.That(Directory.GetFiles(Path.Combine(m_root, "registry"), "manifest.json.replace-backup-*"),
                Has.Length.EqualTo(1));
            m_indeterminateDecision = false;

            await Assert.ThatAsync(
                async () => await m_coordinator.RefreshAsync(HandoffRequest("handoff-conflict", 1))
                    .ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>().With.Message.Contains("reload")).ConfigureAwait(false);

            Assert.That(probe.DecisionCount, Is.EqualTo(2));
            Assert.That(probe.AcceptedCount, Is.EqualTo(1));
            Assert.That(probe.PublicationCount, Is.EqualTo(1));
            Assert.That(probe.Publications, Has.Count.EqualTo(2));
            Assert.That(probe.RuntimeCreatedCount, Is.EqualTo(2));
            Assert.That(probe.RuntimeDisposedCount, Is.EqualTo(1));
            Assert.That(m_registry.Current, Is.SameAs(before));
            Assert.That(m_server.NodeManagerLifecycle.Registrations, Is.EqualTo(registrations));
            Assert.That((await ReadNodeClassAsync(Root(first)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.Good));
            Assert.That((await ReadNodeClassAsync(NewHandoffNode(first)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            Assert.That(m_events, Is.Empty);
        }

        /// <summary>
        /// Verifies that notification failure does not skip committed events or the next publication.
        /// </summary>
        /// <param name="failingKind">The failing kind used by this test case.</param>
        [TestCase(WotMaterializationEventKind.Resource)]
        [TestCase(WotMaterializationEventKind.RefreshCompleted)]
        [Platform("Win")]
        public async Task NotificationFailureDoesNotSkipCommittedEventsOrTheNextPublication(
            WotMaterializationEventKind failingKind)
        {
            const string requestId = "handoff-event-warning";
            const string failureMessage = "The committed event observer failed.";
            HandoffProbe probe = ObserveHandoff();
            WotResource first = await AddAsync("first").ConfigureAwait(false);
            WotResource second = await AddAsync("second").ConfigureAwait(false);
            var delivered = new List<WotMaterializationEventArgs>();
            int failures = 0;
            EventHandler<WotMaterializationEventArgs> failing = (_, change) =>
            {
                if (change.Kind == failingKind && failures == 0)
                {
                    failures++;
                    throw new InvalidOperationException(failureMessage);
                }
            };
            EventHandler<WotMaterializationEventArgs> remaining = (_, change) => delivered.Add(change);
            m_coordinator.Event += failing;
            m_coordinator.Event += remaining;
            WotRefreshResult result;
            try
            {
                result = await m_coordinator.RefreshAsync(HandoffRequest(requestId)).ConfigureAwait(false);
            }
            finally
            {
                m_coordinator.Event -= failing;
                m_coordinator.Event -= remaining;
            }

            Assert.That(failures, Is.EqualTo(1));
            Assert.That(result.Summary.Outcome, Is.EqualTo(WoTOutcomeEnum.Warning));
            Assert.That(result.NewGeneration, Is.EqualTo(1u));
            Assert.That(result.Results.All(row => row.Message!.Contains(failureMessage, StringComparison.Ordinal)),
                Is.True);
            Assert.That(delivered.Select(change => change.Kind),
                Is.EqualTo(m_events.Select(change => change.Kind)));
            AssertHandoffEvents(requestId, 1, first, second);
            Assert.That(probe.DecisionCount, Is.EqualTo(1));
            Assert.That(probe.PublicationCount, Is.EqualTo(1));
            Assert.That(probe.RuntimeDisposedCount, Is.Zero);
            await AssertLiveImageAsync(1, first, second).ConfigureAwait(false);

            WotRefreshResult next = await m_coordinator.RefreshAsync(
                HandoffRequest("handoff-event-next", 1, force: true)).ConfigureAwait(false);

            Assert.That(next.NewGeneration, Is.EqualTo(2u));
            Assert.That(probe.DecisionCount, Is.EqualTo(2));
            Assert.That(probe.PublicationCount, Is.EqualTo(2));
            AssertHandoffEvents("handoff-event-next", 2, first, second);
        }

        /// <summary>
        /// Verifies that committed reconciliation warning preserves native ownership and next admission.
        /// </summary>
        [Test]
        [Platform("Win")]
        public async Task CommittedReconciliationWarningPreservesNativeOwnershipAndNextAdmission()
        {
            const string requestId = "handoff-reconciliation-warning";
            var failure = new InvalidOperationException("The committed-state observer failed.");
            HandoffProbe probe = ObserveHandoff();
            WotResource first = await AddAsync("first").ConfigureAwait(false);
            WotResource second = await AddAsync("second").ConfigureAwait(false);
            long before = m_registry.Current.Generation;
            probe.PublicationFailure = failure;

            WotRefreshResult result = await m_coordinator.RefreshAsync(HandoffRequest(requestId)).ConfigureAwait(false);

            Assert.That(probe.Publications[0].IsCommitted, Is.True);
            Assert.That(probe.Publications[0].CleanupFailure, Is.TypeOf<AggregateException>());
            var warnings = (AggregateException)probe.Publications[0].CleanupFailure!;
            Assert.That(warnings.Flatten().InnerExceptions, Does.Contain(failure));
            Assert.That(probe.DecisionCount, Is.EqualTo(1));
            Assert.That(probe.AcceptedCount, Is.EqualTo(1));
            Assert.That(probe.PublicationCount, Is.EqualTo(1));
            Assert.That(probe.DisposalCount, Is.EqualTo(1));
            Assert.That(probe.RuntimeDisposedCount, Is.Zero);
            Assert.That(m_registry.Current.Generation, Is.EqualTo(before + 1));
            Assert.That(result.Summary.Outcome, Is.EqualTo(WoTOutcomeEnum.Warning));
            Assert.That(result.Results.All(row => row.Message!.Contains(failure.Message, StringComparison.Ordinal)),
                Is.True);
            AssertHandoffEvents(requestId, 1, first, second);
            await AssertLiveImageAsync(1, first, second).ConfigureAwait(false);
            probe.PublicationFailure = null;

            WotRefreshResult next = await m_coordinator.RefreshAsync(
                HandoffRequest("handoff-reconciliation-next", 1, force: true)).ConfigureAwait(false);

            Assert.That(next.NewGeneration, Is.EqualTo(2u));
            Assert.That(probe.DecisionCount, Is.EqualTo(2));
            Assert.That(probe.AcceptedCount, Is.EqualTo(2));
            Assert.That(probe.PublicationCount, Is.EqualTo(2));
            AssertHandoffEvents("handoff-reconciliation-next", 2, first, second);
            await AssertLiveImageAsync(2, first, second).ConfigureAwait(false);
        }

        private static WotRefreshRequest HandoffRequest(string requestId, uint expected = 0, bool force = false)
        {
            return new WotRefreshRequest
            {
                RequestId = requestId,
                ExpectedGeneration = expected,
                Options = new WoTRefreshOptionsDataType { Atomicity = WoTAtomicityEnum.PerRegistry, Force = force }
            };
        }

        private async Task UpdateHandoffResourceAsync(WotResource resource)
        {
            WotRegistryMutationResult result = await m_registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = resource.GroupId,
                ResourceId = resource.ResourceId,
                VersionId = "v2",
                Kind = resource.Kind,
                Content = ByteString.From(TestMaterialization.Td("urn:" + resource.ResourceId, "2"))
            }).ConfigureAwait(false);
            Assert.That(result.Changed, Is.True, result.Message);
            m_converter.SetNodeCount(resource.ResourceId, 3);
        }

        private NodeId NewHandoffNode(WotResource resource)
        {
            return new NodeId(5002u, Root(resource).NamespaceIndex);
        }

        private async Task AssertLiveImageAsync(uint generation, params WotResource[] resources)
        {
            Assert.That(m_coordinator.Generation, Is.EqualTo(generation));
            Assert.That(m_registry.Current.RefreshGeneration, Is.EqualTo(generation));
            Assert.That(m_coordinator.CommittedPublication.RegistrySnapshot, Is.SameAs(m_registry.Current));
            foreach (WotResource resource in resources)
            {
                WotResource active = m_registry.Current.FindResource(resource.GroupId, resource.ResourceId)!;
                Assert.That(active.RefreshGeneration, Is.EqualTo(generation));
                Assert.That(active.RootNodeId, Is.EqualTo(Root(resource)));
                Assert.That((await ReadNodeClassAsync(Root(resource)).ConfigureAwait(false)).StatusCode,
                    Is.EqualTo(StatusCodes.Good));
            }
        }

        private void AssertHandoffEvents(string requestId, uint generation, params WotResource[] resources)
        {
            WotMaterializationEventArgs[] active = [.. m_events.Where(change =>
                change.RequestId == requestId && change.Kind == WotMaterializationEventKind.Resource &&
                change.LoadState == WoTLoadStateEnum.Active)];
            Assert.That(active.Select(change => change.Xid),
                Is.EquivalentTo(resources.Select(resource => resource.Xid)));
            Assert.That(active.All(change => change.Generation == generation && change.VersionId == "v1"), Is.True);
            WotMaterializationEventArgs completion = m_events.Single(change =>
                change.RequestId == requestId && change.Kind == WotMaterializationEventKind.RefreshCompleted);
            Assert.That(completion.Generation, Is.EqualTo(generation));
            Assert.That(completion.Summary!.Generation, Is.EqualTo(generation));
            Assert.That(completion.Summary.RequestId, Is.EqualTo(requestId));
        }

        private HandoffProbe ObserveHandoff(
            IWotViewProjectionHost? viewHost = null,
            IWotDocumentConverter? converter = null,
            IWotBinderRegistry? binderRegistry = null)
        {
            var probe = new HandoffProbe();
            var runtimeFactory = new Mock<IWotProjectionBindingRuntimeFactory>(MockBehavior.Strict);
            runtimeFactory.Setup(factory => factory.CreateAsync(
                It.IsAny<INodeManagerBuilder>(), It.IsAny<ArrayOf<WotBindingPlan>>(),
                It.IsAny<CancellationToken>())).Returns(() =>
                {
                    probe.RuntimeCreatedCount++;
                    var runtime = new Mock<IAsyncDisposable>(MockBehavior.Strict);
                    runtime.Setup(owner => owner.DisposeAsync()).Returns(() =>
                    {
                        probe.RuntimeDisposedCount++;
                        probe.OnRuntimeDisposed?.Invoke();
                        return default;
                    });
                    probe.OnRuntimeCreated?.Invoke();
                    return new ValueTask<IAsyncDisposable?>(runtime.Object);
                });
            var inner = new LifecycleWotProjectionHost(m_server.NodeManagerLifecycle, runtimeFactory.Object);
            IWotPreparedProjectionPublication Track(
                IWotPreparedProjectionPublication prepared, ArrayOf<WotProjectionChange> changes)
            {
                probe.Changes.Add(changes);
                probe.Publications.Add(prepared);
                var publication = new Mock<IWotPreparedProjectionPublication>(MockBehavior.Strict);
                publication.As<IWotPreparedProjectionNamespaceSource>()
                    .SetupGet(value => value.CapturedNamespaceUris)
                    .Returns(() => (prepared as IWotPreparedProjectionNamespaceSource)?.CapturedNamespaceUris ?? []);
                publication.SetupGet(value => value.Projections).Returns(prepared.Projections);
                publication.SetupGet(value => value.ViewGraph).Returns(prepared.ViewGraph);
                publication.SetupGet(value => value.IsCommitted).Returns(() => prepared.IsCommitted);
                publication.SetupGet(value => value.CleanupFailure).Returns(() => prepared.CleanupFailure);
                publication.Setup(value => value.BindReadImages(It.IsAny<ArrayOf<INodeManagerReadImage>>()))
                    .Callback((ArrayOf<INodeManagerReadImage> images) =>
                    {
                        if (probe.RejectReadImages)
                        {
                            throw new NotSupportedException("The custom source provider cannot retain read images.");
                        }
                        prepared.BindReadImages(images);
                    });
                publication.Setup(value => value.CommitAsync(
                    It.IsAny<Func<CancellationToken, ValueTask>>(), It.IsAny<Action>(),
                    It.IsAny<CancellationToken>())).Returns(async (
                        Func<CancellationToken, ValueTask> decide, Action publish, CancellationToken ct) =>
                    {
                        await prepared.CommitAsync(async decisionToken =>
                        {
                            if (probe.BeforeDecisionAsync is { } before)
                            {
                                await before(decisionToken).ConfigureAwait(false);
                            }
                            decisionToken.ThrowIfCancellationRequested();
                            probe.DecisionCount++;
                            await decide(decisionToken).ConfigureAwait(false);
                            probe.AcceptedCount++;
                            if (probe.AfterDecisionAsync is { } after)
                            {
                                await after(decisionToken).ConfigureAwait(false);
                            }
                        }, () =>
                        {
                            probe.PublicationCount++;
                            publish();
                            if (probe.PublicationFailure is { } failure)
                            {
                                throw failure;
                            }
                        }, ct).ConfigureAwait(false);
                    });
                publication.Setup(value => value.DisposeAsync()).Returns(async () =>
                {
                    await prepared.DisposeAsync().ConfigureAwait(false);
                    probe.DisposalCount++;
                });
                return publication.Object;
            }
            var host = new Mock<IWotPreparedProjectionHost>(MockBehavior.Strict);
            host.SetupGet(owner => owner.SupportsPreparedPublication).Returns(() => inner.SupportsPreparedPublication);
            host.Setup(owner => owner.RemoveAsync(
                    It.IsAny<WotProjectionHandle>(), It.IsAny<CancellationToken>()))
                .Returns((WotProjectionHandle handle, CancellationToken token) => inner.RemoveAsync(handle, token));
            host.Setup(owner => owner.PrepareAsync(
                It.IsAny<ArrayOf<WotProjectionChange>>(), It.IsAny<IWotPreparedViewPublication?>(),
                It.IsAny<IOperationContext?>(), It.IsAny<CancellationToken>())).Returns(async (
                    ArrayOf<WotProjectionChange> changes,
                    IWotPreparedViewPublication? views,
                    IOperationContext? callerContext,
                    CancellationToken token) =>
                Track(await inner.PrepareAsync(changes, views, callerContext, token).ConfigureAwait(false), changes));
            Mock<IWotInvocationProjectionHost> isolated = host.As<IWotInvocationProjectionHost>();
            isolated.SetupGet(owner => owner.SupportedAtomicities).Returns(() => inner.SupportedAtomicities);
            isolated.Setup(owner => owner.CapturePublication()).Returns(() =>
            {
                IWotProjectionPublicationCapture captured = inner.CapturePublication();
                var capture = new Mock<IWotProjectionPublicationCapture>(MockBehavior.Strict);
                capture.Setup(value => value.BeginAsync(
                        It.IsAny<IOperationContext?>(), It.IsAny<CancellationToken>()))
                    .Returns(async (IOperationContext? callerContext, CancellationToken token) =>
                    {
                        IWotProjectionPublication admitted = await captured.BeginAsync(callerContext, token)
                            .ConfigureAwait(false);
                        var invocation = new Mock<IWotProjectionPublication>(MockBehavior.Strict);
                        invocation.SetupGet(value => value.IsCurrent).Returns(() => admitted.IsCurrent);
                        invocation.Setup(value => value.PrepareAsync(
                            It.IsAny<ArrayOf<WotProjectionChange>>(), It.IsAny<IWotPreparedViewPublication?>(),
                            It.IsAny<CancellationToken>())).Returns(async (
                                ArrayOf<WotProjectionChange> changes, IWotPreparedViewPublication? views,
                                CancellationToken ct) =>
                        {
                            if (probe.BeforePrepareAsync is { } before)
                            {
                                await before(ct).ConfigureAwait(false);
                            }
                            return Track(await admitted.PrepareAsync(changes, views, ct).ConfigureAwait(false), changes);
                        });
                        invocation.Setup(value => value.PrepareReadImagesAsync(
                            It.IsAny<ArrayOf<INodeManagerReadImage>>(), It.IsAny<CancellationToken>()))
                            .Returns((ArrayOf<INodeManagerReadImage> images, CancellationToken ct) =>
                                admitted.PrepareReadImagesAsync(images, ct));
                        invocation.As<IWotProjectionValidationPublication>().Setup(value => value.ValidateAsync(
                            It.IsAny<ArrayOf<WotProjectionChange>>(),
                            It.IsAny<Func<IWotPreparedProjectionPublication, CancellationToken, ValueTask>>(),
                            It.IsAny<IWotPreparedViewPublication?>(),
                            It.IsAny<CancellationToken>())).Returns((
                                ArrayOf<WotProjectionChange> changes,
                                Func<IWotPreparedProjectionPublication, CancellationToken, ValueTask> inspect,
                                IWotPreparedViewPublication? views,
                                CancellationToken ct) =>
                            ((IWotProjectionValidationPublication)admitted).ValidateAsync(
                                changes, (candidate, token) => inspect(Track(candidate, changes), token), views, ct));
                        invocation.Setup(value => value.DisposeAsync()).Returns(() => admitted.DisposeAsync());
                        return invocation.Object;
                    });
                return capture.Object;
            });
            m_coordinator.Dispose();
            m_coordinator = new WotMaterializationCoordinator(
                m_registry, host.Object, binderRegistry: binderRegistry,
                documentConverter: converter ?? m_converter, viewProjectionHost: viewHost)
            {
                ServerNamespaceUris = m_server.CurrentInstance.NamespaceUris
            };
            m_coordinator.Event += (_, change) => m_events.Add(change);
            return probe;
        }

        private sealed class HandoffProbe
        {
            /// <summary>
            /// Gets or sets the callback before an invocation prepares its next publication unit.
            /// </summary>
            public Func<CancellationToken, ValueTask>? BeforePrepareAsync { get; set; }

            /// <summary>
            /// Gets or sets the before decision async for <c>HandoffProbe</c>.
            /// </summary>
            public Func<CancellationToken, ValueTask>? BeforeDecisionAsync { get; set; }

            /// <summary>
            /// Gets or sets the after decision async for <c>HandoffProbe</c>.
            /// </summary>
            public Func<CancellationToken, ValueTask>? AfterDecisionAsync { get; set; }

            /// <summary>
            /// Gets or sets the publication failure for <c>HandoffProbe</c>.
            /// </summary>
            public Exception? PublicationFailure { get; set; }

            /// <summary>
            /// Gets or sets the reject read images for <c>HandoffProbe</c>.
            /// </summary>
            public bool RejectReadImages { get; set; }

            /// <summary>
            /// Gets or sets the on runtime created for <c>HandoffProbe</c>.
            /// </summary>
            public Action? OnRuntimeCreated { get; set; }

            /// <summary>
            /// Gets the changes for <c>HandoffProbe</c>.
            /// </summary>
            public List<ArrayOf<WotProjectionChange>> Changes { get; } = [];

            /// <summary>
            /// Gets the publications for <c>HandoffProbe</c>.
            /// </summary>
            public List<IWotPreparedProjectionPublication> Publications { get; } = [];

            /// <summary>
            /// Gets or sets the decision count for <c>HandoffProbe</c>.
            /// </summary>
            public int DecisionCount { get; set; }

            /// <summary>
            /// Gets or sets the accepted count for <c>HandoffProbe</c>.
            /// </summary>
            public int AcceptedCount { get; set; }

            /// <summary>
            /// Gets or sets the publication count for <c>HandoffProbe</c>.
            /// </summary>
            public int PublicationCount { get; set; }

            /// <summary>
            /// Gets or sets the disposal count for <c>HandoffProbe</c>.
            /// </summary>
            public int DisposalCount { get; set; }

            /// <summary>
            /// Gets or sets the runtime created count for <c>HandoffProbe</c>.
            /// </summary>
            public int RuntimeCreatedCount { get; set; }

            /// <summary>
            /// Gets or sets the runtime disposed count for <c>HandoffProbe</c>.
            /// </summary>
            public int RuntimeDisposedCount { get; set; }

            /// <summary>
            /// Gets or sets the callback observing completion of a runtime's asynchronous retirement.
            /// </summary>
            public Action? OnRuntimeDisposed { get; set; }
        }
    }

    /// <summary>
    /// Tests queued publication admission on a real in-process lifecycle without native TCP fixtures.
    /// </summary>
    [TestFixture]
    public sealed class WotAtomicPublicationSocketFreeAdmissionTests
    {
        /// <summary>
        /// Add must capture lifecycle revision after it has waited for writer admission.
        /// </summary>
        [Test]
        public async Task AddWaitsForWriterAdmissionBeforeCapturingRevisionAsync()
        {
            await using var harness = new SocketFreePublicationHarness();
            INodeManagerLifecycle lifecycle = harness.Server.NodeManagerLifecycle;
            long revisionBefore = lifecycle.Revision;
            await using IPreparedNodeManagerBatch unrelated = await PrepareUnrelatedPublicationAsync(
                lifecycle, "urn:wot:admission-unrelated-add").ConfigureAwait(false);
            var queued = NewAdmissionSignal();
            var host = new LifecycleWotProjectionHost(new AdmissionObservedLifecycle(lifecycle, queued));
            WotProjectionDocument document = AdmissionDocument(
                "admission-add", "urn:wot:admission-add");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));

            Task<WotProjectionHandle>? pending = null;
            await CommitUnrelatedPublicationAndReleaseAdmissionAsync(
                lifecycle,
                unrelated,
                queued,
                revisionBefore,
                () => pending = host.AddAsync(document, timeout.Token).AsTask(),
                timeout.Token).ConfigureAwait(false);
            WotProjectionHandle added = await pending!.WaitAsync(timeout.Token).ConfigureAwait(false);

            NodeId root = AdmissionRoot(harness.Data, "urn:wot:admission-add");
            NodeState? published = await harness.Data.NodeManager
                .FindNodeInAddressSpaceAsync(root).ConfigureAwait(false);
            Assert.That(published, Is.Not.Null,
                "Add must publish its projection after the unrelated lifecycle commit.");
            Assert.That(added.Registration, Is.Not.Null);
            Assert.That(lifecycle.Registrations.ToList().Any(
                value => value.Id == added.Registration!.Id), Is.True);
            Assert.That(lifecycle.Revision, Is.EqualTo(revisionBefore + 2));
            await host.RemoveAsync(added, timeout.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// Remove must capture lifecycle revision after it has waited for writer admission.
        /// </summary>
        [Test]
        public async Task RemoveWaitsForWriterAdmissionBeforeCapturingRevisionAsync()
        {
            await using var harness = new SocketFreePublicationHarness();
            INodeManagerLifecycle lifecycle = harness.Server.NodeManagerLifecycle;
            var host = new LifecycleWotProjectionHost(lifecycle);
            WotProjectionHandle added = await host.AddAsync(
                AdmissionDocument("admission-remove", "urn:wot:admission-remove")).ConfigureAwait(false);
            NodeId root = AdmissionRoot(harness.Data, "urn:wot:admission-remove");
            Assert.That(
                await harness.Data.NodeManager.FindNodeInAddressSpaceAsync(root).ConfigureAwait(false),
                Is.Not.Null);

            long revisionBefore = lifecycle.Revision;
            await using IPreparedNodeManagerBatch unrelated = await PrepareUnrelatedPublicationAsync(
                lifecycle, "urn:wot:admission-unrelated-remove").ConfigureAwait(false);
            var queued = NewAdmissionSignal();
            host = new LifecycleWotProjectionHost(new AdmissionObservedLifecycle(lifecycle, queued));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));

            Task? pending = null;
            await CommitUnrelatedPublicationAndReleaseAdmissionAsync(
                lifecycle,
                unrelated,
                queued,
                revisionBefore,
                () => pending = host.RemoveAsync(added, timeout.Token).AsTask(),
                timeout.Token).ConfigureAwait(false);
            await pending!.WaitAsync(timeout.Token).ConfigureAwait(false);

            Assert.That(
                await harness.Data.NodeManager.FindNodeInAddressSpaceAsync(root).ConfigureAwait(false),
                Is.Null,
                "Remove must retire the projection after the unrelated lifecycle commit.");
            Assert.That(lifecycle.Registrations.ToList().Any(
                value => value.Id == added.Registration!.Id), Is.False);
            Assert.That(lifecycle.Revision, Is.EqualTo(revisionBefore + 2));
        }

        /// <summary>
        /// A single-document reload must capture lifecycle revision after writer admission.
        /// </summary>
        [Test]
        public async Task SingleDocumentReloadWaitsForWriterAdmissionBeforeCapturingRevisionAsync()
        {
            await using var harness = new SocketFreePublicationHarness();
            INodeManagerLifecycle lifecycle = harness.Server.NodeManagerLifecycle;
            var host = new LifecycleWotProjectionHost(lifecycle);
            WotProjectionHandle first = await host.AddAsync(
                AdmissionDocument("admission-reload-old", "urn:wot:admission-reload-old")).ConfigureAwait(false);
            NodeId oldRoot = AdmissionRoot(harness.Data, "urn:wot:admission-reload-old");
            Assert.That(
                await harness.Data.NodeManager.FindNodeInAddressSpaceAsync(oldRoot).ConfigureAwait(false),
                Is.Not.Null);

            long revisionBefore = lifecycle.Revision;
            await using IPreparedNodeManagerBatch unrelated = await PrepareUnrelatedPublicationAsync(
                lifecycle, "urn:wot:admission-unrelated-reload").ConfigureAwait(false);
            var queued = NewAdmissionSignal();
            host = new LifecycleWotProjectionHost(new AdmissionObservedLifecycle(lifecycle, queued));
            WotProjectionDocument replacement = AdmissionDocument(
                "admission-reload-new", "urn:wot:admission-reload-new");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));

            Task<WotProjectionHandle>? pending = null;
            await CommitUnrelatedPublicationAndReleaseAdmissionAsync(
                lifecycle,
                unrelated,
                queued,
                revisionBefore,
                () => pending = host.ShadowReloadAsync(first, replacement, timeout.Token).AsTask(),
                timeout.Token).ConfigureAwait(false);
            WotProjectionHandle reloaded = await pending!.WaitAsync(timeout.Token).ConfigureAwait(false);

            NodeState? retiredRoot = await harness.Data.NodeManager
                .FindNodeInAddressSpaceAsync(oldRoot).ConfigureAwait(false);
            NodeState? newRoot = await harness.Data.NodeManager
                .FindNodeInAddressSpaceAsync(AdmissionRoot(harness.Data, "urn:wot:admission-reload-new"))
                .ConfigureAwait(false);
            Assert.That(retiredRoot, Is.Null,
                "The old document must not remain routed after its single-document reload.");
            Assert.That(newRoot, Is.Not.Null,
                "The replacement document must be published after the unrelated lifecycle commit.");
            Assert.That(reloaded.Generation, Is.GreaterThan(first.Generation));
            Assert.That(lifecycle.Registrations.ToList().Any(
                value => value.Id == reloaded.Registration!.Id), Is.True);
            Assert.That(lifecycle.Revision, Is.EqualTo(revisionBefore + 2));
            await host.RemoveAsync(reloaded, timeout.Token).ConfigureAwait(false);
        }

        private static TaskCompletionSource<bool> NewAdmissionSignal()
        {
            return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private static WotProjectionDocument AdmissionDocument(string key, string namespaceUri)
        {
            return new WotProjectionDocument(
                key,
                [new WotProjectionSource(key, [namespaceUri], TestNodeSets.XmlBytes(namespaceUri))]);
        }

        private static NodeId AdmissionRoot(ServerInternalData server, string namespaceUri)
        {
            int namespaceIndex = server.NamespaceUris.GetIndex(namespaceUri);
            Assert.That(namespaceIndex, Is.GreaterThanOrEqualTo(0));
            return new NodeId(5000u, (ushort)namespaceIndex);
        }

        private static ValueTask<IPreparedNodeManagerBatch> PrepareUnrelatedPublicationAsync(
            INodeManagerLifecycle lifecycle,
            string namespaceUri)
        {
            byte[] xml = TestNodeSets.XmlBytes(namespaceUri);
            RuntimeNodeSetSource source = RuntimeNodeSetSource.FromStream(
                "unrelated",
                _ => new ValueTask<Stream>(new MemoryStream(xml, writable: false)),
                [namespaceUri]);
            var options = new RuntimeNodeSetOptions
            {
                Sources = [source],
                DefaultNamespaceUri = namespaceUri
            };
            return lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(new RuntimeNodeSetNodeManagerFactory(options))]);
        }

        private static async Task CommitUnrelatedPublicationAndReleaseAdmissionAsync(
            INodeManagerLifecycle lifecycle,
            IPreparedNodeManagerBatch unrelated,
            TaskCompletionSource<bool> queued,
            long revisionBefore,
            Func<Task> startPendingOperation,
            CancellationToken cancellationToken)
        {
            var decisionEntered = NewAdmissionSignal();
            var releaseDecision = NewAdmissionSignal();
            Task<NodeManagerBatchResult> commit = unrelated.CommitAsync(async _ =>
            {
                decisionEntered.TrySetResult(true);
                await releaseDecision.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }, cancellationToken).AsTask();
            await decisionEntered.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            Task pendingOperation = startPendingOperation();
            await queued.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            Assert.That(pendingOperation.IsCompleted, Is.False,
                "The operation must remain queued behind the held writer admission.");
            Assert.That(lifecycle.Revision, Is.EqualTo(revisionBefore),
                "The host operation must be queued before the competing publication advances revision.");
            releaseDecision.TrySetResult(true);
            NodeManagerBatchResult published = await commit.ConfigureAwait(false);
            await unrelated.DisposeAsync().ConfigureAwait(false);
            Assert.That(published.Revision, Is.EqualTo(revisionBefore + 1));
            // Releasing the real writer may already have let the queued operation commit.
            Assert.That(lifecycle.Revision, Is.GreaterThanOrEqualTo(published.Revision));
        }

        /// <summary>
        /// Observes real call submission, whether admission occurs in BeginAsync or PrepareAsync.
        /// </summary>
        private sealed class AdmissionObservedLifecycle(
            INodeManagerLifecycle inner,
            TaskCompletionSource<bool> preparationQueued) : INodeManagerBatchLifecycle
        {
            public long Revision => inner.Revision;

            public ArrayOf<NodeManagerSlot> Slots => inner.Slots;

            public ArrayOf<NodeManagerRegistration> Registrations => inner.Registrations;

            public bool IsShuttingDown => inner.IsShuttingDown;

            public bool SupportsPreparedPublication => inner.SupportsPreparedPublication;

            public bool SupportsPublicationIsolation => inner.SupportsPublicationIsolation;

            public INodeManagerPublicationCapture CapturePublication()
            {
                return new AdmissionObservedCapture(inner.CapturePublication(), preparationQueued);
            }

            public ValueTask<IPreparedNodeManagerBatch> PrepareAsync(
                ArrayOf<NodeManagerBatchChange> changes,
                CancellationToken cancellationToken = default)
            {
                ValueTask<IPreparedNodeManagerBatch> pending = inner.PrepareAsync(changes, cancellationToken);
                preparationQueued.TrySetResult(true);
                return pending;
            }
        }

        private sealed class AdmissionObservedCapture(
            INodeManagerPublicationCapture inner,
            TaskCompletionSource<bool> preparationQueued) : INodeManagerPublicationCapture
        {
            public async ValueTask<INodeManagerPublication> BeginAsync(
                CancellationToken cancellationToken = default)
            {
                ValueTask<INodeManagerPublication> pending = inner.BeginAsync(cancellationToken);
                preparationQueued.TrySetResult(true);
                INodeManagerPublication publication = await pending.ConfigureAwait(false);
                return new AdmissionObservedPublication(publication, preparationQueued);
            }
        }

        private sealed class AdmissionObservedPublication(
            INodeManagerPublication inner,
            TaskCompletionSource<bool> preparationQueued) : INodeManagerPublication
        {
            public bool IsCurrent => inner.IsCurrent;

            public ValueTask<IPreparedNodeManagerBatch> PrepareAsync(
                ArrayOf<NodeManagerBatchChange> changes,
                CancellationToken cancellationToken = default)
            {
                ValueTask<IPreparedNodeManagerBatch> pending = inner.PrepareAsync(changes, cancellationToken);
                preparationQueued.TrySetResult(true);
                return pending;
            }

            public ValueTask<IPreparedNodeManagerBatch> PrepareReadImagesAsync(
                ArrayOf<INodeManagerReadImage> images,
                CancellationToken cancellationToken = default)
            {
                return inner.PrepareReadImagesAsync(images, cancellationToken);
            }

            public ValueTask ValidateAsync(
                ArrayOf<NodeManagerBatchChange> changes,
                Func<IPreparedNodeManagerBatch, CancellationToken, ValueTask> inspectAsync,
                CancellationToken cancellationToken = default)
            {
                return inner.ValidateAsync(changes, inspectAsync, cancellationToken);
            }

            public ValueTask DisposeAsync()
            {
                return inner.DisposeAsync();
            }
        }

        private sealed class SocketFreePublicationHarness : IAsyncDisposable
        {
            public SocketFreePublicationHarness()
            {
                ITelemetryContext telemetry = NUnitTelemetryContext.Create();
                var configuration = new ApplicationConfiguration
                {
                    ApplicationUri = "urn:test:socket-free:wot-admission",
                    ServerConfiguration = new ServerConfiguration()
                };
                Server = new StandardServer(telemetry) { LoadComplexTypes = false };
                Data = new ServerInternalData(new ServerProperties(), configuration,
                    new ServiceMessageContext(telemetry, EncodeableFactory.Create()));
                Data.TypeTree.AddSubtype(Opc.Ua.Types.DataTypeIds.BaseDataType, NodeId.Null);
                Data.TypeTree.AddSubtype(
                    Opc.Ua.Types.DataTypeIds.Structure, Opc.Ua.Types.DataTypeIds.BaseDataType);
                var status = new ServerStatusValue(new ServerStatusState(null),
                    new ServerStatusDataType { State = ServerState.Running }, new Lock());
                typeof(ServerInternalData).GetProperty("NonThreadSafeStatus",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .SetValue(Data, status);
                typeof(ServerBase).GetProperty("Configuration",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .SetValue(Server, configuration);
                typeof(StandardServer).GetField("m_serverInternal",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .SetValue(Server, Data);
                var factory = new Mock<IMainNodeManagerFactory>();
                var configurationNodeManager = new Mock<IConfigurationNodeManager>();
                configurationNodeManager.As<IDiagnosticsNodeManager>()
                    .Setup(value => value.FindPredefinedNode<ServerObjectState>(Opc.Ua.ObjectIds.Server))
                    .Returns(new ServerObjectState(null));
                factory.Setup(value => value.CreateConfigurationNodeManager())
                    .Returns(configurationNodeManager.Object);
                factory.Setup(value => value.CreateCoreNodeManager(It.IsAny<ushort>()))
                    .Returns(new Mock<ICoreNodeManager>().Object);
                Data.SetMainNodeManagerFactory(factory.Object);
                var sessions = new Mock<ISessionManager>();
                sessions.Setup(value => value.GetSessions()).Returns([]);
                Data.SetSessionManager(sessions.Object, new Mock<ISubscriptionManager>().Object);
                typeof(ServerInternalData).GetProperty(nameof(ServerInternalData.EventManager))!
                    .SetValue(Data, new EventManager(Data, 100, 100));
                // TODO: Replace this standalone fixture constructor when a hosted lifecycle test harness is available.
#pragma warning disable CS0618
                var master = new MasterNodeManager(
                    Data, configuration, null, Array.Empty<IAsyncNodeManager>(), null);
#pragma warning restore CS0618
                Data.SetNodeManager(master);
                Assert.That(Data.IsRunning, Is.True);
                Assert.That(configuration.ServerConfiguration.BaseAddresses, Is.Empty);
            }

            public StandardServer Server { get; }
            public ServerInternalData Data { get; }

            public async ValueTask DisposeAsync()
            {
                // The facade was never started: no listeners, sessions or startup workers exist.
                ((IDisposable)Server.NodeManagerLifecycle).Dispose();
                typeof(StandardServer).GetField("m_serverInternal",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .SetValue(Server, null);
                await Data.DisposeAsync().ConfigureAwait(false);
                Server.Dispose();
            }
        }
    }
}
