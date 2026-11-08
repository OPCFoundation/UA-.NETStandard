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
using Opc.Ua.Schema;

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
                new NodeManagerReloadCommittedException(
                    committedRegistration, result, "reload", originalFailure),
                new NodeManagerRemovalCommittedException(
                    committedRegistration, compatibilityResult, originalFailure)
            ];
            Assert.That(shims.All(shim => ReferenceEquals(shim.CommittedResult, result)), Is.True);
            Assert.That(((NodeManagerReloadCommittedException)shims[2]).Result, Is.Null);
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
            using var pinnedContext = new OperationContext(
                new RequestHeader(), null, RequestType.Read, RequestLifetime.None);
            ((IRequestRouting)m_server.CurrentInstance.NodeManager).PinRequest(pinnedContext);
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
        /// A superseded reconciliation result and its completed tasks do not retain the physically retired manager.
        /// </summary>
        [Test]
        public async Task SupersededLifecycleResultDoesNotRetainRetiredManagerAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            (LifecycleResult published,
                LifecycleResult removed,
                ArrayOf<NodeManagerReconciliationIssue> pendingIssues,
                Task retry,
                Exception failure,
                WeakReference<NodeManagerRegistration> registration,
                WeakReference<IAsyncNodeManager> manager,
                WeakReference<NodeManagerGeneration> generation,
                int[] attempts) = await CreateSupersededResultAfterRetirementAsync(timeout.Token)
                    .ConfigureAwait(false);
            Task retirement = removed.Retired;
            await Task.Yield();
            for (int attempt = 0; attempt < 3; attempt++)
            {
                CollectLifecycleOwnershipTargets();
            }

            NodeManagerRegistrationSnapshot snapshot = published.Registrations[0];
            using (Assert.EnterMultipleScope())
            {
                Assert.That(registration.TryGetTarget(out _), Is.False);
                Assert.That(manager.TryGetTarget(out _), Is.False);
                Assert.That(generation.TryGetTarget(out _), Is.False);
                Assert.That(snapshot.TryGetNodeManager(out _), Is.False);
                Assert.That(snapshot.TryGetRegistration(out _), Is.False);
                Assert.That(snapshot.Slot.TryGetCurrent(out _), Is.False);
                Assert.That(snapshot.Generation, Is.EqualTo(1));
                Assert.That(published.PublishedSlots.ToArray()!
                    .Single(value => ReferenceEquals(value.Slot, snapshot.Slot)).Generation, Is.EqualTo(1));
                Assert.That(published.Revision, Is.LessThan(removed.Revision));
                Assert.That(published.RetirementStatus, Is.EqualTo(NodeManagerRetirementStatus.Completed));
                Assert.That(removed.RetirementStatus, Is.EqualTo(NodeManagerRetirementStatus.Completed));
                Assert.That(retirement.Status, Is.EqualTo(TaskStatus.RanToCompletion));
                Assert.That(retry.Status, Is.EqualTo(TaskStatus.RanToCompletion));
                Assert.That(attempts[0], Is.EqualTo(2));
                Assert.That(pendingIssues.Count, Is.EqualTo(1));
                Assert.That(pendingIssues[0].InitialFailure, Is.SameAs(failure));
                Assert.That(pendingIssues[0].CurrentFailure, Is.SameAs(failure));
                Assert.That(pendingIssues[0].IsRetryable, Is.True);
                Assert.That(pendingIssues[0].IsSuperseded, Is.False);
                Assert.That(published.Issues.Count, Is.EqualTo(1));
                Assert.That(published.Issues[0].Stage, Is.EqualTo(NodeManagerReconciliationStage.ApplicationState));
                Assert.That(published.Issues[0].InitialFailure, Is.SameAs(failure));
                Assert.That(published.Issues[0].IsRetryable, Is.False);
                Assert.That(published.Issues[0].IsSuperseded, Is.True);
                Assert.That(((AggregateException)published.CleanupFailure!).Flatten().InnerExceptions,
                    Has.Exactly(1).SameAs(failure));
            }
            await published.RetryReconciliationAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(attempts[0], Is.EqualTo(2));
            GC.KeepAlive(published);
            GC.KeepAlive(removed);
            GC.KeepAlive(pendingIssues);
            GC.KeepAlive(retry);
            GC.KeepAlive(retirement);
            GC.KeepAlive(failure);
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
        /// Publishes a failing application callback, retries it, then supersedes and retires its generation.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private async Task<(
            LifecycleResult Published,
            LifecycleResult Removed,
            ArrayOf<NodeManagerReconciliationIssue> PendingIssues,
            Task Retry,
            Exception Failure,
            WeakReference<NodeManagerRegistration> Registration,
            WeakReference<IAsyncNodeManager> Manager,
            WeakReference<NodeManagerGeneration> Generation,
            int[] Attempts)> CreateSupersededResultAfterRetirementAsync(CancellationToken cancellationToken)
        {
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            var failure = new InvalidOperationException("Application state publication failed.");
            int[] attempts = [0];
            LifecycleResult published;
            await using (IPreparedChanges prepared = await lifecycle.PrepareAsync(
                [LifecycleChange.Add(CreateTrackingNodeManagementFactory(kGeneration1Value, _ => { }))],
                cancellationToken).ConfigureAwait(false))
            {
                published = await prepared.CommitAsync(
                    static _ => default,
                    CreateFailingApplicationStateCallback(failure, attempts),
                    cancellationToken).ConfigureAwait(false);
                Assert.That(prepared.IsCommitted, Is.True);
            }

            NodeManagerRegistrationSnapshot snapshot = published.Registrations[0];
            NodeManagerRegistration? registration = RequireRegistration(snapshot);
            IAsyncNodeManager? manager = registration.NodeManager;
            var registrationReference = new WeakReference<NodeManagerRegistration>(registration);
            var managerReference = new WeakReference<IAsyncNodeManager>(manager);
            var generationReference = new WeakReference<NodeManagerGeneration>(registration.Identity);
            ArrayOf<NodeManagerReconciliationIssue> pendingIssues = published.Issues;
            using (Assert.EnterMultipleScope())
            {
                Assert.That(pendingIssues.Count, Is.EqualTo(1));
                Assert.That(pendingIssues[0].Stage, Is.EqualTo(NodeManagerReconciliationStage.ApplicationState));
                Assert.That(pendingIssues[0].InitialFailure, Is.SameAs(failure));
                Assert.That(pendingIssues[0].IsRetryable, Is.True);
                Assert.That(attempts[0], Is.EqualTo(1));
                Assert.That(snapshot.TryGetNodeManager(out IAsyncNodeManager? current), Is.True);
                Assert.That(current, Is.SameAs(manager));
                current = null;
            }

            Task retry = published.RetryReconciliationAsync(cancellationToken).AsTask();
            await retry.WaitAsync(cancellationToken).ConfigureAwait(false);
            Assert.That(attempts[0], Is.EqualTo(2));
            LifecycleResult removed = await lifecycle.ApplyAsync(
                [LifecycleChange.Remove(snapshot.Slot)], cancellationToken).ConfigureAwait(false);
            await removed.Retired.WaitAsync(cancellationToken).ConfigureAwait(false);
            var trackingManager = (TrackingLifecycleNodeManager)manager;
            using (Assert.EnterMultipleScope())
            {
                Assert.That(trackingManager.DeleteAddressSpaceCount, Is.EqualTo(1));
                Assert.That(trackingManager.DisposeCount, Is.EqualTo(1));
            }
            registration = null;
            manager = null;
            return (published, removed, pendingIssues, retry, failure,
                registrationReference, managerReference, generationReference, attempts);
        }

        /// <summary>
        /// Creates a failing callback that retains only its exception and attempt counter.
        /// </summary>
        private static Action CreateFailingApplicationStateCallback(Exception failure, int[] attempts)
        {
            return () =>
            {
                attempts[0]++;
                throw failure;
            };
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
    /// Exercises the real lifecycle and writer over server internals without starting runtime workers.
    /// </summary>
    [TestFixture]
    [Category("NodeManagerLifecycle")]
    public sealed class NodeManagerLifecycleChangeSetCleanupTests
    {
        private const string kNamespaceUri = "urn:test:change-set-cleanup";

        /// <summary>
        /// Retained cleanup and validation tasks do not own candidates after their owners finish cleanup.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task CompletedPreparedDisposalTasksDoNotRetainPrivateCandidatesAsync(bool failCleanup)
        {
            (WeakReference candidate,
                WeakReference metadata,
                Task disposal,
                IPreparedChanges prepared,
                CleanupCallCounts cleanupCalls) =
                await CompletePrivateCandidateDisposalAsync(failCleanup).ConfigureAwait(false);
            // Do not run collection inline in the producer's completion callback.
            await Task.Yield();
            try
            {
                if (failCleanup)
                {
                    for (int attempt = 0; attempt < 3 && metadata.IsAlive; attempt++)
                    {
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        GC.Collect();
                    }
                    AssertFailedPreparedCleanupRetainsCandidate(prepared, candidate, metadata, disposal, cleanupCalls);
                }

                await prepared.DisposeAsync().ConfigureAwait(false);
                await Task.Yield();
                for (int attempt = 0; attempt < 3 && candidate.IsAlive; attempt++)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                }
                NodeManagerRegistrationSnapshot snapshot = prepared.Registrations[0];
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(candidate.IsAlive, Is.False);
                    Assert.That(metadata.IsAlive, Is.False);
                    Assert.That(disposal.Status, Is.EqualTo(
                        failCleanup ? TaskStatus.Faulted : TaskStatus.RanToCompletion));
                    Assert.That(prepared.IsCommitted, Is.False);
                    Assert.That(prepared.Registrations.Count, Is.EqualTo(1));
                    Assert.That(prepared.Slots.Count, Is.EqualTo(1));
                    Assert.That(snapshot.Slot, Is.SameAs(prepared.Slots[0]));
                    Assert.That(snapshot.TryGetNodeManager(out _), Is.False);
                    Assert.That(snapshot.TryGetRegistration(out _), Is.False);
                    Assert.That(snapshot.Slot.TryGetCurrent(out _), Is.False);
                    Assert.That(cleanupCalls.DeleteCount, Is.EqualTo(1));
                    Assert.That(cleanupCalls.DisposeCount, Is.EqualTo(1));
                }
                await prepared.DisposeAsync().ConfigureAwait(false);
                Assert.That(cleanupCalls.DeleteCount, Is.EqualTo(1));
                Assert.That(cleanupCalls.DisposeCount, Is.EqualTo(1));
            }
            finally
            {
                try
                {
                    await prepared.DisposeAsync().ConfigureAwait(false);
                }
                finally
                {
                    GC.KeepAlive(disposal);
                    GC.KeepAlive(prepared);
                }
            }
            await AssertRetainedValidationReleasesCandidateAsync(failInspection: failCleanup).ConfigureAwait(false);
        }

        /// <summary>
        /// Concurrent prepared-unit disposal joins the actual abort owner and its cleanup outcome.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task PreparedUnitDisposalJoinsAbortAndSharesCleanupOutcomeAsync(bool failCleanup)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using var harness = new ChangeSetCleanupHarness();
            Mock<IAsyncNodeManager> candidate = CreateManager(kNamespaceUri);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var failure = new InvalidOperationException("Private candidate abort failed.");
            candidate.Setup(value => value.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()))
                .Returns(async () =>
                {
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                    if (failCleanup)
                    {
                        throw failure;
                    }
                });
            IPreparedChanges prepared = await harness.Lifecycle.PrepareAsync(
                [LifecycleChange.Add(CreateFactory(candidate))], timeout.Token).ConfigureAwait(false);
            Task disposal = prepared.DisposeAsync().AsTask();
            Task? joined = null;
            Task? drained = null;
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                joined = prepared.DisposeAsync().AsTask();
                drained = await harness.Writer.BeginShutdownAsync(timeout.Token).ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(disposal.IsCompleted, Is.False);
                    Assert.That(joined.IsCompleted, Is.False);
                    Assert.That(drained.IsCompleted, Is.False);
                    Assert.That(harness.Writer.ActiveOperationCount, Is.EqualTo(1));
                    candidate.As<IDisposable>().Verify(value => value.Dispose(), Times.Never);
                }
                release.TrySetResult(true);
                await ObserveCleanupAsync(disposal).ConfigureAwait(false);
                await ObserveCleanupAsync(joined).ConfigureAwait(false);
                await drained.WaitAsync(timeout.Token).ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(harness.Writer.ActiveOperationCount, Is.Zero);
                    Assert.That(prepared.IsCommitted, Is.False);
                    if (failCleanup)
                    {
                        Assert.That(joined.Exception!.InnerException, Is.SameAs(disposal.Exception!.InnerException));
                    }
                    candidate.As<IDisposable>().Verify(value => value.Dispose(), Times.Once);
                    candidate.Verify(value => value.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()), Times.Once);
                }
                await prepared.DisposeAsync().ConfigureAwait(false);
                candidate.As<IDisposable>().Verify(value => value.Dispose(), Times.Once);
                candidate.Verify(value => value.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()), Times.Once);
            }
            finally
            {
                release.TrySetResult(true);
                try
                {
                    await ObserveCleanupAsync(disposal).ConfigureAwait(false);
                    if (joined is not null)
                    {
                        await ObserveCleanupAsync(joined).ConfigureAwait(false);
                    }
                }
                finally
                {
                    if (drained is not null)
                    {
                        await drained.WaitAsync(timeout.Token).ConfigureAwait(false);
                        harness.Writer.EndShutdown(prepared: false);
                        harness.Writer.BeginStartup(static () => { });
                    }
                }
            }

            async Task ObserveCleanupAsync(Task cleanup)
            {
                if (failCleanup)
                {
                    await Assert.ThatAsync(() => cleanup.WaitAsync(timeout.Token),
                        Throws.TypeOf<AggregateException>().With.InnerException.SameAs(failure)).ConfigureAwait(false);
                }
                else
                {
                    await cleanup.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Stale commit itself disposes the candidate and releases admission before caller disposal.
        /// </summary>
        [Test]
        public async Task SlotChangeSetStaleCommitDisposesCandidateAndDrainsOperationBeforeCallerDisposalAsync()
        {
            await using var harness = new ChangeSetCleanupHarness();
            NodeManagerLifecycle lifecycle = harness.Lifecycle;
            LifecycleResult added = await lifecycle.ApplyAsync(
                [LifecycleChange.Add(CreateFactory(CreateManager(kNamespaceUri)))])
                .ConfigureAwait(false);
            await added.Retired.ConfigureAwait(false);
            LifecycleWriter writer = harness.Writer;
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
            NodeManagerLifecycle lifecycle = harness.Lifecycle;
            LifecycleResult added = await lifecycle.ApplyAsync(
                [LifecycleChange.Add(CreateFactory(CreateManager(kNamespaceUri)))])
                .ConfigureAwait(false);
            await added.Retired.ConfigureAwait(false);
            LifecycleWriter writer = harness.Writer;
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

        /// <summary>
        /// Finishes candidate cleanup outside the collection test's stack while retaining its disposal task.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static async Task<(
            WeakReference Candidate,
            WeakReference Metadata,
            Task Disposal,
            IPreparedChanges Prepared,
            CleanupCallCounts CleanupCalls)> CompletePrivateCandidateDisposalAsync(bool failCleanup)
        {
            await using var harness = new ChangeSetCleanupHarness();
            Mock<IAsyncNodeManager> candidate = CreateManager(kNamespaceUri);
            var reference = new WeakReference(candidate.Object);
            var failure = new InvalidOperationException("Private candidate abort failed.");
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cleanupCalls = new CleanupCallCounts();
            candidate.As<IDisposable>()
                .Setup(value => value.Dispose())
                .Callback(() => cleanupCalls.DisposeCount++);
            candidate.Setup(value => value.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()))
                .Returns(async () =>
                {
                    cleanupCalls.DeleteCount++;
                    await release.Task.ConfigureAwait(false);
                    if (failCleanup)
                    {
                        throw failure;
                    }
                });
            IPreparedChanges prepared = await harness.Lifecycle.PrepareAsync(
                [LifecycleChange.Add(CreateFactory(candidate))]).ConfigureAwait(false);
            var metadata = new Mock<INodeManagerReadImage>();
            metadata.SetupGet(value => value.Owner).Returns(candidate.Object);
            var metadataReference = new WeakReference(metadata.Object);
            prepared.SetMetadata(prepared.Slots[0], metadata.Object);
            Task disposal = prepared.DisposeAsync().AsTask();
            try
            {
                Assert.That(disposal.IsCompleted, Is.False);
            }
            finally
            {
                release.TrySetResult(true);
            }
            if (failCleanup)
            {
                AggregateException? observed = null;
                try
                {
                    await disposal.ConfigureAwait(false);
                }
                catch (AggregateException exception)
                {
                    observed = exception;
                }
                Assert.That(observed, Is.TypeOf<AggregateException>());
                Assert.That(observed!.InnerException, Is.SameAs(failure));
            }
            else
            {
                await disposal.ConfigureAwait(false);
            }
            candidate.As<IDisposable>().Verify(value => value.Dispose(), Times.Once);
            candidate.Verify(value => value.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()), Times.Once);
            Assert.That(harness.Writer.ActiveOperationCount, Is.Zero);
            return (reference, metadataReference, disposal, prepared, cleanupCalls);
        }

        /// <summary>
        /// Checks the retained candidate before retrying failed provider cleanup.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void AssertFailedPreparedCleanupRetainsCandidate(
            IPreparedChanges prepared,
            WeakReference candidate,
            WeakReference metadata,
            Task disposal,
            CleanupCallCounts cleanupCalls)
        {
            NodeManagerRegistrationSnapshot snapshot = prepared.Registrations[0];
            bool hasManager = snapshot.TryGetNodeManager(out IAsyncNodeManager? manager);
            bool hasRegistration = snapshot.TryGetRegistration(out NodeManagerRegistration? registration);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(candidate.IsAlive, Is.True);
                Assert.That(metadata.IsAlive, Is.False);
                Assert.That(hasManager, Is.True);
                Assert.That(manager, Is.SameAs(candidate.Target));
                Assert.That(hasRegistration, Is.True);
                Assert.That(registration?.NodeManager, Is.SameAs(manager));
                Assert.That(snapshot.Slot, Is.SameAs(prepared.Slots[0]));
                Assert.That(snapshot.Slot.TryGetCurrent(out _), Is.False);
                Assert.That(prepared.IsCommitted, Is.False);
                Assert.That(disposal.Status, Is.EqualTo(TaskStatus.Faulted));
                Assert.That(cleanupCalls.DeleteCount, Is.EqualTo(1));
                Assert.That(cleanupCalls.DisposeCount, Is.EqualTo(1));
            }
        }

        /// <summary>
        /// Retains a real validation task and inspection view after the writer releases its batches.
        /// </summary>
        private static async Task AssertRetainedValidationReleasesCandidateAsync(bool failInspection)
        {
            await using var harness = new ChangeSetCleanupHarness();
            long revision = harness.Lifecycle.Revision;
            await using ILifecycleWriter writer = await harness.Lifecycle.BeginAsync().ConfigureAwait(false);
            (IPreparedChanges inspected,
                Task validation,
                WeakReference candidate,
                InvalidOperationException failure,
                CleanupCallCounts cleanupCalls) = await CompletePrivateValidationAsync(writer, failInspection)
                .ConfigureAwait(false);
            try
            {
                Assert.That(inspected.Registrations.Count, Is.EqualTo(1));
                Assert.That(inspected.Slots.Count, Is.EqualTo(1));
                NodeManagerRegistrationSnapshot snapshot = inspected.Registrations[0];
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(harness.Writer.ActiveOperationCount, Is.EqualTo(1));
                    Assert.That(harness.Lifecycle.Revision, Is.EqualTo(revision));
                    Assert.That(snapshot.Slot.TryGetCurrent(out _), Is.False);
                    Assert.That(inspected.IsCommitted, Is.False);
                    Assert.That(validation.Status, Is.EqualTo(
                        failInspection ? TaskStatus.Faulted : TaskStatus.RanToCompletion));
                    if (failInspection)
                    {
                        Assert.That(validation.Exception?.InnerException, Is.SameAs(failure));
                    }
                    Assert.That(cleanupCalls.DeleteCount, Is.EqualTo(1));
                    Assert.That(cleanupCalls.DisposeCount, Is.EqualTo(1));
                }

                await writer.DisposeAsync().ConfigureAwait(false);
                Assert.That(harness.Writer.ActiveOperationCount, Is.Zero);
                await Task.Yield();
                for (int attempt = 0; attempt < 3 && candidate.IsAlive; attempt++)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                }
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(candidate.IsAlive, Is.False);
                    Assert.That(snapshot.TryGetNodeManager(out _), Is.False);
                    Assert.That(snapshot.TryGetRegistration(out _), Is.False);
                    Assert.That(snapshot.Slot.TryGetCurrent(out _), Is.False);
                    Assert.That(inspected.Slots[0], Is.SameAs(snapshot.Slot));
                    Assert.That(inspected.Registrations[0].Id, Is.EqualTo(snapshot.Id));
                    Assert.That(inspected.IsCommitted, Is.False);
                    Assert.That(validation.Status, Is.EqualTo(
                        failInspection ? TaskStatus.Faulted : TaskStatus.RanToCompletion));
                    if (failInspection)
                    {
                        Assert.That(validation.Exception?.InnerException, Is.SameAs(failure));
                    }
                    Assert.That(cleanupCalls.DeleteCount, Is.EqualTo(1));
                    Assert.That(cleanupCalls.DisposeCount, Is.EqualTo(1));
                }
            }
            finally
            {
                GC.KeepAlive(inspected);
                GC.KeepAlive(validation);
                GC.KeepAlive(failure);
                GC.KeepAlive(writer);
            }
        }

        /// <summary>
        /// Completes real validation outside the collection assertion's stack.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static async Task<(
            IPreparedChanges Inspected,
            Task Validation,
            WeakReference Candidate,
            InvalidOperationException Failure,
            CleanupCallCounts CleanupCalls)> CompletePrivateValidationAsync(
            ILifecycleWriter writer,
            bool failInspection)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var cleanupCalls = new CleanupCallCounts();
            var factory = new ValidationCandidateFactory(cleanupCalls);
            var entered = new TaskCompletionSource<IPreparedChanges>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var failure = new InvalidOperationException("Private validation inspection failed.");
            Task validation = writer.ValidateAsync(
                [LifecycleChange.Add(factory)], async (changes, token) =>
                {
                    entered.TrySetResult(changes);
                    await release.Task.WaitAsync(token).ConfigureAwait(false);
                    if (failInspection)
                    {
                        throw failure;
                    }
                }, timeout.Token).AsTask();
            IPreparedChanges inspected;
            try
            {
                Task first = await Task.WhenAny(entered.Task, validation).WaitAsync(timeout.Token)
                    .ConfigureAwait(false);
                if (ReferenceEquals(first, validation))
                {
                    await validation.ConfigureAwait(false);
                    throw new AssertionException("Validation completed before inspection was entered.");
                }
                inspected = await entered.Task.ConfigureAwait(false);
                Assert.That(inspected.Registrations.Count, Is.EqualTo(1));
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(validation.IsCompleted, Is.False);
                    Assert.That(factory.Candidate?.IsAlive, Is.True);
                    Assert.That(inspected.Registrations[0].TryGetNodeManager(out _), Is.True);
                    Assert.That(inspected.Registrations[0].TryGetRegistration(out _), Is.True);
                    Assert.That(cleanupCalls.DeleteCount, Is.Zero);
                    Assert.That(cleanupCalls.DisposeCount, Is.Zero);
                }
            }
            finally
            {
                release.TrySetResult(true);
            }

            InvalidOperationException? observed = null;
            try
            {
                await validation.WaitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (InvalidOperationException exception)
            {
                observed = exception;
            }
            if (failInspection)
            {
                Assert.That(observed, Is.SameAs(failure));
            }
            else
            {
                Assert.That(observed, Is.Null);
            }
            WeakReference candidate = factory.Candidate ??
                throw new AssertionException("Validation did not create its candidate.");
            return (inspected, validation, candidate, failure, cleanupCalls);
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

        /// <summary>
        /// Creates a candidate without retaining it in the factory after creation.
        /// </summary>
        private sealed class ValidationCandidateFactory(CleanupCallCounts cleanupCalls) : IAsyncNodeManagerFactory
        {
            /// <inheritdoc/>
            public ArrayOf<string> NamespacesUris => [kNamespaceUri];

            /// <summary>
            /// Gets a non-owning reference to the created candidate.
            /// </summary>
            public WeakReference? Candidate { get; private set; }

            /// <inheritdoc/>
            public ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server,
                ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                if (Candidate is not null)
                {
                    throw new InvalidOperationException("Validation created more than one candidate.");
                }
                Mock<IAsyncNodeManager> manager = CreateManager(kNamespaceUri);
                manager.As<IDisposable>()
                    .Setup(value => value.Dispose())
                    .Callback(() => cleanupCalls.DisposeCount++);
                manager.Setup(value => value.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()))
                    .Callback((CancellationToken _) => cleanupCalls.DeleteCount++)
                    .Returns(default(ValueTask));
                IAsyncNodeManager candidate = manager.Object;
                Candidate = new WeakReference(candidate);
                return new ValueTask<IAsyncNodeManager>(candidate);
            }
        }

        /// <summary>
        /// Records the candidate cleanup calls without retaining the candidate.
        /// </summary>
        private sealed class CleanupCallCounts
        {
            /// <summary>
            /// Gets or sets the number of address-space deletion calls.
            /// </summary>
            public int DeleteCount { get; set; }

            /// <summary>
            /// Gets or sets the number of disposal calls.
            /// </summary>
            public int DisposeCount { get; set; }
        }

        /// <summary>
        /// Creates the actual lifecycle, writer and master manager without listeners or background workers.
        /// </summary>
        internal sealed class ChangeSetCleanupHarness : IAsyncDisposable
        {
            /// <summary>
            /// Creates the serving metadata and captures the lifecycle's actual writer.
            /// </summary>
            public ChangeSetCleanupHarness()
            {
                ITelemetryContext telemetry = Opc.Ua.Tests.NUnitTelemetryContext.Create();
                var configuration = new ApplicationConfiguration
                {
                    ApplicationUri = "urn:test:change-set-cleanup:server",
                    ServerConfiguration = new ServerConfiguration()
                };
                var status = new ServerStatusValue(new ServerStatusState(null),
                    new ServerStatusDataType { State = ServerState.Running }, new Lock());
                Data = new ServerInternalData(new ServerProperties(), configuration,
                    new ServiceMessageContext(telemetry, EncodeableFactory.Create()), null, null, status);
                Data.NamespaceUris.GetIndexOrAppend(DeterministicServerMock.TestNamespaceUri);
                Data.TypeTree.AddSubtype(DataTypeIds.BaseDataType, NodeId.Null);
                Data.TypeTree.AddSubtype(DataTypeIds.Structure, DataTypeIds.BaseDataType);
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
                var host = new Mock<INodeManagerLifecycleHost>(MockBehavior.Strict);
                host.SetupGet(value => value.CurrentInstance).Returns(Data);
                host.SetupGet(value => value.CurrentConfiguration).Returns(configuration);
                host.Setup(value => value.PrepareComplexTypesAsync(
                        It.IsAny<IServerInternal>(), It.IsAny<IAsyncNodeManager>(), It.IsAny<CancellationToken>()))
                    .Returns(default(ValueTask<IDataTypeDefinitionResolver?>));
                LifecycleWriter? writer = null;
                Lifecycle = new NodeManagerLifecycle(host.Object, TimeProvider.System, canDispose =>
                {
                    writer = new LifecycleWriter(canDispose);
                    return writer;
                });
                Writer = writer ?? throw new InvalidOperationException("The lifecycle writer was not constructed.");
                Assert.That(Data.IsRunning, Is.True);
                Assert.That(Master.AsyncNodeManagers, Does.Contain(ServingManager.Object));
            }

            /// <summary>
            /// Gets the lifecycle owning this harness's publication and cleanup state.
            /// </summary>
            public NodeManagerLifecycle Lifecycle { get; }

            /// <summary>
            /// Gets the actual writer constructed for this server's lifecycle.
            /// </summary>
            public LifecycleWriter Writer { get; }

            public ServerInternalData Data { get; }
            public MasterNodeManager Master { get; }
            public Mock<IAsyncNodeManager> ServingManager { get; }

            public async ValueTask DisposeAsync()
            {
                Lifecycle.Dispose();
                await Data.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
