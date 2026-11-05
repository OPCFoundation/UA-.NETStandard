/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Configuration;
using Opc.Ua.PackML.Client;
using Opc.Ua.PackML.Server;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Unit = Opc.Ua.PackML.Server.PackMLUnit;

namespace Opc.Ua.PackML.Tests
{
    [TestFixture]
    [Category("PackML")]
    [Category("Integration")]
    [NonParallelizable]
    public sealed class PackMLClientServerTests
    {
        private ServiceProvider m_services = null!;
        private IHostedService m_host = null!;
        private ClientFixture m_clientFixture = null!;
        private ManagedSession m_session = null!;
        private PackMLClient m_client = null!;
        private PackMLNodeManager m_manager = null!;
        private Unit m_unit = null!;
        private EquipmentProvider m_equipment = null!;
        private string m_endpoint = string.Empty;

        [SetUp]
        public async Task StartAsync()
        {
            m_equipment = new EquipmentProvider();
            var ready = PackMLTestSupport.Signal();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpcUa().AddServer<StandardServer>(ConfigureServer).AddPackML(options =>
            {
                options.NamespaceUri = "urn:packml:session:units";
                options.ConfigureAsync = async (manager, ct) =>
                {
                    m_manager = manager;
                    m_unit = await manager.Runtime.AddUnitAsync(
                        new QualifiedName("Filler", manager.InstanceNamespaceIndex), m_equipment,
                        unit => unit.Modes = [new PackMLMode(), new PackMLMode { Value = 7, Name = "Clean" }],
                        cancellationToken: ct);
                    ready.TrySetResult(true);
                };
            });
            m_services = services.BuildServiceProvider();
            m_host = m_services.GetServices<IHostedService>().Single();
            await m_host.StartAsync(CancellationToken.None);
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(60));

            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            m_clientFixture = new ClientFixture(telemetry);
            await m_clientFixture.LoadClientConfigurationAsync(Path.Combine(
                TestContext.CurrentContext.WorkDirectory, "packml-client", Guid.NewGuid().ToString("N")));
            EndpointDescription? endpoint = await CoreClientUtils.SelectEndpointAsync(
                m_clientFixture.Config, m_endpoint, false, 15000, telemetry);
            Assert.That(endpoint, Is.Not.Null);
            m_session = await new ManagedSessionBuilder(m_clientFixture.Config, telemetry)
                .UseEndpoint(new ConfiguredEndpoint(
                    null, endpoint!, EndpointConfiguration.Create(m_clientFixture.Config)))
                .WithSessionName(nameof(PackMLClientServerTests))
                .ConnectAsync();
            m_client = m_session.PackML(telemetry);
        }

