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
using System.Collections.Generic;
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
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task PreparedBatchRetirementCutsOffSourcesBeforeReadinessAsync(bool replace, bool immediate)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            IServerInternal server = m_server.CurrentInstance;
            TrackingLifecycleNodeManager originalManager = null;
            NodeManagerRegistration original = await m_server.NodeManagerLifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(
                    kGeneration1Value, manager => originalManager = manager, kSecondModelNamespaceUri),
                null, timeout.Token).ConfigureAwait(false);
            NodeId valueId = new(kValueNodeId, (ushort)server.NamespaceUris.GetIndex(kSecondModelNamespaceUri));
            var originalValue = (BaseVariableState)originalManager.Find(valueId);
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            (uint dataSubscription, uint dataItem) = await CreateSubscriptionAndMonitoredItemAsync(
                services, valueId, 1).ConfigureAwait(false);
            uint eventItem = await CreateEventMonitoredItemAsync(
                services, dataSubscription, ObjectIds.Server, 2).ConfigureAwait(false);
            (_, ArrayOf<SubscriptionAcknowledgement> acknowledgements) = await PublishForDataChangeAsync(
                services, dataSubscription, default, 1).ConfigureAwait(false);
            var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ReadinessLifecycleNodeManager candidate = null;
            TrackingLifecycleNodeManager replacement = null;
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [
                    NodeManagerBatchChange.Add(CreateCutoffReadinessFactory(manager =>
                    {
                        candidate = manager;
                        manager.ReadinessCallback = async token =>
                        {
                            Assert.That(token.CanBeCanceled, Is.False);
                            ready.TrySetResult(true);
                            await release.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                        };
                    })),
                    replace
                        ? NodeManagerBatchChange.Replace(original, CreateTrackingNodeManagementFactory(
                            kGeneration2Value, manager => replacement = manager, kSecondModelNamespaceUri), immediate)
                        : NodeManagerBatchChange.Remove(original, immediate)
                ], timeout.Token).ConfigureAwait(false);
            Task<NodeManagerBatchResult> commit = prepared.CommitAsync(_ => default, timeout.Token).AsTask();
            NodeManagerBatchResult result = null;
            try
            {
                await ready.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(prepared.IsCommitted, Is.True);
                Assert.That(commit.IsCompleted, Is.False);
                Assert.That(candidate.ReadinessCompletedCount, Is.Zero);
                DataValue current = await ReadValueAsync(valueId).ConfigureAwait(false);
                Assert.That(current.StatusCode, Is.EqualTo(replace ? StatusCodes.Good : StatusCodes.BadNodeIdUnknown));
                if (replace)
                {
                    Assert.That(current.WrappedValue.TryGetValue(out int value), Is.True);
                    Assert.That(value, Is.EqualTo(kGeneration2Value));
                }

                originalValue.Value = 777;
                originalValue.Timestamp = DateTimeUtc.Now;
                originalValue.StatusCode = StatusCodes.Good;
                originalValue.UpdateChangeMasks(NodeStateChangeMasks.Value);
                await originalValue.ClearChangeMasksAsync(server.DefaultSystemContext, includeChildren: false)
                    .ConfigureAwait(false);
                var delivery = await PublishForDataChangeAsync(
                    services, dataSubscription, acknowledgements, 1).ConfigureAwait(false);
                Assert.That(delivery.Value.HasValue, Is.True);
                DataValue observed = delivery.Value.GetValueOrDefault();
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(observed.StatusCode,
                        Is.EqualTo(immediate ? StatusCodes.BadNodeIdUnknown : StatusCodes.Good),
                        "A committed immediate source must stop new data before another candidate becomes ready.");
                    if (!immediate)
                    {
                        Assert.That(observed.WrappedValue.TryGetValue(out int pushed), Is.True);
                        Assert.That(pushed, Is.EqualTo(777));
                    }
                    Assert.That(originalManager.AllEventsUnsubscribeCount, Is.EqualTo(immediate ? 1 : 0),
                        "Immediate cutoff must also release the old all-events source binding.");
                    Assert.That(candidate.ReadinessCompletedCount, Is.Zero);
                    Assert.That(commit.IsCompleted, Is.False);
                }
            }
            finally
            {
                release.TrySetResult(true);
                result = await commit.WaitAsync(timeout.Token).ConfigureAwait(false);
                await DeleteMonitoredItemAsync(services, dataSubscription, dataItem).ConfigureAwait(false);
                await DeleteMonitoredItemAsync(services, dataSubscription, eventItem).ConfigureAwait(false);
                await DeleteSubscriptionAsync(services, dataSubscription).ConfigureAwait(false);
            }

            Assert.That(result.CleanupFailure, Is.Null);
            Assert.That(candidate.ReadinessCompletedCount, Is.EqualTo(1));
            await originalManager.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(originalManager.AllEventsUnsubscribeCount, Is.EqualTo(1));
            Assert.That(originalManager.DisposeCount, Is.EqualTo(1));
            if (replace)
            {
                Assert.That(replacement.DisposeCount, Is.Zero);
            }
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task PreparedBatchRejectedRetirementLeavesSourcesAttachedAsync(bool replace, bool immediate)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            IServerInternal server = m_server.CurrentInstance;
            TrackingLifecycleNodeManager originalManager = null;
            NodeManagerRegistration original = await m_server.NodeManagerLifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(
                    kGeneration1Value, manager => originalManager = manager, kSecondModelNamespaceUri),
                null, timeout.Token).ConfigureAwait(false);
            NodeId valueId = new(kValueNodeId, (ushort)server.NamespaceUris.GetIndex(kSecondModelNamespaceUri));
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            (uint subscriptionId, _) = await CreateSubscriptionAndMonitoredItemAsync(
                services, valueId, 1).ConfigureAwait(false);
            await CreateEventMonitoredItemAsync(services, subscriptionId, ObjectIds.Server, 2).ConfigureAwait(false);
            var initial = await PublishForDataChangeAsync(services, subscriptionId, default, 1).ConfigureAwait(false);
            ReadinessLifecycleNodeManager candidate = null;
            TrackingLifecycleNodeManager replacement = null;
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [
                    NodeManagerBatchChange.Add(CreateCutoffReadinessFactory(manager => candidate = manager)),
                    replace
                        ? NodeManagerBatchChange.Replace(original, CreateTrackingNodeManagementFactory(
                            kGeneration2Value, manager => replacement = manager, kSecondModelNamespaceUri), immediate)
                        : NodeManagerBatchChange.Remove(original, immediate)
                ], timeout.Token).ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var rejection = new IOException("No durable retirement decision was committed.");
            Task<NodeManagerBatchResult> commit = prepared.CommitAsync(async token =>
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(token).ConfigureAwait(false);
                throw rejection;
            }, timeout.Token).AsTask();
            try
            {
                try
                {
                    await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                    await CreateEventMonitoredItemAsync(services, subscriptionId, ObjectIds.Server, 3)
                        .ConfigureAwait(false);
                    await PushRetiredValueAsync(server, originalManager, valueId, 555).ConfigureAwait(false);
                    var during = await PublishForDataChangeAsync(
                        services, subscriptionId, initial.Acknowledgements, 1).ConfigureAwait(false);
                    using (Assert.EnterMultipleScope())
                    {
                        Assert.That(prepared.IsCommitted, Is.False);
                        Assert.That(commit.IsCompleted, Is.False);
                        Assert.That(during.Value.GetValueOrDefault().StatusCode, Is.EqualTo(StatusCodes.Good));
                        Assert.That(during.Value.GetValueOrDefault().WrappedValue, Is.EqualTo(new Variant(555)));
                        Assert.That(originalManager.AllEventsSubscribeCount, Is.EqualTo(2));
                        Assert.That(originalManager.AllEventsUnsubscribeCount, Is.Zero);
                        Assert.That(originalManager.DisposeCount, Is.Zero);
                        Assert.That(candidate.ReadinessCount, Is.Zero);
                    }
                    initial = during;
                }
                finally
                {
                    release.TrySetResult(true);
                    await Assert.ThatAsync(() => commit,
                        Throws.TypeOf<IOException>().With.Message.EqualTo(rejection.Message)).ConfigureAwait(false);
                }
                await prepared.DisposeAsync().ConfigureAwait(false);
                await PushRetiredValueAsync(server, originalManager, valueId, 888).ConfigureAwait(false);
                var after = await PublishForDataChangeAsync(
                    services, subscriptionId, initial.Acknowledgements, 1).ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(after.Value.GetValueOrDefault().StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(after.Value.GetValueOrDefault().WrappedValue, Is.EqualTo(new Variant(888)));
                    Assert.That(prepared.IsCommitted, Is.False);
                    Assert.That(lifecycle.Registrations[0], Is.SameAs(original));
                    Assert.That(originalManager.AllEventsUnsubscribeCount, Is.Zero);
                    Assert.That(originalManager.DisposeCount, Is.Zero);
                    Assert.That(candidate.ReadinessCount, Is.Zero);
                    Assert.That(candidate.DisposeCount, Is.EqualTo(1));
                    if (replace)
                    {
                        Assert.That(replacement.DisposeCount, Is.EqualTo(1));
                    }
                }
            }
            finally
            {
                release.TrySetResult(true);
                await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
            }
        }

        [Category("Integration")]
        [Category("NativeTcp")]
        [TestCase(false)]
        [TestCase(true)]
        public async Task PreparedBatchImmediateCutoffPreservesCapturedNativeReadAsync(bool replace)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            IServerInternal server = m_server.CurrentInstance;
            var master = (MasterNodeManager)server.NodeManager;
            TrackingLifecycleNodeManager originalManager = null;
            NodeManagerRegistration original = await m_server.NodeManagerLifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(
                    kGeneration1Value, manager => originalManager = manager, kSecondModelNamespaceUri),
                null, timeout.Token).ConfigureAwait(false);
            ushort ns = (ushort)server.NamespaceUris.GetIndex(kSecondModelNamespaceUri);
            NodeId valueId = new(kValueNodeId, ns);
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            (uint subscriptionId, uint itemId) = await CreateSubscriptionAndMonitoredItemAsync(
                services, valueId, 1).ConfigureAwait(false);
            var initial = await PublishForDataChangeAsync(services, subscriptionId, default, 1).ConfigureAwait(false);
            await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using Opc.Ua.Client.ISession session = await client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            var readEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseRead = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseReadiness = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool retainedRoutes = false;
            originalManager.ReadCallbackNodeId = valueId;
            originalManager.ReadCallback = async token =>
            {
                readEntered.TrySetResult(true);
                await releaseRead.Task.WaitAsync(token).ConfigureAwait(false);
                retainedRoutes = master.NamespaceManagers[ns].Contains(originalManager);
            };
            TrackingLifecycleNodeManager replacement = null;
            ReadinessLifecycleNodeManager candidate = null;
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [
                    NodeManagerBatchChange.Add(CreateCutoffReadinessFactory(manager =>
                    {
                        candidate = manager;
                        manager.ReadinessCallback = async _ =>
                        {
                            ready.TrySetResult(true);
                            await releaseReadiness.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                        };
                    })),
                    replace
                        ? NodeManagerBatchChange.Replace(original, CreateTrackingNodeManagementFactory(
                            kGeneration2Value, manager => replacement = manager, kSecondModelNamespaceUri), true)
                        : NodeManagerBatchChange.Remove(original, true)
                ], timeout.Token).ConfigureAwait(false);
            Task<ReadResponse> pendingRead = ReadNativeAsync();
            Task<NodeManagerBatchResult> commit = null;
            ReadResponse captured = null;
            NodeManagerBatchResult result = null;
            try
            {
                await readEntered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                commit = prepared.CommitAsync(_ => default, timeout.Token).AsTask();
                await ready.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                var retiredData = await PublishForDataChangeAsync(
                    services, subscriptionId, initial.Acknowledgements, 1).ConfigureAwait(false);
                ReadResponse current = await ReadNativeAsync().ConfigureAwait(false);
                var owner = (ISubscriptionMonitoredItemLifecycle)server.SubscriptionManager.GetSubscriptions()
                    .Single(subscription => subscription.Id == subscriptionId);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(prepared.IsCommitted, Is.True);
                    Assert.That(commit.IsCompleted, Is.False);
                    Assert.That(pendingRead.IsCompleted, Is.False);
                    Assert.That(candidate.ReadinessCompletedCount, Is.Zero);
                    Assert.That(retiredData.Value.GetValueOrDefault().StatusCode,
                        Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                    Assert.That(owner.HasMonitoredItems(originalManager), Is.False);
                    Assert.That(originalManager.DeleteAddressSpaceCount, Is.Zero);
                    Assert.That(originalManager.DisposeCount, Is.Zero);
                    Assert.That(current.Results[0].StatusCode,
                        Is.EqualTo(replace ? StatusCodes.Good : StatusCodes.BadNodeIdUnknown));
                    if (replace)
                    {
                        Assert.That(current.Results[0].WrappedValue, Is.EqualTo(new Variant(kGeneration2Value)));
                    }
                }
            }
            finally
            {
                releaseReadiness.TrySetResult(true);
                releaseRead.TrySetResult(true);
                captured = await pendingRead.WaitAsync(timeout.Token).ConfigureAwait(false);
                try
                {
                    if (commit is not null)
                    {
                        result = await commit.WaitAsync(timeout.Token).ConfigureAwait(false);
                    }
                    if (replace)
                    {
                        var owner = (ISubscriptionMonitoredItemLifecycle)server.SubscriptionManager.GetSubscriptions()
                            .Single(subscription => subscription.Id == subscriptionId);
                        Assert.That(owner.HasMonitoredItems(replacement), Is.False,
                            "Immediate replacement must not recover the source items invalidated by this switch.");
                    }
                }
                finally
                {
                    await DeleteMonitoredItemAsync(services, subscriptionId, itemId).ConfigureAwait(false);
                    await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
                    await session.CloseAsync(timeout.Token).ConfigureAwait(false);
                }
            }
            using (Assert.EnterMultipleScope())
            {
                Assert.That(captured.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(captured.Results[0].WrappedValue, Is.EqualTo(new Variant(kGeneration1Value)));
                Assert.That(retainedRoutes, Is.True);
                Assert.That(result.CleanupFailure, Is.Null);
                Assert.That(result.Retired, Is.EqualTo(1u));
                Assert.That(originalManager.DisposeCount, Is.EqualTo(1));
                if (replace)
                {
                    Assert.That(replacement.DisposeCount, Is.Zero);
                }
            }

            async Task<ReadResponse> ReadNativeAsync()
            {
                return await session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                    [new ReadValueId { NodeId = valueId, AttributeId = Attributes.Value }], timeout.Token)
                    .ConfigureAwait(false);
            }
        }

        [Test]
        public async Task PreparedBatchImmediateCutoffKeepsCapturedNotificationAndQueuedEventsAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            IServerInternal server = m_server.CurrentInstance;
            var master = (MasterNodeManager)server.NodeManager;
            TrackingLifecycleNodeManager originalManager = null;
            NodeManagerRegistration original = await m_server.NodeManagerLifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(
                    kGeneration1Value, manager => originalManager = manager, kSecondModelNamespaceUri),
                null, timeout.Token).ConfigureAwait(false);
            TrackingLifecycleNodeManager survivor = null;
            await m_server.NodeManagerLifecycle.AddAsync(CreateTrackingNodeManagementFactory(
                333, manager => survivor = manager, kReadinessProbeNamespaceUri), null, timeout.Token)
                .ConfigureAwait(false);
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            (uint subscriptionId, uint itemId) = await CreateSubscriptionAndEventMonitoredItemAsync(
                services, ObjectIds.Server).ConfigureAwait(false);
            await ModifyEventMonitoredItemAsync(services, subscriptionId, itemId).ConfigureAwait(false);
            IEventMonitoredItem item = server.EventManager.GetMonitoredItems().Single(value => value.Id == itemId);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseDispatch = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var published = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseReadiness = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            originalManager.ConditionRefreshCallback = async (_, token) =>
            {
                if (originalManager.ConditionRefreshCount == 1)
                {
                    entered.TrySetResult(true);
                    await releaseDispatch.Task.WaitAsync(token).ConfigureAwait(false);
                    QueueOccurrence("captured-before-cutoff");
                }
            };
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [
                    NodeManagerBatchChange.Add(CreateCutoffReadinessFactory(manager =>
                        manager.ReadinessCallback = async _ =>
                        {
                            ready.TrySetResult(true);
                            await releaseReadiness.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                        })),
                    NodeManagerBatchChange.Remove(original, true)
                ], timeout.Token).ConfigureAwait(false);
            QueueOccurrence("queued-before-cutoff");
            ISession session = server.SessionManager.GetSession(m_requestHeader.AuthenticationToken);
            using var context = new OperationContext(session, DiagnosticsMasks.None);
            Task dispatch = master.ConditionRefreshAsync(context, [item], timeout.Token).AsTask();
            Task<NodeManagerBatchResult> commit = null;
            NodeManagerBatchResult result = null;
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                commit = prepared.CommitAsync(_ => default, () => published.TrySetResult(true), timeout.Token).AsTask();
                await published.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                await master.ConditionRefreshAsync(context, [item], timeout.Token).ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(originalManager.ConditionRefreshCount, Is.EqualTo(1),
                        "New dispatches must not enter the retired source after the publication cutoff.");
                    Assert.That(originalManager.AllEventsUnsubscribeCount, Is.Zero,
                        "The dispatch admitted before cutoff still owns its all-events binding.");
                    Assert.That(ready.Task.IsCompleted, Is.False);
                    Assert.That(dispatch.IsCompleted, Is.False);
                    Assert.That(originalManager.DisposeCount, Is.Zero);
                }
                releaseDispatch.TrySetResult(true);
                await dispatch.WaitAsync(timeout.Token).ConfigureAwait(false);
                await ready.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(originalManager.AllEventsUnsubscribeCount, Is.EqualTo(1));
                Assert.That(survivor.AllEventsUnsubscribeCount, Is.Zero);
                Assert.That(survivor.ConditionRefreshCount, Is.EqualTo(2));

                var messages = new List<string>();
                ArrayOf<SubscriptionAcknowledgement> acknowledgements = default;
                while (messages.Count < 2)
                {
                    RequestHeader header = m_requestHeader;
                    header.Timestamp = DateTimeUtc.Now;
                    PublishResponse response = await services.PublishAsync(header, acknowledgements, timeout.Token)
                        .ConfigureAwait(false);
                    Assert.That(response.SubscriptionId, Is.EqualTo(subscriptionId));
                    acknowledgements = response.AvailableSequenceNumbers.ToArrayOf(sequence =>
                        new SubscriptionAcknowledgement { SubscriptionId = subscriptionId, SequenceNumber = sequence });
                    foreach (ExtensionObject notification in response.NotificationMessage.NotificationData)
                    {
                        if (!notification.TryGetValue(out EventNotificationList events))
                        {
                            continue;
                        }
                        foreach (EventFieldList occurrence in events.Events)
                        {
                            Assert.That(occurrence.ClientHandle, Is.EqualTo(1));
                            Assert.That(occurrence.EventFields[1].TryGetValue(out LocalizedText message), Is.True);
                            messages.Add(message.Text);
                        }
                    }
                }
                Assert.That(messages, Has.Count.EqualTo(2));
                Assert.That(messages[0], Is.EqualTo("queued-before-cutoff"));
                Assert.That(messages[1], Is.EqualTo("captured-before-cutoff"));
                Assert.That(server.EventManager.GetMonitoredItems().Single(value => value.Id == itemId),
                    Is.SameAs(item));
            }
            finally
            {
                releaseDispatch.TrySetResult(true);
                releaseReadiness.TrySetResult(true);
                await dispatch.WaitAsync(timeout.Token).ConfigureAwait(false);
                if (commit is not null)
                {
                    result = await commit.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
                await DeleteMonitoredItemAsync(services, subscriptionId, itemId).ConfigureAwait(false);
                RequestHeader header = m_requestHeader;
                header.Timestamp = DateTimeUtc.Now;
                DeleteMonitoredItemsResponse repeated = await services.DeleteMonitoredItemsAsync(
                    header, subscriptionId, [itemId]).ConfigureAwait(false);
                Assert.That(repeated.Results[0], Is.EqualTo(StatusCodes.BadMonitoredItemIdInvalid));
                await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
            }
            Assert.That(result.CleanupFailure, Is.Null);
            Assert.That(originalManager.AllEventsUnsubscribeCount, Is.EqualTo(1));
            Assert.That(survivor.AllEventsUnsubscribeCount, Is.EqualTo(1));
            Assert.That(originalManager.DisposeCount, Is.EqualTo(1));
            Assert.That(survivor.DisposeCount, Is.Zero);

            void QueueOccurrence(string message)
            {
                var occurrence = new BaseEventState(null);
                occurrence.Initialize(server.DefaultSystemContext, server.ServerObject,
                    EventSeverity.Medium, new LocalizedText(message));
                item.QueueEvent(occurrence);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task PreparedBatchImmediateCutoffFailuresRemainCommittedAndRetryExactBindingsAsync(bool badStatus)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            IServerInternal server = m_server.CurrentInstance;
            TrackingLifecycleNodeManager failing = null;
            TrackingLifecycleNodeManager healthy = null;
            TrackingLifecycleNodeManager survivor = null;
            NodeManagerRegistration first = await m_server.NodeManagerLifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(
                    kGeneration1Value, manager => failing = manager, kSecondModelNamespaceUri),
                null, timeout.Token).ConfigureAwait(false);
            NodeManagerRegistration second = await m_server.NodeManagerLifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(
                    kGeneration2Value, manager => healthy = manager, kReadinessProbeNamespaceUri),
                null, timeout.Token).ConfigureAwait(false);
            await m_server.NodeManagerLifecycle.AddAsync(CreateTrackingNodeManagementFactory(
                303, manager => survivor = manager, "urn:opcfoundation.org:Tests:CutoffFailureSurvivor"),
                null, timeout.Token).ConfigureAwait(false);
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            (uint subscriptionId, uint itemId) = await CreateSubscriptionAndEventMonitoredItemAsync(
                services, ObjectIds.Server).ConfigureAwait(false);
            await ModifyEventMonitoredItemAsync(services, subscriptionId, itemId).ConfigureAwait(false);
            var failure = new IOException("The retired source rejected its first unsubscribe.");
            if (badStatus)
            {
                failing.AllEventsUnsubscribeResult = StatusCodes.BadUnexpectedError;
            }
            else
            {
                failing.AllEventsCallback = (_, unsubscribe, _) => unsubscribe ? throw failure : default;
            }
            var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ReadinessLifecycleNodeManager candidate = null;
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [
                    NodeManagerBatchChange.Add(CreateCutoffReadinessFactory(manager =>
                    {
                        candidate = manager;
                        manager.ReadinessCallback = async _ =>
                        {
                            ready.TrySetResult(true);
                            await release.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                        };
                    })),
                    NodeManagerBatchChange.Remove(first, true),
                    NodeManagerBatchChange.Remove(second, true)
                ], timeout.Token).ConfigureAwait(false);
            Task<NodeManagerBatchResult> commit = prepared.CommitAsync(_ => default, timeout.Token).AsTask();
            NodeManagerBatchResult result = null;
            try
            {
                await ready.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                await failing.ReportOwnedEmissionAsync("failed-unsubscribe-new-business", timeout.Token)
                    .ConfigureAwait(false);
                await healthy.ReportOwnedEmissionAsync("healthy-retired-new-business", timeout.Token)
                    .ConfigureAwait(false);
                await survivor.ReportOwnedEmissionAsync("cleanup-failure-fence", timeout.Token).ConfigureAwait(false);
                var delivery = await PublishForModelChangeEventAsync(services, subscriptionId, default)
                    .ConfigureAwait(false);
                Assert.That(delivery.EventFields.EventFields[1].TryGetValue(out LocalizedText message), Is.True);
                Assert.That(message.Text, Is.EqualTo("cleanup-failure-fence"));
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(prepared.IsCommitted, Is.True);
                    Assert.That(commit.IsCompleted, Is.False);
                    Assert.That(failing.AllEventsUnsubscribeCount, Is.EqualTo(1));
                    Assert.That(healthy.AllEventsUnsubscribeCount, Is.EqualTo(1),
                        "One failing source must not prevent cutoff of later retired sources.");
                    Assert.That(candidate.ReadinessCount, Is.EqualTo(1));
                    Assert.That(server.EventManager.GetMonitoredItems().Single().Id, Is.EqualTo(itemId));
                }
            }
            finally
            {
                failing.AllEventsCallback = null;
                failing.AllEventsUnsubscribeResult = ServiceResult.Good;
                release.TrySetResult(true);
                result = await commit.WaitAsync(timeout.Token).ConfigureAwait(false);
                await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
            }
            Assert.That(result.CleanupFailure, Is.TypeOf<AggregateException>());
            var failures = ((AggregateException)result.CleanupFailure).Flatten().InnerExceptions;
            Assert.That(failures, Has.Count.EqualTo(1));
            if (badStatus)
            {
                Assert.That(failures[0], Is.TypeOf<ServiceResultException>());
                Assert.That(((ServiceResultException)failures[0]).StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
            }
            else
            {
                Assert.That(failures[0], Is.SameAs(failure));
            }
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.Retired, Is.EqualTo(2u));
                Assert.That(failing.AllEventsUnsubscribeCount, Is.EqualTo(2),
                    "Retry only the retained failed binding, not the whole live all-events set.");
                Assert.That(healthy.AllEventsUnsubscribeCount, Is.EqualTo(1));
                Assert.That(failing.DisposeCount, Is.EqualTo(1));
                Assert.That(healthy.DisposeCount, Is.EqualTo(1));
                Assert.That(candidate.ReadinessCompletedCount, Is.EqualTo(1));
            }
        }

        private IAsyncNodeManagerFactory CreateCutoffReadinessFactory(
            Action<ReadinessLifecycleNodeManager> configure)
        {
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(candidate => candidate.CreateAsync(
                It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .Returns((IServerInternal server, ApplicationConfiguration configuration, CancellationToken _) =>
                {
                    var manager = new ReadinessLifecycleNodeManager(
                        server, configuration, m_logger, kFirstRegistrationValue);
                    configure(manager);
                    return new ValueTask<IAsyncNodeManager>(manager);
                });
            return factory.Object;
        }
    }
}
