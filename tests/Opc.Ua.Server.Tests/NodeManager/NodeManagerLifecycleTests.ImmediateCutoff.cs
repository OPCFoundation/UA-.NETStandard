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
using static Opc.Ua.Server.Tests.NodeManager.LifecycleInspection;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Live lifecycle integration tests for <see cref="INodeManagerLifecycle"/> and its
    /// <see cref="NodeManagerRegistration"/> handles against a real, running
    /// <see cref="ReferenceServer"/>, focused on registration identity/copy semantics,
    /// monitored-item transitions, and rollback paths for Add/Reload/Remove.
    /// </summary>
    public sealed partial class NodeManagerLifecycleTests
    {
        /// <summary>
        /// Verifies that prepared batch retirement blocks old-generation samples before replacement readiness.
        /// </summary>
        /// <param name="replace">The replace flag for this test case.</param>
        /// <param name="immediate">The immediate flag for this test case.</param>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task PreparedBatchRetirementCutsOffSourcesBeforeReadinessAsync(bool replace, bool immediate)
        {
            Task exactWriter1Disposal = Task.CompletedTask;
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                IServerInternal server = m_server.CurrentInstance;
                TrackingLifecycleNodeManager? originalManager = null;
                LifecycleResult publicationResult1 = await CommitChangeAsync(
                    m_server.NodeManagerLifecycle, LifecycleChange.Add(CreateTrackingNodeManagementFactory(
                        kGeneration1Value, manager => originalManager = manager, kSecondModelNamespaceUri)),
                            timeout.Token).ConfigureAwait(false);
                NodeManagerRegistration original = RequireRegistration(publicationResult1.Registrations[0]);
                AssertLifecycleValue(originalManager);
                NodeId valueId = new(kValueNodeId, (ushort)server.NamespaceUris.GetIndex(kSecondModelNamespaceUri));
                BaseVariableState originalValue = RequireLifecycleValue(
                    originalManager.Find(valueId) as BaseVariableState);
                var services = new ServerTestServices(m_server, m_secureChannelContext);
                (uint dataSubscription, uint dataItem) = await CreateSubscriptionAndMonitoredItemAsync(
                    services, valueId, 1).ConfigureAwait(false);
                uint eventItem = await CreateEventMonitoredItemAsync(
                    services, dataSubscription, ObjectIds.Server, 2).ConfigureAwait(false);
                (_, ArrayOf<SubscriptionAcknowledgement> acknowledgements) = await PublishForDataChangeAsync(
                    services, dataSubscription, default, 1).ConfigureAwait(false);
                var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                ReadinessLifecycleNodeManager? candidate = null;
                TrackingLifecycleNodeManager? replacement = null;
                var lifecycle = (INodeManagerLifecycle)m_server.NodeManagerLifecycle;
                await using ILifecycleWriter exactWriter1 = await LifecycleInspection.Capture(
                    m_server.NodeManagerLifecycle, original).BeginAsync(timeout.Token).ConfigureAwait(false);
                await using IPreparedChanges prepared = await exactWriter1.PrepareAsync([
                        LifecycleChange.Add(CreateCutoffReadinessFactory(manager =>
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
                            ? LifecycleChange.Replace(original.Slot, CreateTrackingNodeManagementFactory(
                                kGeneration2Value, manager => replacement = manager, kSecondModelNamespaceUri),
                                    immediate
                                        ? NodeManagerMonitoredItemTransitionMode.Invalidate
                                        : NodeManagerMonitoredItemTransitionMode.Migrate)
                            : LifecycleChange.Remove(original.Slot)
                    ], timeout.Token).ConfigureAwait(false);
                AssertLifecycleValue(candidate);
                Task<LifecycleResult> commit = prepared.CommitAsync(_ => default, timeout.Token).AsTask();
                exactWriter1Disposal = exactWriter1.DisposeAsync().AsTask();
                LifecycleResult? result = null;
                try
                {
                    await ready.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                    Assert.That(prepared.IsCommitted, Is.True);
                    Assert.That(commit.IsCompleted, Is.False);
                    Assert.That(candidate.ReadinessCompletedCount, Is.Zero);
                    DataValue current = await ReadValueAsync(valueId).ConfigureAwait(false);
                    bool migrated = replace && !immediate;
                    Assert.That(
                        current.StatusCode, Is.EqualTo(replace ? StatusCodes.Good : StatusCodes.BadNodeIdUnknown));
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
                            Is.EqualTo(migrated ? StatusCodes.Good : StatusCodes.BadNodeIdUnknown),
                            "A retired source must stop before another candidate becomes ready.");
                        if (migrated)
                        {
                            Assert.That(observed.WrappedValue.TryGetValue(out int pushed), Is.True);
                            Assert.That(pushed, Is.EqualTo(kGeneration2Value),
                                "The migrated item must not publish a value from the retiring generation.");
                        }
                        Assert.That(originalManager.AllEventsUnsubscribeCount, Is.Zero,
                            "The retired gate cuts off sources; cleanup releases the binding after readiness.");
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
                    Assert.That(RequireLifecycleValue(replacement).DisposeCount, Is.Zero);
                }
            }
            finally
            {
                await exactWriter1Disposal.ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies that prepared batch rejected retirement leaves sources attached.
        /// </summary>
        /// <param name="replace">The replace flag for this test case.</param>
        /// <param name="immediate">The immediate flag for this test case.</param>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task PreparedBatchRejectedRetirementLeavesSourcesAttachedAsync(bool replace, bool immediate)
        {
            Task exactWriter2Disposal = Task.CompletedTask;
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                IServerInternal server = m_server.CurrentInstance;
                TrackingLifecycleNodeManager? originalManager = null;
                LifecycleResult publicationResult2 = await CommitChangeAsync(
                    m_server.NodeManagerLifecycle, LifecycleChange.Add(CreateTrackingNodeManagementFactory(
                        kGeneration1Value, manager => originalManager = manager, kSecondModelNamespaceUri)),
                            timeout.Token).ConfigureAwait(false);
                NodeManagerRegistration original = RequireRegistration(publicationResult2.Registrations[0]);
                AssertLifecycleValue(originalManager);
                NodeId valueId = new(kValueNodeId, (ushort)server.NamespaceUris.GetIndex(kSecondModelNamespaceUri));
                var services = new ServerTestServices(m_server, m_secureChannelContext);
                (uint subscriptionId, _) = await CreateSubscriptionAndMonitoredItemAsync(
                    services, valueId, 1).ConfigureAwait(false);
                await CreateEventMonitoredItemAsync(
                    services, subscriptionId, ObjectIds.Server, 2).ConfigureAwait(false);
                var initial = await PublishForDataChangeAsync(
                    services, subscriptionId, default, 1).ConfigureAwait(false);
                ReadinessLifecycleNodeManager? candidate = null;
                TrackingLifecycleNodeManager? replacement = null;
                var lifecycle = (INodeManagerLifecycle)m_server.NodeManagerLifecycle;
                await using ILifecycleWriter exactWriter2 = await LifecycleInspection.Capture(
                    m_server.NodeManagerLifecycle, original).BeginAsync(timeout.Token).ConfigureAwait(false);
                await using IPreparedChanges prepared = await exactWriter2.PrepareAsync([
                        LifecycleChange.Add(CreateCutoffReadinessFactory(manager => candidate = manager)),
                        replace
                            ? LifecycleChange.Replace(original.Slot, CreateTrackingNodeManagementFactory(
                                kGeneration2Value, manager => replacement = manager, kSecondModelNamespaceUri),
                                    immediate
                                        ? NodeManagerMonitoredItemTransitionMode.Invalidate
                                        : NodeManagerMonitoredItemTransitionMode.Migrate)
                            : LifecycleChange.Remove(original.Slot)
                    ], timeout.Token).ConfigureAwait(false);
                AssertLifecycleValue(candidate);
                var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var rejection = new IOException("No durable retirement decision was committed.");
                Task<LifecycleResult> commit = prepared.CommitAsync(async token =>
                {
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(token).ConfigureAwait(false);
                    throw rejection;
                }, timeout.Token).AsTask();
                exactWriter2Disposal = exactWriter2.DisposeAsync().AsTask();
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
                        Assert.That(GetBranchRegistration(lifecycle, original.Id), Is.SameAs(original));
                        Assert.That(originalManager.AllEventsUnsubscribeCount, Is.Zero);
                        Assert.That(originalManager.DisposeCount, Is.Zero);
                        Assert.That(candidate.ReadinessCount, Is.Zero);
                        Assert.That(candidate.DisposeCount, Is.EqualTo(1));
                        if (replace)
                        {
                            AssertLifecycleValue(replacement);
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
            finally
            {
                await exactWriter2Disposal.ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies that prepared batch immediate cutoff preserves captured native read.
        /// </summary>
        /// <param name="replace">The replace flag for this test case.</param>
        [Category("Integration")]
        [Category("NativeTcp")]
        [TestCase(false)]
        [TestCase(true)]
        public async Task PreparedBatchImmediateCutoffPreservesCapturedNativeReadAsync(bool replace)
        {
            Task exactWriter3Disposal = Task.CompletedTask;
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                IServerInternal server = m_server.CurrentInstance;
                var master = (MasterNodeManager)server.NodeManager;
                TrackingLifecycleNodeManager? originalManager = null;
                LifecycleResult publicationResult3 = await CommitChangeAsync(
                    m_server.NodeManagerLifecycle, LifecycleChange.Add(CreateTrackingNodeManagementFactory(
                        kGeneration1Value, manager => originalManager = manager, kSecondModelNamespaceUri)),
                            timeout.Token).ConfigureAwait(false);
                NodeManagerRegistration original = RequireRegistration(publicationResult3.Registrations[0]);
                AssertLifecycleValue(originalManager);
                ushort ns = (ushort)server.NamespaceUris.GetIndex(kSecondModelNamespaceUri);
                NodeId valueId = new(kValueNodeId, ns);
                var services = new ServerTestServices(m_server, m_secureChannelContext);
                (uint subscriptionId, uint itemId) = await CreateSubscriptionAndMonitoredItemAsync(
                    services, valueId, 1).ConfigureAwait(false);
                var initial = await PublishForDataChangeAsync(
                    services, subscriptionId, default, 1).ConfigureAwait(false);
                await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create());
                await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
                using Opc.Ua.Client.ISession session = await client.ConnectAsync(
                    new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                    .ConfigureAwait(false);
                var readEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var releaseRead = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var releaseReadiness = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                bool retainedRoutes = false;
                originalManager.ReadCallbackNodeId = valueId;
                originalManager.ReadCallback = async (context, token) =>
                {
                    readEntered.TrySetResult(true);
                    await releaseRead.Task.WaitAsync(token).ConfigureAwait(false);
                    retainedRoutes = context.RoutingSnapshot!.VisibleNamespaceManagers[ns].Contains(originalManager);
                };
                TrackingLifecycleNodeManager? replacement = null;
                ReadinessLifecycleNodeManager? candidate = null;
                var lifecycle = (INodeManagerLifecycle)m_server.NodeManagerLifecycle;
                await using ILifecycleWriter exactWriter3 = await LifecycleInspection.Capture(
                    m_server.NodeManagerLifecycle, original).BeginAsync(timeout.Token).ConfigureAwait(false);
                await using IPreparedChanges prepared = await exactWriter3.PrepareAsync([
                        LifecycleChange.Add(CreateCutoffReadinessFactory(manager =>
                        {
                            candidate = manager;
                            manager.ReadinessCallback = async _ =>
                            {
                                ready.TrySetResult(true);
                                await releaseReadiness.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                            };
                        })),
                        replace
                            ? LifecycleChange.Replace(original.Slot, CreateTrackingNodeManagementFactory(
                                kGeneration2Value, manager => replacement = manager, kSecondModelNamespaceUri),
                                    NodeManagerMonitoredItemTransitionMode.Invalidate)
                            : LifecycleChange.Remove(original.Slot)
                    ], timeout.Token).ConfigureAwait(false);
                AssertLifecycleValue(candidate);
                if (replace)
                {
                    AssertLifecycleValue(replacement);
                }
                Task<ReadResponse> pendingRead = ReadNativeAsync();
                Task<LifecycleResult>? commit = null;
                ReadResponse? captured = null;
                LifecycleResult? result = null;
                try
                {
                    await readEntered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                    commit = prepared.CommitAsync(_ => default, timeout.Token).AsTask();
                    exactWriter3Disposal = exactWriter3.DisposeAsync().AsTask();
                    await ready.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                    var retiredData = await PublishForDataChangeAsync(
                        services, subscriptionId, initial.Acknowledgements, 1).ConfigureAwait(false);
                    ReadResponse current = await ReadNativeAsync().ConfigureAwait(false);
                    var owner = (ISubscriptionMonitoredItemLifecycle)server.SubscriptionManager
                        .GetSubscriptions()
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
                            var owner = (ISubscriptionMonitoredItemLifecycle)server.SubscriptionManager
                                .GetSubscriptions()
                                .Single(subscription => subscription.Id == subscriptionId);
                            Assert.That(owner.HasMonitoredItems(RequireLifecycleValue(replacement)), Is.False,
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
                AssertLifecycleValue(captured);
                AssertLifecycleValue(result);
                await result.Retired.WaitAsync(timeout.Token).ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(captured.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(captured.Results[0].WrappedValue, Is.EqualTo(new Variant(kGeneration1Value)));
                    Assert.That(retainedRoutes, Is.True);
                    Assert.That(result.CleanupFailure, Is.Null);
                    Assert.That(originalManager.DisposeCount, Is.EqualTo(1));
                    if (replace)
                    {
                        Assert.That(RequireLifecycleValue(replacement).DisposeCount, Is.Zero);
                    }
                }

                async Task<ReadResponse> ReadNativeAsync()
                {
                    return await session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                        [new ReadValueId { NodeId = valueId, AttributeId = Attributes.Value }], timeout.Token)
                        .ConfigureAwait(false);
                }
            }
            finally
            {
                await exactWriter3Disposal.ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies that retirement preserves admitted events but rejects a target after its callback returns.
        /// </summary>
        /// <param name="emitAfterCutoff">Whether the admitted callback queues another event after publication.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task PreparedBatchRetirementPreservesAdmittedEventsAndRejectsExpiredTargetsAsync(
            bool emitAfterCutoff)
        {
            Task exactWriter4Disposal = Task.CompletedTask;
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                IServerInternal server = m_server.CurrentInstance;
                var master = (MasterNodeManager)server.NodeManager;
                TrackingLifecycleNodeManager? originalManager = null;
                LifecycleResult publicationResult4 = await CommitChangeAsync(
                    m_server.NodeManagerLifecycle, LifecycleChange.Add(CreateTrackingNodeManagementFactory(
                        kGeneration1Value, manager => originalManager = manager, kSecondModelNamespaceUri)),
                            timeout.Token).ConfigureAwait(false);
                NodeManagerRegistration original = RequireRegistration(publicationResult4.Registrations[0]);
                AssertLifecycleValue(originalManager);
                TrackingLifecycleNodeManager? survivor = null;
                RequireRegistration(
                    (await CommitChangeAsync(
                    m_server.NodeManagerLifecycle, LifecycleChange.Add(CreateTrackingNodeManagementFactory(
                    333, manager => survivor = manager, kReadinessProbeNamespaceUri)), timeout.Token)
                        .ConfigureAwait(false)).Registrations[0]);
                AssertLifecycleValue(survivor);
                var services = new ServerTestServices(m_server, m_secureChannelContext);
                (uint subscriptionId, uint itemId) = await CreateSubscriptionAndEventMonitoredItemAsync(
                    services, ObjectIds.Server).ConfigureAwait(false);
                await ModifyEventMonitoredItemAsync(services, subscriptionId, itemId).ConfigureAwait(false);
                IEventMonitoredItem item = server.EventManager.GetMonitoredItems().Single(value => value.Id == itemId);
                var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var releaseDispatch = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var published = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var releaseReadiness = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                IEventMonitoredItem? capturedTarget = null;
                originalManager.ConditionRefreshCallback = async (targets, token) =>
                {
                    if (originalManager.ConditionRefreshCount == 1)
                    {
                        capturedTarget = targets.Single(value => value.Id == itemId);
                        QueueOccurrence("queued-before-cutoff", capturedTarget);
                        entered.TrySetResult(true);
                        await releaseDispatch.Task.WaitAsync(token).ConfigureAwait(false);
                        if (emitAfterCutoff)
                        {
                            QueueOccurrence("admitted-after-cutoff", capturedTarget);
                        }
                    }
                };
                var lifecycle = (INodeManagerLifecycle)m_server.NodeManagerLifecycle;
                await using ILifecycleWriter exactWriter4 = await LifecycleInspection.Capture(
                    m_server.NodeManagerLifecycle, original).BeginAsync(timeout.Token).ConfigureAwait(false);
                await using IPreparedChanges prepared = await exactWriter4.PrepareAsync([
                        LifecycleChange.Add(CreateCutoffReadinessFactory(manager =>
                            manager.ReadinessCallback = async _ =>
                            {
                                ready.TrySetResult(true);
                                await releaseReadiness.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                            })),
                        LifecycleChange.Remove(original.Slot)
                    ], timeout.Token).ConfigureAwait(false);
                ISession session = RequireLifecycleValue(
                    server.SessionManager.GetSession(m_requestHeader.AuthenticationToken));
                Task? dispatch = null;
                Task<LifecycleResult>? commit = null;
                LifecycleResult? result = null;
                try
                {
                    using (var context = new OperationContext(session, DiagnosticsMasks.None))
                    {
                        dispatch = master.ConditionRefreshAsync(context, [item], timeout.Token).AsTask();
                        try
                        {
                            await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                            commit = prepared.CommitAsync(
                                _ => default,
                                () => published.TrySetResult(true),
                                timeout.Token).AsTask();
                            exactWriter4Disposal = exactWriter4.DisposeAsync().AsTask();
                            await published.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                            await ready.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                            await master.ConditionRefreshAsync(context, [item], timeout.Token).ConfigureAwait(false);
                            using (Assert.EnterMultipleScope())
                            {
                                Assert.That(originalManager.ConditionRefreshCount, Is.EqualTo(1),
                                    "New dispatches must not enter the retired source after the publication cutoff.");
                                Assert.That(originalManager.AllEventsUnsubscribeCount, Is.Zero,
                                    "The dispatch admitted before cutoff still owns its all-events binding.");
                                Assert.That(ready.Task.IsCompleted, Is.True,
                                    "New-generation readiness must not wait for an unrelated retired dispatch.");
                                Assert.That(dispatch.IsCompleted, Is.False);
                                Assert.That(originalManager.DisposeCount, Is.Zero);
                            }
                        }
                        finally
                        {
                            releaseDispatch.TrySetResult(true);
                            await dispatch.WaitAsync(timeout.Token).ConfigureAwait(false);
                        }
                    }
                    Assert.That(originalManager.AllEventsUnsubscribeCount, Is.Zero,
                        "Retirement cleanup waits for the blocked new-generation readiness callback.");
                    Assert.That(survivor.AllEventsUnsubscribeCount, Is.Zero);
                    Assert.That(survivor.ConditionRefreshCount, Is.EqualTo(2));
                    QueueOccurrence("expired-after-callback", RequireLifecycleValue(capturedTarget));

                    var messages = new List<string>();
                    int expectedCount = emitAfterCutoff ? 2 : 1;
                    ArrayOf<SubscriptionAcknowledgement> acknowledgements = default;
                    while (messages.Count < expectedCount)
                    {
                        RequestHeader header = m_requestHeader;
                        header.Timestamp = DateTimeUtc.Now;
                        PublishResponse response = await services.PublishAsync(header, acknowledgements, timeout.Token)
                            .ConfigureAwait(false);
                        Assert.That(response.SubscriptionId, Is.EqualTo(subscriptionId));
                        acknowledgements = response.AvailableSequenceNumbers.ToArrayOf(sequence =>
                            new SubscriptionAcknowledgement
                            {
                                SubscriptionId = subscriptionId,
                                SequenceNumber = sequence
                            });
                        foreach (ExtensionObject notification in response.NotificationMessage.NotificationData)
                        {
                            if (!notification.TryGetValue(out EventNotificationList? events))
                            {
                                continue;
                            }
                            foreach (EventFieldList occurrence in events.Events)
                            {
                                Assert.That(occurrence.ClientHandle, Is.EqualTo(1));
                                Assert.That(occurrence.EventFields[1].TryGetValue(out LocalizedText message), Is.True);
                                messages.Add(RequireLifecycleValue(message.Text));
                            }
                        }
                    }
                    Assert.That(messages, Has.Count.EqualTo(expectedCount), () => string.Join(", ", messages));
                    Assert.That(messages[0], Is.EqualTo("queued-before-cutoff"));
                    if (emitAfterCutoff)
                    {
                        Assert.That(messages[1], Is.EqualTo("admitted-after-cutoff"));
                    }
                    Assert.That(server.EventManager.GetMonitoredItems().Single(value => value.Id == itemId),
                        Is.SameAs(item));
                }
                finally
                {
                    releaseDispatch.TrySetResult(true);
                    releaseReadiness.TrySetResult(true);
                    if (dispatch is not null)
                    {
                        await dispatch.WaitAsync(timeout.Token).ConfigureAwait(false);
                    }
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
                await RequireLifecycleValue(result).Retired.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(RequireLifecycleValue(result).CleanupFailure, Is.Null);
                Assert.That(originalManager.AllEventsUnsubscribeCount, Is.EqualTo(1));
                Assert.That(survivor.AllEventsUnsubscribeCount, Is.EqualTo(1));
                Assert.That(originalManager.DisposeCount, Is.EqualTo(1));
                Assert.That(survivor.DisposeCount, Is.Zero);

                void QueueOccurrence(string message, IEventMonitoredItem target)
                {
                    var occurrence = new BaseEventState(null);
                    occurrence.Initialize(server.DefaultSystemContext, server.ServerObject,
                        EventSeverity.Medium, new LocalizedText(message));
                    target.QueueEvent(occurrence);
                }
            }
            finally
            {
                await exactWriter4Disposal.ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies that prepared batch immediate cutoff failures remain committed and retry exact bindings.
        /// </summary>
        /// <param name="badStatus">The bad-status flag for this test case.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task PreparedBatchImmediateCutoffFailuresRemainCommittedAndRetryExactBindingsAsync(bool badStatus)
        {
            Task exactWriter5Disposal = Task.CompletedTask;
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                IServerInternal server = m_server.CurrentInstance;
                var master = (MasterNodeManager)server.NodeManager;
                TrackingLifecycleNodeManager? failing = null;
                TrackingLifecycleNodeManager? healthy = null;
                TrackingLifecycleNodeManager? survivor = null;
                LifecycleResult publicationResult5 = await CommitChangeAsync(
                    m_server.NodeManagerLifecycle, LifecycleChange.Add(CreateTrackingNodeManagementFactory(
                        kGeneration1Value, manager => failing = manager, kSecondModelNamespaceUri)),
                            timeout.Token).ConfigureAwait(false);
                NodeManagerRegistration first = RequireRegistration(publicationResult5.Registrations[0]);
                LifecycleResult publicationResult6 = await CommitChangeAsync(
                    m_server.NodeManagerLifecycle, LifecycleChange.Add(CreateTrackingNodeManagementFactory(
                        kGeneration2Value, manager => healthy = manager, kReadinessProbeNamespaceUri)),
                            timeout.Token).ConfigureAwait(false);
                NodeManagerRegistration second = RequireRegistration(publicationResult6.Registrations[0]);
                LifecycleResult publicationResult7 = await CommitChangeAsync(
                    m_server.NodeManagerLifecycle, LifecycleChange.Add(CreateTrackingNodeManagementFactory(
                        303, manager => survivor = manager,
                            "urn:opcfoundation.org:Tests:CutoffFailureSurvivor")), timeout.Token).ConfigureAwait(false);
                NodeManagerRegistration survivorRegistration = RequireRegistration(publicationResult7.Registrations[0]);
                AssertLifecycleValue(failing);
                AssertLifecycleValue(healthy);
                AssertLifecycleValue(survivor);
                var services = new ServerTestServices(m_server, m_secureChannelContext);
                // The retry can outlive the default subscription's 10-second lifetime.
                (uint subscriptionId, uint itemId) = await CreateSubscriptionAndEventMonitoredItemAsync(
                    services, ObjectIds.Server, requestedLifetimeCount: 1000).ConfigureAwait(false);
                await ModifyEventMonitoredItemAsync(services, subscriptionId, itemId).ConfigureAwait(false);
                Assert.That(survivor.SubscribedAllEventIds.ToList(), Does.Contain(itemId));
                await survivor.ReportOwnedEmissionAsync("pre-cutoff-fence", timeout.Token).ConfigureAwait(false);
                var baselineDelivery = await PublishForModelChangeEventAsync(services, subscriptionId, default)
                    .ConfigureAwait(false);
                Assert.That(RequireLifecycleEvent(baselineDelivery.EventFields).EventFields[1]
                    .TryGetValue(out LocalizedText baselineMessage), Is.True);
                Assert.That(baselineMessage.Text, Is.EqualTo("pre-cutoff-fence"));
                var failure = new IOException("The retired source rejected its first unsubscribe.");
                var cleanupAttempted = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                int failuresRemaining = 1;
                failing.AllEventsCallback = (_, unsubscribe, _) =>
                {
                    if (unsubscribe)
                    {
                        cleanupAttempted.TrySetResult(true);
                        if (Interlocked.Exchange(ref failuresRemaining, 0) != 0)
                        {
                            if (badStatus)
                            {
                                throw new ServiceResultException(StatusCodes.BadUnexpectedError);
                            }
                            throw failure;
                        }
                    }
                    return default;
                };
                var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                ReadinessLifecycleNodeManager? candidate = null;
                var lifecycle = (INodeManagerLifecycle)m_server.NodeManagerLifecycle;
                await using ILifecycleWriter exactWriter5 = await LifecycleInspection.Capture(
                    m_server.NodeManagerLifecycle, first, second).BeginAsync(timeout.Token).ConfigureAwait(false);
                await using IPreparedChanges prepared = await exactWriter5.PrepareAsync([
                        LifecycleChange.Add(CreateCutoffReadinessFactory(manager =>
                        {
                            candidate = manager;
                            manager.ReadinessCallback = async _ =>
                            {
                                ready.TrySetResult(true);
                                await release.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                            };
                        })),
                        LifecycleChange.Remove(first.Slot),
                        LifecycleChange.Remove(second.Slot)
                    ], timeout.Token).ConfigureAwait(false);
                AssertLifecycleValue(candidate);
                Task<LifecycleResult> commit = prepared.CommitAsync(_ => default, timeout.Token).AsTask();
                exactWriter5Disposal = exactWriter5.DisposeAsync().AsTask();
                LifecycleResult? result = null;
                try
                {
                    await ready.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                    await failing.ReportOwnedEmissionAsync("failed-unsubscribe-new-business", timeout.Token)
                        .ConfigureAwait(false);
                    await healthy.ReportOwnedEmissionAsync("healthy-retired-new-business", timeout.Token)
                        .ConfigureAwait(false);
                    Assert.That(survivor.SubscribedAllEventIds.ToList(), Does.Contain(itemId),
                        "Retiring unrelated managers must preserve the surviving all-events binding.");
                    Assert.That(
                        master.TryGetCurrentGeneration(
                        survivor, out NodeManagerGeneration? currentGeneration), Is.True);
                    Assert.That(currentGeneration!.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
                    await survivor.ReportOwnedEmissionAsync(
                        "cleanup-failure-fence", timeout.Token).ConfigureAwait(false);
                    var delivery = await PublishForModelChangeEventAsync(services, subscriptionId, default)
                        .ConfigureAwait(false);
                    EventFieldList deliveredFields = RequireLifecycleEvent(delivery.EventFields);
                    Assert.That(deliveredFields.EventFields[1].TryGetValue(out LocalizedText message), Is.True);
                    Assert.That(message.Text, Is.EqualTo("cleanup-failure-fence"));
                    using (Assert.EnterMultipleScope())
                    {
                        Assert.That(prepared.IsCommitted, Is.True);
                        Assert.That(commit.IsCompleted, Is.False);
                        Assert.That(failing.AllEventsUnsubscribeCount, Is.Zero);
                        Assert.That(healthy.AllEventsUnsubscribeCount, Is.Zero);
                        Assert.That(candidate.ReadinessCount, Is.EqualTo(1));
                        Assert.That(server.EventManager.GetMonitoredItems().Single().Id, Is.EqualTo(itemId));
                    }
                }
                finally
                {
                    release.TrySetResult(true);
                    try
                    {
                        result = await commit.WaitAsync(timeout.Token).ConfigureAwait(false);
                        await cleanupAttempted.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                    }
                    finally
                    {
                        failing.AllEventsCallback = (_, _, _) => default;
                        failing.AllEventsUnsubscribeResult = ServiceResult.Good;
                        await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
                    }
                }
                await CommitChangeAsync(
                    LifecycleInspection.Capture(
                    m_server.NodeManagerLifecycle, survivorRegistration), LifecycleChange.Remove(
                    survivorRegistration.Slot), timeout.Token).ConfigureAwait(false);
                AssertLifecycleValue(result);
                Assert.That(result.CleanupFailure, Is.Null);
                TrackingLifecycleNodeManager? retryTrigger = null;
                LifecycleResult publicationResult8 = await CommitChangeAsync(
                    m_server.NodeManagerLifecycle, LifecycleChange.Add(CreateTrackingNodeManagementFactory(
                        404,
                        manager => retryTrigger = manager,
                        "urn:opcfoundation.org:Tests:CutoffFailureRetryTrigger")), timeout.Token).ConfigureAwait(false);
                NodeManagerRegistration retryRegistration = RequireRegistration(publicationResult8.Registrations[0]);
                AssertLifecycleValue(retryTrigger);
                if (badStatus)
                {
                    await Assert.ThatAsync(
                        () => result.Retired.WaitAsync(timeout.Token),
                        Throws.TypeOf<ServiceResultException>()).ConfigureAwait(false);
                }
                else
                {
                    await Assert.ThatAsync(
                        () => result.Retired.WaitAsync(timeout.Token),
                        Throws.Exception.SameAs(failure)).ConfigureAwait(false);
                }
                var failures = RequireLifecycleValue(result.Retired.Exception).Flatten().InnerExceptions;
                Assert.That(failures, Has.Count.EqualTo(1));
                if (badStatus)
                {
                    Assert.That(failures[0], Is.TypeOf<ServiceResultException>());
                    Assert.That(((ServiceResultException)failures[0]).StatusCode,
                        Is.EqualTo(StatusCodes.BadUnexpectedError));
                }
                else
                {
                    Assert.That(failures[0], Is.SameAs(failure));
                }
                await failing.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
                await healthy.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(result.Retired.IsFaulted, Is.True,
                        "A successful retry must not overwrite the original retirement failure.");
                    Assert.That(failing.AllEventsUnsubscribeCount, Is.EqualTo(2),
                        "Retry only the retained failed binding, not the whole live all-events set.");
                    Assert.That(healthy.AllEventsUnsubscribeCount, Is.EqualTo(1));
                    Assert.That(failing.DisposeCount, Is.EqualTo(1));
                    Assert.That(healthy.DisposeCount, Is.EqualTo(1));
                    Assert.That(candidate.ReadinessCompletedCount, Is.EqualTo(1));
                }
                await CommitChangeAsync(
                    LifecycleInspection.Capture(
                    m_server.NodeManagerLifecycle, retryRegistration), LifecycleChange.Remove(
                    retryRegistration.Slot), timeout.Token).ConfigureAwait(false);
            }
            finally
            {
                await exactWriter5Disposal.ConfigureAwait(false);
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