        [TearDown]
        public async Task StopAsync()
        {
            try
            {
                if (m_session != null)
                {
                    await m_session.DisposeAsync();
                }
            }
            finally
            {
                try
                {
                    m_clientFixture?.Dispose();
                }
                finally
                {
                    try
                    {
                        if (m_host != null)
                        {
                            await m_host.StopAsync(CancellationToken.None);
                        }
                    }
                    finally
                    {
                        if (m_services != null)
                        {
                            await m_services.DisposeAsync();
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Verifies PackML's directly required size-five queue retains and publishes samples in order.
        /// </summary>
        [TestCase(1)]
        [TestCase(34)]
        public async Task PackMLMonitoredItemSupportsQueueSizeOfAtLeastFive(int queuedItemCount)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            CancellationToken ct = deadline.Token;
            var speed = m_unit.Node.Status.Required().CurMachSpeed.Required();
            bool verifyCapacity = queuedItemCount == 34;
            int itemCount = verifyCapacity ? 102 : 2;
            uint witnessHandle = (uint)queuedItemCount + 1;
            ArrayOf<SubscriptionAcknowledgement> acknowledgements = default;
            CreateSubscriptionResponse subscription = await m_session.CreateSubscriptionAsync(
                null, 100, 100, 10, 0, true, 0, ct).ConfigureAwait(false);
            try
            {
                Assert.That(StatusCode.IsGood(subscription.ResponseHeader.ServiceResult), Is.True);
                Assert.That(subscription.SubscriptionId, Is.GreaterThan(0u));
                if (verifyCapacity)
                {
                    ReadResponse limits = await m_session.ReadAsync(
                        null, 0, TimestampsToReturn.Neither,
                        [
                            new ReadValueId
                            {
                                NodeId = VariableIds.Server_ServerCapabilities_MaxMonitoredItems,
                                AttributeId = Attributes.Value
                            },
                            new ReadValueId
                            {
                                NodeId = VariableIds.Server_ServerCapabilities_MaxMonitoredItemsPerSubscription,
                                AttributeId = Attributes.Value
                            }
                        ], ct).ConfigureAwait(false);
                    Assert.That(StatusCode.IsGood(limits.ResponseHeader.ServiceResult), Is.True);
                    Assert.That(limits.Results.Count, Is.EqualTo(2));
                    foreach (DataValue limit in limits.Results)
                    {
                        Assert.That(StatusCode.IsGood(limit.StatusCode), Is.True);
                        Assert.That(limit.WrappedValue.TryGetValue(out uint supported), Is.True);
                        Assert.That(supported, Is.EqualTo(102u));
                    }
                    Assert.That(queuedItemCount, Is.EqualTo((itemCount + 2) / 3));
                }
                MonitoredItemCreateRequest[] requests = Enumerable.Range(1, itemCount)
                    .Select(handle => CreateRequest((uint)handle, handle <= queuedItemCount ? 5u : 1u))
                    .ToArray();
                CreateMonitoredItemsResponse response = await m_session.CreateMonitoredItemsAsync(
                    null,
                    subscription.SubscriptionId,
                    TimestampsToReturn.Both,
                    requests.ToArrayOf(),
                    ct).ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(response.ResponseHeader.ServiceResult), Is.True);
                Assert.That(response.Results.Count, Is.EqualTo(itemCount));
                Assert.That(response.Results.ToList().Select(result => result.MonitoredItemId).Distinct().Count(),
                    Is.EqualTo(itemCount));
                foreach (MonitoredItemCreateResult result in response.Results)
                {
                    Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
                    Assert.That(result.MonitoredItemId, Is.GreaterThan(0u));
                    Assert.That(result.RevisedSamplingInterval, Is.Zero);
                }
                for (int index = 0; index < queuedItemCount; index++)
                {
                    Assert.That(response.Results[index].RevisedQueueSize, Is.GreaterThanOrEqualTo(5u));
                }
                if (verifyCapacity)
                {
                    CreateMonitoredItemsResponse excess = await m_session.CreateMonitoredItemsAsync(
                        null, subscription.SubscriptionId, TimestampsToReturn.Both,
                        [CreateRequest(103, 5)], ct).ConfigureAwait(false);
                    Assert.That(StatusCode.IsGood(excess.ResponseHeader.ServiceResult), Is.True);
                    Assert.That(excess.Results.Count, Is.EqualTo(1));
                    Assert.That(excess.Results[0].StatusCode,
                        Is.EqualTo((StatusCode)StatusCodes.BadTooManyMonitoredItems));
                }

                await MonitoredNodeSamplingFence.WaitAsync(speed, ct).ConfigureAwait(false);
                ArrayOf<MonitoredItemNotification> initial = await PublishChangesAsync(itemCount)
                    .ConfigureAwait(false);
                Assert.That(initial.ToList().Select(item => item.ClientHandle).Distinct(),
                    Is.EquivalentTo(Enumerable.Range(1, itemCount).Select(handle => (uint)handle)));

                uint[] queuedIds = response.Results.ToList().Take(queuedItemCount)
                    .Select(result => result.MonitoredItemId).ToArray();
                if (itemCount > queuedItemCount + 1)
                {
                    SetMonitoringModeResponse fillers = await m_session.SetMonitoringModeAsync(
                        null, subscription.SubscriptionId, MonitoringMode.Sampling,
                        response.Results.ToList().Skip(queuedItemCount + 1)
                            .Select(result => result.MonitoredItemId).ToArray().ToArrayOf(), ct).ConfigureAwait(false);
                    Assert.That(fillers.Results.Count, Is.EqualTo(itemCount - queuedItemCount - 1));
                    Assert.That(fillers.Results.ToList().All(StatusCode.IsGood), Is.True);
                }

                SetMonitoringModeResponse sampling = await m_session.SetMonitoringModeAsync(
                    null, subscription.SubscriptionId, MonitoringMode.Sampling,
                    queuedIds.ToArrayOf(), ct).ConfigureAwait(false);
                Assert.That(sampling.Results.Count, Is.EqualTo(queuedItemCount));
                Assert.That(sampling.Results.ToList().All(StatusCode.IsGood), Is.True);

                // Fence the shared consumer before publishing the witness or changing the source again.
                float[] expected = [1, 2, 3, 4, 5];
                foreach (float value in expected)
                {
                    m_equipment.Snapshot = m_equipment.Snapshot with { CurrentMachineSpeed = value };
                    await m_unit.RefreshAsync(ct).ConfigureAwait(false);
                    await MonitoredNodeSamplingFence.WaitAsync(speed, ct).ConfigureAwait(false);
                    ArrayOf<MonitoredItemNotification> witness = await PublishChangesAsync(1)
                        .ConfigureAwait(false);
                    foreach (MonitoredItemNotification sample in witness)
                    {
                        Assert.That(sample.ClientHandle, Is.EqualTo(witnessHandle));
                        Assert.That(StatusCode.IsGood(sample.Value.StatusCode), Is.True);
                        Assert.That(sample.Value.WrappedValue.TryGetValue(out float sampled), Is.True);
                        Assert.That(sampled, Is.EqualTo(value));
                    }
                }

                SetMonitoringModeResponse disabled = await m_session.SetMonitoringModeAsync(
                    null, subscription.SubscriptionId, MonitoringMode.Disabled,
                    [response.Results[queuedItemCount].MonitoredItemId], ct).ConfigureAwait(false);
                Assert.That(disabled.Results.Count, Is.EqualTo(1));
                Assert.That(StatusCode.IsGood(disabled.Results[0]), Is.True);
                SetMonitoringModeResponse reporting = await m_session.SetMonitoringModeAsync(
                    null, subscription.SubscriptionId, MonitoringMode.Reporting,
                    queuedIds.ToArrayOf(), ct).ConfigureAwait(false);
                Assert.That(reporting.Results.Count, Is.EqualTo(queuedItemCount));
                Assert.That(reporting.Results.ToList().All(StatusCode.IsGood), Is.True);

                ArrayOf<MonitoredItemNotification> queued = await PublishChangesAsync(5 * queuedItemCount)
                    .ConfigureAwait(false);
                for (uint handle = 1; handle <= queuedItemCount; handle++)
                {
                    MonitoredItemNotification[] retained = queued.ToList()
                        .Where(item => item.ClientHandle == handle).ToArray();
                    Assert.That(retained.Length, Is.EqualTo(5));
                    for (int index = 0; index < expected.Length; index++)
                    {
                        Assert.That(StatusCode.IsGood(retained[index].Value.StatusCode), Is.True);
                        Assert.That(retained[index].Value.WrappedValue.TryGetValue(out float sampled), Is.True);
                        Assert.That(sampled, Is.EqualTo(expected[index]));
                    }
                }
            }
            finally
            {
                if (subscription.SubscriptionId != 0)
                {
                    DeleteSubscriptionsResponse deleted = await m_session.DeleteSubscriptionsAsync(
                        null, [subscription.SubscriptionId], CancellationToken.None).ConfigureAwait(false);
                    Assert.That(deleted.Results.Count, Is.EqualTo(1));
                    Assert.That(StatusCode.IsGood(deleted.Results[0]), Is.True);
                }
            }

            MonitoredItemCreateRequest CreateRequest(uint clientHandle, uint queueSize)
            {
                return new MonitoredItemCreateRequest
                {
                    ItemToMonitor = new ReadValueId { NodeId = speed.NodeId, AttributeId = Attributes.Value },
                    MonitoringMode = MonitoringMode.Reporting,
                    RequestedParameters = new MonitoringParameters
                    {
                        ClientHandle = clientHandle,
                        SamplingInterval = 0,
                        QueueSize = queueSize,
                        DiscardOldest = true
                    }
                };
            }

            async Task<ArrayOf<MonitoredItemNotification>> PublishChangesAsync(int count)
            {
                var changes = new List<MonitoredItemNotification>();
                while (changes.Count < count)
                {
                    PublishResponse published = await m_session.PublishAsync(null, acknowledgements, ct)
                        .ConfigureAwait(false);
                    Assert.That(StatusCode.IsGood(published.ResponseHeader.ServiceResult), Is.True);
                    Assert.That(published.SubscriptionId, Is.EqualTo(subscription.SubscriptionId));
                    if (published.NotificationMessage.NotificationData.Count == 0)
                    {
                        continue;
                    }
                    acknowledgements =
                    [
                        new SubscriptionAcknowledgement
                        {
                            SubscriptionId = subscription.SubscriptionId,
                            SequenceNumber = published.NotificationMessage.SequenceNumber
                        }
                    ];
                    foreach (ExtensionObject extension in published.NotificationMessage.NotificationData)
                    {
                        Assert.That(extension.TryGetValue(out DataChangeNotification? notification), Is.True);
                        Assert.That(notification, Is.Not.Null);
                        changes.AddRange(notification!.MonitoredItems.ToList());
                    }
                }
                Assert.That(changes.Count, Is.EqualTo(count));
                return changes.ToArray().ToArrayOf();
            }
        }

        [Test]
        public async Task EveryWireCommandReportsActingStateUntilMatchingEquipmentCompletion()
        {
            (PackMLStateCommand Command, uint Acting, uint Stable)[] sequence =
            [
                (PackMLStateCommand.Reset, PackMLStateNumbers.Resetting, PackMLStateNumbers.Idle),
                (PackMLStateCommand.Start, PackMLStateNumbers.Starting, PackMLStateNumbers.Execute),
                (PackMLStateCommand.Hold, PackMLStateNumbers.Holding, PackMLStateNumbers.Held),
                (PackMLStateCommand.Unhold, PackMLStateNumbers.Unholding, PackMLStateNumbers.Execute),
                (PackMLStateCommand.Suspend, PackMLStateNumbers.Suspending, PackMLStateNumbers.Suspended),
                (PackMLStateCommand.Unsuspend, PackMLStateNumbers.Unsuspending, PackMLStateNumbers.Execute),
                (PackMLStateCommand.ToComplete, PackMLStateNumbers.Completing, PackMLStateNumbers.Complete),
                (PackMLStateCommand.ResetExecute, PackMLStateNumbers.Resetting, PackMLStateNumbers.Idle),
                (PackMLStateCommand.Stop, PackMLStateNumbers.Stopping, PackMLStateNumbers.Stopped),
                (PackMLStateCommand.Abort, PackMLStateNumbers.Aborting, PackMLStateNumbers.Aborted),
                (PackMLStateCommand.Clear, PackMLStateNumbers.Clearing, PackMLStateNumbers.Stopped)
            ];
            foreach (var step in sequence)
            {
                await m_client.CommandAsync(m_unit.Node.NodeId, step.Command);
                PackMLStatusSnapshot accepted = await m_client.ReadStatusAsync(m_unit.Node.NodeId);
                Assert.That(accepted.StateChangeInProcess.Value, Is.True, step.Command.ToString());
                Assert.That(m_unit.CurrentState, Is.EqualTo(step.Acting));
                long id = m_unit.PendingRequestId;
                Assert.That(id, Is.GreaterThan(0));
                Assert.That(m_equipment.Executed[^1].RequestId, Is.EqualTo(id));
                Assert.That((await m_unit.CompleteActingStateAsync(id + 1, step.Acting)).StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
                Assert.That((await m_unit.CompleteActingStateAsync(id, step.Stable)).StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
                Assert.That((await m_client.ReadStatusAsync(m_unit.Node.NodeId)).StateChangeInProcess.Value, Is.True);
                Assert.That(ServiceResult.IsGood(await m_unit.CompleteActingStateAsync(id, step.Acting)), Is.True);
                PackMLStatusSnapshot complete = await m_client.ReadStatusAsync(m_unit.Node.NodeId);
                Assert.That(complete.StateChangeInProcess.Value, Is.False);
                Assert.That(m_unit.CurrentState, Is.EqualTo(step.Stable));
                PackMLValue<NodeId> active = step.Stable == PackMLStateNumbers.Aborted
                    ? complete.BaseState
                    : step.Stable == PackMLStateNumbers.Stopped ? complete.MachineState : complete.ExecuteState;
                Assert.That(active.IsGood, Is.True);
                Assert.That(active.Value.TryGetValue(out uint stateId), Is.True);
                Assert.That(PackMLStateNumbers.FromStateId(stateId), Is.EqualTo(step.Stable));
            }
            Assert.That(m_equipment.Executed, Has.Count.EqualTo(sequence.Length));
        }

        [Test]
        public async Task WireModeRequestIsBooleanAndCurrentModeWaitsForEquipment()
        {
            await m_client.SetUnitModeAsync(m_unit.Node.NodeId, 7);
            PackMLStatusSnapshot pending = await m_client.ReadStatusAsync(m_unit.Node.NodeId);
            Assert.That(pending.UnitModeRequested.Value, Is.True);
            Assert.That(pending.UnitModeCurrent.Value, Is.EqualTo(1));
            Assert.That(pending.UnitModeChangeInProcess.Value, Is.True);
            Assert.That(pending.UnitSupportedModes.Value, Is.EqualTo(m_unit.ModeDataType.NodeId));
            m_equipment.Snapshot = m_equipment.Snapshot with { CurrentMode = 7 };
            await m_unit.RefreshAsync();
            PackMLStatusSnapshot completed = await m_client.ReadStatusAsync(m_unit.Node.NodeId);
            Assert.That(completed.UnitModeRequested.Value, Is.False);
            Assert.That(completed.UnitModeCurrent.Value, Is.EqualTo(7));
            Assert.That(completed.UnitModeChangeInProcess.Value, Is.False);
            Assert.That(m_equipment.Executed.Single().Mode, Is.EqualTo(7));
        }

        [TestCase(true, false)]
        [TestCase(false, true)]
        public async Task WireReadPreservesPerValueQualityAndSeparatesSetpointFromMeasurement(
            bool blocked,
            bool starved)
        {
            await m_client.SetMachSpeedAsync(m_unit.Node.NodeId, 75f);
            m_equipment.Snapshot = m_equipment.Snapshot with
            {
                CurrentMachineSpeed = 19,
                EquipmentBlocked = blocked,
                EquipmentStarved = starved,
                StatusCode = StatusCodes.UncertainDataSubNormal,
                SourceTimestamp = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc)
            };
            await m_unit.RefreshAsync();
            PackMLStatusSnapshot snapshot = await m_client.ReadStatusAsync(m_unit.Node.NodeId);
            Assert.That(snapshot.MachSpeed.Value, Is.EqualTo(75f));
            Assert.That(snapshot.MachSpeed.IsGood, Is.True);
            Assert.That(snapshot.CurMachSpeed.Value, Is.EqualTo(19f));
            Assert.That(snapshot.CurMachSpeed.IsGood, Is.False);
            Assert.That(snapshot.CurMachSpeed.DataValue.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.UncertainDataSubNormal));
            Assert.That(snapshot.CurMachSpeed.DataValue.SourceTimestamp,
                Is.EqualTo(m_equipment.Snapshot.SourceTimestamp));
            Assert.That(snapshot.EquipmentBlocked.Value, Is.EqualTo(blocked));
            Assert.That(snapshot.EquipmentStarved.Value, Is.EqualTo(starved));
            Assert.That(snapshot.EquipmentBlocked.DataValue.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.UncertainDataSubNormal));
            Assert.That(snapshot.EquipmentStarved.DataValue.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.UncertainDataSubNormal));
            Assert.That(snapshot.EquipmentBlocked.DataValue.SourceTimestamp,
                Is.EqualTo(m_equipment.Snapshot.SourceTimestamp));
            Assert.That(snapshot.EquipmentStarved.DataValue.SourceTimestamp,
                Is.EqualTo(m_equipment.Snapshot.SourceTimestamp));
            Assert.That(snapshot.MaterialInterlock.IsPresent, Is.False);
            Assert.That(snapshot.MaterialInterlock.HasValue, Is.False);
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task WireDenialOrRejectionDoesNotPublishSetpoint(bool deny)
        {
            await m_client.SetMachSpeedAsync(m_unit.Node.NodeId, 10);
            m_equipment.Executed.Clear();
            if (deny)
            {
                m_equipment.Authorization = new ServiceResult(StatusCodes.BadUserAccessDenied);
            }
            else
            {
                m_equipment.Acceptance = new ServiceResult(StatusCodes.BadOutOfService);
            }
            ServiceResultException error = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await m_client.SetMachSpeedAsync(m_unit.Node.NodeId, 99)))!;
            Assert.That(error.StatusCode,
                Is.EqualTo((StatusCode)(deny ? StatusCodes.BadUserAccessDenied : StatusCodes.BadOutOfService)));
            Assert.That(m_equipment.Executed, Has.Count.EqualTo(deny ? 0 : 1));
            Assert.That((await m_client.ReadStatusAsync(m_unit.Node.NodeId)).MachSpeed.Value, Is.EqualTo(10f));
        }

