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
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class NodeManagerLifecycleTests
    {
        /// <summary>
        /// Verifies that a held event callback does not serialize unrelated client operations.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task BlockedEventCallbackAllowsUnrelatedCreateModifyDeleteAndActivationAsync(bool unsubscribe)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            TrackingLifecycleNodeManager? manager = null;
            await m_server.NodeManagerLifecycle.AddAsync(CreateTrackingNodeManagementFactory(
                kGeneration1Value, value => manager = value), null, timeout.Token).ConfigureAwait(false);
            AssertLifecycleValue(manager);
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            m_requestHeader.Timestamp = DateTimeUtc.Now;
            CreateSubscriptionResponse subscription = await services.CreateSubscriptionAsync(
                m_requestHeader, 100, 100, 10, 0, true, 0).ConfigureAwait(false);
            uint subscriptionId = subscription.SubscriptionId;
            uint unrelatedItem = await CreateEventMonitoredItemAsync(
                services, subscriptionId, ObjectIds.Server, 1).ConfigureAwait(false);
            uint heldItem = unsubscribe
                ? await CreateEventMonitoredItemAsync(
                    services, subscriptionId, ObjectIds.Server, 2).ConfigureAwait(false)
                : 0;
            var entered = new TaskCompletionSource<uint>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int callbacks = 0;
            manager.AllEventsCallback = async (item, removing, _) =>
            {
                if (removing != unsubscribe || Interlocked.Increment(ref callbacks) != 1)
                {
                    return;
                }
                entered.TrySetResult(item.Id);
                await release.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            };
            Task held = unsubscribe
                ? DeleteMonitoredItemAsync(services, subscriptionId, heldItem)
                : CreateEventMonitoredItemAsync(services, subscriptionId, ObjectIds.Server, 2);
            Task<(RequestHeader, SecureChannelContext)>? activation = null;
            Task<uint>? creation = null;
            try
            {
                heldItem = await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                creation = CreateEventMonitoredItemAsync(services, subscriptionId, ObjectIds.Server, 3);
                activation = m_server.CreateAndActivateSessionAsync(
                    "IndependentOfHeldEventCallback", clientApplicationUri: kClientApplicationUri);
                uint createdItem = await creation.WaitAsync(timeout.Token).ConfigureAwait(false);
                (RequestHeader header, _) = await activation.WaitAsync(timeout.Token).ConfigureAwait(false);
                NodeId sessionId = RequireLifecycleValue(m_server.CurrentInstance.SessionManager
                    .GetSession(header.AuthenticationToken)).Id;
                var filter = new EventFilter();
                filter.AddSelectClause(ObjectTypeIds.BaseEventType, QualifiedName.From(BrowseNames.EventType));
                m_requestHeader.Timestamp = DateTimeUtc.Now;
                ModifyMonitoredItemsResponse modified = await services.ModifyMonitoredItemsAsync(
                    m_requestHeader, subscriptionId, TimestampsToReturn.Both,
                    [
                        new MonitoredItemModifyRequest
                        {
                            MonitoredItemId = unrelatedItem,
                            RequestedParameters = new MonitoringParameters
                            {
                                ClientHandle = 1,
                                Filter = new ExtensionObject(filter),
                                QueueSize = 2,
                                DiscardOldest = true
                            }
                        }
                    ]).AsTask().WaitAsync(timeout.Token).ConfigureAwait(false);
                await DeleteMonitoredItemAsync(services, subscriptionId, unrelatedItem)
                    .WaitAsync(timeout.Token).ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(held.IsCompleted, Is.False,
                        "Unrelated requests must finish before the held callback is released.");
                    Assert.That(modified.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(modified.Results[0].RevisedQueueSize, Is.EqualTo(2));
                    Assert.That(manager.ActivatedSessionIds.ToList().Count(id => id == sessionId), Is.EqualTo(1));
                    Assert.That(manager.SubscribedAllEventIds.ToList().Count(id => id == createdItem), Is.EqualTo(1));
                    Assert.That(manager.UnsubscribedAllEventIds.ToList().Count(id => id == unrelatedItem),
                        Is.EqualTo(1));
                }
            }
            finally
            {
                release.TrySetResult(true);
                manager.AllEventsCallback = (_, _, _) => default;
                await held.WaitAsync(timeout.Token).ConfigureAwait(false);
                if (creation is not null)
                {
                    await creation.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
                if (activation is not null)
                {
                    (RequestHeader header, SecureChannelContext channel) =
                        await activation.WaitAsync(timeout.Token).ConfigureAwait(false);
                    header.Timestamp = DateTimeUtc.Now;
                    await m_server.CloseSessionAsync(channel, header, true, RequestLifetime.None).ConfigureAwait(false);
                }
                await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
            }
            using (Assert.EnterMultipleScope())
            {
                Assert.That(manager.SubscribedAllEventIds.ToList().Count(id => id == heldItem), Is.EqualTo(1));
                Assert.That(manager.UnsubscribedAllEventIds.ToList().Count(id => id == heldItem), Is.EqualTo(1));
            }
        }

        /// <summary>
        /// Verifies that nested lifecycle calls need no callback context and cannot affect a peer binding.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task NullOrUnrelatedContextNestedAddLeavesPeerBindingIndependentAsync(bool nullContext)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            TrackingLifecycleNodeManager? manager = null;
            await m_server.NodeManagerLifecycle.AddAsync(CreateTrackingNodeManagementFactory(
                kGeneration1Value, value => manager = value), null, timeout.Token).ConfigureAwait(false);
            AssertLifecycleValue(manager);
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            m_requestHeader.Timestamp = DateTimeUtc.Now;
            CreateSubscriptionResponse subscription = await services.CreateSubscriptionAsync(
                m_requestHeader, 100, 100, 10, 0, true, 0).ConfigureAwait(false);
            TrackingLifecycleNodeManager? nested = null;
            IAsyncNodeManagerFactory nestedFactory = CreateTrackingNodeManagementFactory(
                304, value => nested = value, kReadinessProbeNamespaceUri);
            var callbackFactory = new CountingNodeManagerFactory(
                [kReadinessProbeNamespaceUri], nestedFactory.CreateAsync);
            IOperationContext? custom = nullContext ? null : Mock.Of<IOperationContext>();
            var added = new TaskCompletionSource<NodeManagerRegistration>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int callbacks = 0;
            manager.AllEventsCallback = async (_, unsubscribe, token) =>
            {
                if (unsubscribe || Interlocked.Increment(ref callbacks) != 1)
                {
                    return;
                }
                NodeManagerRegistration registration = await m_server.NodeManagerLifecycle.AddAsync(
                    callbackFactory, custom, timeout.Token).ConfigureAwait(false);
                added.TrySetResult(registration);
                await release.Task.WaitAsync(token).ConfigureAwait(false);
            };
            Task<uint>? admission = null;
            Task<uint>? peer = null;
            try
            {
                admission = CreateEventMonitoredItemAsync(
                    services, subscription.SubscriptionId, ObjectIds.Server, 1);
                NodeManagerRegistration registration = await added.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                peer = CreateEventMonitoredItemAsync(
                    services, subscription.SubscriptionId, ObjectIds.Server, 2);
                uint peerId = await peer.WaitAsync(timeout.Token).ConfigureAwait(false);
                AssertLifecycleValue(nested);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(registration.NodeManager, Is.SameAs(nested));
                    Assert.That(admission.IsCompleted, Is.False,
                        "The peer must complete while the original callback is still held.");
                    Assert.That(callbackFactory.CreateCount, Is.EqualTo(1));
                    Assert.That(manager.SubscribedAllEventIds.ToList().Count(id => id == peerId), Is.EqualTo(1));
                    Assert.That(nested.SubscribedAllEventIds.ToList().Count(id => id == peerId), Is.EqualTo(1));
                }
                release.TrySetResult(true);
                uint itemId = await admission.WaitAsync(timeout.Token).ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(manager.SubscribedAllEventIds.ToList(), Is.EquivalentTo(new[] { itemId, peerId }));
                    Assert.That(nested.SubscribedAllEventIds.ToList(), Is.EquivalentTo(new[] { itemId, peerId }));
                }
            }
            finally
            {
                release.TrySetResult(true);
                manager.AllEventsCallback = (_, _, _) => default;
                if (admission is not null)
                {
                    await admission.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
                if (peer is not null)
                {
                    await peer.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
                await DeleteSubscriptionAsync(services, subscription.SubscriptionId).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies that deletion is visible before unsubscribe and a nested publication cannot replay the item.
        /// </summary>
        [Test]
        public async Task DeleteRemovesEventRegistryEntryBeforeUnsubscribeAndNestedAddAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            TrackingLifecycleNodeManager? manager = null;
            await m_server.NodeManagerLifecycle.AddAsync(CreateTrackingNodeManagementFactory(
                kGeneration1Value, value => manager = value), null, timeout.Token).ConfigureAwait(false);
            AssertLifecycleValue(manager);
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            m_requestHeader.Timestamp = DateTimeUtc.Now;
            CreateSubscriptionResponse subscription = await services.CreateSubscriptionAsync(
                m_requestHeader, 100, 100, 10, 0, true, 0).ConfigureAwait(false);
            uint itemId = await CreateEventMonitoredItemAsync(
                services, subscription.SubscriptionId, ObjectIds.Server, 1).ConfigureAwait(false);
            TrackingLifecycleNodeManager? nested = null;
            IAsyncNodeManagerFactory nestedFactory = CreateTrackingNodeManagementFactory(
                305, value => nested = value, kReadinessProbeNamespaceUri);
            var callbackFactory = new CountingNodeManagerFactory(
                [kReadinessProbeNamespaceUri], nestedFactory.CreateAsync);
            NodeManagerRegistration? added = null;
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            manager.AllEventsCallback = async (item, unsubscribe, token) =>
            {
                if (unsubscribe && item.Id == itemId)
                {
                    Assert.That(m_server.CurrentInstance.EventManager.GetMonitoredItems()
                        .Any(current => current.Id == itemId), Is.False,
                        "The unsubscribe callback must observe the registry removal.");
                    added = await m_server.NodeManagerLifecycle.AddAsync(
                        callbackFactory, null, token).ConfigureAwait(false);
                    AssertLifecycleValue(nested);
                    Assert.That(nested.SubscribedAllEventIds.ToList(), Is.Empty,
                        "A nested Add must not subscribe the item being deleted.");
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
            };
            Task<DeleteMonitoredItemsResponse>? deletion = null;
            try
            {
                m_requestHeader.Timestamp = DateTimeUtc.Now;
                deletion = services.DeleteMonitoredItemsAsync(
                    m_requestHeader, subscription.SubscriptionId, [itemId]).AsTask();
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(deletion.IsCompleted, Is.False);
                Assert.That(m_server.CurrentInstance.EventManager.GetMonitoredItems()
                    .Any(item => item.Id == itemId), Is.False);
                release.TrySetResult(true);
                DeleteMonitoredItemsResponse response = await deletion.WaitAsync(timeout.Token).ConfigureAwait(false);
                AssertLifecycleValue(nested);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(response.Results[0], Is.EqualTo(StatusCodes.Good));
                    Assert.That(added, Is.Not.Null);
                    Assert.That(callbackFactory.CreateCount, Is.EqualTo(1));
                    Assert.That(manager.UnsubscribedAllEventIds.ToList(), Is.EqualTo(new[] { itemId }));
                    Assert.That(nested.SubscribedAllEventIds.ToList(), Is.Empty);
                    Assert.That(nested.UnsubscribedAllEventIds.ToList(), Is.Empty);
                }
                uint later = await CreateEventMonitoredItemAsync(
                    services, subscription.SubscriptionId, ObjectIds.Server, 2)
                    .WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(nested.SubscribedAllEventIds.ToList(), Is.EqualTo(new[] { later }),
                    "The nested manager must still bind later live items exactly once.");
            }
            finally
            {
                release.TrySetResult(true);
                manager.AllEventsCallback = (_, _, _) => default;
                if (deletion is not null)
                {
                    await deletion.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
                await DeleteSubscriptionAsync(services, subscription.SubscriptionId).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies that cancellation after registry withdrawal is surfaced only after all owners are released.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task CancellationDuringFirstEventUnsubscribeStillReleasesLaterOwnersAsync(bool throwCancellation)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var cancellation = new CancellationTokenSource();
            TrackingLifecycleNodeManager? first = null;
            TrackingLifecycleNodeManager? second = null;
            await m_server.NodeManagerLifecycle.AddAsync(CreateTrackingNodeManagementFactory(
                kGeneration1Value, value => first = value), null, timeout.Token).ConfigureAwait(false);
            await m_server.NodeManagerLifecycle.AddAsync(CreateTrackingNodeManagementFactory(
                kGeneration2Value, value => second = value, kSecondModelNamespaceUri), null, timeout.Token)
                .ConfigureAwait(false);
            AssertLifecycleValue(first);
            AssertLifecycleValue(second);
            IServerInternal server = m_server.CurrentInstance;
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            (uint subscriptionId, uint itemId) = await CreateSubscriptionAndEventMonitoredItemAsync(
                services, ObjectIds.Server).ConfigureAwait(false);
            IEventMonitoredItem item = server.EventManager.GetMonitoredItems().Single(value => value.Id == itemId);
            var secondEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseSecond = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            first.AllEventsCallback = (current, unsubscribe, token) =>
            {
                if (unsubscribe && ReferenceEquals(current, item))
                {
                    Assert.That(server.EventManager.GetMonitoredItems().Any(value => ReferenceEquals(value, item)),
                        Is.False, "Registry withdrawal must precede the first unsubscribe callback.");
                    Assert.That(token.CanBeCanceled, Is.False);
                    cancellation.Cancel();
                    if (throwCancellation)
                    {
                        throw new OperationCanceledException(cancellation.Token);
                    }
                }
                return default;
            };
            second.AllEventsCallback = async (current, unsubscribe, token) =>
            {
                if (unsubscribe && ReferenceEquals(current, item))
                {
                    Assert.That(cancellation.IsCancellationRequested, Is.True,
                        "The later owner must be released after the first owner cancels the caller.");
                    Assert.That(token.CanBeCanceled, Is.False);
                    secondEntered.TrySetResult(true);
                    await releaseSecond.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
            };
            using var context = new OperationContext(item);
            ServiceResult[] errors = [ServiceResult.Good];
            Task deletion = server.NodeManager.DeleteMonitoredItemsAsync(
                context, subscriptionId, [item], errors, cancellation.Token).AsTask();
            try
            {
                await secondEntered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(deletion.IsCompleted, Is.False,
                        "Caller cancellation must not surface before the release sweep finishes.");
                    Assert.That(server.EventManager.GetMonitoredItems().Any(value => ReferenceEquals(value, item)),
                        Is.False);
                    Assert.That(first.UnsubscribedAllEventIds.ToList(), Is.EqualTo(new[] { itemId }));
                    Assert.That(second.UnsubscribedAllEventIds.ToList(), Is.EqualTo(new[] { itemId }));
                }
                releaseSecond.TrySetResult(true);
                await Assert.ThatAsync(
                    async () => await deletion.WaitAsync(timeout.Token).ConfigureAwait(false),
                    Throws.InstanceOf<OperationCanceledException>()
                        .With.Property(nameof(OperationCanceledException.CancellationToken))
                        .EqualTo(cancellation.Token)).ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(first.UnsubscribedAllEventIds.ToList(), Is.EqualTo(new[] { itemId }));
                    Assert.That(second.UnsubscribedAllEventIds.ToList(), Is.EqualTo(new[] { itemId }));
                    Assert.That(server.EventManager.GetMonitoredItems().Any(value => ReferenceEquals(value, item)),
                        Is.False);
                }
            }
            finally
            {
                releaseSecond.TrySetResult(true);
                first.AllEventsCallback = (_, _, _) => default;
                second.AllEventsCallback = (_, _, _) => default;
                try
                {
                    await deletion.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException failure) when (failure.CancellationToken == cancellation.Token)
                {
                    // Caller cancellation is expected after the release sweep.
                }
                await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies that a captured in-flight generation bind releases a deleted item exactly once.
        /// </summary>
        [Test]
        public async Task DeletedEventCapturedByInFlightBindIsReleasedWithoutNestedRebindAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            TrackingLifecycleNodeManager? original = null;
            await m_server.NodeManagerLifecycle.AddAsync(CreateTrackingNodeManagementFactory(
                kGeneration1Value, value => original = value), null, timeout.Token).ConfigureAwait(false);
            AssertLifecycleValue(original);
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            (uint subscriptionId, uint itemId) = await CreateSubscriptionAndEventMonitoredItemAsync(
                services, ObjectIds.Server).ConfigureAwait(false);
            TrackingLifecycleNodeManager? candidate = null;
            TrackingLifecycleNodeManager? nested = null;
            var bindEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseBind = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var nestedAdded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            IAsyncNodeManagerFactory nestedFactory = CreateTrackingNodeManagementFactory(
                306, value => nested = value, kReadinessProbeNamespaceUri);
            var callbackFactory = new CountingNodeManagerFactory(
                [kReadinessProbeNamespaceUri], nestedFactory.CreateAsync);
            original.AllEventsCallback = async (item, unsubscribe, token) =>
            {
                if (unsubscribe && item.Id == itemId)
                {
                    Assert.That(m_server.CurrentInstance.EventManager.GetMonitoredItems()
                        .Any(current => current.Id == itemId), Is.False);
                    await m_server.NodeManagerLifecycle.AddAsync(callbackFactory, null, token).ConfigureAwait(false);
                    nestedAdded.TrySetResult(true);
                }
            };
            Task<NodeManagerRegistration> adding = m_server.NodeManagerLifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(kGeneration2Value, value =>
                {
                    candidate = value;
                    value.AllEventsCallback = async (item, unsubscribe, _) =>
                    {
                        if (!unsubscribe && item.Id == itemId)
                        {
                            bindEntered.TrySetResult(true);
                            await releaseBind.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                        }
                    };
                }, kSecondModelNamespaceUri), null, timeout.Token).AsTask();
            Task? deletion = null;
            try
            {
                await bindEntered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                deletion = DeleteMonitoredItemAsync(services, subscriptionId, itemId);
                await nestedAdded.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                await deletion.WaitAsync(timeout.Token).ConfigureAwait(false);
                AssertLifecycleValue(candidate);
                AssertLifecycleValue(nested);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(adding.IsCompleted, Is.False);
                    Assert.That(m_server.CurrentInstance.EventManager.GetMonitoredItems()
                        .Any(item => item.Id == itemId), Is.False);
                    Assert.That(nested.SubscribedAllEventIds.ToList(), Is.Empty);
                    Assert.That(candidate.SubscribedAllEventIds.ToList(), Is.EqualTo(new[] { itemId }));
                    Assert.That(candidate.UnsubscribedAllEventIds.ToList(), Is.Empty,
                        "An in-flight subscribe owns its own release after its callback returns.");
                    Assert.That(original.UnsubscribedAllEventIds.ToList(), Is.EqualTo(new[] { itemId }));
                }
                releaseBind.TrySetResult(true);
                NodeManagerRegistration registration = await adding.WaitAsync(timeout.Token).ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(registration.NodeManager, Is.SameAs(candidate));
                    Assert.That(candidate.SubscribedAllEventIds.ToList(), Is.EqualTo(new[] { itemId }));
                    Assert.That(candidate.UnsubscribedAllEventIds.ToList(), Is.EqualTo(new[] { itemId }));
                    Assert.That(nested.SubscribedAllEventIds.ToList(), Is.Empty);
                    Assert.That(nested.UnsubscribedAllEventIds.ToList(), Is.Empty);
                    Assert.That(callbackFactory.CreateCount, Is.EqualTo(1));
                }
                uint later = await CreateEventMonitoredItemAsync(
                    services, subscriptionId, ObjectIds.Server, 2).WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(candidate.SubscribedAllEventIds.ToList(), Is.EquivalentTo(new[] { itemId, later }));
                Assert.That(nested.SubscribedAllEventIds.ToList(), Is.EqualTo(new[] { later }));
                await DeleteMonitoredItemAsync(services, subscriptionId, later)
                    .WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(candidate.UnsubscribedAllEventIds.ToList(), Is.EquivalentTo(new[] { itemId, later }));
                Assert.That(nested.UnsubscribedAllEventIds.ToList(), Is.EqualTo(new[] { later }));
            }
            finally
            {
                releaseBind.TrySetResult(true);
                original.AllEventsCallback = (_, _, _) => default;
                if (deletion is not null)
                {
                    await deletion.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
                await adding.WaitAsync(timeout.Token).ConfigureAwait(false);
                await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies that a queued nested event callback completes after a batch decision or cancellation.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task PreparedBatchNestedEventAddCompletesAfterDecisionOrCancellationAsync(
            bool rejectDecision, bool cancelNested)
        {
            await VerifyNestedLifecycleCallbackCompletionAsync(rejectDecision, cancelNested, false)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Verifies that a failed event create releases bindings established by nested lifecycle operations.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task FailedEventCreateDoesNotRetainNewLifecycleBindingsAsync(bool rejectDecision)
        {
            await VerifyNestedLifecycleCallbackCompletionAsync(rejectDecision, false, true).ConfigureAwait(false);
        }

        /// <summary>
        /// Verifies that a native client session activates before an unrelated event unsubscribe returns.
        /// </summary>
        [Test]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task IndependentActivationCompletesWhileDeleteCallbackIsBlockedAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create())
            {
                OperationTimeout = 60000,
                SessionTimeout = 60000
            };
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            TrackingLifecycleNodeManager? manager = null;
            await m_server.NodeManagerLifecycle.AddAsync(CreateTrackingNodeManagementFactory(
                kGeneration1Value, value => manager = value), null, timeout.Token).ConfigureAwait(false);
            AssertLifecycleValue(manager);
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            m_requestHeader.Timestamp = DateTimeUtc.Now;
            CreateSubscriptionResponse subscription = await services.CreateSubscriptionAsync(
                m_requestHeader, 100, 100, 10, 0, true, 0).ConfigureAwait(false);
            uint itemId = await CreateEventMonitoredItemAsync(
                services, subscription.SubscriptionId, ObjectIds.Server, 1).ConfigureAwait(false);
            var deleteEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var deleteRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int activations = 0;
            manager.AllEventsCallback = async (_, unsubscribe, token) =>
            {
                if (unsubscribe)
                {
                    deleteEntered.TrySetResult(true);
                    await deleteRelease.Task.WaitAsync(token).ConfigureAwait(false);
                }
            };
            manager.SessionActivatedCallback = _ =>
            {
                Interlocked.Increment(ref activations);
                return default;
            };
            Task<DeleteMonitoredItemsResponse>? delete = null;
            Task<Opc.Ua.Client.ISession>? activation = null;
            Opc.Ua.Client.ISession? activatedSession = null;
            try
            {
                m_requestHeader.Timestamp = DateTimeUtc.Now;
                delete = services.DeleteMonitoredItemsAsync(
                    m_requestHeader, subscription.SubscriptionId, [itemId]).AsTask();
                await deleteEntered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);

                activation = client.ConnectAsync(
                    new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None);

                activatedSession = await activation.WaitAsync(timeout.Token).ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(delete.IsCompleted, Is.False,
                        "The native activation must finish before the unrelated delete callback is released.");
                    Assert.That(Volatile.Read(ref activations), Is.EqualTo(1));
                    Assert.That(manager.ActivatedSessionIds.ToList()
                        .Count(id => id == activatedSession.SessionId), Is.EqualTo(1));
                }
                deleteRelease.TrySetResult(true);
                DeleteMonitoredItemsResponse response = await delete.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(response.Results[0], Is.EqualTo(StatusCodes.Good));
                Assert.That(manager.UnsubscribedAllEventIds.ToList(), Is.EqualTo(new[] { itemId }));
            }
            finally
            {
                deleteRelease.TrySetResult(true);
                manager.AllEventsCallback = (_, _, _) => default;
                manager.SessionActivatedCallback = _ => default;
                if (delete is not null)
                {
                    await delete.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
                activatedSession ??= activation is null ? null : await activation.WaitAsync(timeout.Token)
                    .ConfigureAwait(false);
                if (activatedSession is not null)
                {
                    await activatedSession.CloseAsync(timeout.Token).ConfigureAwait(false);
                    activatedSession.Dispose();
                }
                await DeleteSubscriptionAsync(services, subscription.SubscriptionId).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies that a session callback can await a nested Add without forwarding its operation context.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task PreparedBatchAllowsNestedSessionCallbackAddWithoutContextAsync(bool rejectDecision)
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
            TrackingLifecycleNodeManager? original = null;
            TrackingLifecycleNodeManager? candidate = null;
            TrackingLifecycleNodeManager? nested = null;
            await lifecycle.AddAsync(CreateTrackingNodeManagementFactory(
                kGeneration1Value, value => original = value), null, timeout.Token).ConfigureAwait(false);
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(CreateTrackingNodeManagementFactory(
                    kGeneration2Value, value => candidate = value, kSecondModelNamespaceUri))],
                null, timeout.Token).ConfigureAwait(false);
            AssertLifecycleValue(original);
            AssertLifecycleValue(candidate);
            IAsyncNodeManagerFactory inner = CreateTrackingNodeManagementFactory(
                303, value => nested = value, kReadinessProbeNamespaceUri);
            var factory = new CountingNodeManagerFactory([kReadinessProbeNamespaceUri], inner.CreateAsync);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var queued = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            original.SessionActivatedCallback = async _ =>
            {
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
            Opc.Ua.Client.ISession? activatedSession = null;
            try
            {
                await queued.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                Task requestDrain = server.RequestManager.WaitForCurrentRequestsAsync(timeout.Token).AsTask();
                Assert.That(requestDrain.IsCompleted, Is.False,
                    "Ordinary drain must include the activation awaiting its nested lifecycle operation.");
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
                await requestDrain.WaitAsync(timeout.Token).ConfigureAwait(false);
                NodeId initialId = RequireLifecycleValue(
                    server.SessionManager.GetSession(m_requestHeader.AuthenticationToken)).Id;
                AssertLifecycleValue(nested);
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
                original.SessionActivatedCallback = _ => default;
                if (activatedSession is not null)
                {
                    await activatedSession.CloseAsync(timeout.Token).ConfigureAwait(false);
                    activatedSession.Dispose();
                }
            }
        }

        /// <summary>
        /// Verifies that a binding callback can await a nested Add after a concurrent batch publishes.
        /// </summary>
        [Test]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task PreparedBatchAllowsNestedBindingCallbackAddWithoutContextAsync()
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
            TrackingLifecycleNodeManager? original = null;
            await lifecycle.AddAsync(CreateTrackingNodeManagementFactory(
                kGeneration1Value, manager => original = manager), null, timeout.Token).ConfigureAwait(false);
            AssertLifecycleValue(original);
            TrackingLifecycleNodeManager? candidate = null;
            await using IPreparedNodeManagerBatch prepared = await ((INodeManagerBatchLifecycle)lifecycle).PrepareAsync(
                [NodeManagerBatchChange.Add(CreateTrackingNodeManagementFactory(
                    kSecondRegistrationValue, manager => candidate = manager, kSecondModelNamespaceUri))],
                null, timeout.Token).ConfigureAwait(false);
            AssertLifecycleValue(candidate);
            TrackingLifecycleNodeManager? nested = null;
            IAsyncNodeManagerFactory nestedFactory = CreateTrackingNodeManagementFactory(
                303, manager => nested = manager, kReadinessProbeNamespaceUri);
            var callbackFactory = new CountingNodeManagerFactory(
                [kReadinessProbeNamespaceUri], nestedFactory.CreateAsync);
            var decisionEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseDecision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var callbackEntered = new TaskCompletionSource<uint>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseCallback = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var lifecycleQueued = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            NodeManagerRegistration? added = null;
            int decisions = 0;
            original.AllEventsCallback = async (item, unsubscribe, _) =>
            {
                if (unsubscribe)
                {
                    return;
                }
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
                Task requestDrain = server.RequestManager.WaitForCurrentRequestsAsync(timeout.Token).AsTask();
                Assert.That(requestDrain.IsCompleted, Is.False,
                    "Ordinary drain must include the event admission awaiting its nested lifecycle operation.");
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
                    await requestDrain.WaitAsync(timeout.Token).ConfigureAwait(false);
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
                        "for lifecycle admission; neither operation may depend on ordinary request drain.");
                }
                NodeManagerBatchResult result = await commit.ConfigureAwait(false);
                NodeId sessionId = RequireLifecycleValue(
                    server.SessionManager.GetSession(m_requestHeader.AuthenticationToken)).Id;
                AssertLifecycleValue(added);
                AssertLifecycleValue(nested);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(result.CleanupFailure, Is.Null);
                    Assert.That(decisions, Is.EqualTo(1));
                    Assert.That(prepared.IsCommitted, Is.True);
                    Assert.That(added.NodeManager, Is.SameAs(nested));
                    Assert.That(GetBranchRegistrations(lifecycle).Count, Is.EqualTo(3));
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
                original.AllEventsCallback = (_, _, _) => default;
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

        /// <summary>
        /// Checks nested Add completion and provisional event cleanup across batch decisions and cancellation.
        /// </summary>
        private async Task VerifyNestedLifecycleCallbackCompletionAsync(
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
            TrackingLifecycleNodeManager? original = null;
            TrackingLifecycleNodeManager? candidate = null;
            TrackingLifecycleNodeManager? nested = null;
            await lifecycle.AddAsync(CreateTrackingNodeManagementFactory(
                kGeneration1Value, value => original = value), null, timeout.Token).ConfigureAwait(false);
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(CreateTrackingNodeManagementFactory(
                    kGeneration2Value, value => candidate = value, kSecondModelNamespaceUri))],
                null, timeout.Token).ConfigureAwait(false);
            AssertLifecycleValue(original);
            AssertLifecycleValue(candidate);
            IAsyncNodeManagerFactory inner = CreateTrackingNodeManagementFactory(
                303, value => nested = value, kReadinessProbeNamespaceUri);
            var nestedFactory = new CountingNodeManagerFactory([kReadinessProbeNamespaceUri], inner.CreateAsync);
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
                Task requestDrain = server.RequestManager.WaitForCurrentRequestsAsync(timeout.Token).AsTask();
                Assert.That(requestDrain.IsCompleted, Is.False,
                    "Ordinary drain must include the event request awaiting its nested lifecycle operation.");
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
                await requestDrain.WaitAsync(timeout.Token).ConfigureAwait(false);
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
                        AssertLifecycleValue(nested);
                        Assert.That(nested.SubscribedAllEventIds.ToList(), Is.EqualTo(new[] { eventId }));
                        Assert.That(nested.UnsubscribedAllEventIds.ToList(),
                            Is.EqualTo(failAdmission ? new[] { eventId } : Array.Empty<uint>()));
                    }
                }
                original.AllEventsCallback = (_, _, _) => default;
                CreateMonitoredItemsResponse later = await CreateEventAsync(2).WaitAsync(timeout.Token)
                    .ConfigureAwait(false);
                Assert.That(later.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good),
                    "Later client creates must succeed after a nested lifecycle operation completes or is cancelled.");
            }
            finally
            {
                release.TrySetResult(true);
                nestedCancellation.Cancel();
                original.AllEventsCallback = (_, _, _) => default;
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
