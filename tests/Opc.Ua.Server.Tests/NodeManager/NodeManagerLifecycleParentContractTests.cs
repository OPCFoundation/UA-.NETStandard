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
            NodeManagerRegistration unchanged = await lifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(kFirstRegistrationValue, _ => { }),
                timeout.Token).ConfigureAwait(false);
            await using INodeManagerPublication publication = await lifecycle.CapturePublication()
                .BeginAsync(timeout.Token).ConfigureAwait(false);

            var previousImage = new LifecycleReadImage(unchanged.NodeManager, 1);
            await using (IPreparedNodeManagerBatch imageBatch = await publication.PrepareReadImagesAsync(
                [previousImage], timeout.Token).ConfigureAwait(false))
            {
                await imageBatch.CommitAsync(_ => default, timeout.Token).ConfigureAwait(false);
            }

            IServerInternal server = m_server.CurrentInstance;
            NodeManagerRoutingTable routing = GetRoutingTable((MasterNodeManager)server.NodeManager);
            using var pinnedContext = new OperationContext(
                new RequestHeader(), null, RequestType.Read, RequestLifetime.None);
            pinnedContext.PinRouting(routing);
            NodeManagerRoutingTable.RoutingSnapshot before = pinnedContext.RoutingSnapshot!;
            var nextImage = new LifecycleReadImage(unchanged.NodeManager, 2);
            TrackingLifecycleNodeManager? candidateManager = null;
            IAsyncNodeManagerFactory factory = CreateTrackingNodeManagementFactory(
                kSecondRegistrationValue, manager => candidateManager = manager, kSecondModelNamespaceUri);
            await using IPreparedNodeManagerBatch batch = await publication.PrepareAsync(
                [NodeManagerBatchChange.Add(factory)], timeout.Token).ConfigureAwait(false);
            batch.BindReadImages([nextImage]);
            IAsyncNodeManager candidate = batch.Registrations[0].NodeManager;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(routing.Revision, Is.SameAs(before));
                Assert.That(before.ReadImages[unchanged.NodeManager], Is.SameAs(previousImage));
                Assert.That(Array.IndexOf(before.NodeManagers, candidate), Is.EqualTo(-1));
            }

            if (commit)
            {
                await batch.CommitAsync(_ => default, timeout.Token).ConfigureAwait(false);
                NodeManagerRoutingTable.RoutingSnapshot after = routing.Revision;
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
                    Assert.That(routing.Revision, Is.SameAs(before));
                    Assert.That(routing.Revision.ReadImages[unchanged.NodeManager], Is.SameAs(previousImage));
                    Assert.That(Array.IndexOf(routing.Revision.NodeManagers, candidate), Is.EqualTo(-1));
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
            INodeManagerPublicationCapture capture = lifecycle.CapturePublication();
            var inputRequested = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseInput = new TaskCompletionSource<NodeManagerBatchChange>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Task<NodeManagerBatchChange> deferredInput = AcquireInputAsync();
            var factory = new CountingNodeManagerFactory(
                [kSecondModelNamespaceUri],
                CreateTrackingNodeManagementFactory(kSecondRegistrationValue, _ => { },
                    kSecondModelNamespaceUri).CreateAsync);

            await inputRequested.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            await lifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(kFirstRegistrationValue, _ => { }), timeout.Token)
                .ConfigureAwait(false);
            releaseInput.TrySetResult(NodeManagerBatchChange.Add(factory));
            NodeManagerBatchChange acquiredChange = await deferredInput.WaitAsync(timeout.Token).ConfigureAwait(false);

            await using INodeManagerPublication publication = await capture.BeginAsync(timeout.Token)
                .ConfigureAwait(false);
            Assert.That(publication.IsCurrent, Is.False);
            await Assert.ThatAsync(() => publication.PrepareAsync([acquiredChange], timeout.Token).AsTask(),
                Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
            Assert.That(factory.CreateCount, Is.Zero);

            Task<NodeManagerBatchChange> AcquireInputAsync()
            {
                inputRequested.TrySetResult(true);
                return releaseInput.Task.WaitAsync(timeout.Token);
            }
        }

        private sealed record LifecycleReadImage(IAsyncNodeManager Owner, int Revision) : INodeManagerReadImage;
    }
}
