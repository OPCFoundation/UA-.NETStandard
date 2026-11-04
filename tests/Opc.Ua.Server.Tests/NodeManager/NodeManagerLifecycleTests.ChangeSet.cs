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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class NodeManagerLifecycleTests
    {
        /// <summary>
        /// A stable target selects the current generation on each new preparation, not an old registration.
        /// </summary>
        [Test]
        public async Task SlotChangeSetReplacesAndRemovesWithoutRefreshingHandleAsync()
        {
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            LifecycleResult added = await lifecycle.ApplyAsync(
                [LifecycleChange.Add(CreateTrackingNodeManagementFactory(kGeneration1Value, _ => { }))])
                .ConfigureAwait(false);
            NodeManagerSlot slot = added.Registrations[0].Slot;
            var replace = LifecycleChange.Replace(
                slot, CreateTrackingNodeManagementFactory(kGeneration2Value, _ => { }));
            LifecycleResult second = await lifecycle.ApplyAsync([replace]).ConfigureAwait(false);
            LifecycleResult third = await lifecycle.ApplyAsync([replace]).ConfigureAwait(false);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(second.Registrations[0].Generation, Is.EqualTo(2));
                Assert.That(third.Registrations[0].Generation, Is.EqualTo(3));
                Assert.That(third.Registrations[0].Slot, Is.SameAs(slot));
                Assert.That((added.PublishedSlots.ToArray() ?? []).Single(value => value.Slot == slot).Generation,
                    Is.EqualTo(1));
                Assert.That((second.PublishedSlots.ToArray() ?? []).Single(value => value.Slot == slot).Generation,
                    Is.EqualTo(2));
                Assert.That(slot.TryGetCurrent(out NodeManagerSlotSnapshot current), Is.True);
                Assert.That(current.Generation, Is.EqualTo(3));
            }
            LifecycleResult removed = await lifecycle.ApplyAsync([LifecycleChange.Remove(slot)])
                .ConfigureAwait(false);
            await removed.Retired.ConfigureAwait(false);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(removed.Registrations, Is.Empty);
                Assert.That((removed.PublishedSlots.ToArray() ?? []).Any(value => value.Slot == slot), Is.False);
                Assert.That(slot.TryGetCurrent(out _), Is.False);
                Assert.That(removed.RetirementStatus, Is.EqualTo(NodeManagerRetirementStatus.Completed));
                Assert.That(removed.Revision, Is.GreaterThan(third.Revision));
            }
        }

        /// <summary>
        /// Mixed changes publish one revision and retain addition/replacement order in the committed result.
        /// </summary>
        [Test]
        public async Task SlotChangeSetPublishesMixedChangesAtOneRevisionAsync()
        {
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            LifecycleResult initial = await lifecycle.ApplyAsync(
            [
                LifecycleChange.Add(CreateTrackingNodeManagementFactory(kGeneration1Value, _ => { })),
                LifecycleChange.Add(CreateTrackingNodeManagementFactory(
                    kSecondRegistrationValue, _ => { }, kSecondModelNamespaceUri))
            ]).ConfigureAwait(false);
            NodeManagerSlot replaced = initial.Registrations[0].Slot;
            NodeManagerSlot removed = initial.Registrations[1].Slot;
            long revision = initial.Revision;
            LifecycleResult result = await lifecycle.ApplyAsync(
            [
                LifecycleChange.Remove(removed),
                LifecycleChange.Add(CreateTrackingNodeManagementFactory(
                    303, _ => { }, kSecondModelNamespaceUri + ":new")),
                LifecycleChange.Replace(replaced, CreateTrackingNodeManagementFactory(kGeneration2Value, _ => { }))
            ]).ConfigureAwait(false);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.Revision, Is.EqualTo(revision + 1));
                Assert.That(result.Registrations.Count, Is.EqualTo(2));
                Assert.That(result.Registrations[0].Slot, Is.Not.SameAs(replaced));
                Assert.That(result.Registrations[1].Slot, Is.SameAs(replaced));
                Assert.That(result.Registrations[1].Generation, Is.EqualTo(2));
                Assert.That((result.PublishedSlots.ToArray() ?? []).Any(value => value.Slot == removed), Is.False);
                Assert.That(removed.TryGetCurrent(out _), Is.False);
                Assert.That(result.Issues, Is.Empty);
                Assert.That(result.CleanupFailure, Is.Null);
            }
            await result.Retired.ConfigureAwait(false);
        }

        /// <summary>
        /// Disposal aborts only private candidates; the target slot and routing revision remain unchanged.
        /// </summary>
        [Test]
        public async Task SlotChangeSetAbortDisposesCandidateExactlyOnceAsync()
        {
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            LifecycleResult added = await lifecycle.ApplyAsync(
                [LifecycleChange.Add(CreateTrackingNodeManagementFactory(kGeneration1Value, _ => { }))])
                .ConfigureAwait(false);
            TrackingLifecycleNodeManager? candidate = null;
            IPreparedChanges prepared = await lifecycle.PrepareAsync(
                [LifecycleChange.Replace(added.Registrations[0].Slot,
                    CreateTrackingNodeManagementFactory(kGeneration2Value, value => candidate = value))])
                .ConfigureAwait(false);
            await prepared.DisposeAsync().ConfigureAwait(false);
            await prepared.DisposeAsync().ConfigureAwait(false);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(prepared.IsCommitted, Is.False);
                Assert.That(RequireLifecycleValue(candidate).DisposeCount, Is.EqualTo(1));
                Assert.That(lifecycle.Revision, Is.EqualTo(added.Revision));
                Assert.That(added.Registrations[0].Slot.TryGetCurrent(out NodeManagerSlotSnapshot current), Is.True);
                Assert.That(current.Generation, Is.EqualTo(1));
            }
        }

        /// <summary>
        /// Prepared slot targets never rebase or replay factories after a competing publication.
        /// </summary>
        [Test]
        public async Task SlotChangeSetStaleCommitIsConsumedBeforeDecisionWithoutRetryAsync()
        {
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            LifecycleResult added = await lifecycle.ApplyAsync(
                [LifecycleChange.Add(CreateTrackingNodeManagementFactory(kGeneration1Value, _ => { }))])
                .ConfigureAwait(false);
            TrackingLifecycleNodeManager? candidate = null;
            var factory = new CountingNodeManagerFactory(
                [kModelNamespaceUri],
                CreateTrackingNodeManagementFactory(kGeneration2Value, value => candidate = value).CreateAsync);
            await using IPreparedChanges prepared = await lifecycle.PrepareAsync(
                [LifecycleChange.Replace(added.Registrations[0].Slot, factory)]).ConfigureAwait(false);
            LifecycleResult winner = await lifecycle.ApplyAsync(
                [LifecycleChange.Replace(added.Registrations[0].Slot,
                    CreateTrackingNodeManagementFactory(kGeneration2Value + 1, _ => { }))]).ConfigureAwait(false);
            int decisions = 0;
            await Assert.ThatAsync(async () => await prepared.CommitAsync(_ =>
            {
                decisions++;
                return default;
            }).ConfigureAwait(false), Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
            await Assert.ThatAsync(async () => await prepared.CommitAsync(_ =>
            {
                decisions++;
                return default;
            }).ConfigureAwait(false), Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
            await prepared.DisposeAsync().ConfigureAwait(false);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(decisions, Is.Zero);
                Assert.That(factory.CreateCount, Is.EqualTo(1));
                Assert.That(prepared.IsCommitted, Is.False);
                Assert.That(RequireLifecycleValue(candidate).DisposeCount, Is.EqualTo(1));
                Assert.That(lifecycle.Revision, Is.EqualTo(winner.Revision));
                NodeManagerRegistration current = (lifecycle.Registrations.ToArray() ?? [])
                    .Single(value => value.Slot == added.Registrations[0].Slot);
                Assert.That(current.Slot, Is.SameAs(winner.Registrations[0].Slot));
                Assert.That(current.Generation, Is.EqualTo(winner.Registrations[0].Generation));
            }
        }

        /// <summary>
        /// Captured stale invocations reject the slot API before any candidate is created.
        /// </summary>
        [Test]
        public async Task SlotChangeSetStaleInvocationRejectsPreparationBeforeFactoryAsync()
        {
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            INodeManagerPublicationCapture capture = lifecycle.CapturePublication();
            await lifecycle.ApplyAsync(
                [LifecycleChange.Add(CreateTrackingNodeManagementFactory(kGeneration1Value, _ => { }))])
                .ConfigureAwait(false);
            var factory = new CountingNodeManagerFactory(
                [kSecondModelNamespaceUri],
                CreateTrackingNodeManagementFactory(
                    kSecondRegistrationValue, _ => { }, kSecondModelNamespaceUri).CreateAsync);
            await using INodeManagerPublication invocation = await capture.BeginAsync().ConfigureAwait(false);
            await Assert.ThatAsync(async () => await invocation.PrepareAsync([LifecycleChange.Add(factory)])
                .ConfigureAwait(false), Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
            Assert.That(factory.CreateCount, Is.Zero);
            Assert.That(invocation.IsCurrent, Is.False);
        }

        /// <summary>
        /// A queued slot change selects the generation published before admission, while an exact target stays stale.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task SlotChangeSetResolvesAfterAdmissionAndPreservesExactTargetAsync(bool stableTarget)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            LifecycleResult added = await lifecycle.ApplyAsync(
                [LifecycleChange.Add(CreateTrackingNodeManagementFactory(kGeneration1Value, _ => { }))],
                timeout.Token).ConfigureAwait(false);
            NodeManagerRegistration original = RequireRegistration(added.Registrations[0]);
            await using IPreparedChanges competing = await lifecycle.PrepareAsync(
                [LifecycleChange.Replace(original.Slot,
                    CreateTrackingNodeManagementFactory(kGeneration2Value, _ => { }))], timeout.Token)
                .ConfigureAwait(false);
            var decisionEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseDecision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<LifecycleResult> winner = Task.Run(async () => await competing.CommitAsync(async token =>
            {
                decisionEntered.TrySetResult(true);
                await releaseDecision.Task.WaitAsync(token).ConfigureAwait(false);
            }, timeout.Token).ConfigureAwait(false), timeout.Token);
            var factory = new CountingNodeManagerFactory([kModelNamespaceUri],
                CreateTrackingNodeManagementFactory(kGeneration2Value + 1, _ => { }).CreateAsync);
            Task<IPreparedChanges>? slotPreparation = null;
            Task<IPreparedNodeManagerBatch>? exactPreparation = null;
            try
            {
                await decisionEntered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                if (stableTarget)
                {
                    slotPreparation = lifecycle.PrepareAsync(
                        [LifecycleChange.Replace(original.Slot, factory)], timeout.Token).AsTask();
                    Assert.That(slotPreparation.IsCompleted, Is.False);
                }
                else
                {
                    exactPreparation = lifecycle.PrepareAsync(
                        [NodeManagerBatchChange.Replace(original, factory)], timeout.Token).AsTask();
                    Assert.That(exactPreparation.IsCompleted, Is.False);
                }
                Assert.That(factory.CreateCount, Is.Zero);
            }
            finally
            {
                releaseDecision.TrySetResult(true);
            }
            LifecycleResult published = await winner.WaitAsync(timeout.Token).ConfigureAwait(false);
            if (stableTarget)
            {
                await using IPreparedChanges prepared =
                    await slotPreparation!.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(prepared.Registrations[0].Slot, Is.SameAs(original.Slot));
                Assert.That(prepared.Registrations[0].Generation, Is.EqualTo(3));
                Assert.That(factory.CreateCount, Is.EqualTo(1));
                LifecycleResult result = await prepared.CommitAsync(cancellationToken: timeout.Token)
                    .ConfigureAwait(false);
                Assert.That(result.Revision, Is.EqualTo(published.Revision + 1));
            }
            else
            {
                await Assert.ThatAsync(() => exactPreparation!.WaitAsync(timeout.Token),
                    Throws.InvalidOperationException).ConfigureAwait(false);
                Assert.That(factory.CreateCount, Is.Zero);
                Assert.That(lifecycle.Revision, Is.EqualTo(published.Revision));
            }
        }

        /// <summary>
        /// Unknown, removed and duplicate targets fail before factories run.
        /// </summary>
        [Test]
        public async Task SlotChangeSetRejectsInvalidTargetsBeforeFactoryAsync()
        {
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            LifecycleResult added = await lifecycle.ApplyAsync(
                [LifecycleChange.Add(CreateTrackingNodeManagementFactory(kGeneration1Value, _ => { }))])
                .ConfigureAwait(false);
            NodeManagerSlot slot = added.Registrations[0].Slot;
            var factory = new CountingNodeManagerFactory(
                [kModelNamespaceUri], CreateTrackingNodeManagementFactory(kGeneration2Value, _ => { }).CreateAsync);
            await Assert.ThatAsync(async () => await lifecycle.PrepareAsync(
                [LifecycleChange.Replace(slot, factory), LifecycleChange.Remove(slot)]).ConfigureAwait(false),
                Throws.TypeOf<ArgumentException>()).ConfigureAwait(false);
            await Assert.ThatAsync(async () => await lifecycle.PrepareAsync(
                [LifecycleChange.Replace(new NodeManagerSlot(), factory)]).ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
            LifecycleResult removed = await lifecycle.ApplyAsync([LifecycleChange.Remove(slot)])
                .ConfigureAwait(false);
            await Assert.ThatAsync(async () => await lifecycle.PrepareAsync(
                [LifecycleChange.Replace(slot, factory)]).ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
            Assert.That(factory.CreateCount, Is.Zero);
            Assert.That(lifecycle.Revision, Is.EqualTo(removed.Revision));
        }

        /// <summary>
        /// Callback failure returns a committed result; retry reuses its revision and never repeats the decision.
        /// </summary>
        [Test]
        public async Task SlotChangeSetCommittedCallbackFailureReturnsAuthoritativeResultAsync()
        {
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            int decisions = 0;
            int publications = 0;
            bool fail = true;
            var originalFailure = new InvalidOperationException("Application state publication failed.");
            await using IPreparedChanges prepared = await lifecycle.PrepareAsync(
                [LifecycleChange.Add(CreateTrackingNodeManagementFactory(kGeneration1Value, _ => { }))])
                .ConfigureAwait(false);
            LifecycleResult result = await prepared.CommitAsync(_ =>
            {
                decisions++;
                return default;
            }, () =>
            {
                publications++;
                if (fail)
                {
                    throw originalFailure;
                }
            }).ConfigureAwait(false);
            Assert.That(prepared.IsCommitted, Is.True);
            Assert.That((result.Issues.ToArray() ?? []).Single().InitialFailure, Is.SameAs(originalFailure));
            Assert.That((result.Issues.ToArray() ?? []).Single().Stage,
                Is.EqualTo(NodeManagerReconciliationStage.ApplicationState));
            Assert.That((result.Issues.ToArray() ?? []).Single().IsRetryable, Is.True);
            Assert.That(result.RetirementStatus, Is.EqualTo(NodeManagerRetirementStatus.Completed));
            fail = false;
            await result.RetryReconciliationAsync().ConfigureAwait(false);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.Issues, Is.Empty);
                Assert.That(result.CleanupFailure, Is.Not.Null);
                Assert.That(decisions, Is.EqualTo(1));
                Assert.That(publications, Is.EqualTo(2));
                Assert.That(result.Revision, Is.EqualTo(lifecycle.Revision));
                Assert.That((result.PublishedSlots.ToArray() ?? [])
                    .Single(value => value.Slot == result.Registrations[0].Slot),
                    Is.EqualTo(new NodeManagerGenerationSnapshot(
                        result.Registrations[0].Slot, result.Registrations[0].Generation)));
            }
            var compatibilityResult = new NodeManagerBatchResult(result, [], []);
            NodeManagerRegistration committedRegistration = RequireRegistration(result.Registrations[0]);
            INodeManagerCommittedFailure[] shims =
            [
                new NodeManagerAddCommittedException(
                    committedRegistration, compatibilityResult, "add", originalFailure),
                new NodeManagerReloadCommittedException(
                    committedRegistration, compatibilityResult, "reload", originalFailure),
                new NodeManagerRemovalCommittedException(
                    committedRegistration, compatibilityResult, originalFailure)
            ];
            Assert.That(shims.All(shim => ReferenceEquals(shim.CommittedResult, result)), Is.True);
        }

        /// <summary>
        /// New publication supersedes old application-state recovery without replaying its callback.
        /// </summary>
        [Test]
        public async Task SlotChangeSetSupersessionDoesNotReplayCommittedCallbackAsync()
        {
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            int publications = 0;
            await using IPreparedChanges prepared = await lifecycle.PrepareAsync(
                [LifecycleChange.Add(CreateTrackingNodeManagementFactory(kGeneration1Value, _ => { }))])
                .ConfigureAwait(false);
            LifecycleResult result = await prepared.CommitAsync(_ => default, () =>
            {
                publications++;
                throw new InvalidOperationException("Application state publication failed.");
            }).ConfigureAwait(false);
            LifecycleResult removed = await lifecycle.ApplyAsync(
                [LifecycleChange.Remove(result.Registrations[0].Slot)]).ConfigureAwait(false);
            await removed.Retired.ConfigureAwait(false);
            await result.RetryReconciliationAsync().ConfigureAwait(false);
            using (Assert.EnterMultipleScope())
            {
                Assert.That((result.Issues.ToArray() ?? []).Single().IsSuperseded, Is.True);
                Assert.That((result.Issues.ToArray() ?? []).Single().IsRetryable, Is.False);
                Assert.That(publications, Is.EqualTo(1));
                Assert.That(result.Revision, Is.LessThan(removed.Revision));
                Assert.That((result.PublishedSlots.ToArray() ?? [])
                    .Single(value => value.Slot == result.Registrations[0].Slot)
                    .Generation, Is.EqualTo(1));
                Assert.That(result.Registrations[0].Slot.TryGetCurrent(out _), Is.False);
            }
        }

        /// <summary>
        /// Nested readiness publication advances live slots without rewriting the parent's committed snapshot.
        /// </summary>
        [Test]
        public async Task SlotChangeSetNestedPublicationPreservesParentRevisionAndSnapshotsAsync()
        {
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            LifecycleResult? child = null;
            LifecycleResult parent = await lifecycle.ApplyAsync(
            [
                LifecycleChange.Add(CreateRetryReadinessFactory(
                    manager => manager.ReadinessCallback = async token =>
                        child = await lifecycle.ApplyAsync(
                            [LifecycleChange.Add(CreateTrackingNodeManagementFactory(
                                kSecondRegistrationValue, _ => { }, kSecondModelNamespaceUri))], token)
                            .ConfigureAwait(false)))
            ]).ConfigureAwait(false);
            LifecycleResult nested = RequireLifecycleValue(child);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(parent.Revision, Is.LessThan(nested.Revision));
                Assert.That(lifecycle.Revision, Is.EqualTo(nested.Revision));
                Assert.That((parent.PublishedSlots.ToArray() ?? [])
                    .Any(value => value.Slot == nested.Registrations[0].Slot), Is.False);
                Assert.That((nested.PublishedSlots.ToArray() ?? [])
                    .Any(value => value.Slot == parent.Registrations[0].Slot), Is.True);
                Assert.That(parent.Issues, Is.Empty);
            }
        }

        /// <summary>
        /// A writer advances only through its own units and rejects external supersession before factories.
        /// </summary>
        [Test]
        public async Task SlotChangeSetWriterAdvancesOwnUnitsAndRejectsExternalPublicationAsync()
        {
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            await using ILifecycleWriter writer = await lifecycle.BeginAsync(lifecycle.Revision).ConfigureAwait(false);
            await using IPreparedChanges first = await writer.PrepareAsync(
                [LifecycleChange.Add(CreateTrackingNodeManagementFactory(kGeneration1Value, _ => { }))])
                .ConfigureAwait(false);
            LifecycleResult added = await first.CommitAsync().ConfigureAwait(false);
            Assert.That(writer.IsCurrent, Is.True);
            await using IPreparedChanges second = await writer.PrepareAsync(
                [LifecycleChange.Reload(added.Registrations[0].Slot,
                    CreateTrackingNodeManagementFactory(kGeneration2Value, _ => { }))]).ConfigureAwait(false);
            LifecycleResult replaced = await second.CommitAsync().ConfigureAwait(false);
            Assert.That(writer.IsCurrent, Is.True);
            Assert.That(replaced.Revision, Is.EqualTo(added.Revision + 1));
            await lifecycle.ApplyAsync([LifecycleChange.Remove(added.Registrations[0].Slot)]).ConfigureAwait(false);
            var factory = new CountingNodeManagerFactory([kModelNamespaceUri],
                CreateTrackingNodeManagementFactory(kGeneration1Value, _ => { }).CreateAsync);
            await Assert.ThatAsync(async () => await writer.PrepareAsync([LifecycleChange.Add(factory)])
                .ConfigureAwait(false), Throws.InvalidOperationException).ConfigureAwait(false);
            Assert.That(writer.IsCurrent, Is.False);
            Assert.That(factory.CreateCount, Is.Zero);
        }

        /// <summary>
        /// Revision mismatch and cancellation reject writer admission without changing routing.
        /// </summary>
        [Test]
        public async Task SlotChangeSetWriterRejectsUnexpectedRevisionAndCancellationAsync()
        {
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            long revision = lifecycle.Revision;
            await Assert.ThatAsync(async () => await lifecycle.BeginAsync(revision - 1).ConfigureAwait(false),
                Throws.InvalidOperationException).ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync().ConfigureAwait(false);
            await Assert.ThatAsync(async () => await lifecycle.BeginAsync(cancellationToken: cancellation.Token)
                .ConfigureAwait(false), Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            Assert.That(lifecycle.Revision, Is.EqualTo(revision));
        }

        /// <summary>
        /// Metadata binds an exact private owner once and publishes through the same revision.
        /// </summary>
        [Test]
        public async Task SlotChangeSetBindsTypedMetadataAndPublishesMetadataOnlyUnitAsync()
        {
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            await using ILifecycleWriter writer = await lifecycle.BeginAsync().ConfigureAwait(false);
            await using IPreparedChanges prepared = await writer.PrepareAsync(
                [LifecycleChange.Add(CreateTrackingNodeManagementFactory(kGeneration1Value, _ => { }))])
                .ConfigureAwait(false);
            NodeManagerRegistration candidate = RequireRegistration(prepared.Registrations[0]);
            var image = new Mock<INodeManagerReadImage>();
            image.SetupGet(value => value.Owner).Returns(candidate.NodeManager);
            var wrong = new Mock<INodeManagerReadImage>();
            wrong.SetupGet(value => value.Owner).Returns(new Mock<IAsyncNodeManager>().Object);
            Assert.That(() => prepared.SetMetadata(candidate.Slot, wrong.Object), Throws.ArgumentException);
            prepared.SetMetadata(candidate.Slot, image.Object);
            Assert.That(() => prepared.SetMetadata(candidate.Slot, image.Object), Throws.InvalidOperationException);
            LifecycleResult added = await prepared.CommitAsync().ConfigureAwait(false);
            var host = (IDynamicNodeManagerBatchHost)m_server.CurrentInstance.NodeManager;
            Assert.That(host.RoutingRevision.ReadImages[candidate.NodeManager], Is.SameAs(image.Object));
            Assert.That(() => prepared.SetMetadata(candidate.Slot, image.Object), Throws.InvalidOperationException);

            var updated = new Mock<INodeManagerReadImage>();
            updated.SetupGet(value => value.Owner).Returns(candidate.NodeManager);
            await using IPreparedChanges metadata = await writer.PrepareReadImagesAsync([updated.Object])
                .ConfigureAwait(false);
            Assert.That(metadata.Slots[0], Is.SameAs(candidate.Slot));
            Assert.That(() => metadata.SetMetadata(candidate.Slot, updated.Object), Throws.InvalidOperationException);
            LifecycleResult changed = await metadata.CommitAsync().ConfigureAwait(false);
            Assert.That(changed.Revision, Is.EqualTo(added.Revision + 1));
            Assert.That(host.RoutingRevision.ReadImages[candidate.NodeManager], Is.SameAs(updated.Object));
            Assert.That(changed.Registrations, Is.Empty);
            Assert.That(writer.IsCurrent, Is.True);
        }

        /// <summary>
        /// Stable registry metadata and a new source generation share one decision and routing revision.
        /// Aborting the same unit changes neither owner nor metadata.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task SlotChangeSetMixedOwnerMetadataPublishesOrAbortsTogetherAsync(bool commit)
        {
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            LifecycleResult initial = await lifecycle.ApplyAsync(
                [LifecycleChange.Add(CreateTrackingNodeManagementFactory(kGeneration1Value, _ => { }))])
                .ConfigureAwait(false);
            NodeManagerRegistration stable = RequireRegistration(initial.Registrations[0]);
            var stableImage = new Mock<INodeManagerReadImage>();
            stableImage.SetupGet(image => image.Owner).Returns(stable.NodeManager);
            await using ILifecycleWriter writer = await lifecycle.CaptureChanges().BeginAsync().ConfigureAwait(false);
            TrackingLifecycleNodeManager? manager = null;
            await using IPreparedChanges prepared = await writer.PrepareAsync(
                [LifecycleChange.Add(CreateTrackingNodeManagementFactory(
                    kSecondRegistrationValue, value => manager = value, kSecondModelNamespaceUri))])
                .ConfigureAwait(false);
            NodeManagerRegistration candidate = RequireRegistration(prepared.Registrations[0]);
            var candidateImage = new Mock<INodeManagerReadImage>();
            candidateImage.SetupGet(image => image.Owner).Returns(candidate.NodeManager);
            prepared.SetMetadata(stableImage.Object);
            prepared.SetMetadata(candidate.Slot, candidateImage.Object);
            Assert.That(() => prepared.SetMetadata(stable.Slot, stableImage.Object), Throws.InvalidOperationException);
            var host = (IDynamicNodeManagerBatchHost)m_server.CurrentInstance.NodeManager;
            NodeManagerRoutingTable routing = GetRoutingTable((MasterNodeManager)m_server.CurrentInstance.NodeManager);
            using var pinnedContext = new OperationContext(
                new RequestHeader(), null, RequestType.Read, RequestLifetime.None);
            pinnedContext.PinRouting(routing);
            NodeManagerRoutingTable.RoutingSnapshot before = pinnedContext.RoutingSnapshot!;
            Assert.That(host.RoutingRevision.ReadImages.ContainsKey(stable.NodeManager), Is.False);
            Assert.That(Array.IndexOf(before.NodeManagers, candidate.NodeManager), Is.EqualTo(-1));
            int decisions = 0;
            if (commit)
            {
                LifecycleResult result = await prepared.CommitAsync(_ =>
                {
                    decisions++;
                    Assert.That(host.RoutingRevision.ReadImages.ContainsKey(stable.NodeManager), Is.False);
                    return default;
                }, () =>
                {
                    Assert.That(host.RoutingRevision.ReadImages[stable.NodeManager], Is.SameAs(stableImage.Object));
                    Assert.That(host.RoutingRevision.ReadImages[candidate.NodeManager],
                        Is.SameAs(candidateImage.Object));
                    Assert.That(Array.IndexOf(host.RoutingRevision.NodeManagers, candidate.NodeManager),
                        Is.GreaterThanOrEqualTo(0));
                    Assert.That(pinnedContext.RoutingSnapshot, Is.SameAs(before));
                    Assert.That(before.ReadImages.ContainsKey(stable.NodeManager), Is.False);
                    Assert.That(Array.IndexOf(before.NodeManagers, candidate.NodeManager), Is.EqualTo(-1));
                }).ConfigureAwait(false);
                Assert.That(result.Revision, Is.EqualTo(initial.Revision + 1));
                Assert.That(result.Issues, Is.Empty);
            }
            await prepared.DisposeAsync().ConfigureAwait(false);
            Assert.That(decisions, Is.EqualTo(commit ? 1 : 0));
            Assert.That(prepared.IsCommitted, Is.EqualTo(commit));
            Assert.That(RequireLifecycleValue(manager).DisposeCount, Is.EqualTo(commit ? 0 : 1));
            Assert.That(lifecycle.Revision, Is.EqualTo(initial.Revision + (commit ? 1 : 0)));
            Assert.That(stable.Slot.TryGetCurrent(out NodeManagerSlotSnapshot current), Is.True);
            Assert.That(current.NodeManager, Is.SameAs(stable.NodeManager));
            Assert.That(host.RoutingRevision.ReadImages.ContainsKey(stable.NodeManager), Is.EqualTo(commit));
            Assert.That(host.RoutingRevision.ReadImages.ContainsKey(candidate.NodeManager), Is.EqualTo(commit));
            Assert.That(Array.IndexOf(host.RoutingRevision.NodeManagers, candidate.NodeManager),
                commit ? Is.GreaterThanOrEqualTo(0) : Is.EqualTo(-1));
            if (!commit)
            {
                Assert.That(host.RoutingRevision, Is.SameAs(before));
            }
        }

        /// <summary>
        /// Removed and replaced predecessor images are rejected before the decision.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task SlotChangeSetRejectsMetadataForWithdrawnOwnerAsync(bool replace)
        {
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            LifecycleResult initial = await lifecycle.ApplyAsync(
                [LifecycleChange.Add(CreateTrackingNodeManagementFactory(kGeneration1Value, _ => { }))])
                .ConfigureAwait(false);
            NodeManagerRegistration current = RequireRegistration(initial.Registrations[0]);
            await using IPreparedChanges prepared = await lifecycle.PrepareAsync(
                [replace
                    ? LifecycleChange.Replace(current.Slot,
                        CreateTrackingNodeManagementFactory(kGeneration2Value, _ => { }))
                    : LifecycleChange.Remove(current.Slot)]).ConfigureAwait(false);
            var image = new Mock<INodeManagerReadImage>();
            image.SetupGet(value => value.Owner).Returns(current.NodeManager);
            Assert.That(() => prepared.SetMetadata(image.Object), Throws.ArgumentException);
            Assert.That(() => prepared.SetMetadata((INodeManagerReadImage)null!), Throws.ArgumentNullException);
            var unregistered = new Mock<INodeManagerReadImage>();
            unregistered.SetupGet(value => value.Owner).Returns(Mock.Of<IAsyncNodeManager>());
            Assert.That(() => prepared.SetMetadata(unregistered.Object), Throws.ArgumentException);
            Assert.That(lifecycle.Revision, Is.EqualTo(initial.Revision));
        }

        /// <summary>
        /// The new capture retains pre-input evidence instead of recapturing when the writer begins.
        /// </summary>
        [TestCase("Routing")]
        [TestCase("Types")]
        [TestCase("Factory")]
        public async Task SlotChangeSetPreInputCaptureRejectsStaleBeginBeforeFactoryAsync(string changed)
        {
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            ILifecycleCapture capture = lifecycle.CaptureChanges();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var inputRequested = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseInput = new TaskCompletionSource<LifecycleChange>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Task<LifecycleChange> inputs = CaptureInputsAsync();
            var factory = new CountingNodeManagerFactory([kSecondModelNamespaceUri],
                CreateTrackingNodeManagementFactory(
                    kSecondRegistrationValue, _ => { }, kSecondModelNamespaceUri).CreateAsync);
            await inputRequested.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            switch (changed)
            {
                case "Routing":
                    await lifecycle.ApplyAsync(
                        [LifecycleChange.Add(CreateTrackingNodeManagementFactory(kGeneration1Value, _ => { }))])
                        .ConfigureAwait(false);
                    break;
                case "Types":
                    m_server.CurrentInstance.TypeTree.AddSubtype(new NodeId(99001), ObjectTypeIds.BaseObjectType);
                    break;
                default:
                    Assert.That(m_server.CurrentInstance.Factory.TryGetEncodeableType(
                        DataTypeIds.Range, out IEncodeableType? rangeType), Is.True);
                    m_server.CurrentInstance.Factory.Builder.AddEncodeableType(
                        new ExpandedNodeId(99002), rangeType!).Commit();
                    break;
            }
            releaseInput.TrySetResult(LifecycleChange.Add(factory));
            LifecycleChange acquired = await inputs.WaitAsync(timeout.Token).ConfigureAwait(false);
            await using ILifecycleWriter writer = await capture.BeginAsync(timeout.Token).ConfigureAwait(false);
            await Assert.ThatAsync(async () => await writer.PrepareAsync([acquired], timeout.Token)
                .ConfigureAwait(false), Throws.InvalidOperationException).ConfigureAwait(false);
            await Assert.ThatAsync(async () => await writer.ValidateAsync(
                [acquired], (_, _) => default, timeout.Token).ConfigureAwait(false),
                Throws.InvalidOperationException).ConfigureAwait(false);
            Assert.That(writer.IsCurrent, Is.False);
            Assert.That(factory.CreateCount, Is.Zero);

            Task<LifecycleChange> CaptureInputsAsync()
            {
                inputRequested.TrySetResult(true);
                return releaseInput.Task.WaitAsync(timeout.Token);
            }
        }

        /// <summary>
        /// Validation binds mixed-owner metadata but cannot decide, publish, or retain mutable candidates.
        /// </summary>
        [Test]
        public async Task SlotChangeSetValidationInspectsMixedMetadataWithoutPublicationAsync()
        {
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            LifecycleResult initial = await lifecycle.ApplyAsync(
                [LifecycleChange.Add(CreateTrackingNodeManagementFactory(kGeneration1Value, _ => { }))])
                .ConfigureAwait(false);
            NodeManagerRegistration stable = RequireRegistration(initial.Registrations[0]);
            var image = new Mock<INodeManagerReadImage>();
            image.SetupGet(value => value.Owner).Returns(stable.NodeManager);
            await using ILifecycleWriter writer = await lifecycle.CaptureChanges().BeginAsync().ConfigureAwait(false);
            TrackingLifecycleNodeManager? manager = null;
            IAsyncNodeManagerFactory factory = CreateTrackingNodeManagementFactory(
                kSecondRegistrationValue, value => manager = value, kSecondModelNamespaceUri);
            ArrayOf<string> namespaces = m_server.CurrentInstance.NamespaceUris.ToArrayOf();
            int decisions = 0;
            IPreparedChanges? inspected = null;
            await writer.ValidateAsync([LifecycleChange.Add(factory)], async (prepared, token) =>
            {
                inspected = prepared;
                Assert.That(prepared.Registrations.Count, Is.EqualTo(1));
                prepared.SetMetadata(image.Object);
                await Assert.ThatAsync(async () => await prepared.CommitAsync(_ =>
                {
                    decisions++;
                    return default;
                }, token).ConfigureAwait(false), Throws.InvalidOperationException).ConfigureAwait(false);
            }).ConfigureAwait(false);
            Assert.That(decisions, Is.Zero);
            Assert.That(RequireLifecycleValue(inspected).IsCommitted, Is.False);
            Assert.That(() => RequireLifecycleValue(inspected).SetMetadata(image.Object),
                Throws.InvalidOperationException);
            Assert.That(RequireLifecycleValue(manager).DisposeCount, Is.EqualTo(1));
            Assert.That(lifecycle.Revision, Is.EqualTo(initial.Revision));
            Assert.That(m_server.CurrentInstance.NamespaceUris.ToArrayOf(), Is.EqualTo(namespaces));
            var host = (IDynamicNodeManagerBatchHost)m_server.CurrentInstance.NodeManager;
            Assert.That(host.RoutingRevision.ReadImages.ContainsKey(stable.NodeManager), Is.False);
            await using IPreparedChanges next = await writer.PrepareAsync([LifecycleChange.Add(factory)])
                .ConfigureAwait(false);
            LifecycleResult result = await next.CommitAsync().ConfigureAwait(false);
            Assert.That(result.Revision, Is.EqualTo(initial.Revision + 1));
            Assert.That(writer.IsCurrent, Is.True);
        }

        /// <summary>
        /// Inspector failure and cancellation release private candidates without consuming writer evidence.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task SlotChangeSetValidationFailureReleasesCandidatesAsync(bool cancel)
        {
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            long revision = lifecycle.Revision;
            TrackingLifecycleNodeManager? manager = null;
            IAsyncNodeManagerFactory factory = CreateTrackingNodeManagementFactory(
                kGeneration1Value, value => manager = value);
            await using ILifecycleWriter writer = await lifecycle.BeginAsync().ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            await Assert.ThatAsync(async () => await writer.ValidateAsync(
                [LifecycleChange.Add(factory)], (_, _) =>
                {
                    if (cancel)
                    {
                        cancellation.Cancel();
                        return default;
                    }
                    throw new InvalidOperationException("Inspection failed.");
                }, cancellation.Token).ConfigureAwait(false),
                cancel ? Throws.InstanceOf<OperationCanceledException>() : Throws.InvalidOperationException)
                .ConfigureAwait(false);
            Assert.That(RequireLifecycleValue(manager).DisposeCount, Is.EqualTo(1));
            Assert.That(lifecycle.Revision, Is.EqualTo(revision));
            Assert.That(writer.IsCurrent, Is.True);
            await using IPreparedChanges next = await writer.PrepareAsync([LifecycleChange.Add(factory)])
                .ConfigureAwait(false);
            Assert.That(next.Registrations.Count, Is.EqualTo(1));
        }

        /// <summary>
        /// Cancellation and failed durable decisions consume private candidates without replaying factories.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task SlotChangeSetDecisionFailureAbortsWithoutRetryAsync(bool cancel)
        {
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            long revision = lifecycle.Revision;
            TrackingLifecycleNodeManager? manager = null;
            var factory = new CountingNodeManagerFactory([kModelNamespaceUri],
                CreateTrackingNodeManagementFactory(kGeneration1Value, value => manager = value).CreateAsync);
            await using IPreparedChanges prepared = await lifecycle.PrepareAsync([LifecycleChange.Add(factory)])
                .ConfigureAwait(false);
            int decisions = 0;
            using var cancellation = new CancellationTokenSource();
            if (cancel)
            {
                await cancellation.CancelAsync().ConfigureAwait(false);
            }
            await Assert.ThatAsync(async () => await prepared.CommitAsync(_ =>
            {
                decisions++;
                throw new InvalidOperationException("The durable decision failed.");
            }, cancellation.Token).ConfigureAwait(false),
                cancel ? Throws.InstanceOf<OperationCanceledException>() : Throws.InvalidOperationException)
                .ConfigureAwait(false);
            await Assert.ThatAsync(async () => await prepared.CommitAsync().ConfigureAwait(false),
                Throws.InvalidOperationException).ConfigureAwait(false);
            await prepared.DisposeAsync().ConfigureAwait(false);
            Assert.That(prepared.IsCommitted, Is.False);
            Assert.That(decisions, Is.EqualTo(cancel ? 0 : 1));
            Assert.That(factory.CreateCount, Is.EqualTo(1));
            Assert.That(RequireLifecycleValue(manager).DisposeCount, Is.EqualTo(1));
            Assert.That(lifecycle.Revision, Is.EqualTo(revision));
        }

        /// <summary>
        /// Empty and null entries are rejected before preparation effects.
        /// </summary>
        [Test]
        public async Task SlotChangeSetRejectsEmptyAndNullEntriesAsync()
        {
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            long revision = lifecycle.Revision;
            await Assert.ThatAsync(async () => await ((INodeManagerLifecycle)null!).ApplyAsync([])
                .ConfigureAwait(false),
                Throws.ArgumentNullException.With.Property(nameof(ArgumentException.ParamName)).EqualTo("lifecycle"))
                .ConfigureAwait(false);
            await Assert.ThatAsync(async () => await lifecycle.ApplyAsync([]).ConfigureAwait(false),
                Throws.ArgumentException).ConfigureAwait(false);
            await Assert.ThatAsync(async () => await lifecycle.ApplyAsync(ArrayOf<LifecycleChange>.Null)
                .ConfigureAwait(false), Throws.ArgumentException).ConfigureAwait(false);
            await Assert.ThatAsync(async () => await lifecycle.ApplyAsync([null!]).ConfigureAwait(false),
                Throws.ArgumentException).ConfigureAwait(false);
            Assert.That(lifecycle.Revision, Is.EqualTo(revision));
        }

        /// <summary>
        /// A malformed provider result is disposed rather than leaked during adapter construction.
        /// </summary>
        [Test]
        public async Task SlotChangeSetRejectsMalformedProviderAndDisposesPreparedOwnerAsync()
        {
            var provider = new Mock<INodeManagerLifecycle>();
            var prepared = new Mock<IPreparedNodeManagerBatch>();
            prepared.SetupGet(value => value.Registrations).Returns([]);
            provider.Setup(value => value.PrepareAsync(
                It.IsAny<ArrayOf<NodeManagerBatchChange>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(prepared.Object);
            await Assert.ThatAsync(async () => await provider.Object.PrepareAsync(
                [LifecycleChange.Add(CreateTrackingNodeManagementFactory(kGeneration1Value, _ => { }))])
                .ConfigureAwait(false),
                Throws.InvalidOperationException.With.Message.Contains("candidate registrations"))
                .ConfigureAwait(false);
            prepared.Verify(value => value.DisposeAsync(), Times.Once);
            prepared.Verify(value => value.CommitAsync(
                It.IsAny<Func<CancellationToken, ValueTask>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// An old provider cannot turn a slot reload into an unrelated addition without failing before decision.
        /// </summary>
        [Test]
        public async Task SlotChangeSetRejectsProviderReloadThatCreatesDifferentSlotAsync()
        {
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            LifecycleResult added = await lifecycle.ApplyAsync(
                [LifecycleChange.Add(CreateTrackingNodeManagementFactory(kGeneration1Value, _ => { }))])
                .ConfigureAwait(false);
            TrackingLifecycleNodeManager? candidate = null;
            IAsyncNodeManagerFactory factory = CreateTrackingNodeManagementFactory(
                kSecondRegistrationValue, value => candidate = value, kSecondModelNamespaceUri);
            await using IPreparedNodeManagerBatch unrelated = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(factory)]).ConfigureAwait(false);
            var provider = new Mock<INodeManagerLifecycle>();
            provider.Setup(value => value.PrepareAsync(
                It.IsAny<ArrayOf<NodeManagerBatchChange>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(unrelated);
            await Assert.ThatAsync(async () => await provider.Object.PrepareAsync(
                [LifecycleChange.Reload(added.Registrations[0].Slot, factory)]).ConfigureAwait(false),
                Throws.InvalidOperationException.With.Message.Contains("target slot")).ConfigureAwait(false);
            Assert.That(unrelated.IsCommitted, Is.False);
            Assert.That(RequireLifecycleValue(candidate).DisposeCount, Is.EqualTo(1));
            Assert.That(lifecycle.Revision, Is.EqualTo(added.Revision));
        }

        /// <summary>
        /// Physical completion status and legacy result properties delegate to the same task and result owner.
        /// </summary>
        [Test]
        public void SlotChangeSetRetirementStatusUsesAuthoritativeTask()
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var failed = Task.FromException(new InvalidOperationException("Earlier cleanup attempt failed."));
            Task pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously).Task;
            (Task Retirement, NodeManagerRetirementStatus Status)[] cases =
            [
                (pending, NodeManagerRetirementStatus.Pending),
                (Task.CompletedTask, NodeManagerRetirementStatus.Completed),
                (failed, NodeManagerRetirementStatus.CompletedWithFailure),
                (Task.FromCanceled(cancellation.Token), NodeManagerRetirementStatus.Canceled)
            ];
            foreach ((Task retirement, NodeManagerRetirementStatus status) in cases)
            {
                var result = new LifecycleResult([], 3, null, 42, [], retirement);
                var compatibility = new NodeManagerBatchResult(result, [], []);
                Assert.That(result.RetirementStatus, Is.EqualTo(status));
                Assert.That(compatibility.RetirementStatus, Is.EqualTo(status));
                Assert.That(compatibility.CommittedResult, Is.SameAs(result));
                Assert.That(compatibility.Retirement, Is.SameAs(result.Retired));
                Assert.That(compatibility.Revision, Is.EqualTo(result.Revision));
                Assert.That(compatibility.Retired, Is.EqualTo(result.RetiredCount));
            }
            Assert.That(failed.Exception, Is.Not.Null);
        }

        /// <summary>
        /// The explicit factory adapter preserves namespaces and refuses canceled creation before the sync factory.
        /// </summary>
        [Test]
        public async Task SlotChangeSetAsAsyncPreservesFactoryAndCancellationAsync()
        {
            var factory = new Mock<INodeManagerFactory>();
            factory.SetupGet(value => value.NamespacesUris).Returns([kModelNamespaceUri]);
            var manager = new Mock<INodeManager>();
            factory.Setup(value => value.Create(It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>()))
                .Returns(manager.Object);
            IAsyncNodeManagerFactory adapted = factory.Object.AsAsync();
            Assert.That(adapted.NamespacesUris[0], Is.EqualTo(kModelNamespaceUri));
            var configuration = new ApplicationConfiguration();
            IAsyncNodeManager created = await adapted.CreateAsync(m_server.CurrentInstance, configuration)
                .ConfigureAwait(false);
            Assert.That(created, Is.InstanceOf<AsyncNodeManagerAdapter>());
            Assert.That(((AsyncNodeManagerAdapter)created).SyncNodeManager, Is.SameAs(manager.Object));
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync().ConfigureAwait(false);
            await Assert.ThatAsync(async () => await adapted.CreateAsync(
                m_server.CurrentInstance, configuration, cancellation.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            factory.Verify(value => value.Create(It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>()),
                Times.Once);
            Assert.That(() => ((INodeManagerFactory)null!).AsAsync(), Throws.ArgumentNullException);
        }

        /// <summary>
        /// Commit and reconciliation complete while admitted work keeps physical retirement pending.
        /// </summary>
        [Test]
        public async Task SlotChangeSetRetirementWaitsForAdmissionWithoutBlockingCommitAsync()
        {
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            TrackingLifecycleNodeManager? manager = null;
            LifecycleResult added = await lifecycle.ApplyAsync(
                [LifecycleChange.Add(CreateTrackingNodeManagementFactory(kGeneration1Value, value => manager = value))])
                .ConfigureAwait(false);
            NodeManagerRegistration registration = RequireRegistration(added.Registrations[0]);
            Assert.That(registration.Identity.Gate.TryEnter(out int stripe), Is.True);
            LifecycleResult removed;
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                removed = await lifecycle.ApplyAsync([LifecycleChange.Remove(registration.Slot)], timeout.Token)
                    .ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(removed.RetirementStatus, Is.EqualTo(NodeManagerRetirementStatus.Pending));
                    Assert.That(removed.Retired.IsCompleted, Is.False);
                    Assert.That(RequireLifecycleValue(manager).DisposeCount, Is.Zero);
                    Assert.That(registration.Slot.TryGetCurrent(out _), Is.False);
                    Assert.That(removed.Issues, Is.Empty);
                }
            }
            finally
            {
                registration.Identity.Gate.Exit(stripe);
            }
            await removed.Retired.ConfigureAwait(false);
            Assert.That(removed.RetirementStatus, Is.EqualTo(NodeManagerRetirementStatus.Completed));
            Assert.That(RequireLifecycleValue(manager).DisposeCount, Is.EqualTo(1));
        }

        /// <summary>
        /// A legacy result retains its exact strong registration handle after publication ownership ends.
        /// </summary>
        [Test]
        public async Task SlotChangeSetLegacyResultRetainsOriginalRegistrationHandleAsync()
        {
            (NodeManagerBatchResult result,
                WeakReference<NodeManagerRegistration> registrationReference,
                WeakReference<IAsyncNodeManager> managerReference) =
                await CreateLegacyResultAfterRetirementAsync().ConfigureAwait(false);
            CollectLifecycleOwnershipTargets();

            bool registrationAvailable = registrationReference.TryGetTarget(out NodeManagerRegistration? original);
            bool managerAvailable = managerReference.TryGetTarget(out IAsyncNodeManager? manager);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.Registrations.Count, Is.EqualTo(1));
                Assert.That(registrationAvailable, Is.True);
                Assert.That(result.Registrations[0], Is.SameAs(original));
                Assert.That(managerAvailable, Is.True);
                Assert.That(result.Registrations[0].NodeManager, Is.SameAs(manager));
                Assert.That(original!.NodeManager, Is.SameAs(manager));
            }
            GC.KeepAlive(result);
        }

        /// <summary>
        /// A modern result retains value metadata but not its weak registration or published manager.
        /// </summary>
        [Test]
        public async Task SlotChangeSetLifecycleResultMetadataDoesNotRetainRegistrationOrPublishedManagerAsync()
        {
            (LifecycleResult result,
                WeakReference<NodeManagerRegistration> registrationReference,
                WeakReference<IAsyncNodeManager> managerReference,
                NodeManagerSlot slot,
                Guid id,
                long generation,
                string[] namespaceUris) =
                await CreateModernResultAfterRetirementAsync().ConfigureAwait(false);
            CollectLifecycleOwnershipTargets();

            NodeManagerRegistrationSnapshot snapshot = result.Registrations[0];
            bool registrationAvailable = registrationReference.TryGetTarget(out _);
            bool managerAvailable = managerReference.TryGetTarget(out _);
            bool snapshotManagerAvailable = snapshot.TryGetNodeManager(out _);
            bool snapshotRegistrationAvailable = snapshot.TryGetRegistration(out _);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(registrationAvailable, Is.False);
                Assert.That(managerAvailable, Is.False);
                Assert.That(snapshotManagerAvailable, Is.False);
                Assert.That(snapshotRegistrationAvailable, Is.False);
                Assert.That(snapshot.Slot, Is.SameAs(slot));
                Assert.That(snapshot.Id, Is.EqualTo(id));
                Assert.That(snapshot.Generation, Is.EqualTo(generation));
                Assert.That(snapshot.NamespaceUris.ToArray(), Is.EqualTo(namespaceUris));
                Assert.That(result.Slots.Contains(slot), Is.True);
                Assert.That(result.PublishedSlots.ToArray()!
                    .Single(value => ReferenceEquals(value.Slot, slot)).Generation, Is.EqualTo(generation));
                Assert.That(slot.TryGetCurrent(out _), Is.False);
            }
            GC.KeepAlive(result);
        }

        /// <summary>
        /// Creates and retires a legacy publication, returning only its owning result and weak observations.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private async Task<(
            NodeManagerBatchResult Result,
            WeakReference<NodeManagerRegistration> Registration,
            WeakReference<IAsyncNodeManager> Manager)> CreateLegacyResultAfterRetirementAsync()
        {
            INodeManagerBatchLifecycle lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            NodeManagerBatchResult result;
            await using (IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(CreateTrackingNodeManagementFactory(
                    kGeneration1Value, _ => { }, kModelNamespaceUri))]).ConfigureAwait(false))
            {
                result = await prepared.CommitAsync(_ => default).ConfigureAwait(false);
            }

            NodeManagerRegistration? original = result.Registrations[0];
            var registrationReference = new WeakReference<NodeManagerRegistration>(original);
            var managerReference = new WeakReference<IAsyncNodeManager>(original.NodeManager);
            await using (IPreparedNodeManagerBatch removal = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Remove(original)]).ConfigureAwait(false))
            {
                NodeManagerBatchResult retired = await removal.CommitAsync(_ => default).ConfigureAwait(false);
                await retired.Retirement.ConfigureAwait(false);
            }
            original = null;
            return (result, registrationReference, managerReference);
        }

        /// <summary>
        /// Creates and retires a modern publication without returning any strong candidate handle.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private async Task<(
            LifecycleResult Result,
            WeakReference<NodeManagerRegistration> Registration,
            WeakReference<IAsyncNodeManager> Manager,
            NodeManagerSlot Slot,
            Guid Id,
            long Generation,
            string[] NamespaceUris)> CreateModernResultAfterRetirementAsync()
        {
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            LifecycleResult result = await lifecycle.ApplyAsync(
                [LifecycleChange.Add(CreateTrackingNodeManagementFactory(
                    kGeneration1Value, _ => { }, kModelNamespaceUri))]).ConfigureAwait(false);
            NodeManagerRegistrationSnapshot snapshot = result.Registrations[0];
            if (!snapshot.TryGetNodeManager(out IAsyncNodeManager? manager) || manager is null ||
                !snapshot.TryGetRegistration(out NodeManagerRegistration? registration) || registration is null)
            {
                throw new InvalidOperationException("The live lifecycle owner did not expose its candidate.");
            }

            var registrationReference = new WeakReference<NodeManagerRegistration>(registration);
            var managerReference = new WeakReference<IAsyncNodeManager>(manager);
            NodeManagerSlot slot = snapshot.Slot;
            Guid id = snapshot.Id;
            long generation = snapshot.Generation;
            string[] namespaceUris = snapshot.NamespaceUris.ToArray() ?? [];

            LifecycleResult removed = await lifecycle.ApplyAsync([LifecycleChange.Remove(slot)]).ConfigureAwait(false);
            await removed.Retired.ConfigureAwait(false);
            registration = null;
            manager = null;
            return (result, registrationReference, managerReference, slot, id, generation, namespaceUris);
        }

        /// <summary>
        /// Reacquires a strong registration while the lifecycle still owns its NodeManager.
        /// </summary>
        private static NodeManagerRegistration RequireRegistration(NodeManagerRegistrationSnapshot snapshot)
        {
            if (snapshot.TryGetRegistration(out NodeManagerRegistration? registration) && registration is not null)
            {
                return registration;
            }
            throw new InvalidOperationException("The live lifecycle owner did not expose its registration.");
        }

        /// <summary>
        /// Performs full collection cycles without extending observed targets' lifetimes.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void CollectLifecycleOwnershipTargets()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

    }
}

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Exercises change-set cleanup on a real, unstarted server facade without opening listeners.
    /// </summary>
    [TestFixture]
    [Category("NodeManagerLifecycle")]
    public sealed class NodeManagerLifecycleChangeSetCleanupTests
    {
        private const string kNamespaceUri = "urn:test:change-set-cleanup";

        /// <summary>
        /// Stale commit itself disposes the candidate and releases admission before caller disposal.
        /// </summary>
        [Test]
        public async Task SlotChangeSetStaleCommitDisposesCandidateAndDrainsOperationBeforeCallerDisposalAsync()
        {
            await using var harness = new ChangeSetCleanupHarness();
            INodeManagerLifecycle lifecycle = harness.Server.NodeManagerLifecycle;
            LifecycleResult added = await lifecycle.ApplyAsync(
                [LifecycleChange.Add(CreateFactory(CreateManager(kNamespaceUri)))])
                .ConfigureAwait(false);
            await added.Retired.ConfigureAwait(false);
            LifecycleWriter writer = GetWriter(lifecycle);
            Assert.That(writer.ActiveOperationCount, Is.Zero);

            Mock<IAsyncNodeManager> candidate = CreateManager(kNamespaceUri);
            int candidateFactoryCalls = 0;
            var candidateFactory = CreateFactory(candidate, () => candidateFactoryCalls++);
            IPreparedChanges prepared = await lifecycle.PrepareAsync(
                [LifecycleChange.Add(candidateFactory)]).ConfigureAwait(false);
            Task? drained = null;
            try
            {
                LifecycleResult winner = await lifecycle.ApplyAsync(
                    [LifecycleChange.Add(CreateFactory(CreateManager(kNamespaceUri)))])
                    .ConfigureAwait(false);
                await winner.Retired.ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    candidate.As<IDisposable>().Verify(manager => manager.Dispose(), Times.Never);
                    Assert.That(writer.ActiveOperationCount, Is.EqualTo(1));
                    Assert.That(candidateFactoryCalls, Is.EqualTo(1));
                }

                int decisions = 0;
                await Assert.ThatAsync(async () => await prepared.CommitAsync(_ =>
                {
                    decisions++;
                    return default;
                }).ConfigureAwait(false), Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
                await Assert.ThatAsync(async () => await prepared.CommitAsync(_ =>
                {
                    decisions++;
                    return default;
                }).ConfigureAwait(false), Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);

                // Ask the actual writer to drain, then inspect its signal before caller disposal can mask a leak.
                drained = await writer.BeginShutdownAsync(CancellationToken.None).ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    candidate.As<IDisposable>().Verify(manager => manager.Dispose(), Times.Once);
                    Assert.That(writer.ActiveOperationCount, Is.Zero);
                    Assert.That(drained.Status, Is.EqualTo(TaskStatus.RanToCompletion));
                    Assert.That(decisions, Is.Zero);
                    Assert.That(prepared.IsCommitted, Is.False);
                    Assert.That(lifecycle.Revision, Is.EqualTo(winner.Revision));
                    Assert.That(added.Registrations[0].Slot.TryGetCurrent(
                        out NodeManagerSlotSnapshot current), Is.True);
                    Assert.That(current.Generation, Is.EqualTo(added.Registrations[0].Generation));
                }
            }
            finally
            {
                try
                {
                    await prepared.DisposeAsync().ConfigureAwait(false);
                }
                finally
                {
                    if (drained is not null)
                    {
                        await drained.ConfigureAwait(false);
                        writer.EndShutdown(prepared: false);
                        writer.BeginStartup(static () => { });
                    }
                }
            }

            candidate.As<IDisposable>().Verify(manager => manager.Dispose(), Times.Once);
            Assert.That(writer.ActiveOperationCount, Is.Zero);
        }

        /// <summary>
        /// A rejected durable decision disposes the candidate and releases admission before caller disposal.
        /// </summary>
        [Test]
        public async Task SlotChangeSetFailedDecisionDisposesCandidateAndDrainsOperationBeforeCallerDisposalAsync()
        {
            await using var harness = new ChangeSetCleanupHarness();
            INodeManagerLifecycle lifecycle = harness.Server.NodeManagerLifecycle;
            LifecycleResult added = await lifecycle.ApplyAsync(
                [LifecycleChange.Add(CreateFactory(CreateManager(kNamespaceUri)))])
                .ConfigureAwait(false);
            await added.Retired.ConfigureAwait(false);
            LifecycleWriter writer = GetWriter(lifecycle);
            Assert.That(writer.ActiveOperationCount, Is.Zero);

            Mock<IAsyncNodeManager> candidate = CreateManager(kNamespaceUri);
            int candidateFactoryCalls = 0;
            IAsyncNodeManagerFactory candidateFactory = CreateFactory(candidate, () => candidateFactoryCalls++);
            IPreparedChanges prepared = await lifecycle.PrepareAsync(
                [LifecycleChange.Add(candidateFactory)]).ConfigureAwait(false);
            Task? drained = null;
            try
            {
                using (Assert.EnterMultipleScope())
                {
                    candidate.As<IDisposable>().Verify(manager => manager.Dispose(), Times.Never);
                    Assert.That(writer.ActiveOperationCount, Is.EqualTo(1));
                    Assert.That(candidateFactoryCalls, Is.EqualTo(1));
                }

                int decisions = 0;
                var failure = new InvalidOperationException("The durable decision was rejected.");
                await Assert.ThatAsync(async () => await prepared.CommitAsync(_ =>
                {
                    decisions++;
                    throw failure;
                }).ConfigureAwait(false), Throws.Exception.SameAs(failure)).ConfigureAwait(false);
                await Assert.ThatAsync(async () => await prepared.CommitAsync(_ =>
                {
                    decisions++;
                    return default;
                }).ConfigureAwait(false), Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);

                drained = await writer.BeginShutdownAsync(CancellationToken.None).ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    candidate.As<IDisposable>().Verify(manager => manager.Dispose(), Times.Once);
                    Assert.That(writer.ActiveOperationCount, Is.Zero);
                    Assert.That(drained.Status, Is.EqualTo(TaskStatus.RanToCompletion));
                    Assert.That(decisions, Is.EqualTo(1));
                    Assert.That(prepared.IsCommitted, Is.False);
                    Assert.That(lifecycle.Revision, Is.EqualTo(added.Revision));
                    Assert.That(added.Registrations[0].Slot.TryGetCurrent(
                        out NodeManagerSlotSnapshot current), Is.True);
                    Assert.That(current.Generation, Is.EqualTo(added.Registrations[0].Generation));
                }
            }
            finally
            {
                try
                {
                    await prepared.DisposeAsync().ConfigureAwait(false);
                }
                finally
                {
                    if (drained is not null)
                    {
                        await drained.ConfigureAwait(false);
                        writer.EndShutdown(prepared: false);
                        writer.BeginStartup(static () => { });
                    }
                }
            }

            candidate.As<IDisposable>().Verify(manager => manager.Dispose(), Times.Once);
            Assert.That(writer.ActiveOperationCount, Is.Zero);
        }

        private static Mock<IAsyncNodeManager> CreateManager(string namespaceUri)
        {
            var manager = new Mock<IAsyncNodeManager>();
            manager.As<IDisposable>();
            manager.SetupGet(value => value.NamespaceUris).Returns([namespaceUri]);
            manager.SetupGet(value => value.SyncNodeManager).Returns(new Mock<INodeManager>().Object);
            manager.Setup(value => value.CreateAddressSpaceAsync(
                    It.IsAny<System.Collections.Generic.IDictionary<NodeId,
                        System.Collections.Generic.IList<IReference>>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            manager.Setup(value => value.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            return manager;
        }

        private static IAsyncNodeManagerFactory CreateFactory(
            Mock<IAsyncNodeManager> manager,
            Action? created = null)
        {
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .Returns((IServerInternal _, ApplicationConfiguration _, CancellationToken _) =>
                {
                    created?.Invoke();
                    return new ValueTask<IAsyncNodeManager>(manager.Object);
                });
            return factory.Object;
        }

        private static LifecycleWriter GetWriter(INodeManagerLifecycle lifecycle)
        {
            return (LifecycleWriter)(typeof(NodeManagerLifecycle).GetField(
                "m_writer",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(lifecycle) ?? throw new InvalidOperationException("The lifecycle writer is unavailable."));
        }

        /// <summary>
        /// Creates the actual server lifecycle and master manager without starting the server or binding ports.
        /// </summary>
        private sealed class ChangeSetCleanupHarness : IAsyncDisposable
        {
            public ChangeSetCleanupHarness()
            {
                ITelemetryContext telemetry = Opc.Ua.Tests.NUnitTelemetryContext.Create();
                var configuration = new ApplicationConfiguration
                {
                    ApplicationUri = "urn:test:change-set-cleanup:server",
                    ServerConfiguration = new ServerConfiguration()
                };
                Server = new StandardServer(telemetry) { LoadComplexTypes = false };
                Data = new ServerInternalData(new ServerProperties(), configuration,
                    new ServiceMessageContext(telemetry, EncodeableFactory.Create()));
                Data.NamespaceUris.GetIndexOrAppend(DeterministicServerMock.TestNamespaceUri);
                Data.TypeTree.AddSubtype(DataTypeIds.BaseDataType, NodeId.Null);
                Data.TypeTree.AddSubtype(DataTypeIds.Structure, DataTypeIds.BaseDataType);
                var status = new ServerStatusValue(new ServerStatusState(null),
                    new ServerStatusDataType { State = ServerState.Running }, new Lock());
                typeof(ServerInternalData).GetProperty("NonThreadSafeStatus",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(Data, status);
                typeof(ServerBase).GetProperty("Configuration",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .SetValue(Server, configuration);
                typeof(StandardServer).GetField("m_serverInternal",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .SetValue(Server, Data);
                var mainFactory = new Mock<IMainNodeManagerFactory>();
                var configurationNodeManager = new Mock<IConfigurationNodeManager>();
                configurationNodeManager.As<IDiagnosticsNodeManager>()
                    .Setup(value => value.FindPredefinedNode<ServerObjectState>(ObjectIds.Server))
                    .Returns(new ServerObjectState(null));
                mainFactory.Setup(value => value.CreateConfigurationNodeManager())
                    .Returns(configurationNodeManager.Object);
                mainFactory.Setup(value => value.CreateCoreNodeManager(It.IsAny<ushort>()))
                    .Returns(new Mock<ICoreNodeManager>().Object);
                Data.SetMainNodeManagerFactory(mainFactory.Object);
                ServingManager = CreateManager(DeterministicServerMock.TestNamespaceUri);
                Master = new MasterNodeManager(Data, configuration, null, [ServingManager.Object], null);
                Data.SetNodeManager(Master);
                Assert.That(Data.IsRunning, Is.True);
                Assert.That(Master.AsyncNodeManagers, Does.Contain(ServingManager.Object));
            }

            public StandardServer Server { get; }
            public ServerInternalData Data { get; }
            public MasterNodeManager Master { get; }
            public Mock<IAsyncNodeManager> ServingManager { get; }

            public async ValueTask DisposeAsync()
            {
                // This facade was never started, so the harness owns no listeners or background server work.
                ((NodeManagerLifecycle)Server.NodeManagerLifecycle).Dispose();
                typeof(StandardServer).GetField("m_serverInternal",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .SetValue(Server, null);
                await Data.DisposeAsync().ConfigureAwait(false);
                Server.Dispose();
            }
        }
    }
}