        [Test]
        public async Task MissingOptionalMethodsReturnNotSupportedWithoutProviderExecution()
        {
            ServiceResultException remote = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await m_client.RemoteCommandAsync(m_unit.Node.NodeId, [])))!;
            ServiceResultException interlock = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await m_client.SetInterlockAsync(m_unit.Node.NodeId, 0, true)))!;
            Assert.That(remote.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNotSupported));
            Assert.That(interlock.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNotSupported));
            Assert.That(m_equipment.Executed, Is.Empty);
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task UncertainProviderDecisionDoesNotAppearAsSuccessfulAcceptance(bool authorization)
        {
            await m_client.SetMachSpeedAsync(m_unit.Node.NodeId, 10);
            m_equipment.Executed.Clear();
            if (authorization)
            {
                m_equipment.Authorization = new ServiceResult(StatusCodes.Uncertain);
            }
            else
            {
                m_equipment.Acceptance = new ServiceResult(StatusCodes.Uncertain);
            }

            ServiceResultException error = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await m_client.SetMachSpeedAsync(m_unit.Node.NodeId, 99)))!;

            Assert.That(StatusCode.IsBad(error.StatusCode), Is.True);
            Assert.That(m_equipment.Executed, Has.Count.EqualTo(authorization ? 0 : 1));
            Assert.That((await m_client.ReadStatusAsync(m_unit.Node.NodeId)).MachSpeed.Value, Is.EqualTo(10f));
        }

        [Test]
        public async Task ResetOnWrongNestedMachineIsRejectedBeforeProviderExecution()
        {
            // The runtime keeps Executable false on methods of inactive or
            // non-permitting machines, so the Call service rejects them with
            // BadNotExecutable (OPC 10000-4 5.11.2) before the handler runs.
            ServiceResultException inactive = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await m_client.CommandAsync(m_unit.Node.NodeId, PackMLStateCommand.ResetExecute)))!;
            Assert.That(inactive.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNotExecutable));
            Assert.That(m_equipment.Executed, Is.Empty);
            await m_client.CommandAsync(m_unit.Node.NodeId, PackMLStateCommand.Reset);
            await m_unit.CompleteActingStateAsync(m_unit.PendingRequestId, PackMLStateNumbers.Resetting);
            await m_client.CommandAsync(m_unit.Node.NodeId, PackMLStateCommand.Start);
            await m_unit.CompleteActingStateAsync(m_unit.PendingRequestId, PackMLStateNumbers.Starting);
            await m_client.CommandAsync(m_unit.Node.NodeId, PackMLStateCommand.ToComplete);
            await m_unit.CompleteActingStateAsync(m_unit.PendingRequestId, PackMLStateNumbers.Completing);
            int calls = m_equipment.Executed.Count;
            ServiceResultException wrong = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await m_client.CommandAsync(m_unit.Node.NodeId, PackMLStateCommand.Reset)))!;
            Assert.That(wrong.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNotExecutable));
            Assert.That(m_equipment.Executed, Has.Count.EqualTo(calls));
            Assert.That(m_unit.CurrentState, Is.EqualTo(PackMLStateNumbers.Complete));
        }

        [Test]
        public async Task StatusIsReadOnlyOverActualWriteService()
        {
            PackMLStatusSnapshot status = await m_client.ReadStatusAsync(m_unit.Node.NodeId);
            WriteResponse response = await m_session.WriteAsync(null,
            [
                new WriteValue
                {
                    NodeId = status.CurMachSpeed.NodeId, AttributeId = Attributes.Value,
                    Value = new DataValue(Variant.From(999f))
                }
            ], CancellationToken.None);
            Assert.That(response.Results[0], Is.EqualTo((StatusCode)StatusCodes.BadNotWritable));
            Assert.That((await m_client.ReadStatusAsync(m_unit.Node.NodeId)).CurMachSpeed.Value, Is.EqualTo(12.5f));
            Assert.That(m_equipment.Executed, Is.Empty);
        }

        [Test]
        public async Task FluentClientFactoryBorrowsTheExistingSessionAcrossRepeatedCreation()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddClient(_ => { }).AddPackMLClient();
            int borrowed = 0;
            services.AddSingleton<Func<CancellationToken, Task<ManagedSession>>>(_ => ct =>
            {
                ct.ThrowIfCancellationRequested();
                borrowed++;
                return Task.FromResult(m_session);
            });
            await using (ServiceProvider container = services.BuildServiceProvider())
            {
                Func<CancellationToken, Task<PackMLClient>> create =
                    container.GetRequiredService<Func<CancellationToken, Task<PackMLClient>>>();
                PackMLClient first = await create(CancellationToken.None);
                PackMLClient second = await create(CancellationToken.None);
                Assert.That(first.Session, Is.SameAs(m_session));
                Assert.That(second.Session, Is.SameAs(m_session));
                Assert.That(first, Is.Not.SameAs(second));
                Assert.That(borrowed, Is.EqualTo(2));
            }
            Assert.That((await m_client.ReadStatusAsync(m_unit.Node.NodeId)).CurMachSpeed.Value,
                Is.EqualTo(12.5f), "disposing the factory container does not dispose its borrowed session");
        }

        [Test]
        public async Task DiscoverUsesQualifiedInstanceNamesAndPackMLType()
        {
            ArrayOf<Client.PackMLUnit> units = await m_client.DiscoverAsync();
            Client.PackMLUnit discovered = units.Span.ToArray().Single(u => u.NodeId == m_unit.Node.NodeId);
            Assert.That(discovered.BrowseName.Name, Is.EqualTo("Filler"));
            Assert.That(discovered.BrowseName.NamespaceIndex, Is.EqualTo(m_manager.InstanceNamespaceIndex));
            Assert.That(discovered.TypeDefinition, Is.EqualTo(ExpandedNodeId.ToNodeId(
                ObjectTypeIds.PackMLBaseObjectType, m_session.NamespaceUris)));
        }

        [Test]
        public async Task WireStructuredOperationsDecodeConcreteProductsAndParameters()
        {
            await m_client.SetProductAsync(m_unit.Node.NodeId, [new PackMLProductDataType { ProductID = 42 }]);
            await m_client.SetParameterAsync(m_unit.Node.NodeId,
                [new PackMLDescriptorDataType { ID = 3, Name = "Fill", Value = 250f }]);
            PackMLStatusSnapshot snapshot = await m_client.ReadStatusAsync(m_unit.Node.NodeId);
            Assert.That(snapshot.Product.Value[0].ProductID, Is.EqualTo(42));
            Assert.That(snapshot.Parameter.Value[0].ID, Is.EqualTo(3));
            Assert.That(snapshot.Parameter.Value[0].Name, Is.EqualTo("Fill"));
            Assert.That(snapshot.Parameter.Value[0].Value, Is.EqualTo(250f));
            Assert.That(m_equipment.Executed.Select(r => r.Operation),
                Is.EqualTo(new[] { PackMLOperation.SetProduct, PackMLOperation.SetParameter }));
        }

        [Test]
        public async Task StreamingObservationPreservesAcquisitionMetadataAndDoesNotOwnSession()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            Task<PackMLValue<float>> observed = m_client.WaitForAsync(
                m_unit.Node.NodeId, PackMLFields.Status.CurMachSpeed, value => value == 44,
                TimeSpan.FromSeconds(15), cancellationToken: timeout.Token).AsTask();
            m_equipment.Snapshot = m_equipment.Snapshot with { CurrentMachineSpeed = 44 };
            await m_unit.RefreshAsync(timeout.Token);
            PackMLValue<float> value = await observed;
            Assert.That(value.IsGood, Is.True);
            Assert.That(value.Value, Is.EqualTo(44));
            Assert.That(value.DataValue.SourceTimestamp, Is.EqualTo(m_equipment.Snapshot.SourceTimestamp));
            Assert.That((await m_client.ReadStatusAsync(m_unit.Node.NodeId)).CurMachSpeed.Value, Is.EqualTo(44));
            Assert.That(m_equipment.Executed, Is.Empty);
        }

        [Test]
        public async Task ObservationTimeoutDoesNotSendEquipmentCommands()
        {
            await Assert.ThatAsync(async () => await m_client.WaitForAsync(
                m_unit.Node.NodeId, PackMLFields.Status.StateChangeInProcess, value => value,
                TimeSpan.FromMilliseconds(100)), Throws.TypeOf<TimeoutException>());
            Assert.That(m_equipment.Executed, Is.Empty);
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task OptionalBasePropertiesRetainTheirPublishedTypesAndPresence(bool enabled)
        {
            Unit added = await m_manager.Runtime.AddUnitAsync(
                new QualifiedName("Properties", m_manager.InstanceNamespaceIndex), new EquipmentProvider(),
                options =>
                {
                    options.TagId = enabled ? "Line 4 filler" : null;
                    options.IncludeVersion = enabled;
                });
            PackMLValue<string> tag = await m_client.ReadAsync(added.Node.NodeId, PackMLFields.TagID);
            PackMLValue<string> version = await m_client.ReadAsync(added.Node.NodeId, PackMLFields.PackMLVersion);
            Assert.That(tag.IsPresent, Is.EqualTo(enabled));
            Assert.That(version.IsPresent, Is.EqualTo(enabled));
            Assert.That(tag.HasValue, Is.EqualTo(enabled));
            Assert.That(version.HasValue, Is.EqualTo(enabled));
            if (enabled)
            {
                Assert.That(tag.Value, Is.EqualTo("Line 4 filler"));
                Assert.That(version.Value, Is.EqualTo("TR88.00.02-2015"));
            }
        }

        [Test]
        public async Task DiscoveryAcceptsVendorSubtypesWithoutDependingOnInstanceNames()
        {
            var vendorType = new BaseObjectTypeState
            {
                NodeId = new NodeId("VendorFillerType", m_manager.InstanceNamespaceIndex),
                BrowseName = new QualifiedName("VendorFillerType", m_manager.InstanceNamespaceIndex),
                DisplayName = new LocalizedText("Vendor filler type"),
                SuperTypeId = m_client.BaseObjectTypeId
            };
            vendorType.AddReference(Ua.ReferenceTypeIds.HasSubtype, true, m_client.BaseObjectTypeId);
            await m_manager.AddPredefinedNodeAsync(vendorType);
            m_unit.Node.TypeDefinitionId = vendorType.NodeId;
            ArrayOf<Client.PackMLUnit> units = await m_client.DiscoverAsync();
            Client.PackMLUnit found = units.Span.ToArray().Single(unit => unit.NodeId == m_unit.Node.NodeId);
            Assert.That(found.TypeDefinition, Is.EqualTo(vendorType.NodeId));
            Assert.That(found.BrowseName, Is.EqualTo(m_unit.Node.BrowseName));
            Assert.That((await m_client.ReadStatusAsync(found.NodeId)).CurMachSpeed.Value, Is.EqualTo(12.5f));
        }

        [Test]
        public async Task UnitsAddedAfterStartupRemainDiscoverableBelowObjects()
        {
            Unit added = await m_manager.Runtime.AddUnitAsync(
                new QualifiedName("Dynamic", m_manager.InstanceNamespaceIndex), new EquipmentProvider());
            ArrayOf<Client.PackMLUnit> units = await m_client.DiscoverAsync();
            Assert.That(units.Span.ToArray().Select(unit => unit.NodeId),
                Does.Contain(added.Node.NodeId));
            Assert.That((await m_client.ReadStatusAsync(added.Node.NodeId)).UnitSupportedModes.Value,
                Is.EqualTo(added.ModeDataType.NodeId));
        }

        [Test]
        public async Task RuntimeComposesUnderChosenParentAndRejectsDuplicateNames()
        {
            var parent = new BaseObjectState(null)
            {
                NodeId = new NodeId("Line", m_manager.InstanceNamespaceIndex),
                BrowseName = new QualifiedName("Line", m_manager.InstanceNamespaceIndex),
                DisplayName = new LocalizedText("Line"),
                TypeDefinitionId = Ua.ObjectTypeIds.BaseObjectType
            };
            await m_manager.AddPredefinedNodeAsync(parent);
            Unit child = await m_manager.Runtime.AddUnitAsync(
                new QualifiedName("Filler", m_manager.InstanceNamespaceIndex), new EquipmentProvider(), parent: parent);
            Assert.That(child.Node.Parent, Is.SameAs(parent));
            ArrayOf<Client.PackMLUnit> units = await m_client.DiscoverAsync(parent.NodeId);
            Assert.That(units.Count, Is.EqualTo(1));
            Assert.That(units[0].NodeId, Is.EqualTo(child.Node.NodeId));
            ServiceResultException duplicate = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await m_manager.Runtime.AddUnitAsync(child.Node.BrowseName,
                    new EquipmentProvider(), parent: parent)))!;
            Assert.That(duplicate.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadBrowseNameDuplicated));
            Assert.That(m_manager.Runtime.Units.Count, Is.EqualTo(2));
            Assert.That((await m_client.ReadStatusAsync(child.Node.NodeId)).CurMachSpeed.Value, Is.EqualTo(12.5f));
        }

        [Test]
        public async Task RuntimeRollsBackUnitWhenParentNotificationFailsAndPermitsRetry()
        {
            var parent = new BaseObjectState(null)
            {
                NodeId = new NodeId("FailingLine", m_manager.InstanceNamespaceIndex),
                BrowseName = new QualifiedName("FailingLine", m_manager.InstanceNamespaceIndex),
                DisplayName = new LocalizedText("FailingLine"),
                TypeDefinitionId = Ua.ObjectTypeIds.BaseObjectType
            };
            await m_manager.AddPredefinedNodeAsync(parent);
            var notificationError = new InvalidOperationException("Parent subscriber failed.");
            parent.OnStateChangedAsync = async (_, _, _, _) =>
            {
                await Task.Yield();
                throw notificationError;
            };
            var browseName = new QualifiedName("Capper", m_manager.InstanceNamespaceIndex);
            int unitCount = m_manager.Runtime.Units.Count;

            InvalidOperationException failure = (await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await m_manager.Runtime.AddUnitAsync(
                    browseName, new EquipmentProvider(), parent: parent)))!;

            Assert.That(failure, Is.SameAs(notificationError));
            Assert.That(m_manager.Runtime.Units.Count, Is.EqualTo(unitCount));
            Assert.That(
                m_manager.Runtime.Units.ToList().Exists(unit => unit.Node.BrowseName == browseName), Is.False);
            var children = new List<BaseInstanceState>();
            parent.GetChildren(m_manager.SystemContext, children);
            Assert.That(children.Exists(child => child.BrowseName == browseName), Is.False);

            parent.OnStateChangedAsync = null;
            Unit retried = await m_manager.Runtime.AddUnitAsync(browseName, new EquipmentProvider(), parent: parent);

            Assert.That(retried.Node.Parent, Is.SameAs(parent));
            Assert.That(m_manager.Runtime.Units.Count, Is.EqualTo(unitCount + 1));
            Assert.That(m_manager.Runtime.Units.ToList(), Does.Contain(retried));
        }

        [Test]
        public async Task RuntimeUnitsHideUnitWhileParentNotificationIsPending()
        {
            var parent = new BaseObjectState(null)
            {
                NodeId = new NodeId("GatedLine", m_manager.InstanceNamespaceIndex),
                BrowseName = new QualifiedName("GatedLine", m_manager.InstanceNamespaceIndex),
                DisplayName = new LocalizedText("GatedLine"),
                TypeDefinitionId = Ua.ObjectTypeIds.BaseObjectType
            };
            await m_manager.AddPredefinedNodeAsync(parent);
            TaskCompletionSource<bool> entered = PackMLTestSupport.Signal();
            TaskCompletionSource<bool> release = PackMLTestSupport.Signal();
            var notificationError = new InvalidOperationException("Parent subscriber failed.");
            parent.OnStateChangedAsync = async (_, _, _, _) =>
            {
                entered.TrySetResult(true);
                await release.Task.ConfigureAwait(false);
                throw notificationError;
            };
            var browseName = new QualifiedName("Labeler", m_manager.InstanceNamespaceIndex);
            int unitCount = m_manager.Runtime.Units.Count;

            Task<Unit> adding = m_manager.Runtime.AddUnitAsync(
                browseName, new EquipmentProvider(), parent: parent).AsTask();
            ArrayOf<Unit> whilePending;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                whilePending = m_manager.Runtime.Units;
            }
            finally
            {
                release.TrySetResult(true);
            }
            InvalidOperationException failure = (await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await adding.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false)))!;

            Assert.That(failure, Is.SameAs(notificationError));
            Assert.That(whilePending.Count, Is.EqualTo(unitCount));
            Assert.That(whilePending.ToList().Exists(unit => unit.Node.BrowseName == browseName), Is.False);
            Assert.That(m_manager.Runtime.Units.Count, Is.EqualTo(unitCount));
            parent.OnStateChangedAsync = null;
        }

        [Test]
        public async Task RuntimeAdmitsWireCallsWhileParentNotificationIsPending(
            [Values(true, false)] bool published)
        {
            string lineName = published ? "AdmittingLine" : "RollbackLine";
            var parent = new BaseObjectState(null)
            {
                NodeId = new NodeId(lineName, m_manager.InstanceNamespaceIndex),
                BrowseName = new QualifiedName(lineName, m_manager.InstanceNamespaceIndex),
                DisplayName = new LocalizedText(lineName),
                TypeDefinitionId = Ua.ObjectTypeIds.BaseObjectType
            };
            await m_manager.AddPredefinedNodeAsync(parent);
            TaskCompletionSource<bool> entered = PackMLTestSupport.Signal();
            TaskCompletionSource<bool> release = PackMLTestSupport.Signal();
            var notificationError = new InvalidOperationException("Parent subscriber failed.");
            parent.OnStateChangedAsync = async (_, _, _, _) =>
            {
                entered.TrySetResult(true);
                await release.Task.ConfigureAwait(false);
                if (!published)
                {
                    throw notificationError;
                }
            };
            var browseName = new QualifiedName("Sealer", m_manager.InstanceNamespaceIndex);
            var provider = new EquipmentProvider();
            bool listedWhenExecuted = true;
            provider.Execute = (request, _) =>
            {
                listedWhenExecuted = m_manager.Runtime.Units.ToList()
                    .Exists(unit => unit.Node.BrowseName == browseName);
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            };

            Task<Unit> adding = m_manager.Runtime.AddUnitAsync(browseName, provider, parent: parent).AsTask();
            ServiceResult result;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                var children = new List<BaseInstanceState>();
                parent.GetChildren(m_manager.SystemContext, children);
                PackMLBaseObjectState pending = children.OfType<PackMLBaseObjectState>()
                    .Single(child => child.BrowseName == browseName);
                result = await pending.SetMachSpeed.Required().CallAsync(
                    m_manager.SystemContext, pending.NodeId, new[] { Variant.From(10f) }.ToArrayOf(),
                    new List<ServiceResult>(), new List<Variant>()).AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);

                Assert.That(adding.IsCompleted, Is.False);
            }
            finally
            {
                release.TrySetResult(true);
            }

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Good));
            Assert.That(provider.Executed, Has.Count.EqualTo(1));
            Assert.That(provider.Executed[0].MachineSpeed, Is.EqualTo(10f));
            Assert.That(listedWhenExecuted, Is.False);
            if (published)
            {
                Unit unit = await adding.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                Assert.That(m_manager.Runtime.Units.ToList(), Does.Contain(unit));
            }
            else
            {
                InvalidOperationException failure = (await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await adding.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false)))!;
                Assert.That(failure, Is.SameAs(notificationError));
                Assert.That(
                    m_manager.Runtime.Units.ToList().Exists(unit => unit.Node.BrowseName == browseName), Is.False);
            }
            Assert.That(provider.Executed, Has.Count.EqualTo(1));
            parent.OnStateChangedAsync = null;
        }
        [Test]
        public async Task EnabledWireOperationsPreservePayloadAndReadinessMeaning()
        {
            var provider = new EquipmentProvider
            {
                Snapshot = new PackMLEquipmentSnapshot
                {
                    MaterialReadiness = [true, false],
                    RemoteParameters = [new PackMLRemoteInterfaceDataType { Number = 4, ControlCmdNumber = 9 }],
                    CurrentMachineSpeed = 5,
                    SourceTimestamp = DateTime.UtcNow,
                    StatusCode = StatusCodes.Good
                }
            };
            Unit unit = await m_manager.Runtime.AddUnitAsync(
                new QualifiedName("Auxiliary", m_manager.InstanceNamespaceIndex), provider,
                options =>
                {
                    options.EnableRemoteCommand = true;
                    options.MaterialCount = 2;
                });
            await m_client.SetInterlockAsync(unit.Node.NodeId, 1, true);
            await m_client.RemoteCommandAsync(unit.Node.NodeId,
                [new PackMLRemoteInterfaceDataType { Number = 4, ControlCmdNumber = 7 }]);
            PackMLStatusSnapshot status = await m_client.ReadStatusAsync(unit.Node.NodeId);
            Assert.That(provider.Executed[0].InterlockId, Is.EqualTo(1));
            Assert.That(provider.Executed[0].InterlockState, Is.True);
            Assert.That(provider.Executed[1].RemoteInterfaces[0].Number, Is.EqualTo(4));
            Assert.That(provider.Executed[1].RemoteInterfaces[0].ControlCmdNumber, Is.EqualTo(7));
            Assert.That(status.MaterialInterlock.Value.Span.ToArray(), Is.EqualTo(s_materialReadiness));
            Assert.That(status.MaterialInterlocked.Value, Is.True);
            Assert.That(status.RemoteParameter.Value[0].ControlCmdNumber, Is.EqualTo(9),
                "accepted remote commands do not overwrite equipment-reported parameters");
            string?[] common = m_manager.ConformanceUnits.Span.ToArray().Select(name => name.Name).ToArray();
            Assert.That(common, Does.Not.Contain("PackML SetInterlock"), "not enabled on every unit");
        }

        [Test]
        public async Task AnonymousCannotReadAdministrativePayloadByKnownNodeId()
        {
            ReadResponse response = await m_session.ReadAsync(null, 0, TimestampsToReturn.Both,
            [
                new ReadValueId
                {
                    NodeId = m_unit.Node.Admin.Required().ProdProcessedCount.Required().NodeId,
                    AttributeId = Attributes.Value
                }
            ], CancellationToken.None);
            Assert.That(response.Results[0].StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied));
            Assert.That(m_equipment.Executed, Is.Empty);
        }

        [Test]
        public async Task AuthorizedAdministrationSnapshotDecodesMatricesStructuresAndAcquisitionMetadata()
        {
            var clock = new ManualTimeProvider();
            Unit unit = await m_manager.Runtime.AddUnitAsync(
                new QualifiedName("Administrative", m_manager.InstanceNamespaceIndex), new EquipmentProvider(),
                options =>
                {
                    options.TimeProvider = clock;
                    options.AdministrationExtents = new PackMLAdministrationExtents
                    {
                        Parameters = 2,
                        Alarms = 3,
                        AlarmHistory = 4,
                        Warnings = 5,
                        StopReasons = 1,
                        ConsumedCounts = 2,
                        ProcessedCounts = 2,
                        DefectiveCounts = 2
                    };
                    options.AdministrationRolePermissions =
                    [
                        new RolePermissionType
                        {
                            RoleId = Ua.ObjectIds.WellKnownRole_Anonymous,
                            Permissions = (uint)(PermissionType.Browse | PermissionType.Read)
                        }
                    ];
                });
            clock.Advance(TimeSpan.FromSeconds(2));
            DateTimeUtc acquired = clock.GetUtcNow().UtcDateTime;
            await unit.PublishAdministrationAsync(new PackMLAdministrationData
            {
                Parameters = [new PackMLDescriptorDataType { ID = 9, Name = "Fill", Value = 50 }],
                Alarms = [new PackMLAlarmDataType { ID = 10, Message = "Active", Trigger = true }],
                AlarmHistory = [new PackMLAlarmDataType { ID = 11, Message = "Previous" }],
                Warnings = [new PackMLAlarmDataType { ID = 12, Message = "Warning" }],
                StopReason = new PackMLAlarmDataType { ID = 13, Message = "First out" },
                ConsumedCounts = [new PackMLCountDataType { ID = 20, Count = 2, AccCount = 100 }],
                ProcessedCounts = [new PackMLCountDataType { ID = 21, Count = 3, AccCount = 200 }],
                DefectiveCounts = [new PackMLCountDataType { ID = 22, Count = 4, AccCount = 300 }],
                MachineDesignSpeed = 180,
                SourceTimestamp = acquired,
                StatusCode = StatusCodes.Uncertain
            });

            PackMLAdminSnapshot snapshot = await m_client.ReadAdminAsync(unit.Node.NodeId);

            Assert.That(snapshot.Parameter.Value[0].ID, Is.EqualTo(9));
            Assert.That(snapshot.Alarm.Value[0].Message, Is.EqualTo("Active"));
            Assert.That(snapshot.AlarmHistory.Value[0].ID, Is.EqualTo(11));
            Assert.That(snapshot.Warning.Value[0].ID, Is.EqualTo(12));
            Assert.That(snapshot.StopReason.Value.ID, Is.EqualTo(13));
            Assert.That(snapshot.StopReasonExtent.Value, Is.EqualTo(1));
            Assert.That(snapshot.AlarmExtent.Value, Is.EqualTo(3));
            Assert.That(snapshot.AlarmHistoryExtent.Value, Is.EqualTo(4));
            Assert.That(snapshot.WarningExtent.Value, Is.EqualTo(5));
            Assert.That(snapshot.ProdConsumedCount.Value[0].AccCount, Is.EqualTo(100));
            Assert.That(snapshot.ProdProcessedCount.Value[0].Count, Is.EqualTo(3));
            Assert.That(snapshot.ProdDefectiveCount.Value[0].Count, Is.EqualTo(4));
            Assert.That(snapshot.MachDesignSpeed.Value, Is.EqualTo(180f));
            Assert.That(snapshot.MachDesignSpeed.IsGood, Is.False);
            Assert.That(snapshot.MachDesignSpeed.DataValue.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Uncertain));
            Assert.That(snapshot.MachDesignSpeed.DataValue.SourceTimestamp, Is.EqualTo(acquired));
            Assert.That(snapshot.ModeCurrentTime.Value[1], Is.EqualTo(2));
            Assert.That(snapshot.ModeCumulativeTime.Value[1], Is.EqualTo(2));
            Assert.That(snapshot.StateCurrentTime.Value.Dimensions, Is.EqualTo(s_stateDimensions));
            Assert.That(snapshot.StateCumulativeTime.Value.Span[20 + (int)PackMLStateNumbers.Stopped], Is.EqualTo(2));
            Assert.That(snapshot.AccTimeSinceReset.Value, Is.EqualTo(2));
        }

        [Test]
        public async Task RuntimeCancelsAllUnitsBeforeWaitingForEquipmentToDrain()
        {
            var firstEntered = PackMLTestSupport.Signal();
            var secondEntered = PackMLTestSupport.Signal();
            var secondCancelled = PackMLTestSupport.Signal();
            var firstProvider = new EquipmentProvider
            {
                Execute = async (_, token) =>
                {
                    firstEntered.TrySetResult(true);
                    await secondCancelled.Task.WaitAsync(TimeSpan.FromSeconds(15), CancellationToken.None);
                    token.ThrowIfCancellationRequested();
                    return ServiceResult.Good;
                }
            };
            var secondProvider = new EquipmentProvider
            {
                Execute = async (_, token) =>
                {
                    using CancellationTokenRegistration registration =
                        token.Register(() => secondCancelled.TrySetResult(true));
                    secondEntered.TrySetResult(true);
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    return ServiceResult.Good;
                }
            };
            Unit first = await m_manager.Runtime.AddUnitAsync(
                new QualifiedName("First", m_manager.InstanceNamespaceIndex), firstProvider);
            Unit second = await m_manager.Runtime.AddUnitAsync(
                new QualifiedName("Second", m_manager.InstanceNamespaceIndex), secondProvider);
            var request = new PackMLRequest { Operation = PackMLOperation.SetMachSpeed, MachineSpeed = 5 };
            Task<ServiceResult> firstRequest = first.RequestAsync(m_manager.SystemContext, request).AsTask();
            Task<ServiceResult> secondRequest = second.RequestAsync(m_manager.SystemContext, request).AsTask();
            await Task.WhenAll(firstEntered.Task, secondEntered.Task).WaitAsync(TimeSpan.FromSeconds(10));

            await m_manager.Runtime.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));

            Assert.That(secondCancelled.Task.IsCompleted, Is.True);
            await Assert.ThatAsync(() => firstRequest, Throws.InstanceOf<OperationCanceledException>());
            await Assert.ThatAsync(() => secondRequest, Throws.InstanceOf<OperationCanceledException>());
            Assert.That(firstProvider.Executed, Has.Count.EqualTo(1));
            Assert.That(secondProvider.Executed, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task RuntimePreservesEveryUnitShutdownFailure()
        {
            var runtime = new PackMLRuntime(m_manager);
            var firstEntered = PackMLTestSupport.Signal();
            var secondEntered = PackMLTestSupport.Signal();
            var firstCancelled = PackMLTestSupport.Signal();
            var secondCancelled = PackMLTestSupport.Signal();
            var firstError = new InvalidOperationException("First unit cancellation failed.");
            var secondError = new InvalidOperationException("Second unit cancellation failed.");
            var firstProvider = new EquipmentProvider
            {
                Execute = async (_, token) =>
                {
                    using CancellationTokenRegistration registration = token.Register(() =>
                    {
                        firstCancelled.TrySetResult(true);
                        throw firstError;
                    });
                    firstEntered.TrySetResult(true);
                    await firstCancelled.Task;
                    token.ThrowIfCancellationRequested();
                    return ServiceResult.Good;
                }
            };
            var secondProvider = new EquipmentProvider
            {
                Execute = async (_, token) =>
                {
                    using CancellationTokenRegistration registration = token.Register(() =>
                    {
                        secondCancelled.TrySetResult(true);
                        throw secondError;
                    });
                    secondEntered.TrySetResult(true);
                    await secondCancelled.Task;
                    token.ThrowIfCancellationRequested();
                    return ServiceResult.Good;
                }
            };
            Unit first = await runtime.AddUnitAsync(
                new QualifiedName("FirstFailure", m_manager.InstanceNamespaceIndex), firstProvider);
            Unit second = await runtime.AddUnitAsync(
                new QualifiedName("SecondFailure", m_manager.InstanceNamespaceIndex), secondProvider);
            var request = new PackMLRequest { Operation = PackMLOperation.SetMachSpeed, MachineSpeed = 5 };
            Task<ServiceResult> firstRequest = first.RequestAsync(m_manager.SystemContext, request).AsTask();
            Task<ServiceResult> secondRequest = second.RequestAsync(m_manager.SystemContext, request).AsTask();
            await Task.WhenAll(firstEntered.Task, secondEntered.Task).WaitAsync(TimeSpan.FromSeconds(10));

            AggregateException failure = (await Assert.ThrowsAsync<AggregateException>(
                async () => await runtime.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10))))!;

            Assert.That(failure.Flatten().InnerExceptions, Does.Contain(firstError));
            Assert.That(failure.Flatten().InnerExceptions, Does.Contain(secondError));
            Assert.That(failure.Flatten().InnerExceptions, Has.Count.EqualTo(2));
            await Assert.ThatAsync(() => firstRequest, Throws.InstanceOf<OperationCanceledException>());
            await Assert.ThatAsync(() => secondRequest, Throws.InstanceOf<OperationCanceledException>());
        }

        private void ConfigureServer(OpcUaServerOptions options)
        {
            if (TestContext.CurrentContext.Test.MethodName == nameof(PackMLMonitoredItemSupportsQueueSizeOfAtLeastFive))
            {
                options.ConfigureBuilder = builder =>
                {
                    if (builder is not ApplicationConfigurationBuilder configuration)
                    {
                        throw new InvalidOperationException("The test requires the application configuration builder.");
                    }
                    configuration.ApplicationConfiguration.ServerConfiguration.MaxMonitoredItemCount = 102;
                    configuration.ApplicationConfiguration.ServerConfiguration.MaxMonitoredItemsPerSubscription = 102;
                    configuration.ApplicationConfiguration.ServerConfiguration.MaxNotificationQueueSize = 5;
                };
            }
            options.ApplicationName = nameof(PackMLClientServerTests);
            options.ApplicationUri = "urn:localhost:packml-tests";
            options.ProductUri = "urn:localhost:packml-tests:product";
            options.PkiRoot = Path.Combine(
                TestContext.CurrentContext.WorkDirectory, "packml-pki", Guid.NewGuid().ToString("N"));
            options.AutoAcceptUntrustedCertificates = true;
            options.IncludeUnsecurePolicyNone = true;
            options.EndpointUrls.Clear();
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port;
            try
            {
                port = ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
            m_endpoint = "opc.tcp://localhost:" + port.ToString(CultureInfo.InvariantCulture) + "/PackML";
            options.EndpointUrls.Add(m_endpoint);
        }

        private static readonly bool[] s_materialReadiness = [true, false];
        private static readonly int[] s_stateDimensions = [2, 20];
    }
}
