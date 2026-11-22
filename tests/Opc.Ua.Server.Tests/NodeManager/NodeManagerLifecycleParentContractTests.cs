// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class NodeManagerLifecycleTests
    {
        /// <summary>
        /// Read-image metadata for an unchanged manager shares the candidate routing publication and abort boundary.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task PreparedBatchPublishesUnchangedOwnerImageWithCandidateOrAbortsBothAsync(bool commit)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            LifecycleResult added = await lifecycle.ApplyAsync(
                [LifecycleChange.Add(CreateTrackingNodeManagementFactory(kFirstRegistrationValue, _ => { }))],
                timeout.Token).ConfigureAwait(false);
            Assert.That(added.Issues, Is.Empty);
            NodeManagerRegistration unchanged = RequireRegistration(added.Registrations[0]);
            await using ILifecycleWriter publication = await lifecycle.CaptureChanges()
                .BeginAsync(timeout.Token).ConfigureAwait(false);

            var previousImage = new LifecycleReadImage(unchanged.NodeManager, 1);
            await using (IPreparedChanges imageBatch = await publication.PrepareReadImagesAsync(
                [previousImage], timeout.Token).ConfigureAwait(false))
            {
                await imageBatch.CommitAsync(_ => default, timeout.Token).ConfigureAwait(false);
            }

            IServerInternal server = m_server.CurrentInstance;
            var host = (IDynamicNodeManagerBatchHost)server.NodeManager;
            using var pinnedContext = new OperationContext(
                new RequestHeader(), null, RequestType.Read, RequestLifetime.None);
            ((IRequestRouting)server.NodeManager).PinRequest(pinnedContext);
            NodeManagerRoutingTable.RoutingSnapshot before = pinnedContext.RoutingSnapshot!;
            var nextImage = new LifecycleReadImage(unchanged.NodeManager, 2);
            TrackingLifecycleNodeManager? candidateManager = null;
            IAsyncNodeManagerFactory factory = CreateTrackingNodeManagementFactory(
                kSecondRegistrationValue, manager => candidateManager = manager, kSecondModelNamespaceUri);
            await using IPreparedChanges batch = await publication.PrepareAsync(
                [LifecycleChange.Add(factory)], timeout.Token).ConfigureAwait(false);
            batch.SetMetadata(nextImage);
            IAsyncNodeManager candidate = RequireRegistration(batch.Registrations[0]).NodeManager;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(host.RoutingRevision, Is.SameAs(before));
                Assert.That(before.ReadImages[unchanged.NodeManager], Is.SameAs(previousImage));
                Assert.That(Array.IndexOf(before.NodeManagers, candidate), Is.EqualTo(-1));
            }

            if (commit)
            {
                await batch.CommitAsync(_ => default, timeout.Token).ConfigureAwait(false);
                NodeManagerRoutingTable.RoutingSnapshot after = host.RoutingRevision;
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(after, Is.Not.SameAs(before));
                    Assert.That(after.Revision, Is.EqualTo(before.Revision + 1));
                    Assert.That(after.ReadImages[unchanged.NodeManager], Is.SameAs(nextImage));
                    Assert.That(Array.IndexOf(after.NodeManagers, candidate), Is.GreaterThanOrEqualTo(0));
                    Assert.That(pinnedContext.RoutingSnapshot, Is.SameAs(before));
                    Assert.That(pinnedContext.RoutingSnapshot!.ReadImages[unchanged.NodeManager],
                        Is.SameAs(previousImage));
                    Assert.That(candidateManager?.DisposeCount, Is.Zero);
                }
            }
            else
            {
                await batch.DisposeAsync().ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(host.RoutingRevision, Is.SameAs(before));
                    Assert.That(host.RoutingRevision.ReadImages[unchanged.NodeManager], Is.SameAs(previousImage));
                    Assert.That(Array.IndexOf(host.RoutingRevision.NodeManagers, candidate), Is.EqualTo(-1));
                    Assert.That(candidateManager?.DeleteAddressSpaceCount, Is.EqualTo(1));
                    Assert.That(candidateManager?.DisposeCount, Is.EqualTo(1));
                }
            }
        }

        /// <summary>
        /// A capture taken before deferred input acquisition stays stale after an unrelated publication.
        /// </summary>
        [Test]
        public async Task CapturedPublicationRevisionRemainsBoundAcrossDeferredInputAcquisitionAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            ILifecycleCapture capture = lifecycle.CaptureChanges();
            var inputRequested = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseInput = new TaskCompletionSource<LifecycleChange>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Task<LifecycleChange> deferredInput = AcquireInputAsync();
            var factory = new CountingNodeManagerFactory(
                [kSecondModelNamespaceUri],
                CreateTrackingNodeManagementFactory(kSecondRegistrationValue, _ => { },
                    kSecondModelNamespaceUri).CreateAsync);

            await inputRequested.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            LifecycleResult published = await lifecycle.ApplyAsync(
                [LifecycleChange.Add(CreateTrackingNodeManagementFactory(kFirstRegistrationValue, _ => { }))],
                timeout.Token).ConfigureAwait(false);
            Assert.That(published.Issues, Is.Empty);
            releaseInput.TrySetResult(LifecycleChange.Add(factory));
            LifecycleChange acquiredChange = await deferredInput.WaitAsync(timeout.Token).ConfigureAwait(false);

            await using ILifecycleWriter publication = await capture.BeginAsync(timeout.Token)
                .ConfigureAwait(false);
            Assert.That(publication.IsCurrent, Is.False);
            await Assert.ThatAsync(() => publication.PrepareAsync([acquiredChange], timeout.Token).AsTask(),
                Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
            Assert.That(factory.CreateCount, Is.Zero);

            Task<LifecycleChange> AcquireInputAsync()
            {
                inputRequested.TrySetResult(true);
                return releaseInput.Task.WaitAsync(timeout.Token);
            }
        }

        private sealed record LifecycleReadImage(IAsyncNodeManager Owner, int Revision) : INodeManagerReadImage;
    }
}
