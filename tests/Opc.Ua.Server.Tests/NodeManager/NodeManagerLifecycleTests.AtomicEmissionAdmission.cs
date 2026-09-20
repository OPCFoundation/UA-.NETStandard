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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class NodeManagerLifecycleTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task UnsupportedSourceProviderRejectsImmediateCutoffBeforeDecisionAsync(bool immediate)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            Mock<IAsyncNodeManager> manager = CreateLifecycleNodeManager(kModelNamespaceUri);
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(
                It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IAsyncNodeManager>(manager.Object));
            NodeManagerRegistration registration = await lifecycle.AddAsync(factory.Object, null, timeout.Token)
                .ConfigureAwait(false);
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Remove(registration, immediate)], timeout.Token).ConfigureAwait(false);
            int decisions = 0;
            if (immediate)
            {
                await Assert.ThatAsync(async () => await prepared.CommitAsync(_ =>
                {
                    decisions++;
                    return default;
                }, timeout.Token).ConfigureAwait(false), Throws.TypeOf<NotSupportedException>())
                    .ConfigureAwait(false);
                Assert.That(decisions, Is.Zero);
                Assert.That(prepared.IsCommitted, Is.False);
                Assert.That(lifecycle.Registrations.Count, Is.EqualTo(1));
                Assert.That(lifecycle.Registrations[0], Is.SameAs(registration));
                manager.Verify(value => value.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()), Times.Never);
            }
            else
            {
                NodeManagerBatchResult result = await prepared.CommitAsync(_ =>
                {
                    decisions++;
                    return default;
                }, timeout.Token).ConfigureAwait(false);
                Assert.That(result.CleanupFailure, Is.Null);
                Assert.That(prepared.IsCommitted, Is.True);
                Assert.That(decisions, Is.EqualTo(1));
                Assert.That(lifecycle.Registrations.Count, Is.Zero);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task RejectedCutoffRestoresCustomSourceCreationAdmissionAsync(bool cancel)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var decision = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            TrackingLifecycleNodeManager manager = null;
            NodeManagerRegistration registration = await lifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(101, value => manager = value), null, timeout.Token)
                .ConfigureAwait(false);
            NodeId valueId = new(kValueNodeId, manager.NamespaceIndexes[0]);
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            (uint subscriptionId, _) = await CreateSubscriptionAndMonitoredItemAsync(services, valueId, 1)
                .ConfigureAwait(false);
            int factoryCalls = 0;
            manager.EmissionCreationDecision = MonitoredItemCreateDecision.Use(context =>
            {
                factoryCalls++;
                return context.CreatePushMonitoredItem();
            });
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Remove(registration, true)], timeout.Token).ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<NodeManagerBatchResult> commit = prepared.CommitAsync(async token =>
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(token).ConfigureAwait(false);
                throw new IOException("The capability reservation was not committed.");
            }, decision.Token).AsTask();
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                CreateMonitoredItemsResponse during = await CreateAsync().ConfigureAwait(false);
                Assert.That(during.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
                Assert.That(factoryCalls, Is.Zero, "Unknown custom source effects cannot escape the prepared cutoff.");
                Assert.That(manager.GetEmissionSource(valueId).DataChangeMonitoredItems, Has.Count.EqualTo(1));
                if (cancel)
                {
                    decision.Cancel();
                    await Assert.ThatAsync(() => commit, Throws.InstanceOf<OperationCanceledException>())
                        .ConfigureAwait(false);
                }
                else
                {
                    release.TrySetResult(true);
                    await Assert.ThatAsync(() => commit, Throws.TypeOf<IOException>()).ConfigureAwait(false);
                }
                await prepared.DisposeAsync().ConfigureAwait(false);
                CreateMonitoredItemsResponse after = await CreateAsync().ConfigureAwait(false);
                Assert.That(after.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(factoryCalls, Is.EqualTo(1));
                Assert.That(manager.GetEmissionSource(valueId).DataChangeMonitoredItems, Has.Count.EqualTo(2));
                Assert.That(prepared.IsCommitted, Is.False);
            }
            finally
            {
                release.TrySetResult(true);
                manager.EmissionCreationDecision = null;
                await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
            }

            async Task<CreateMonitoredItemsResponse> CreateAsync()
            {
                RequestHeader header = m_requestHeader;
                header.Timestamp = DateTimeUtc.Now;
                return await services.CreateMonitoredItemsAsync(header, subscriptionId, TimestampsToReturn.Both,
                    [CreateEmissionDataRequest(valueId, 2)], timeout.Token).ConfigureAwait(false);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task SourceSamplingCallbackCompletesNestedLifecycleDuringRetirementAsync(bool immediate)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var abortCallback = new CancellationTokenSource();
            IServerInternal server = m_server.CurrentInstance;
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            TrackingLifecycleNodeManager original = null;
            TrackingLifecycleNodeManager nested = null;
            NodeManagerRegistration registration = await lifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(101, manager => original = manager),
                null, timeout.Token).ConfigureAwait(false);
            NodeId valueId = new(kValueNodeId, original.NamespaceIndexes[0]);
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            (uint subscriptionId, uint itemId) = await CreateSubscriptionAndMonitoredItemAsync(
                services, valueId, 1).ConfigureAwait(false);
            MonitoredNode2 source = original.GetEmissionSource(valueId);
            var variable = (BaseVariableState)original.Find(valueId);
            IAsyncNodeManagerFactory inner = CreateTrackingNodeManagementFactory(
                303, manager => nested = manager, kSecondModelNamespaceUri);
            var factory = new CallbackSafeNodeManagerFactory([kSecondModelNamespaceUri], inner.CreateAsync);
            var deciding = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseDecision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var queued = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<NodeManagerRegistration> adding = null;
            variable.OnReadValueAsync = async (_, _, _, _, _) =>
            {
                adding = lifecycle.AddAsync(factory, null, timeout.Token).AsTask();
                queued.TrySetResult(true);
                try
                {
                    await adding.WaitAsync(abortCallback.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (abortCallback.IsCancellationRequested)
                {
                    // Release the source lease only for bounded teardown after a liveness failure.
                }
                return new AttributeReadResult(ServiceResult.Good, new Variant(404), StatusCodes.Good, DateTimeUtc.Now);
            };
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Remove(registration, immediate)], timeout.Token).ConfigureAwait(false);
            Task<NodeManagerBatchResult> commit = prepared.CommitAsync(async token =>
            {
                deciding.TrySetResult(true);
                await releaseDecision.Task.WaitAsync(token).ConfigureAwait(false);
            }, timeout.Token).AsTask();
            Task sample = null;
            try
            {
                await deciding.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                sample = source.QueueValueAsync(
                    server.DefaultSystemContext, variable, source.DataChangeMonitoredItems[itemId], timeout.Token)
                    .AsTask();
                await queued.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(factory.CreateCount, Is.Zero);
                releaseDecision.TrySetResult(true);
                await Task.WhenAll(commit, sample).WaitAsync(TimeSpan.FromSeconds(10), timeout.Token)
                    .ConfigureAwait(false);
                Assert.That((await commit.ConfigureAwait(false)).CleanupFailure, Is.Null);
                Assert.That(factory.CreateCount, Is.EqualTo(1));
                Assert.That(nested.DisposeCount, Is.Zero);
                Assert.That(lifecycle.Registrations.Contains(value =>
                    ReferenceEquals(value.NodeManager, nested)), Is.True);
            }
            finally
            {
                releaseDecision.TrySetResult(true);
                abortCallback.Cancel();
                variable.OnReadValueAsync = null;
                if (sample is not null)
                {
                    await sample.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
                await commit.WaitAsync(timeout.Token).ConfigureAwait(false);
                if (adding is not null)
                {
                    await adding.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
                await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ShutdownWaitsForCapturedSourceSamplingOwnershipAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var lifecycle = new NodeManagerLifecycle(m_server);
            IServerInternal server = m_server.CurrentInstance;
            TrackingLifecycleNodeManager original = null;
            NodeManagerRegistration registration = await lifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(101, manager => original = manager),
                null, timeout.Token).ConfigureAwait(false);
            NodeId valueId = new(kValueNodeId, original.NamespaceIndexes[0]);
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            (uint subscriptionId, uint itemId) = await CreateSubscriptionAndMonitoredItemAsync(
                services, valueId, 1).ConfigureAwait(false);
            MonitoredNode2 source = original.GetEmissionSource(valueId);
            var variable = (BaseVariableState)original.Find(valueId);
            await lifecycle.ShadowReloadAsync(
                registration, CreateNodeManagementFactory(202, false), timeout.Token).ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            variable.OnReadValueAsync = async (_, _, _, _, token) =>
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(token).ConfigureAwait(false);
                return new AttributeReadResult(ServiceResult.Good, new Variant(505), StatusCodes.Good, DateTimeUtc.Now);
            };
            Task sample = source.QueueValueAsync(
                server.DefaultSystemContext, variable, source.DataChangeMonitoredItems[itemId], timeout.Token)
                .AsTask();
            Task shutdown = null;
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                await lifecycle.BeginShutdownAsync(server, timeout.Token).ConfigureAwait(false);
                await DeleteMonitoredItemAsync(services, subscriptionId, itemId).ConfigureAwait(false);
                shutdown = lifecycle.CompleteShutdownAsync(server, timeout.Token).AsTask();
                Assert.That(shutdown.IsCompleted, Is.False);
                Assert.That(original.DisposeCount, Is.Zero);
                release.TrySetResult(true);
                await Task.WhenAll(sample, shutdown).WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(original.DisposeCount, Is.EqualTo(1));
                Assert.That(lifecycle.Registrations.Count, Is.Zero);
            }
            finally
            {
                release.TrySetResult(true);
                await sample.WaitAsync(timeout.Token).ConfigureAwait(false);
                if (shutdown is not null)
                {
                    await shutdown.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
                await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
            }
        }

        private sealed partial class TrackingLifecycleNodeManager
        {
            public MonitoredItemCreateDecision EmissionCreationDecision { get; set; }

            protected override ValueTask<MonitoredItemCreateDecision> OnCreatingMonitoredItemAsync(
                MonitoredItemCreateContext context,
                CancellationToken cancellationToken = default)
            {
                return EmissionCreationDecision is null
                    ? base.OnCreatingMonitoredItemAsync(context, cancellationToken)
                    : new ValueTask<MonitoredItemCreateDecision>(EmissionCreationDecision);
            }
        }
    }
}
