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
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class NodeManagerLifecycleTests
    {
        [TestCase(false)]
        [TestCase(true)]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task PreparedImmediateRetirementFinishesAdmittedEventMutationAsync(bool delete)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var abortCallback = new CancellationTokenSource();
            IServerInternal server = m_server.CurrentInstance;
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create())
            {
                OperationTimeout = 60000,
                SessionTimeout = 60000
            };
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using Opc.Ua.Client.ISession session = await client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            TrackingLifecycleNodeManager original = null;
            TrackingLifecycleNodeManager candidate = null;
            TrackingLifecycleNodeManager nested = null;
            NodeManagerRegistration registration = await lifecycle.AddAsync(CreateTrackingNodeManagementFactory(
                kGeneration1Value, value => original = value), null, timeout.Token).ConfigureAwait(false);
            CreateSubscriptionResponse subscription = await session.CreateSubscriptionAsync(
                null, 100, 100, 10, 0, true, 0, timeout.Token).ConfigureAwait(false);
            var filter = new EventFilter();
            filter.AddSelectClause(ObjectTypeIds.BaseEventType, QualifiedName.From(BrowseNames.EventType));
            CreateMonitoredItemsResponse created = await session.CreateMonitoredItemsAsync(
                null, subscription.SubscriptionId, TimestampsToReturn.Both,
                [
                    new MonitoredItemCreateRequest
                    {
                        ItemToMonitor = new ReadValueId
                        {
                            NodeId = ObjectIds.Server,
                            AttributeId = Attributes.EventNotifier
                        },
                        MonitoringMode = MonitoringMode.Reporting,
                        RequestedParameters = new MonitoringParameters
                        {
                            ClientHandle = 1,
                            Filter = new ExtensionObject(filter),
                            QueueSize = 1,
                            DiscardOldest = true
                        }
                    }
                ], timeout.Token).ConfigureAwait(false);
            Assert.That(created.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            uint eventId = created.Results[0].MonitoredItemId;
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Replace(registration, CreateTrackingNodeManagementFactory(
                    kGeneration2Value, value => candidate = value), true)], timeout.Token).ConfigureAwait(false);
            IAsyncNodeManagerFactory inner = CreateTrackingNodeManagementFactory(
                303, value => nested = value, kReadinessProbeNamespaceUri);
            var factory = new CallbackSafeNodeManagerFactory([kReadinessProbeNamespaceUri], inner.CreateAsync);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var queued = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<NodeManagerRegistration> adding = null;
            int callbackClaims = 0;
            original.AllEventsCallback = async (item, unsubscribe, _) =>
            {
                if (item.Id != eventId || unsubscribe != delete ||
                    Interlocked.CompareExchange(ref callbackClaims, 1, 0) != 0)
                {
                    return;
                }
                adding = lifecycle.AddAsync(factory, null, timeout.Token).AsTask();
                queued.TrySetResult(true);
                try
                {
                    await adding.WaitAsync(abortCallback.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (abortCallback.IsCancellationRequested)
                {
                    // Permit deterministic teardown after a bounded liveness failure.
                }
            };
            Task<NodeManagerBatchResult> commit = prepared.CommitAsync(async token =>
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(token).ConfigureAwait(false);
            }, timeout.Token).AsTask();
            await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            Task<StatusCode> mutation = MutateAsync();
            try
            {
                await queued.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                await server.RequestManager.WaitForCurrentRequestsAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(factory.CreateCount, Is.Zero);
                release.TrySetResult(true);
                try
                {
                    await Task.WhenAll(commit, mutation).WaitAsync(TimeSpan.FromSeconds(10), timeout.Token)
                        .ConfigureAwait(false);
                }
                finally
                {
                    abortCallback.Cancel();
                    await Task.WhenAll(commit, mutation).WaitAsync(timeout.Token).ConfigureAwait(false);
                }
                NodeManagerBatchResult result = await commit.ConfigureAwait(false);
                Assert.That(await mutation.ConfigureAwait(false), Is.EqualTo(StatusCodes.Good));
                Assert.That(result.CleanupFailure, Is.Null);
                Assert.That(prepared.IsCommitted, Is.True);
                Assert.That(factory.CreateCount, Is.EqualTo(1));
                Assert.That(server.EventManager.GetMonitoredItems().Any(item => item.Id == eventId), Is.EqualTo(!delete));
                Assert.That(candidate.SubscribedAllEventIds.ToList(),
                    Is.EqualTo(delete ? Array.Empty<uint>() : new[] { eventId }));
                Assert.That(nested.SubscribedAllEventIds.ToList(),
                    Is.EqualTo(delete ? Array.Empty<uint>() : new[] { eventId }),
                    "Nested lifecycle work must not bind an item already removed from its Subscription.");
                Assert.That(nested.UnsubscribedAllEventIds.IsEmpty, Is.True);
            }
            finally
            {
                release.TrySetResult(true);
                abortCallback.Cancel();
                original.AllEventsCallback = null;
                if (adding is not null)
                {
                    await adding.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
                await session.DeleteSubscriptionsAsync(null, [subscription.SubscriptionId], timeout.Token)
                    .ConfigureAwait(false);
                await session.CloseAsync(timeout.Token).ConfigureAwait(false);
            }

            async Task<StatusCode> MutateAsync()
            {
                if (delete)
                {
                    DeleteMonitoredItemsResponse response = await session.DeleteMonitoredItemsAsync(
                        null, subscription.SubscriptionId, [eventId], timeout.Token).ConfigureAwait(false);
                    return response.Results[0];
                }
                ModifyMonitoredItemsResponse modified = await session.ModifyMonitoredItemsAsync(
                    null, subscription.SubscriptionId, TimestampsToReturn.Both,
                    [
                        new MonitoredItemModifyRequest
                        {
                            MonitoredItemId = eventId,
                            RequestedParameters = new MonitoringParameters
                            {
                                ClientHandle = 2,
                                Filter = new ExtensionObject(filter),
                                QueueSize = 2,
                                DiscardOldest = true
                            }
                        }
                    ], timeout.Token).ConfigureAwait(false);
                return modified.Results[0].StatusCode;
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task PreparedRetirementLetsAdmittedSessionReleaseItsNotificationLeaseAsync(bool immediate)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var abortCallback = new CancellationTokenSource();
            IServerInternal server = m_server.CurrentInstance;
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create())
            {
                OperationTimeout = 60000,
                SessionTimeout = 60000
            };
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            TrackingLifecycleNodeManager original = null;
            TrackingLifecycleNodeManager candidate = null;
            TrackingLifecycleNodeManager nested = null;
            NodeManagerRegistration registration = await lifecycle.AddAsync(CreateTrackingNodeManagementFactory(
                kGeneration1Value, value => original = value), null, timeout.Token).ConfigureAwait(false);
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Replace(registration, CreateTrackingNodeManagementFactory(
                    kGeneration2Value, value => candidate = value), immediate)], timeout.Token).ConfigureAwait(false);
            IAsyncNodeManagerFactory inner = CreateTrackingNodeManagementFactory(
                303, value => nested = value, kReadinessProbeNamespaceUri);
            var factory = new CallbackSafeNodeManagerFactory([kReadinessProbeNamespaceUri], inner.CreateAsync);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var queued = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<NodeManagerRegistration> adding = null;
            original.SessionActivatedCallback = async _ =>
            {
                Assert.That(server.RequestManager.GetCurrentRequestIdForLifecycleExtension().HasValue, Is.True);
                adding = lifecycle.AddAsync(factory, null, timeout.Token).AsTask();
                queued.TrySetResult(true);
                try
                {
                    await adding.WaitAsync(abortCallback.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (abortCallback.IsCancellationRequested)
                {
                    // Release the captured notification only after detecting the reproduced wait cycle.
                }
            };
            Task<NodeManagerBatchResult> commit = prepared.CommitAsync(async token =>
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(token).ConfigureAwait(false);
            }, timeout.Token).AsTask();
            await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            Task<Opc.Ua.Client.ISession> activation = client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None);
            Opc.Ua.Client.ISession session = null;
            bool cycle = false;
            try
            {
                await queued.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                await server.RequestManager.WaitForCurrentRequestsAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(factory.CreateCount, Is.Zero);
                release.TrySetResult(true);
                try
                {
                    await Task.WhenAll(commit, activation).WaitAsync(TimeSpan.FromSeconds(10), timeout.Token)
                        .ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    cycle = !commit.IsCompleted && !activation.IsCompleted && factory.CreateCount == 1;
                    throw;
                }
                finally
                {
                    abortCallback.Cancel();
                    await commit.WaitAsync(timeout.Token).ConfigureAwait(false);
                    session = await activation.WaitAsync(timeout.Token).ConfigureAwait(false);
                    if (adding is not null)
                    {
                        await adding.WaitAsync(timeout.Token).ConfigureAwait(false);
                    }
                    Assert.That(cycle, Is.False,
                        "Immediate retirement must not hold binding admission while a captured callback " +
                        "needs it to finish and release the notification being drained.");
                }
                NodeManagerBatchResult result = await commit.ConfigureAwait(false);
                NodeId initialId = server.SessionManager.GetSession(m_requestHeader.AuthenticationToken).Id;
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(result.CleanupFailure, Is.Null);
                    Assert.That(prepared.IsCommitted, Is.True);
                    Assert.That(factory.CreateCount, Is.EqualTo(1));
                    Assert.That(candidate.ActivatedSessionIds.ToList(),
                        Is.EquivalentTo(new[] { initialId, session.SessionId }));
                    Assert.That(nested.ActivatedSessionIds.ToList(),
                        Is.EquivalentTo(new[] { initialId, session.SessionId }));
                }
                await original.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(original.DeleteAddressSpaceCount, Is.EqualTo(1));
                Assert.That(original.DisposeCount, Is.EqualTo(1));
            }
            finally
            {
                release.TrySetResult(true);
                abortCallback.Cancel();
                original.SessionActivatedCallback = null;
                if (session is not null)
                {
                    await session.CloseAsync(timeout.Token).ConfigureAwait(false);
                    session.Dispose();
                }
            }
        }
    }
}
