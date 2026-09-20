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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class NodeManagerLifecycleTests
    {
        [Test]
        public async Task OverlappingBindingSuspensionsRestoreAdmissionOnlyAfterTheirLastReleaseAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            TrackingLifecycleNodeManager manager = null;
            await m_server.NodeManagerLifecycle.AddAsync(CreateTrackingNodeManagementFactory(
                kGeneration1Value, value => manager = value), null, timeout.Token).ConfigureAwait(false);
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            m_requestHeader.Timestamp = DateTimeUtc.Now;
            CreateSubscriptionResponse subscription = await services.CreateSubscriptionAsync(
                m_requestHeader, 100, 100, 10, 0, true, 0).ConfigureAwait(false);
            uint subscriptionId = subscription.SubscriptionId;
            var host = (IDynamicNodeManagerBatchHost)m_server.CurrentInstance.NodeManager;
            var suspended = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int callbacks = 0;
            manager.AllEventsCallback = async (_, unsubscribe, token) =>
            {
                if (unsubscribe || Interlocked.Increment(ref callbacks) != 1)
                {
                    return;
                }
                await using IAsyncDisposable first = host.SuspendBindingAdmission();
                await using IAsyncDisposable second = host.SuspendBindingAdmission();
                await first.DisposeAsync().ConfigureAwait(false);
                suspended.TrySetResult(true);
                await release.Task.WaitAsync(token).ConfigureAwait(false);
            };
            Task<uint> firstAdmission = CreateEventMonitoredItemAsync(services, subscriptionId, ObjectIds.Server, 1);
            Task<uint> secondAdmission = null;
            bool blockedByEarlyRelease = false;
            try
            {
                await suspended.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(firstAdmission.IsCompleted, Is.False);
                secondAdmission = CreateEventMonitoredItemAsync(services, subscriptionId, ObjectIds.Server, 2);
                try
                {
                    await secondAdmission.WaitAsync(TimeSpan.FromSeconds(5), timeout.Token).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    blockedByEarlyRelease = !secondAdmission.IsCompleted && Volatile.Read(ref callbacks) == 1;
                }
            }
            finally
            {
                release.TrySetResult(true);
                manager.AllEventsCallback = null;
                await firstAdmission.WaitAsync(timeout.Token).ConfigureAwait(false);
                if (secondAdmission is not null)
                {
                    await secondAdmission.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
                await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
            }
            Assert.That(blockedByEarlyRelease, Is.False,
                "Completing one nested lifecycle operation must not resume admission still suspended by another.");
            Assert.That(manager.AllEventsSubscribeCount, Is.EqualTo(2));
        }

        [Test]
        public async Task OverlappingBindingResumptionsAcquireAdmissionOnlyOnceAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            TrackingLifecycleNodeManager manager = null;
            await m_server.NodeManagerLifecycle.AddAsync(CreateTrackingNodeManagementFactory(
                kGeneration1Value, value => manager = value), null, timeout.Token).ConfigureAwait(false);
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            m_requestHeader.Timestamp = DateTimeUtc.Now;
            CreateSubscriptionResponse subscription = await services.CreateSubscriptionAsync(
                m_requestHeader, 100, 100, 10, 0, true, 0).ConfigureAwait(false);
            var host = (IDynamicNodeManagerBatchHost)m_server.CurrentInstance.NodeManager;
            var suspended = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var peerEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var resume = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var resumptionsQueued = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releasePeer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int callbacks = 0;
            manager.AllEventsCallback = async (_, unsubscribe, token) =>
            {
                if (unsubscribe)
                {
                    return;
                }
                if (Interlocked.Increment(ref callbacks) == 1)
                {
                    await using IAsyncDisposable first = host.SuspendBindingAdmission();
                    suspended.TrySetResult(true);
                    await resume.Task.WaitAsync(token).ConfigureAwait(false);
                    Task firstResumption = first.DisposeAsync().AsTask();
                    await using IAsyncDisposable second = host.SuspendBindingAdmission();
                    Task secondResumption = second.DisposeAsync().AsTask();
                    resumptionsQueued.TrySetResult(true);
                    await Task.WhenAll(firstResumption, secondResumption).WaitAsync(timeout.Token).ConfigureAwait(false);
                }
                else
                {
                    peerEntered.TrySetResult(true);
                    await releasePeer.Task.WaitAsync(token).ConfigureAwait(false);
                }
            };
            Task<uint> firstAdmission = CreateEventMonitoredItemAsync(
                services, subscription.SubscriptionId, ObjectIds.Server, 1);
            Task<uint> secondAdmission = null;
            try
            {
                await suspended.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                secondAdmission = CreateEventMonitoredItemAsync(
                    services, subscription.SubscriptionId, ObjectIds.Server, 2);
                await peerEntered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                resume.TrySetResult(true);
                await resumptionsQueued.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(firstAdmission.IsCompleted, Is.False);
                releasePeer.TrySetResult(true);
                await Task.WhenAll(firstAdmission, secondAdmission).WaitAsync(timeout.Token).ConfigureAwait(false);
                manager.AllEventsCallback = null;
                await CreateEventMonitoredItemAsync(services, subscription.SubscriptionId, ObjectIds.Server, 3)
                    .WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(manager.AllEventsSubscribeCount, Is.EqualTo(3));
            }
            finally
            {
                resume.TrySetResult(true);
                releasePeer.TrySetResult(true);
                manager.AllEventsCallback = null;
                await firstAdmission.WaitAsync(timeout.Token).ConfigureAwait(false);
                if (secondAdmission is not null)
                {
                    await secondAdmission.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
                await DeleteSubscriptionAsync(services, subscription.SubscriptionId).ConfigureAwait(false);
            }
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task PreparedBatchCallbackAdmissionResumesAfterDecisionOrCancellationAsync(
            bool rejectDecision, bool cancelNested)
        {
            await VerifyCallbackAdmissionCompletionAsync(rejectDecision, cancelNested, false).ConfigureAwait(false);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task FailedAdmittedEventDoesNotRetainNewLifecycleBindingsAsync(bool rejectDecision)
        {
            await VerifyCallbackAdmissionCompletionAsync(rejectDecision, false, true).ConfigureAwait(false);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task PreparedBatchAllowsAdmittedSessionCallbackToAddAsync(bool rejectDecision)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var cancellation = new CancellationTokenSource();
            IServerInternal server = m_server.CurrentInstance;
            await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create())
            {
                OperationTimeout = 60000,
                SessionTimeout = 60000
            };
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            TrackingLifecycleNodeManager original = null;
            TrackingLifecycleNodeManager candidate = null;
            TrackingLifecycleNodeManager nested = null;
            await lifecycle.AddAsync(CreateTrackingNodeManagementFactory(
                kGeneration1Value, value => original = value), null, timeout.Token).ConfigureAwait(false);
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(CreateTrackingNodeManagementFactory(
                    kGeneration2Value, value => candidate = value, kSecondModelNamespaceUri))],
                timeout.Token).ConfigureAwait(false);
            IAsyncNodeManagerFactory inner = CreateTrackingNodeManagementFactory(
                303, value => nested = value, kReadinessProbeNamespaceUri);
            var factory = new CallbackSafeNodeManagerFactory([kReadinessProbeNamespaceUri], inner.CreateAsync);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var queued = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            original.SessionActivatedCallback = async _ =>
            {
                Assert.That(server.RequestManager.GetCurrentRequestIdForLifecycleExtension().HasValue, Is.True);
                Task<NodeManagerRegistration> adding = lifecycle.AddAsync(
                    factory, null, cancellation.Token).AsTask();
                queued.TrySetResult(true);
                await adding.ConfigureAwait(false);
            };
            Task<NodeManagerBatchResult> commit = prepared.CommitAsync(async token =>
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(token).ConfigureAwait(false);
                if (rejectDecision)
                {
                    throw new IOException("Confirmed noncommit during Session activation.");
                }
            }, timeout.Token).AsTask();
            await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            Task<Opc.Ua.Client.ISession> activation = client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None);
            Opc.Ua.Client.ISession activatedSession = null;
            try
            {
                await queued.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                await server.RequestManager.WaitForCurrentRequestsAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(factory.CreateCount, Is.Zero);
                release.TrySetResult(true);
                if (rejectDecision)
                {
                    await Assert.ThatAsync(() => commit, Throws.TypeOf<IOException>()).ConfigureAwait(false);
                    await prepared.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    NodeManagerBatchResult result = await commit.WaitAsync(timeout.Token).ConfigureAwait(false);
                    Assert.That(result.CleanupFailure, Is.Null);
                }
                activatedSession = await activation.WaitAsync(timeout.Token).ConfigureAwait(false);
                NodeId initialId = server.SessionManager.GetSession(m_requestHeader.AuthenticationToken).Id;
                NodeId activatedId = activatedSession.SessionId;
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(factory.CreateCount, Is.EqualTo(1));
                    Assert.That(prepared.IsCommitted, Is.EqualTo(!rejectDecision));
                    Assert.That(candidate.ActivatedSessionIds.ToList(),
                        Is.EquivalentTo(rejectDecision ? Array.Empty<NodeId>() : new[] { initialId, activatedId }));
                    Assert.That(nested.ActivatedSessionIds.ToList(), Is.EquivalentTo(new[] { initialId, activatedId }));
                }
            }
            finally
            {
                release.TrySetResult(true);
                cancellation.Cancel();
                original.SessionActivatedCallback = null;
                if (activatedSession is not null)
                {
                    await activatedSession.CloseAsync(timeout.Token).ConfigureAwait(false);
                    activatedSession.Dispose();
                }
            }

        }

        [Test]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task PreparedBatchAllowsAdmittedBindingCallbackToAddAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var cleanup = new CancellationTokenSource();
            IServerInternal server = m_server.CurrentInstance;
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create())
            {
                OperationTimeout = 60000
            };
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using Opc.Ua.Client.ISession session = await client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            TrackingLifecycleNodeManager original = null;
            await lifecycle.AddAsync(CreateTrackingNodeManagementFactory(
                kGeneration1Value, manager => original = manager), null, timeout.Token).ConfigureAwait(false);
            TrackingLifecycleNodeManager candidate = null;
            await using IPreparedNodeManagerBatch prepared = await ((INodeManagerBatchLifecycle)lifecycle).PrepareAsync(
                [NodeManagerBatchChange.Add(CreateTrackingNodeManagementFactory(
                    kSecondRegistrationValue, manager => candidate = manager, kSecondModelNamespaceUri))],
                timeout.Token).ConfigureAwait(false);
            TrackingLifecycleNodeManager nested = null;
            IAsyncNodeManagerFactory nestedFactory = CreateTrackingNodeManagementFactory(
                303, manager => nested = manager, kReadinessProbeNamespaceUri);
            var callbackFactory = new CallbackSafeNodeManagerFactory(
                [kReadinessProbeNamespaceUri], nestedFactory.CreateAsync);
            var decisionEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseDecision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var callbackEntered = new TaskCompletionSource<uint>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseCallback = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var lifecycleQueued = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            NodeManagerRegistration added = null;
            int decisions = 0;
            original.AllEventsCallback = async (item, unsubscribe, _) =>
            {
                if (unsubscribe)
                {
                    return;
                }
                Assert.That(server.RequestManager.GetCurrentRequestIdForLifecycleExtension().HasValue, Is.True);
                callbackEntered.TrySetResult(item.Id);
                await releaseCallback.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                Task<NodeManagerRegistration> operation = lifecycle.AddAsync(
                    callbackFactory, null, cleanup.Token).AsTask();
                lifecycleQueued.TrySetResult(true);
                try
                {
                    added = await operation.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cleanup.IsCancellationRequested)
                {
                    // Break the reproduced cycle only after the bounded liveness assertion has failed.
                }
            };
            CreateSubscriptionResponse subscription = await session.CreateSubscriptionAsync(
                null, 100, 100, 10, 0, true, 0, timeout.Token).ConfigureAwait(false);
            Task<NodeManagerBatchResult> commit = prepared.CommitAsync(async token =>
            {
                Interlocked.Increment(ref decisions);
                decisionEntered.TrySetResult(true);
                await releaseDecision.Task.WaitAsync(token).ConfigureAwait(false);
            }, timeout.Token).AsTask();
            await decisionEntered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            Task<uint> admission = AdmitEventAsync();
            bool circularWait = false;
            try
            {
                uint itemId = await callbackEntered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                releaseCallback.TrySetResult(true);
                await lifecycleQueued.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                await server.RequestManager.WaitForCurrentRequestsAsync(timeout.Token).ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(commit.IsCompleted, Is.False);
                    Assert.That(admission.IsCompleted, Is.False);
                    Assert.That(callbackFactory.CreateCount, Is.Zero);
                    Assert.That(original.SubscribedAllEventIds.ToList(), Is.EqualTo(new[] { itemId }));
                    Assert.That(candidate.SubscribedAllEventIds.ToList(), Is.Empty);
                    Assert.That(prepared.IsCommitted, Is.False);
                }
                releaseDecision.TrySetResult(true);
                try
                {
                    await Task.WhenAll(commit, admission).WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    circularWait = !commit.IsCompleted && !admission.IsCompleted && callbackFactory.CreateCount == 0;
                    throw;
                }
                finally
                {
                    cleanup.Cancel();
                    await Task.WhenAll(commit, admission).WaitAsync(timeout.Token).ConfigureAwait(false);
                    Assert.That(circularWait, Is.False,
                        "The accepted batch waits for the admitted binding callback while its nested Add waits " +
                        "for lifecycle admission; the request drain confirmed the callback was a lifecycle waiter.");
                }
                NodeManagerBatchResult result = await commit.ConfigureAwait(false);
                NodeId sessionId = server.SessionManager.GetSession(m_requestHeader.AuthenticationToken).Id;
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(result.CleanupFailure, Is.Null);
                    Assert.That(decisions, Is.EqualTo(1));
                    Assert.That(prepared.IsCommitted, Is.True);
                    Assert.That(added.NodeManager, Is.SameAs(nested));
                    Assert.That(lifecycle.Registrations.Count, Is.EqualTo(3));
                    Assert.That(await admission.ConfigureAwait(false), Is.EqualTo(itemId));
                    Assert.That(candidate.ActivatedSessionIds.ToList(),
                        Is.EquivalentTo(new[] { sessionId, session.SessionId }));
                    Assert.That(nested.ActivatedSessionIds.ToList(),
                        Is.EquivalentTo(new[] { sessionId, session.SessionId }));
                    Assert.That(candidate.SubscribedAllEventIds.ToList(), Is.EqualTo(new[] { itemId }));
                    Assert.That(nested.SubscribedAllEventIds.ToList(), Is.EqualTo(new[] { itemId }));
                }
            }
            finally
            {
                releaseCallback.TrySetResult(true);
                releaseDecision.TrySetResult(true);
                cleanup.Cancel();
                original.AllEventsCallback = null;
                await admission.WaitAsync(timeout.Token).ConfigureAwait(false);
                await commit.WaitAsync(timeout.Token).ConfigureAwait(false);
                await session.DeleteSubscriptionsAsync(null, [subscription.SubscriptionId], timeout.Token)
                    .ConfigureAwait(false);
                await session.CloseAsync(timeout.Token).ConfigureAwait(false);
            }

            async Task<uint> AdmitEventAsync()
            {
                var filter = new EventFilter();
                filter.AddSelectClause(ObjectTypeIds.BaseEventType, QualifiedName.From(BrowseNames.EventType));
                CreateMonitoredItemsResponse response = await session.CreateMonitoredItemsAsync(
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
                                SamplingInterval = 0,
                                Filter = new ExtensionObject(filter),
                                QueueSize = 1,
                                DiscardOldest = true
                            }
                        }
                    ], timeout.Token).ConfigureAwait(false);
                Assert.That(response.Results.Count, Is.EqualTo(1));
                Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                return response.Results[0].MonitoredItemId;
            }
        }

        private async Task VerifyCallbackAdmissionCompletionAsync(
            bool rejectDecision, bool cancelNested, bool failAdmission)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var nestedCancellation = new CancellationTokenSource();
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
            await lifecycle.AddAsync(CreateTrackingNodeManagementFactory(
                kGeneration1Value, value => original = value), null, timeout.Token).ConfigureAwait(false);
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(CreateTrackingNodeManagementFactory(
                    kGeneration2Value, value => candidate = value, kSecondModelNamespaceUri))],
                timeout.Token).ConfigureAwait(false);
            IAsyncNodeManagerFactory inner = CreateTrackingNodeManagementFactory(
                303, value => nested = value, kReadinessProbeNamespaceUri);
            var nestedFactory = new CallbackSafeNodeManagerFactory([kReadinessProbeNamespaceUri], inner.CreateAsync);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var queued = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            uint eventId = 0;
            bool nestedCancelled = false;
            original.AllEventsCallback = async (item, unsubscribe, _) =>
            {
                if (unsubscribe)
                {
                    return;
                }
                eventId = item.Id;
                Task<NodeManagerRegistration> adding = lifecycle.AddAsync(
                    nestedFactory, null, nestedCancellation.Token).AsTask();
                queued.TrySetResult(true);
                try
                {
                    await adding.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (nestedCancellation.IsCancellationRequested)
                {
                    nestedCancelled = true;
                }
                if (failAdmission)
                {
                    throw new IOException("The original event source rejected this provisional item.");
                }
            };
            CreateSubscriptionResponse subscription = await session.CreateSubscriptionAsync(
                null, 100, 100, 10, 0, true, 0, timeout.Token).ConfigureAwait(false);
            Task<NodeManagerBatchResult> commit = prepared.CommitAsync(async token =>
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(token).ConfigureAwait(false);
                if (rejectDecision)
                {
                    throw new IOException("Confirmed batch noncommit.");
                }
            }, timeout.Token).AsTask();
            await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            Task<CreateMonitoredItemsResponse> admission = CreateEventAsync(1);
            try
            {
                await queued.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                await server.RequestManager.WaitForCurrentRequestsAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(nestedFactory.CreateCount, Is.Zero);
                Assert.That(candidate.SubscribedAllEventIds.IsEmpty, Is.True);
                if (cancelNested)
                {
                    nestedCancellation.Cancel();
                    await admission.WaitAsync(timeout.Token).ConfigureAwait(false);
                    Assert.That(nestedCancelled, Is.True);
                }
                release.TrySetResult(true);
                if (rejectDecision)
                {
                    await Assert.ThatAsync(() => commit, Throws.TypeOf<IOException>()).ConfigureAwait(false);
                    await prepared.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    NodeManagerBatchResult result = await commit.WaitAsync(timeout.Token).ConfigureAwait(false);
                    Assert.That(result.CleanupFailure, Is.Null);
                }
                CreateMonitoredItemsResponse response = await admission.WaitAsync(timeout.Token).ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(response.Results[0].StatusCode,
                        Is.EqualTo(failAdmission ? StatusCodes.BadUnexpectedError : StatusCodes.Good));
                    Assert.That(nestedCancelled, Is.EqualTo(cancelNested));
                    Assert.That(nestedFactory.CreateCount, Is.EqualTo(cancelNested ? 0 : 1));
                    Assert.That(prepared.IsCommitted, Is.EqualTo(!rejectDecision));
                    Assert.That(server.EventManager.GetMonitoredItems().Any(item => item.Id == eventId),
                        Is.EqualTo(!failAdmission));
                    Assert.That(candidate.SubscribedAllEventIds.ToList(),
                        Is.EqualTo(rejectDecision ? Array.Empty<uint>() : new[] { eventId }));
                    if (failAdmission)
                    {
                        Assert.That(candidate.UnsubscribedAllEventIds.ToList(),
                            Is.EqualTo(rejectDecision ? Array.Empty<uint>() : new[] { eventId }),
                            "A published candidate must not retain a provisional event that later failed.");
                    }
                    if (!cancelNested)
                    {
                        Assert.That(nested.SubscribedAllEventIds.ToList(), Is.EqualTo(new[] { eventId }));
                        Assert.That(nested.UnsubscribedAllEventIds.ToList(),
                            Is.EqualTo(failAdmission ? new[] { eventId } : Array.Empty<uint>()));
                    }
                }
                original.AllEventsCallback = null;
                CreateMonitoredItemsResponse later = await CreateEventAsync(2).WaitAsync(timeout.Token)
                    .ConfigureAwait(false);
                Assert.That(later.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good),
                    "A completed or cancelled nested lifecycle operation must restore ordinary binding admission.");
            }
            finally
            {
                release.TrySetResult(true);
                nestedCancellation.Cancel();
                original.AllEventsCallback = null;
                await session.DeleteSubscriptionsAsync(null, [subscription.SubscriptionId], timeout.Token)
                    .ConfigureAwait(false);
                await session.CloseAsync(timeout.Token).ConfigureAwait(false);
            }

            async Task<CreateMonitoredItemsResponse> CreateEventAsync(uint clientHandle)
            {
                var filter = new EventFilter();
                filter.AddSelectClause(ObjectTypeIds.BaseEventType, QualifiedName.From(BrowseNames.EventType));
                return await session.CreateMonitoredItemsAsync(
                    new RequestHeader { TimeoutHint = 60000 },
                    subscription.SubscriptionId, TimestampsToReturn.Both,
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
                                ClientHandle = clientHandle,
                                Filter = new ExtensionObject(filter),
                                QueueSize = 1,
                                DiscardOldest = true
                            }
                        }
                    ], timeout.Token).ConfigureAwait(false);
            }
        }
    }
}
