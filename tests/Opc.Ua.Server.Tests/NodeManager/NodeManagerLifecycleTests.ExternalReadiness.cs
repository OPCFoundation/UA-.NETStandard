// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server.TestFramework;
using static Opc.Ua.Server.Tests.NodeManager.LifecycleInspection;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class NodeManagerLifecycleTests
    {
        /// <summary>
        /// An adopted item's first sample survives pending readiness, including failure and resend requests.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("NodeManagerReadiness")]
        public async Task AdoptedItemRetainsFirstSampleUntilReadinessCompletesAsync(bool readinessFails)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            LifecycleResult publicationResult1 = await CommitChangeAsync(
                lifecycle, LifecycleChange.Add(
                CreateTrackingNodeManagementFactory(kGeneration1Value, _ => { })), timeout.Token).ConfigureAwait(false);
            NodeManagerRegistration original = RequireRegistration(publicationResult1.Registrations[0]);
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            const uint ClientHandle = 51;
            (uint subscriptionId, uint itemId) = await CreateSubscriptionAndMonitoredItemAsync(
                services, ReadinessNodeId(kModelNamespaceUri, kValueNodeId), ClientHandle).ConfigureAwait(false);
            await PublishForDataChangeAsync(services, subscriptionId, default, ClientHandle).ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var expected = new SentinelException("The adopted generation failed readiness after queuing its sample.");
            MonitoredItem? adopted = null;
            long sourceRevision = 0;
            NodeManagerRegistration? completed = null;
            Task<LifecycleResult>? publication = null;
            try
            {
                publication = StartReadinessChangeAsync(
                    ReadinessChange.ShadowReload, original, synchronousFactory: false, kGeneration2Value,
                    manager => manager.ReadinessCallback = async ct =>
                    {
                        Assert.That(ct.CanBeCanceled, Is.False);
                        NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(manager);
                        adopted = (MonitoredItem)generation.MonitoredItems.Keys.Single(item => item.Id == itemId);
                        sourceRevision = ((IMonitoredItemGenerationBinding)adopted).SourceRevision;
                        var sample = new DataValue(Variant.From(4242), StatusCodes.Good);
                        adopted.QueueValue(sample, ServiceResult.Good, ignoreFilters: true);
                        adopted.SetupResendDataTrigger();
                        entered.TrySetResult(true);
                        await release.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                        if (readinessFails)
                        {
                            throw expected;
                        }
                    }, timeout.Token);
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                AssertLifecycleValue(adopted);
                Assert.That(adopted.NodeManager, Is.Not.SameAs(original.NodeManager));
                Assert.That(adopted.ToStorableMonitoredItem().LastValue.WrappedValue.TryGetValue(
                    out int retained), Is.True);
                Assert.That(retained, Is.EqualTo(4242));
                Assert.That(adopted.IsReadyToPublish, Is.False);
                Assert.That(adopted.IsResendData, Is.False);
                Assert.That(((IReadySignalingMonitoredItem)adopted).HasPendingNotifications, Is.False);
                using var context = new OperationContext(adopted);
                var notifications = new Queue<MonitoredItemNotification>();
                var diagnostics = new Queue<DiagnosticInfo>();
                var logger = m_server.CurrentInstance.Telemetry.CreateLogger<NodeManagerLifecycleTests>();
                Assert.That(adopted.Publish(context, notifications, diagnostics, 0, logger), Is.False);
                Assert.That(adopted.Publish(context, notifications, diagnostics, 10, logger), Is.False);
                Assert.That(notifications, Is.Empty);
                Assert.That(adopted.ToStorableMonitoredItem().LastValue.WrappedValue.TryGetValue(
                    out retained), Is.True);
                Assert.That(retained, Is.EqualTo(4242));

                release.TrySetResult(true);
                LifecycleResult published = await publication.WaitAsync(timeout.Token).ConfigureAwait(false);
                if (readinessFails)
                {
                    Assert.That(published.Issues.ToList().Single(
                        issue => issue.Stage == NodeManagerReconciliationStage.Readiness).InitialFailure,
                        Is.SameAs(expected));
                }
                else
                {
                    Assert.That(published.Issues, Is.Empty);
                }
                completed = RequireRegistration(published.Registrations[0]);
                Assert.That(adopted.NodeManager, Is.SameAs(completed.NodeManager));
                Assert.That(((IMonitoredItemGenerationBinding)adopted).SourceRevision, Is.EqualTo(sourceRevision));
                if (readinessFails)
                {
                    Assert.That(adopted.IsReadyToPublish, Is.False);
                    Assert.That(((IReadySignalingMonitoredItem)adopted).HasPendingNotifications, Is.False);
                    Assert.That(adopted.Publish(context, notifications, diagnostics, 10, logger), Is.False);
                    Assert.That(notifications, Is.Empty);
                    Assert.That(adopted.ToStorableMonitoredItem().LastValue.WrappedValue.TryGetValue(
                        out retained), Is.True);
                    Assert.That(retained, Is.EqualTo(4242));
                }
                else
                {
                    Assert.That(((IReadySignalingMonitoredItem)adopted).HasPendingNotifications, Is.True);
                    adopted.Publish(context, notifications, diagnostics, 10, logger);
                    Assert.That(notifications.Any(notification =>
                        notification.Value.StatusCode == StatusCodes.Good &&
                        notification.Value.WrappedValue.TryGetValue(out int value) && value == 4242), Is.True,
                        "Readiness must expose the retained first sample without requiring another producer emission.");
                }
            }
            finally
            {
                release.TrySetResult(true);
                try
                {
                    if (publication is not null && completed is null)
                    {
                        LifecycleResult published = await publication.WaitAsync(timeout.Token).ConfigureAwait(false);
                        completed = RequireRegistration(published.Registrations[0]);
                    }
                }
                finally
                {
                    await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
                    if (completed is not null)
                    {
                        await RemoveReadinessRegistrationAsync(completed).ConfigureAwait(false);
                    }
                }
            }
        }

        /// <summary>
        /// External reads cannot consume an unfinished generation, while readiness itself can read its publication.
        /// </summary>
        [TestCase(ReadinessChange.Add)]
        [TestCase(ReadinessChange.Reload)]
        [TestCase(ReadinessChange.ShadowReload)]
        [TestCase(ReadinessChange.ImmediateReload)]
        [Category("NodeManagerReadiness")]
        public async Task ExternalReadCannotObserveGoodValueBeforeReadinessCompletesAsync(ReadinessChange change)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            NodeManagerRegistration? original = null;
            if (change != ReadinessChange.Add)
            {
                LifecycleResult publicationResult2 = await CommitChangeAsync(
                    lifecycle, LifecycleChange.Add(
                    CreateTrackingNodeManagementFactory(
                    kGeneration1Value, _ => { })), timeout.Token).ConfigureAwait(false);
                original = RequireRegistration(publicationResult2.Registrations[0]);
            }

            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var externalStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ReadinessLifecycleNodeManager? participant = null;
            Task<StatusCode>? external = null;
            Task<LifecycleResult> publication = StartReadinessChangeAsync(
                change,
                original,
                synchronousFactory: false,
                kGeneration2Value,
                manager =>
                {
                    participant = manager;
                    manager.ExplicitReady = async (readiness, ct) =>
                    {
                        await AssertReadinessValueAsync(kModelNamespaceUri, kGeneration2Value, readiness)
                            .ConfigureAwait(false);
                        entered.TrySetResult(true);
                        await release.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                        await AssertReadinessValueAsync(kModelNamespaceUri, kGeneration2Value, readiness)
                            .ConfigureAwait(false);
                    };
                },
                timeout.Token);

            NodeManagerRegistration? completed = null;
            try
            {
                await entered.Task.WaitAsync(s_readinessTimeout).ConfigureAwait(false);
                AssertLifecycleValue(participant);
                Assert.That(participant.ReadinessCompletedCount, Is.Zero);
                Assert.That(publication.IsCompleted, Is.False);
                NodeId valueId = ReadinessNodeId(kModelNamespaceUri, kValueNodeId);
                external = RunWithoutExecutionContext(async () =>
                {
                    externalStarted.TrySetResult(true);
                    try
                    {
                        DataValue value = await ReadValueAsync(valueId).ConfigureAwait(false);
                        return value.StatusCode;
                    }
                    catch (ServiceResultException failure)
                    {
                        return failure.StatusCode;
                    }
                });
                await externalStarted.Task.WaitAsync(s_readinessTimeout).ConfigureAwait(false);

                // Both deferred admission and an explicit bad service/node status are valid during readiness.
                await Task.WhenAny(external, Task.Delay(TimeSpan.FromSeconds(1), timeout.Token))
                    .ConfigureAwait(false);
                if (external.IsCompleted)
                {
                    StatusCode status = await external.ConfigureAwait(false);
                    Assert.That(StatusCode.IsBad(status), Is.True,
                        "An external request must not receive Good data from an unfinished generation.");
                }
                Assert.That(participant.ReadinessCompletedCount, Is.Zero);
                Assert.That(publication.IsCompleted, Is.False);
            }
            finally
            {
                release.TrySetResult(true);
                try
                {
                    completed = RequireRegistration(
                        (await publication.WaitAsync(s_readinessTimeout).ConfigureAwait(false)).Registrations[0]);
                }
                finally
                {
                    if (external is not null)
                    {
                        await external.WaitAsync(s_readinessTimeout).ConfigureAwait(false);
                    }
                }
            }

            AssertLifecycleValue(completed);
            AssertLifecycleValue(participant);
            Assert.That(completed, Is.SameAs(FindReadinessRegistration()));
            Assert.That(participant.ReadinessCount, Is.EqualTo(1));
            Assert.That(participant.ReadinessCompletedCount, Is.EqualTo(1));
            await RunWithoutExecutionContext(
                () => AssertReadinessValueAsync(kModelNamespaceUri, kGeneration2Value)).ConfigureAwait(false);
            await RemoveReadinessRegistrationAsync(completed).ConfigureAwait(false);
            Assert.That(participant.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(participant.DisposeCount, Is.EqualTo(1));
        }
    }
}
