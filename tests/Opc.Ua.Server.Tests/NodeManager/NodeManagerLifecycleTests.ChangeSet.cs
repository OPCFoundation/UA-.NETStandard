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
                Assert.That((lifecycle.Registrations.ToArray() ?? [])
                    .Single(value => value.Slot == added.Registrations[0].Slot),
                    Is.SameAs(winner.Registrations[0]));
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
                    .Single(value => value.Slot == result.Registrations[0].Slot)
                    .NodeManager, Is.SameAs(result.Registrations[0].NodeManager));
            }
            var compatibilityResult = new NodeManagerBatchResult(result);
            INodeManagerCommittedFailure[] shims =
            [
                new NodeManagerAddCommittedException(
                    result.Registrations[0], compatibilityResult, "add", originalFailure),
                new NodeManagerReloadCommittedException(
                    result.Registrations[0], compatibilityResult, "reload", originalFailure),
                new NodeManagerRemovalCommittedException(result.Registrations[0], compatibilityResult, originalFailure)
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
            NodeManagerRegistration candidate = prepared.Registrations[0];
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
                var compatibility = new NodeManagerBatchResult(result);
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
            NodeManagerRegistration registration = added.Registrations[0];
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
    }
}
