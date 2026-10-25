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
        /// Stale captures and competing invocation reservations fail before a candidate factory runs.
        /// </summary>
        [Test]
        public async Task WriterRejectsStaleRevisionAndConflictingReservationBeforeFactoryEffectsAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            INodeManagerPublicationCapture stale = lifecycle.CapturePublication();
            await lifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(kGeneration1Value, _ => { }), timeout.Token)
                .ConfigureAwait(false);
            var factory = new CallbackSafeNodeManagerFactory(
                [kSecondModelNamespaceUri],
                CreateTrackingNodeManagementFactory(
                    kSecondRegistrationValue, _ => { }, kSecondModelNamespaceUri).CreateAsync);
            long revision = lifecycle.Revision;
            await using (INodeManagerPublication invocation = await stale.BeginAsync(timeout.Token)
                .ConfigureAwait(false))
            {
                Assert.That(invocation.IsCurrent, Is.False);
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await invocation.PrepareAsync(
                        [NodeManagerBatchChange.Add(factory)], timeout.Token).ConfigureAwait(false))
                    .ConfigureAwait(false);
                ServiceResultException? conflict = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await lifecycle.CapturePublication().BeginAsync(timeout.Token).ConfigureAwait(false))
                    .ConfigureAwait(false);
                Assert.That(conflict!.StatusCode, Is.EqualTo(StatusCodes.BadServerTooBusy));
                await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await lifecycle.AddAsync(factory, timeout.Token).ConfigureAwait(false)).ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(factory.CreateCount, Is.Zero);
                    Assert.That(lifecycle.Revision, Is.EqualTo(revision));
                }
            }
            NodeManagerRegistration added = await lifecycle.AddAsync(factory, timeout.Token).ConfigureAwait(false);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(factory.CreateCount, Is.EqualTo(1));
                Assert.That(added.Generation, Is.EqualTo(1));
            }
        }

        /// <summary>
        /// An invocation retains shutdown ownership through its unpublished transaction cleanup.
        /// </summary>
        [Test]
        public async Task WriterShutdownWaitsForInvocationAndDisposesItsPreparedTransactionsAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var lifecycle = (NodeManagerLifecycle)m_server.NodeManagerLifecycle;
            TrackingLifecycleNodeManager? candidate = null;
            INodeManagerPublication invocation = await lifecycle.CapturePublication().BeginAsync(timeout.Token)
                .ConfigureAwait(false);
            try
            {
                IPreparedNodeManagerBatch batch = await invocation.PrepareAsync(
                    [NodeManagerBatchChange.Add(CreateTrackingNodeManagementFactory(
                        kGeneration1Value, manager => candidate = manager))], timeout.Token).ConfigureAwait(false);
                Task shutdown = lifecycle.BeginShutdownAsync(m_server.CurrentInstance, timeout.Token).AsTask();
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(shutdown.IsCompleted, Is.False);
                    Assert.That(lifecycle.IsShuttingDown, Is.False, "The invocation owns admission before shutdown.");
                    Assert.That(batch.IsCommitted, Is.False);
                }
                await invocation.DisposeAsync().ConfigureAwait(false);
                await shutdown.WaitAsync(timeout.Token).ConfigureAwait(false);
                AssertLifecycleValue(candidate);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(candidate.DisposeCount, Is.EqualTo(1));
                    Assert.That(lifecycle.IsShuttingDown, Is.True);
                    Assert.That(GetBranchRegistrations(lifecycle), Is.Empty);
                }
            }
            finally
            {
                await invocation.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
