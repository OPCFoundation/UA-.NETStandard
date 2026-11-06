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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class NodeManagerLifecycleTests
    {
        /// <summary>
        /// External browse and monitoring requests wait or fail until the target generation finishes readiness.
        /// </summary>
        [TestCase(ReadinessChange.Add, false)]
        [TestCase(ReadinessChange.Reload, false)]
        [TestCase(ReadinessChange.ShadowReload, false)]
        [TestCase(ReadinessChange.ImmediateReload, false)]
        [TestCase(ReadinessChange.Add, true)]
        [TestCase(ReadinessChange.Reload, true)]
        [TestCase(ReadinessChange.ShadowReload, true)]
        [TestCase(ReadinessChange.ImmediateReload, true)]
        [Category("NodeManagerReadiness")]
        public async Task ExternalServicesRejectUnfinishedGenerationAsync(
            ReadinessChange change,
            bool createMonitoredItem)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            NodeManagerRegistration? original = null;
            if (change != ReadinessChange.Add)
            {
                original = await lifecycle.AddAsync(
                    CreateTrackingNodeManagementFactory(kGeneration1Value, _ => { }),
                    null,
                    timeout.Token).ConfigureAwait(false);
            }

            var services = new ServerTestServices(m_server, m_secureChannelContext);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var externalStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ReadinessLifecycleNodeManager? participant = null;
            NodeManagerRegistration? completed = null;
            Task<NodeManagerRegistration>? publication = null;
            Task<StatusCode>? external = null;
            uint subscriptionId = 0;
            try
            {
                if (createMonitoredItem)
                {
                    CreateSubscriptionResponse subscription = await services.CreateSubscriptionAsync(
                        CreateRequestHeader(), 100, 100, 10, 0, true, 0, timeout.Token).ConfigureAwait(false);
                    subscriptionId = subscription.SubscriptionId;
                    Assert.That(subscriptionId, Is.Not.Zero);
                }

                publication = StartReadinessChangeAsync(
                    change,
                    original,
                    synchronousFactory: false,
                    kGeneration2Value,
                    manager =>
                    {
                        participant = manager;
                        manager.ReadinessCallback = async ct =>
                        {
                            await AssertReadinessValueAsync(kModelNamespaceUri, kGeneration2Value)
                                .ConfigureAwait(false);
                            entered.TrySetResult(true);
                            await release.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                            await AssertReadinessValueAsync(kModelNamespaceUri, kGeneration2Value)
                                .ConfigureAwait(false);
                        };
                    },
                    timeout.Token);

                await entered.Task.WaitAsync(s_readinessTimeout).ConfigureAwait(false);
                AssertLifecycleValue(participant);
                Assert.That(participant.ReadinessCompletedCount, Is.Zero);
                external = RunWithoutExecutionContext(() =>
                {
                    externalStarted.TrySetResult(true);
                    return InvokeServiceAsync(assertReady: false);
                });
                await externalStarted.Task.WaitAsync(s_readinessTimeout).ConfigureAwait(false);
                await Task.WhenAny(external, Task.Delay(TimeSpan.FromSeconds(1), timeout.Token))
                    .ConfigureAwait(false);
                if (external.IsCompleted)
                {
                    StatusCode status = await external.ConfigureAwait(false);
                    Assert.That(StatusCode.IsBad(status), Is.True,
                        "A request must not browse or start monitoring an unfinished generation.");
                }
                Assert.That(publication.IsCompleted, Is.False);
                Assert.That(participant.ReadinessCompletedCount, Is.Zero);

                release.TrySetResult(true);
                completed = await publication.WaitAsync(s_readinessTimeout).ConfigureAwait(false);
                await external.WaitAsync(s_readinessTimeout).ConfigureAwait(false);
                Assert.That(completed, Is.SameAs(FindReadinessRegistration()));
                Assert.That(participant.ReadinessCount, Is.EqualTo(1));
                Assert.That(participant.ReadinessCompletedCount, Is.EqualTo(1));
                StatusCode ready = await RunWithoutExecutionContext(
                    () => InvokeServiceAsync(assertReady: true)).ConfigureAwait(false);
                Assert.That(ready, Is.EqualTo(StatusCodes.Good));
            }
            finally
            {
                release.TrySetResult(true);
                try
                {
                    if (publication is not null)
                    {
                        completed = await publication.WaitAsync(s_readinessTimeout).ConfigureAwait(false);
                    }
                }
                finally
                {
                    try
                    {
                        if (external is not null)
                        {
                            await external.WaitAsync(s_readinessTimeout).ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        try
                        {
                            if (subscriptionId != 0)
                            {
                                await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
                            }
                        }
                        finally
                        {
                            if (completed is not null)
                            {
                                await RemoveReadinessRegistrationAsync(completed).ConfigureAwait(false);
                            }
                        }
                    }
                }
            }

            AssertLifecycleValue(participant);
            Assert.That(participant.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(participant.DisposeCount, Is.EqualTo(1));

            RequestHeader CreateRequestHeader()
            {
                return new RequestHeader
                {
                    AuthenticationToken = m_requestHeader.AuthenticationToken,
                    Timestamp = DateTimeUtc.Now
                };
            }

            async Task<StatusCode> InvokeServiceAsync(bool assertReady)
            {
                try
                {
                    NodeId valueId = ReadinessNodeId(kModelNamespaceUri, kValueNodeId);
                    if (createMonitoredItem)
                    {
                        CreateMonitoredItemsResponse response = await services.CreateMonitoredItemsAsync(
                            CreateRequestHeader(), subscriptionId, TimestampsToReturn.Both,
                            [
                                new MonitoredItemCreateRequest
                                {
                                    ItemToMonitor = new ReadValueId
                                    {
                                        NodeId = valueId,
                                        AttributeId = Attributes.Value
                                    },
                                    MonitoringMode = MonitoringMode.Reporting,
                                    RequestedParameters = new MonitoringParameters
                                    {
                                        ClientHandle = 45122,
                                        SamplingInterval = 0,
                                        QueueSize = 1,
                                        DiscardOldest = true
                                    }
                                }
                            ], timeout.Token).ConfigureAwait(false);
                        Assert.That(response.Results.Count, Is.EqualTo(1));
                        if (assertReady)
                        {
                            Assert.That(response.Results[0].MonitoredItemId, Is.Not.Zero);
                        }
                        return response.Results[0].StatusCode;
                    }

                    BrowseResponse browse = await services.BrowseAsync(
                        CreateRequestHeader(), new ViewDescription(), 0,
                        [
                            new BrowseDescription
                            {
                                NodeId = ReadinessNodeId(kModelNamespaceUri, kRootNodeId),
                                BrowseDirection = BrowseDirection.Forward,
                                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                                IncludeSubtypes = true,
                                ResultMask = (uint)BrowseResultMask.All
                            }
                        ], timeout.Token).ConfigureAwait(false);
                    Assert.That(browse.Results.Count, Is.EqualTo(1));
                    BrowseResult result = browse.Results[0];
                    Assert.That(result.ContinuationPoint.Length, Is.Zero);
                    if (assertReady)
                    {
                        bool found = false;
                        for (int index = 0; index < result.References.Count; index++)
                        {
                            found |= result.References[index].NodeId == new ExpandedNodeId(valueId);
                        }
                        Assert.That(found, Is.True, "Ready browse must expose the generation's value node.");
                    }
                    return result.StatusCode;
                }
                catch (ServiceResultException failure)
                {
                    return failure.StatusCode;
                }
            }
        }

        private static void CollectMessages(PublishResponse response, List<string> messages)
        {
            foreach (ExtensionObject notification in response.NotificationMessage.NotificationData)
            {
                if (notification.TryGetValue(out EventNotificationList? occurrences))
                {
                    foreach (EventFieldList occurrence in occurrences.Events)
                    {
                        Assert.That(occurrence.EventFields[1].TryGetValue(out LocalizedText text), Is.True);
                        messages.Add(RequireLifecycleValue(text.Text));
                    }
                }
            }
        }

        /// <summary>
        /// Events from an unfinished generation are held back from all-events items and delivered once on readiness.
        /// </summary>
        [TestCase(ReadinessChange.Add)]
        [TestCase(ReadinessChange.Reload)]
        [Category("NodeManagerReadiness")]
        public async Task PendingSourceEventsDoNotReachAllEventsItemsAsync(ReadinessChange change)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            NodeManagerRegistration? original = null;
            if (change != ReadinessChange.Add)
            {
                original = await lifecycle.AddAsync(
                    CreateTrackingNodeManagementFactory(kGeneration1Value, _ => { }),
                    null,
                    timeout.Token).ConfigureAwait(false);
            }

            var services = new ServerTestServices(m_server, m_secureChannelContext);
            RequestHeader header = m_requestHeader;
            header.Timestamp = DateTimeUtc.Now;
            CreateSubscriptionResponse subscription = await services.CreateSubscriptionAsync(
                header, 100, 100, 10, 0, true, 0, timeout.Token).ConfigureAwait(false);
            uint subscriptionId = subscription.SubscriptionId;
            header.Timestamp = DateTimeUtc.Now;
            CreateMonitoredItemsResponse created = await services.CreateMonitoredItemsAsync(
                header, subscriptionId, TimestampsToReturn.Both,
                [CreateEmissionEventRequest(ObjectIds.Server, 7)], timeout.Token).ConfigureAwait(false);
            Assert.That(created.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));

            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ReadinessLifecycleNodeManager? participant = null;
            NodeManagerRegistration? completed = null;
            Task<NodeManagerRegistration>? publication = null;
            try
            {
                publication = StartReadinessChangeAsync(
                    change,
                    original,
                    synchronousFactory: false,
                    kGeneration2Value,
                    manager =>
                    {
                        participant = manager;
                        manager.ReadinessCallback = async ct =>
                        {
                            await manager.ReportRootEventAsync("pending-event", ct).ConfigureAwait(false);
                            entered.TrySetResult(true);
                            await release.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                        };
                    },
                    timeout.Token);
                await entered.Task.WaitAsync(s_readinessTimeout).ConfigureAwait(false);
                var messages = new List<string>();
                ArrayOf<SubscriptionAcknowledgement> acknowledgements = default;
                header.Timestamp = DateTimeUtc.Now;
                PublishResponse early = await services.PublishAsync(header, acknowledgements, timeout.Token)
                    .ConfigureAwait(false);
                acknowledgements = early.AvailableSequenceNumbers.ToArrayOf(sequence =>
                    new SubscriptionAcknowledgement { SubscriptionId = subscriptionId, SequenceNumber = sequence });
                CollectMessages(early, messages);
                Assert.That(messages, Does.Not.Contain("pending-event"),
                    "No event of an unfinished generation may be delivered before its readiness completes.");
                release.TrySetResult(true);
                completed = await publication.WaitAsync(s_readinessTimeout).ConfigureAwait(false);
                AssertLifecycleValue(participant);
                await participant.ReportRootEventAsync("ready-event", timeout.Token).ConfigureAwait(false);

                while (!messages.Contains("ready-event"))
                {
                    header.Timestamp = DateTimeUtc.Now;
                    PublishResponse response = await services.PublishAsync(header, acknowledgements, timeout.Token)
                        .ConfigureAwait(false);
                    acknowledgements = response.AvailableSequenceNumbers.ToArrayOf(sequence =>
                        new SubscriptionAcknowledgement { SubscriptionId = subscriptionId, SequenceNumber = sequence });
                    CollectMessages(response, messages);
                }
                Assert.That(messages.IndexOf("pending-event"), Is.EqualTo(messages.LastIndexOf("pending-event")),
                    "The event of an unfinished generation must be delivered at most once.");
                Assert.That(messages, Does.Contain("pending-event"),
                    "The event of an unfinished generation is retained and delivered exactly once on readiness.");
            }
            finally
            {
                release.TrySetResult(true);
                try
                {
                    if (publication is not null)
                    {
                        completed = await publication.WaitAsync(s_readinessTimeout).ConfigureAwait(false);
                    }
                }
                finally
                {
                    try
                    {
                        await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
                    }
                    finally
                    {
                        if (completed is not null)
                        {
                            await RemoveReadinessRegistrationAsync(completed).ConfigureAwait(false);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// External history updates must not reach an unfinished generation's provider.
        /// </summary>
        [TestCase(ReadinessChange.Add)]
        [TestCase(ReadinessChange.Reload)]
        [TestCase(ReadinessChange.ShadowReload)]
        [TestCase(ReadinessChange.ImmediateReload)]
        [Category("NodeManagerReadiness")]
        public async Task ExternalHistoryUpdateSkipsUnfinishedGenerationAsync(ReadinessChange change)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            NodeManagerRegistration? original = null;
            if (change != ReadinessChange.Add)
            {
                original = await lifecycle.AddAsync(
                    CreateTrackingNodeManagementFactory(kGeneration1Value, _ => { }),
                    null,
                    timeout.Token).ConfigureAwait(false);
            }

            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ReadinessLifecycleNodeManager? participant = null;
            NodeManagerRegistration? completed = null;
            Task<NodeManagerRegistration>? publication = null;
            try
            {
                publication = StartReadinessChangeAsync(
                    change,
                    original,
                    synchronousFactory: false,
                    kGeneration2Value,
                    manager =>
                    {
                        participant = manager;
                        manager.ReadinessCallback = async ct =>
                        {
                            entered.TrySetResult(true);
                            await release.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                        };
                    },
                    timeout.Token);
                await entered.Task.WaitAsync(s_readinessTimeout).ConfigureAwait(false);
                AssertLifecycleValue(participant);

                await RunWithoutExecutionContext(() => InvokeHistoryUpdateAsync()).ConfigureAwait(false);
                Assert.That(participant.HistoryUpdateCount, Is.Zero,
                    "An external history update must not reach a generation that is not ready.");

                release.TrySetResult(true);
                completed = await publication.WaitAsync(s_readinessTimeout).ConfigureAwait(false);
                await RunWithoutExecutionContext(() => InvokeHistoryUpdateAsync()).ConfigureAwait(false);
                Assert.That(participant.HistoryUpdateCount, Is.EqualTo(1),
                    "A request after readiness must reach the ready generation.");
            }
            finally
            {
                release.TrySetResult(true);
                try
                {
                    if (publication is not null)
                    {
                        completed = await publication.WaitAsync(s_readinessTimeout).ConfigureAwait(false);
                    }
                }
                finally
                {
                    if (completed is not null)
                    {
                        await RemoveReadinessRegistrationAsync(completed).ConfigureAwait(false);
                    }
                }
            }

            async Task InvokeHistoryUpdateAsync()
            {
                var details = new UpdateDataDetails
                {
                    NodeId = ReadinessNodeId(kModelNamespaceUri, kValueNodeId),
                    PerformInsertReplace = PerformUpdateType.Insert,
                    UpdateValues = [new DataValue(new Variant(1), StatusCodes.Good, DateTimeUtc.Now)]
                };
                try
                {
                    using var lifetime = new RequestLifetime(timeout.Token);
                    await m_server.HistoryUpdateAsync(
                        m_secureChannelContext,
                        new RequestHeader
                        {
                            AuthenticationToken = m_requestHeader.AuthenticationToken,
                            Timestamp = DateTimeUtc.Now
                        },
                        [new ExtensionObject(details)],
                        lifetime).ConfigureAwait(false);
                }
                catch (ServiceResultException)
                {
                }
            }
        }
    }
}
