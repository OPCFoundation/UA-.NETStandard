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
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    public sealed partial class WotAtomicPublicationNativeTests
    {
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

        private HandoffProbe ObserveHandoff()
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
                        return default;
                    });
                    return new ValueTask<IAsyncDisposable?>(runtime.Object);
                });
            var inner = new LifecycleWotProjectionHost(m_server.NodeManagerLifecycle, runtimeFactory.Object);
            IWotPreparedProjectionPublication Track(
                IWotPreparedProjectionPublication prepared, ArrayOf<WotProjectionChange> changes)
            {
                probe.Changes.Add(changes);
                probe.Publications.Add(prepared);
                var publication = new Mock<IWotPreparedProjectionPublication>(MockBehavior.Strict);
                publication.SetupGet(value => value.Projections).Returns(prepared.Projections);
                publication.SetupGet(value => value.ViewGraph).Returns(prepared.ViewGraph);
                publication.SetupGet(value => value.IsCommitted).Returns(() => prepared.IsCommitted);
                publication.SetupGet(value => value.CleanupFailure).Returns(() => prepared.CleanupFailure);
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
            host.SetupGet(owner => owner.SupportsPreparedPublication).Returns(true);
            host.Setup(owner => owner.PrepareAsync(
                It.IsAny<ArrayOf<WotProjectionChange>>(), It.IsAny<IWotPreparedViewPublication?>(),
                It.IsAny<CancellationToken>())).Returns(async (
                    ArrayOf<WotProjectionChange> changes,
                    IWotPreparedViewPublication? views,
                    CancellationToken token) =>
                Track(await inner.PrepareAsync(changes, views, token).ConfigureAwait(false), changes));
            Mock<IWotInvocationProjectionHost> isolated = host.As<IWotInvocationProjectionHost>();
            isolated.SetupGet(owner => owner.SupportedAtomicities).Returns(() => inner.SupportedAtomicities);
            isolated.Setup(owner => owner.CapturePublication()).Returns(() =>
            {
                IWotProjectionPublicationCapture captured = inner.CapturePublication();
                var capture = new Mock<IWotProjectionPublicationCapture>(MockBehavior.Strict);
                capture.Setup(value => value.BeginAsync(It.IsAny<CancellationToken>()))
                    .Returns(async (CancellationToken token) =>
                    {
                        IWotProjectionPublication admitted = await captured.BeginAsync(token).ConfigureAwait(false);
                        var invocation = new Mock<IWotProjectionPublication>(MockBehavior.Strict);
                        invocation.SetupGet(value => value.IsCurrent).Returns(() => admitted.IsCurrent);
                        invocation.Setup(value => value.PrepareAsync(
                            It.IsAny<ArrayOf<WotProjectionChange>>(), It.IsAny<IWotPreparedViewPublication?>(),
                            It.IsAny<CancellationToken>())).Returns(async (
                                ArrayOf<WotProjectionChange> changes, IWotPreparedViewPublication? views,
                                CancellationToken ct) =>
                            Track(await admitted.PrepareAsync(changes, views, ct).ConfigureAwait(false), changes));
                        invocation.Setup(value => value.DisposeAsync()).Returns(() => admitted.DisposeAsync());
                        return invocation.Object;
                    });
                return capture.Object;
            });
            m_coordinator.Dispose();
            m_coordinator = new WotMaterializationCoordinator(
                m_registry, host.Object, documentConverter: m_converter)
            {
                ServerNamespaceUris = m_server.CurrentInstance.NamespaceUris
            };
            m_coordinator.Event += (_, change) => m_events.Add(change);
            return probe;
        }

        private sealed class HandoffProbe
        {
            public Func<CancellationToken, ValueTask>? BeforeDecisionAsync { get; set; }
            public Func<CancellationToken, ValueTask>? AfterDecisionAsync { get; set; }
            public Exception? PublicationFailure { get; set; }
            public List<ArrayOf<WotProjectionChange>> Changes { get; } = [];
            public List<IWotPreparedProjectionPublication> Publications { get; } = [];
            public int DecisionCount { get; set; }
            public int AcceptedCount { get; set; }
            public int PublicationCount { get; set; }
            public int DisposalCount { get; set; }
            public int RuntimeCreatedCount { get; set; }
            public int RuntimeDisposedCount { get; set; }
        }
    }
}
