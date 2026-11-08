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
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Configuration;
using Opc.Ua.PackML.Client;
using Opc.Ua.PackML.Server;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using ClientSubscription = Opc.Ua.Client.Subscriptions.ISubscription;
using ClientSubscriptionManager = Opc.Ua.Client.Subscriptions.ISubscriptionManager;
using MonitoringOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;
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
        private ComposedNodeManager m_composed = null!;
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
            }).AddNodeManager(new ComposedNodeManagerFactory(manager => m_composed = manager));
            // The configurator runs while the address space is built, but the TCP listener is
            // already accepting then and answers every request with BadServerHalted until the
            // server is Running. Startup tasks run only after the server has fully started.
            var started = new ServerStartedSignal();
            services.AddSingleton<IServerStartupTask>(started);
            m_services = services.BuildServiceProvider();
            m_host = m_services.GetServices<IHostedService>().Single();
            await m_host.StartAsync(CancellationToken.None);
            await WaitForServerStartAsync(m_host, started, ready);

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
        /// Verifies the hosted server runs startup tasks only after PackML configuration has completed and
        /// rejects requests until then, so a configurator signal alone does not prove the server is serving.
        /// </summary>
        [Test]
        public async Task PackMLConfigurationCompletesBeforeServerServesRequestsAsync()
        {
            var configured = PackMLTestSupport.Signal();
            var release = PackMLTestSupport.Signal();
            var started = new ServerStartedSignal();
            string fixtureEndpoint = m_endpoint;
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpcUa().AddServer<StandardServer>(ConfigureServer).AddPackML(options =>
            {
                options.NamespaceUri = "urn:packml:session:startup";
                options.ConfigureAsync = async (manager, token) =>
                {
                    configured.TrySetResult(true);
                    await release.Task.WaitAsync(token);
                };
            });
            services.AddSingleton<IServerStartupTask>(started);
            ServiceProvider provider = services.BuildServiceProvider();
            IHostedService host = provider.GetServices<IHostedService>().Single();
            try
            {
                await host.StartAsync(CancellationToken.None);
                Task startup = WaitForServerStartAsync(host, started, configured);
                Task first = await Task.WhenAny(configured.Task, startup);
                if (ReferenceEquals(first, startup))
                {
                    await startup;
                }
                Assert.That(
                    first,
                    Is.SameAs(configured.Task),
                    "The hosted server stopped before PackML configuration ran.");
                string endpointUrl = m_endpoint;
                Task gated = WaitForServerStartAsync(host, started, configured);
                Assert.That(started.Task.IsCompleted, Is.False);

                ITelemetryContext telemetry = NUnitTelemetryContext.Create();
                using var clientFixture = new ClientFixture(telemetry);
                await clientFixture.LoadClientConfigurationAsync(Path.Combine(
                    TestContext.CurrentContext.WorkDirectory, "packml-client", Guid.NewGuid().ToString("N")));
                ServiceResultException? halted = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await CoreClientUtils.SelectEndpointAsync(
                        clientFixture.Config, endpointUrl, false, 15000, telemetry, CancellationToken.None));
                Assert.That(halted!.StatusCode, Is.EqualTo(StatusCodes.BadServerHalted));
                Assert.That(started.Task.IsCompleted, Is.False);
                Assert.That(gated.IsCompleted, Is.False, "The fixture must wait for server startup.");

                release.TrySetResult(true);
                await Task.WhenAll(startup, gated);
                EndpointDescription? endpoint = await CoreClientUtils.SelectEndpointAsync(
                    clientFixture.Config, endpointUrl, false, 15000, telemetry, CancellationToken.None);
                Assert.That(endpoint, Is.Not.Null);
            }
            finally
            {
                release.TrySetResult(true);
                m_endpoint = fixtureEndpoint;
                try
                {
                    await host.StopAsync(CancellationToken.None);
                }
                finally
                {
                    await provider.DisposeAsync();
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
                                NodeId =
                                    Ua.VariableIds
                                        .Server_ServerCapabilities_MaxMonitoredItems,
                                AttributeId = Attributes.Value
                            },
                            new ReadValueId
                            {
                                NodeId =
                                    Ua.VariableIds
                                        .Server_ServerCapabilities_MaxMonitoredItemsPerSubscription,
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
                    Assert.That(retained, Has.Length.EqualTo(5));
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
                Assert.That(changes, Has.Count.EqualTo(count));
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

        /// <summary>
        /// Verifies that PackML state observation continues after reconnect without closing the borrowed session.
        /// </summary>
        [Test]
        [CancelAfter(150_000)]
        public async Task StreamingStateObservationRecoversAfterAutomaticReconnect(CancellationToken ct)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using ManagedSession session = await new ManagedSessionBuilder(m_clientFixture.Config, telemetry)
                .UseEndpoint(new ConfiguredEndpoint(
                    null, m_session.Endpoint, EndpointConfiguration.Create(m_clientFixture.Config)))
                .WithSessionName(nameof(StreamingStateObservationRecoversAfterAutomaticReconnect))
                .WithSessionTimeout(TimeSpan.FromSeconds(60))
                .WithReconnectPolicy(options => options with
                {
                    Strategy = BackoffStrategy.Constant,
                    InitialDelay = TimeSpan.FromMilliseconds(200),
                    MaxRetries = 0,
                    JitterFactor = 0.0
                })
                .ConnectAsync(ct);
            session.KeepAliveInterval = 1_000;

            // Task.FromResult is complete, and CreateAsync is awaited before session disposal.
            // TODO: Remove when CA2025 recognizes the factory callback's completed task.
#pragma warning disable CA2025
            var factory = new PackMLClientFactory(_ => Task.FromResult(session), telemetry);
#pragma warning restore CA2025
            PackMLClient client = await factory.CreateAsync(ct);
            Assert.That(client.Session, Is.SameAs(session));
            Assert.That(session.Connected, Is.True);

            var reconnected = PackMLTestSupport.Signal();
            var closed = PackMLTestSupport.Signal();
            EventHandler<ConnectionStateChangedEventArgs> onStateChanged = (_, change) =>
            {
                if (change.PreviousState == ConnectionState.Reconnecting &&
                    change.NewState == ConnectionState.Connected)
                {
                    reconnected.TrySetResult(true);
                }
            };
            EventHandler onClosing = (_, _) => closed.TrySetResult(true);
            session.ConnectionStateChanged += onStateChanged;
            session.SessionClosing += onClosing;
            try
            {
                var streaming = (StreamingSubscription)session.DefaultStreaming;
                long droppedBefore = streaming.DroppedNotificationCount;
                NodeId changedState = NodeId.Null;
                using var observationStop = CancellationTokenSource.CreateLinkedTokenSource(ct);
                await using (IAsyncEnumerator<PackMLValue<NodeId>> observation = client.ObserveAsync(
                    m_unit.Node.NodeId,
                    PackMLFields.State.MachineState,
                    options: new MonitoringOptions
                    {
                        SamplingInterval = TimeSpan.FromMilliseconds(100),
                        QueueSize = 32
                    },
                    cancellationToken: observationStop.Token).GetAsyncEnumerator(observationStop.Token))
                {
                    Task<bool>? move = null;
                    try
                    {
                        move = observation.MoveNextAsync().AsTask();
                        Assert.That(await move.WaitAsync(TimeSpan.FromSeconds(20), ct), Is.True);
                        move = null;
                        PackMLValue<NodeId> initial = observation.Current;
                        Assert.That(initial.IsGood, Is.True);
                        Assert.That(initial.Value.TryGetValue(out uint initialStateId), Is.True);
                        Assert.That(PackMLStateNumbers.FromStateId(initialStateId),
                            Is.EqualTo(PackMLStateNumbers.Stopped));

                        Assert.That(session.TryGetSubscriptionManager(out ClientSubscriptionManager? manager), Is.True);
                        ClientSubscription subscription = manager!.Items.Single();
                        Assert.That(subscription.Created, Is.True);
                        Assert.That(subscription.MonitoredItems.Items.Single().Created, Is.True);

                        session.TransportChannel.Dispose();
                        Assert.That(await reconnected.Task.WaitAsync(TimeSpan.FromSeconds(60), ct), Is.True,
                            "The managed session must reconnect automatically after channel loss.");
                        Assert.That(session.Connected, Is.True);
                        using var recovery = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        recovery.CancelAfter(TimeSpan.FromSeconds(15));
                        while (!subscription.Created || !subscription.MonitoredItems.Items.Single().Created)
                        {
                            await Task.Delay(50, recovery.Token);
                        }

                        move = observation.MoveNextAsync().AsTask();
                        await client.CommandAsync(m_unit.Node.NodeId, PackMLStateCommand.Reset, ct);
                        Assert.That(await move.WaitAsync(TimeSpan.FromSeconds(20), ct), Is.True);
                        move = null;
                        PackMLValue<NodeId> changed = observation.Current;
                        Assert.That(changed.IsGood, Is.True);
                        Assert.That(changed.NodeId, Is.EqualTo(initial.NodeId));
                        Assert.That(changed.Value.TryGetValue(out uint changedStateId), Is.True);
                        Assert.That(PackMLStateNumbers.FromStateId(changedStateId),
                            Is.EqualTo(PackMLStateNumbers.Running),
                            "The first post-reconnect notification must not be a stale or duplicate state.");
                        Assert.That(m_unit.CurrentState, Is.EqualTo(PackMLStateNumbers.Resetting));
                        changedState = changed.Value;

                        Task<bool> unexpected = observation.MoveNextAsync().AsTask();
                        move = unexpected;
                        TimeSpan quietPeriod = TimeSpan.FromMilliseconds(Math.Max(
                            2_000, subscription.CurrentPublishingInterval.TotalMilliseconds * 3));
                        Task completed = await Task.WhenAny(unexpected, Task.Delay(quietPeriod, ct));
                        ct.ThrowIfCancellationRequested();
                        Assert.That(completed, Is.Not.SameAs(unexpected),
                            "The observer must not receive another duplicate or stale state.");
                        observationStop.Cancel();
                        await Assert.ThatAsync(() => unexpected, Throws.InstanceOf<OperationCanceledException>());
                        move = null;
                        Assert.That(streaming.DroppedNotificationCount, Is.EqualTo(droppedBefore));
                    }
                    finally
                    {
                        observationStop.Cancel();
                        if (move != null)
                        {
                            try
                            {
                                await move;
                            }
                            catch (OperationCanceledException) when (observationStop.IsCancellationRequested)
                            {
                            }
                        }
                    }
                }

                Assert.That(session.DefaultStreaming, Is.SameAs(streaming));
                Assert.That(client.Session, Is.SameAs(session));
                Assert.That(session.Connected, Is.True);
                Assert.That(closed.Task.IsCompleted, Is.False);
                PackMLValue<NodeId> current = await client.ReadAsync(
                    m_unit.Node.NodeId, PackMLFields.State.MachineState, ct);
                Assert.That(current.IsGood, Is.True);
                Assert.That(current.Value, Is.EqualTo(changedState));
                Assert.That(session.Connected, Is.True);
                Assert.That(closed.Task.IsCompleted, Is.False);
            }
            finally
            {
                session.ConnectionStateChanged -= onStateChanged;
                session.SessionClosing -= onClosing;
            }
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

        /// <summary>
        /// Rejects a parent owned by another manager before changing its address space.
        /// </summary>
        [Test]
        public async Task PreparedRuntimeRejectsForeignParentWithoutChangingIt()
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            CancellationToken ct = deadline.Token;
            ushort namespaceIndex = m_composed.NamespaceIndexes[0];
            var parent = new BaseObjectState(null)
            {
                NodeId = new NodeId("ForeignLine", namespaceIndex),
                BrowseName = new QualifiedName("ForeignLine", namespaceIndex),
                DisplayName = new LocalizedText("ForeignLine"),
                TypeDefinitionId = Ua.ObjectTypeIds.BaseObjectType
            };
            var existing = new BaseObjectState(parent)
            {
                NodeId = new NodeId("ForeignLine.Existing", namespaceIndex),
                BrowseName = new QualifiedName("Existing", namespaceIndex),
                DisplayName = new LocalizedText("Existing"),
                TypeDefinitionId = Ua.ObjectTypeIds.BaseObjectType
            };
            parent.AddChild(existing);
            parent.AddNotifier(m_composed.SystemContext, Ua.ReferenceTypeIds.HasEventSource, false, existing);
            parent.AddReference(Ua.ReferenceTypeIds.Organizes, false, existing.NodeId);
            await m_composed.AddPredefinedNodeAsync(parent, ct).ConfigureAwait(false);

            var childrenBefore = new List<BaseInstanceState>();
            var notifiersBefore = new List<NodeState.Notifier>();
            var referencesBefore = new List<IReference>();
            parent.GetChildren(m_composed.SystemContext, childrenBefore);
            parent.GetNotifiers(m_composed.SystemContext, notifiersBefore);
            parent.GetReferences(m_composed.SystemContext, referencesBefore);

            INodeManagerLifecycle lifecycle = m_services.GetRequiredService<INodeManagerLifecycle>();
            var factory = new PreparedPackMLManagerFactory(failFirstUnit: false);
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(factory)], ct).ConfigureAwait(false);
            PreparedPackMLManager candidate = factory.Manager
                ?? throw new InvalidOperationException("The candidate manager was not created.");
            int unitCount = candidate.Runtime.Units.Count;
            bool configured = false;

            ArgumentException? error = await Assert.ThrowsAsync<ArgumentException>(
                async () => await candidate.Runtime.AddUnitAsync(
                    new QualifiedName("RejectedUnit", candidate.NamespaceIndexes[0]),
                    new EquipmentProvider(),
                    _ => configured = true,
                    parent,
                    ct).ConfigureAwait(false)).ConfigureAwait(false);

            Assert.That(error!.ParamName, Is.EqualTo("parent"));
            Assert.That(error.Message, Does.Contain("owned by"));
            Assert.That(configured, Is.False);
            Assert.That(candidate.Runtime.Units.Count, Is.EqualTo(unitCount));
            var childrenAfter = new List<BaseInstanceState>();
            var notifiersAfter = new List<NodeState.Notifier>();
            var referencesAfter = new List<IReference>();
            parent.GetChildren(m_composed.SystemContext, childrenAfter);
            parent.GetNotifiers(m_composed.SystemContext, notifiersAfter);
            parent.GetReferences(m_composed.SystemContext, referencesAfter);
            Assert.That(childrenAfter, Is.EqualTo(childrenBefore));
            Assert.That(
                notifiersAfter.Select(notifier => (notifier.Node, notifier.ReferenceTypeId, notifier.IsInverse)),
                Is.EquivalentTo(
                    notifiersBefore.Select(notifier => (notifier.Node, notifier.ReferenceTypeId, notifier.IsInverse))));
            Assert.That(
                referencesAfter.Select(reference =>
                    (reference.ReferenceTypeId, reference.IsInverse, reference.TargetId)),
                Is.EquivalentTo(
                    referencesBefore.Select(reference =>
                        (reference.ReferenceTypeId, reference.IsInverse, reference.TargetId))));
        }

        /// <summary>
        /// Rejects an unindexed object even when it uses an owned parent's NodeId.
        /// </summary>
        [Test]
        public async Task RuntimeRejectsDifferentParentWithOwnedNodeId()
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            CancellationToken ct = deadline.Token;
            ushort namespaceIndex = m_composed.NamespaceIndexes[0];
            var parent = new BaseObjectState(null)
            {
                NodeId = new NodeId("OwnedLine", namespaceIndex),
                BrowseName = new QualifiedName("OwnedLine", namespaceIndex),
                DisplayName = new LocalizedText("OwnedLine"),
                TypeDefinitionId = Ua.ObjectTypeIds.BaseObjectType
            };
            await m_composed.AddPredefinedNodeAsync(parent, ct).ConfigureAwait(false);
            var impostor = new BaseObjectState(null)
            {
                NodeId = parent.NodeId,
                BrowseName = parent.BrowseName,
                DisplayName = parent.DisplayName,
                TypeDefinitionId = Ua.ObjectTypeIds.BaseObjectType
            };
            bool configured = false;
            await using var runtime = new PackMLRuntime(m_composed);

            ArgumentException? error = await Assert.ThrowsAsync<ArgumentException>(
                async () => await runtime.AddUnitAsync(
                    new QualifiedName("RejectedUnit", namespaceIndex),
                    new EquipmentProvider(),
                    _ => configured = true,
                    impostor,
                    ct).ConfigureAwait(false)).ConfigureAwait(false);

            Assert.That(error!.ParamName, Is.EqualTo("parent"));
            Assert.That(configured, Is.False);
            Assert.That(runtime.Units.Count, Is.Zero);
            Assert.That(m_composed.Find(parent.NodeId), Is.SameAs(parent));
            var children = new List<BaseInstanceState>();
            parent.GetChildren(m_composed.SystemContext, children);
            Assert.That(children, Is.Empty);
            impostor.GetChildren(m_composed.SystemContext, children);
            Assert.That(children, Is.Empty);
        }

        /// <summary>
        /// Validates external-reference ownership before modifying the target.
        /// </summary>
        [Test]
        public async Task ExternalReferenceHelperRejectsInvalidOwnership()
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            CancellationToken ct = deadline.Token;
            ushort namespaceIndex = m_composed.NamespaceIndexes[0];
            var target = new BaseObjectState(null)
            {
                NodeId = new NodeId("ReferenceTarget", namespaceIndex),
                BrowseName = new QualifiedName("ReferenceTarget", namespaceIndex),
                DisplayName = new LocalizedText("ReferenceTarget"),
                TypeDefinitionId = Ua.ObjectTypeIds.BaseObjectType
            };
            await m_composed.AddPredefinedNodeAsync(target, ct).ConfigureAwait(false);

            ArgumentException? wrongTarget = await Assert.ThrowsAsync<ArgumentException>(
                async () => await m_composed.AddExternalReferenceAsync(
                    m_unit.Node, Ua.ObjectIds.ObjectsFolder, Ua.ReferenceTypeIds.Organizes, false, ct)
                    .ConfigureAwait(false)).ConfigureAwait(false);
            ArgumentException? localSource = await Assert.ThrowsAsync<ArgumentException>(
                async () => await m_composed.AddExternalReferenceAsync(
                    target, target.NodeId, Ua.ReferenceTypeIds.Organizes, false, ct)
                    .ConfigureAwait(false)).ConfigureAwait(false);
            ArgumentException? missingSource = await Assert.ThrowsAsync<ArgumentException>(
                async () => await m_composed.AddExternalReferenceAsync(
                    target, NodeId.Null, Ua.ReferenceTypeIds.Organizes, false, ct)
                    .ConfigureAwait(false)).ConfigureAwait(false);
            ArgumentException? missingType = await Assert.ThrowsAsync<ArgumentException>(
                async () => await m_composed.AddExternalReferenceAsync(
                    target, Ua.ObjectIds.ObjectsFolder, NodeId.Null, false, ct)
                    .ConfigureAwait(false)).ConfigureAwait(false);
            var unknownSource = new NodeId("UnknownReferenceSource", namespaceIndex);
            ServiceResultException? unknown = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await m_composed.AddExternalReferenceAsync(
                    target, unknownSource, Ua.ReferenceTypeIds.Organizes, false, ct)
                    .ConfigureAwait(false)).ConfigureAwait(false);

            Assert.That(wrongTarget!.ParamName, Is.EqualTo("target"));
            Assert.That(localSource!.ParamName, Is.EqualTo("sourceId"));
            Assert.That(missingSource!.ParamName, Is.EqualTo("sourceId"));
            Assert.That(missingType!.ParamName, Is.EqualTo("referenceTypeId"));
            Assert.That(unknown!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNodeIdUnknown));
            Assert.That(target.ReferenceExists(
                Ua.ReferenceTypeIds.Organizes, true, Ua.ObjectIds.ObjectsFolder), Is.False);
            Assert.That(target.ReferenceExists(
                Ua.ReferenceTypeIds.Organizes, true, unknownSource), Is.False);
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

        [TestCase(false)]
        [TestCase(true)]
        public async Task RuntimeRollbackDrainsPendingWireProviderBeforeUnlinkingUnit(bool cancellationFails)
        {
            var parent = new BaseObjectState(null)
            {
                NodeId = new NodeId("RollbackDrain", m_manager.InstanceNamespaceIndex),
                BrowseName = new QualifiedName("RollbackDrain", m_manager.InstanceNamespaceIndex),
                DisplayName = new LocalizedText("RollbackDrain"),
                TypeDefinitionId = Ua.ObjectTypeIds.BaseObjectType
            };
            await m_manager.AddPredefinedNodeAsync(parent);
            var parentEntered = PackMLTestSupport.Signal();
            var parentRelease = PackMLTestSupport.Signal();
            var providerEntered = PackMLTestSupport.Signal();
            var providerCancelled = PackMLTestSupport.Signal();
            var providerRelease = PackMLTestSupport.Signal();
            var notificationError = new InvalidOperationException("Parent notification failed.");
            var cancellationError = new InvalidOperationException("Provider cancellation failed.");
            parent.OnStateChangedAsync = async (_, _, _, _) =>
            {
                parentEntered.TrySetResult(true);
                await parentRelease.Task.ConfigureAwait(false);
                throw notificationError;
            };
            var provider = new EquipmentProvider
            {
                Execute = async (_, token) =>
                {
                    using CancellationTokenRegistration registration = token.Register(() =>
                    {
                        providerCancelled.TrySetResult(true);
                        if (cancellationFails)
                        {
                            throw cancellationError;
                        }
                    });
                    providerEntered.TrySetResult(true);
                    await providerRelease.Task.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
                    token.ThrowIfCancellationRequested();
                    return ServiceResult.Good;
                }
            };
            int count = m_manager.Runtime.Units.Count;
            Task<Unit> adding = m_manager.Runtime.AddUnitAsync(
                new QualifiedName("DrainedSealer", m_manager.InstanceNamespaceIndex),
                provider, parent: parent).AsTask();
            Task<ServiceResult>? wire = null;
            try
            {
                await parentEntered.Task.WaitAsync(TimeSpan.FromSeconds(30));
                var children = new List<BaseInstanceState>();
                parent.GetChildren(m_manager.SystemContext, children);
                PackMLBaseObjectState pending = children.OfType<PackMLBaseObjectState>().Single();
                wire = pending.SetMachSpeed.Required().CallAsync(
                    m_manager.SystemContext, pending.NodeId, new[] { Variant.From(10f) }.ToArrayOf(),
                    new List<ServiceResult>(), new List<Variant>()).AsTask();
                await providerEntered.Task.WaitAsync(TimeSpan.FromSeconds(30));
                parentRelease.TrySetResult(true);
                await providerCancelled.Task.WaitAsync(TimeSpan.FromSeconds(30));
                Assert.That(adding.IsCompleted, Is.False, "rollback must join provider drain before unlinking");
                Assert.That(wire.IsCompleted, Is.False);
                children.Clear();
                parent.GetChildren(m_manager.SystemContext, children);
                Assert.That(children, Does.Contain(pending));
                Assert.That(m_manager.Runtime.Units.Count, Is.EqualTo(count));
            }
            finally
            {
                parentRelease.TrySetResult(true);
                providerRelease.TrySetResult(true);
                parent.OnStateChangedAsync = null;
            }
            if (cancellationFails)
            {
                AggregateException? error = await Assert.ThrowsAsync<AggregateException>(
                    async () => await adding.WaitAsync(TimeSpan.FromSeconds(30)));
                Assert.That(error!.Flatten().InnerExceptions, Does.Contain(notificationError));
                Assert.That(error.Flatten().InnerExceptions, Does.Contain(cancellationError));
            }
            else
            {
                InvalidOperationException? error = await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await adding.WaitAsync(TimeSpan.FromSeconds(30)));
                Assert.That(error, Is.SameAs(notificationError));
            }
            Assert.That(ServiceResult.IsBad(await wire!.WaitAsync(TimeSpan.FromSeconds(30))), Is.True);
            var remaining = new List<BaseInstanceState>();
            parent.GetChildren(m_manager.SystemContext, remaining);
            Assert.That(remaining, Is.Empty);
            Assert.That(m_manager.Runtime.Units.Count, Is.EqualTo(count));
            Assert.That(provider.Executed, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task RuntimeDisposeReentryLeavesCommittedUnitsAndRuntimeOpen()
        {
            var parent = new BaseObjectState(null)
            {
                NodeId = new NodeId("DisposeReentry", m_manager.InstanceNamespaceIndex),
                BrowseName = new QualifiedName("DisposeReentry", m_manager.InstanceNamespaceIndex),
                DisplayName = new LocalizedText("DisposeReentry"),
                TypeDefinitionId = Ua.ObjectTypeIds.BaseObjectType
            };
            await m_manager.AddPredefinedNodeAsync(parent);
            InvalidOperationException? rejection = null;
            parent.OnStateChangedAsync = async (_, _, _, _) =>
            {
                rejection = await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await m_manager.Runtime.DisposeAsync().AsTask()
                        .WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None));
            };
            Unit added;
            try
            {
                added = await m_manager.Runtime.AddUnitAsync(
                    new QualifiedName("ReentrantSealer", m_manager.InstanceNamespaceIndex),
                    new EquipmentProvider(), parent: parent).AsTask().WaitAsync(TimeSpan.FromSeconds(30));
            }
            finally
            {
                parent.OnStateChangedAsync = null;
            }
            Assert.That(rejection, Is.Not.Null);
            Assert.That(m_manager.Runtime.Units.ToList(), Does.Contain(added));
            var request = new PackMLRequest { Operation = PackMLOperation.SetMachSpeed, MachineSpeed = 5 };
            Assert.That(ServiceResult.IsGood(await m_unit.RequestAsync(m_manager.SystemContext, request)), Is.True);
            Assert.That(ServiceResult.IsGood(await added.RequestAsync(m_manager.SystemContext, request)), Is.True);
            Unit next = await m_manager.Runtime.AddUnitAsync(
                new QualifiedName("AfterDisposeReentry", m_manager.InstanceNamespaceIndex), new EquipmentProvider());
            Assert.That(m_manager.Runtime.Units.ToList(), Does.Contain(next));
        }

        [Test]
        public async Task RuntimeDisposeFromOwnedCallbackLeavesEveryUnitAndAdmissionOpen(
            [Values(0, 1, 2, 3, 4)] int callbackPhase)
        {
            var provider = new EquipmentProvider { CallbackPhase = callbackPhase };
            var store = new MemoryAdministrationStore();
            Unit owned = await m_manager.Runtime.AddUnitAsync(
                new QualifiedName("OwnedCallback", m_manager.InstanceNamespaceIndex), provider,
                options => options.AdministrationStore = store);
            InvalidOperationException? rejection = null;
            async ValueTask AttemptDisposalAsync(CancellationToken token)
            {
                rejection = await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await m_manager.Runtime.DisposeAsync().AsTask()
                        .WaitAsync(TimeSpan.FromSeconds(30), token));
                Assert.That(token.IsCancellationRequested, Is.False);
            }
            provider.Callback = AttemptDisposalAsync;
            store.SaveCallback = AttemptDisposalAsync;
            BaseVariableState setpoint = owned.Node.Status.Required().MachSpeed.Required();
            if (callbackPhase == 4)
            {
                setpoint.OnStateChangedAsync = (_, _, _, token) => AttemptDisposalAsync(token);
            }
            var request = new PackMLRequest { Operation = PackMLOperation.SetMachSpeed, MachineSpeed = 5 };
            try
            {
                if (callbackPhase == 3)
                {
                    await owned.CheckpointAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30));
                }
                else if (callbackPhase == 2)
                {
                    await owned.RefreshAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30));
                }
                else
                {
                    Assert.That(ServiceResult.IsGood(await owned.RequestAsync(
                        m_manager.SystemContext, request).AsTask().WaitAsync(TimeSpan.FromSeconds(30))), Is.True);
                }
            }
            finally
            {
                provider.Callback = null;
                store.SaveCallback = null;
                setpoint.OnStateChangedAsync = null;
            }
            Assert.That(rejection, Is.Not.Null);
            Assert.That(ServiceResult.IsGood(await owned.RequestAsync(m_manager.SystemContext, request)), Is.True);
            Assert.That(ServiceResult.IsGood(await m_unit.RequestAsync(m_manager.SystemContext, request)), Is.True);
            Unit next = await m_manager.Runtime.AddUnitAsync(
                new QualifiedName("AfterOwnedCallback", m_manager.InstanceNamespaceIndex), new EquipmentProvider());
            Assert.That(m_manager.Runtime.Units.ToList(), Does.Contain(next));
        }

        [Test]
        public async Task RuntimeDisposeFromPendingUnitCallbackLeavesAdmissionOpen()
        {
            var parent = new BaseObjectState(null)
            {
                NodeId = new NodeId("PendingDispose", m_manager.InstanceNamespaceIndex),
                BrowseName = new QualifiedName("PendingDispose", m_manager.InstanceNamespaceIndex),
                DisplayName = new LocalizedText("PendingDispose"),
                TypeDefinitionId = Ua.ObjectTypeIds.BaseObjectType
            };
            await m_manager.AddPredefinedNodeAsync(parent);
            var parentEntered = PackMLTestSupport.Signal();
            var parentRelease = PackMLTestSupport.Signal();
            parent.OnStateChangedAsync = async (_, _, _, _) =>
            {
                parentEntered.TrySetResult(true);
                await parentRelease.Task.ConfigureAwait(false);
            };
            InvalidOperationException? rejection = null;
            var provider = new EquipmentProvider
            {
                Execute = async (_, token) =>
                {
                    rejection = await Assert.ThrowsAsync<InvalidOperationException>(
                        async () => await m_manager.Runtime.DisposeAsync().AsTask()
                            .WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None));
                    Assert.That(token.IsCancellationRequested, Is.False);
                    return ServiceResult.Good;
                }
            };
            Task<Unit> adding = m_manager.Runtime.AddUnitAsync(
                new QualifiedName("PendingUnit", m_manager.InstanceNamespaceIndex), provider, parent: parent).AsTask();
            try
            {
                await parentEntered.Task.WaitAsync(TimeSpan.FromSeconds(30));
                var children = new List<BaseInstanceState>();
                parent.GetChildren(m_manager.SystemContext, children);
                PackMLBaseObjectState pending = children.OfType<PackMLBaseObjectState>().Single();
                Assert.That(m_manager.Runtime.Units.ToList().Exists(unit => ReferenceEquals(unit.Node, pending)),
                    Is.False);
                ServiceResult result = await pending.SetMachSpeed.Required().CallAsync(
                    m_manager.SystemContext, pending.NodeId, new[] { Variant.From(10f) }.ToArrayOf(),
                    new List<ServiceResult>(), new List<Variant>()).AsTask().WaitAsync(TimeSpan.FromSeconds(30));
                Assert.That(ServiceResult.IsGood(result), Is.True);
                Assert.That(adding.IsCompleted, Is.False);
            }
            finally
            {
                parentRelease.TrySetResult(true);
                parent.OnStateChangedAsync = null;
            }

            Unit added = await adding.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.That(rejection, Is.Not.Null);
            Assert.That(m_manager.Runtime.Units.ToList(), Does.Contain(added));
            var request = new PackMLRequest { Operation = PackMLOperation.SetMachSpeed, MachineSpeed = 5 };
            Assert.That(ServiceResult.IsGood(await m_unit.RequestAsync(m_manager.SystemContext, request)), Is.True);
            Unit next = await m_manager.Runtime.AddUnitAsync(
                new QualifiedName("AfterPendingDispose", m_manager.InstanceNamespaceIndex), new EquipmentProvider());
            Assert.That(m_manager.Runtime.Units.ToList(), Does.Contain(next));
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

        /// <summary>
        /// Verifies a parentless unit in an ordinary node manager reaches Server-object event subscribers,
        /// whether the subscription exists before or after the unit is registered.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task ComposedParentlessUnitEventsReachServerObjectSubscribers(bool subscribeFirst)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            CancellationToken ct = deadline.Token;
            var runtime = new PackMLRuntime(m_composed);
            uint subscriptionId = 0;
            try
            {
                if (subscribeFirst)
                {
                    subscriptionId = await SubscribeToServerEventsAsync(ct).ConfigureAwait(false);
                }
                Unit unit = await runtime.AddUnitAsync(
                    new QualifiedName("ComposedEvents", m_composed.NamespaceIndexes[0]), new EquipmentProvider(),
                    cancellationToken: ct).ConfigureAwait(false);
                Assert.That(m_composed.Server.ServerObject.ReferenceExists(
                    Ua.ReferenceTypeIds.HasNotifier, false, unit.Node.NodeId), Is.True);
                if (!subscribeFirst)
                {
                    subscriptionId = await SubscribeToServerEventsAsync(ct).ConfigureAwait(false);
                }
                HashSet<NodeId> sources = CollectSubtree(m_composed.SystemContext, unit.Node);

                ServiceResult reset = await unit.RequestAsync(
                    m_composed.SystemContext,
                    new PackMLRequest { Operation = PackMLOperation.Command, Command = PackMLCommand.Reset },
                    ct).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(reset), Is.True, reset.ToString());

                NodeId source = await PublishUnitEventAsync(subscriptionId, sources, ct).ConfigureAwait(false);
                Assert.That(sources, Does.Contain(source));
            }
            finally
            {
                if (subscriptionId != 0)
                {
                    await m_session.DeleteSubscriptionsAsync(null, [subscriptionId], CancellationToken.None)
                        .ConfigureAwait(false);
                }
                await runtime.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies a failed root registration deletes the materialized unit together with its partial
        /// Server registration, and that the same unit can be composed afterwards.
        /// </summary>
        [Test]
        public async Task ComposedUnitRootRegistrationFailureRollsBackUnit()
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            CancellationToken ct = deadline.Token;
            var runtime = new PackMLRuntime(m_composed);
            var fault = new InvalidOperationException("Root registration failed.");
            var browseName = new QualifiedName("ComposedRollback", m_composed.NamespaceIndexes[0]);
            var materialized = new List<NodeId>();
            try
            {
                m_composed.RegistrationFault = fault;
                m_composed.Registered = materialized.Add;
                Exception? failure = await Assert.CatchAsync(async () => await runtime.AddUnitAsync(
                    browseName, new EquipmentProvider(), cancellationToken: ct).ConfigureAwait(false))
                    .ConfigureAwait(false);
                m_composed.RegistrationFault = null;
                m_composed.Registered = null;

                Assert.That(failure, Is.SameAs(fault));
                Assert.That(materialized, Has.Count.EqualTo(1));
                NodeId rolledBack = materialized[0];
                Assert.That(runtime.Units.Count, Is.Zero);
                Assert.That(m_composed.Find(rolledBack), Is.Null);
                Assert.That(m_composed.Server.ServerObject.ReferenceExists(
                    Ua.ReferenceTypeIds.HasNotifier, false, rolledBack), Is.False);

                Unit unit = await runtime.AddUnitAsync(browseName, new EquipmentProvider(), cancellationToken: ct)
                    .ConfigureAwait(false);
                Assert.That(runtime.Units.Count, Is.EqualTo(1));
                Assert.That(m_composed.Find(unit.Node.NodeId), Is.SameAs(unit.Node));
                Assert.That(m_composed.Server.ServerObject.ReferenceExists(
                    Ua.ReferenceTypeIds.HasNotifier, false, unit.Node.NodeId), Is.True);
            }
            finally
            {
                m_composed.RegistrationFault = null;
                m_composed.Registered = null;
                await runtime.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies deleting a composed parentless unit removes its Server root registration.
        /// </summary>
        [Test]
        public async Task DeletingComposedUnitRemovesServerRootNotifier()
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            CancellationToken ct = deadline.Token;
            var runtime = new PackMLRuntime(m_composed);
            Unit unit;
            try
            {
                unit = await runtime.AddUnitAsync(
                    new QualifiedName("ComposedDeletion", m_composed.NamespaceIndexes[0]), new EquipmentProvider(),
                    cancellationToken: ct).ConfigureAwait(false);
                Assert.That(m_composed.Server.ServerObject.ReferenceExists(
                    Ua.ReferenceTypeIds.HasNotifier, false, unit.Node.NodeId), Is.True);
                Assert.That(unit.Node.OnReportEventAsync, Is.Not.Null);
            }
            finally
            {
                await runtime.DisposeAsync().ConfigureAwait(false);
            }

            Assert.That(await m_composed.DeleteNodeAsync(m_composed.SystemContext, unit.Node.NodeId, ct)
                .ConfigureAwait(false), Is.True);

            Assert.That(m_composed.Find(unit.Node.NodeId), Is.Null);
            Assert.That(m_composed.Server.ServerObject.ReferenceExists(
                Ua.ReferenceTypeIds.HasNotifier, false, unit.Node.NodeId), Is.False);
            Assert.That(unit.Node.OnReportEventAsync, Is.Null);
        }

        /// <summary>
        /// Keeps a prepared unit's external references private until publication or abort.
        /// </summary>
        /// <param name="publish">Whether to publish the prepared manager.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task PreparedPackMLReferencesRemainPrivateUntilPublicationOrAbort(bool publish)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            CancellationToken ct = deadline.Token;
            INodeManagerLifecycle lifecycle = m_services.GetRequiredService<INodeManagerLifecycle>();
            var core = m_manager.Server.NodeManager.CoreNodeManager as CoreNodeManager
                ?? throw new InvalidOperationException("The core node manager is unavailable.");
            NodeState objects = core.FindPredefinedNode<NodeState>(Ua.ObjectIds.ObjectsFolder)
                ?? throw new InvalidOperationException("Objects folder is unavailable.");
            NodeState enumeration = core.FindPredefinedNode<NodeState>(Ua.DataTypeIds.Enumeration)
                ?? throw new InvalidOperationException("Enumeration type is unavailable.");
            var factory = new PreparedPackMLManagerFactory(failFirstUnit: false);
            NodeManagerBatchResult? committed = null;
            NodeId unitId;
            NodeId modeId;

            await using (IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(factory)], ct).ConfigureAwait(false))
            {
                PreparedPackMLManager candidate = factory.Manager
                    ?? throw new InvalidOperationException("The candidate manager was not created.");
                unitId = candidate.CreatedUnit.Node.NodeId;
                modeId = candidate.CreatedUnit.ModeDataType.NodeId;

                AssertPackMLExternalReferences(objects, enumeration, unitId, modeId, expected: false);
                if (publish)
                {
                    committed = await prepared.CommitAsync(_ => default, ct).ConfigureAwait(false);
                    Assert.That(committed.CleanupFailure, Is.Null);
                }
            }

            AssertPackMLExternalReferences(objects, enumeration, unitId, modeId, expected: publish);
            if (committed is not null)
            {
                await RetirePackMLManagerAsync(lifecycle, committed, ct).ConfigureAwait(false);
                AssertPackMLExternalReferences(objects, enumeration, unitId, modeId, expected: false);
            }
        }

        /// <summary>
        /// Attaches existing Server-event subscribers when a prepared PackML manager is published.
        /// </summary>
        /// <param name="facade">The facade returned by the candidate factory.</param>
        [TestCase(ManagerFacadeKind.Direct)]
        [TestCase(ManagerFacadeKind.Adapted)]
        [TestCase(ManagerFacadeKind.Forwarded)]
        public async Task PreparedPackMLRootAttachesExistingServerSubscribersAfterPublication(
            ManagerFacadeKind facade)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            CancellationToken ct = deadline.Token;
            INodeManagerLifecycle lifecycle = m_services.GetRequiredService<INodeManagerLifecycle>();
            uint subscriptionId = await SubscribeToServerEventsAsync(ct).ConfigureAwait(false);
            try
            {
                var factory = new PreparedPackMLManagerFactory(failFirstUnit: false, facade: facade);
                await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Add(factory)], ct).ConfigureAwait(false);
                PreparedPackMLManager candidate = factory.Manager
                    ?? throw new InvalidOperationException("The candidate manager was not created.");
                Unit addedAfterPreparation = await candidate.Runtime.AddUnitAsync(
                    new QualifiedName("AfterPreparationWithSubscribers", candidate.NamespaceIndexes[0]),
                    new EquipmentProvider(),
                    cancellationToken: ct).ConfigureAwait(false);
                Unit[] units = [candidate.CreatedUnit, addedAfterPreparation];
                foreach (Unit unit in units)
                {
                    Assert.That(unit.Node.AreEventsMonitored, Is.False);
                }

                var lateRoot = new BaseObjectState(null)
                {
                    NodeId = new NodeId("LateRootWithSubscribers", candidate.NamespaceIndexes[0]),
                    BrowseName = new QualifiedName("LateRootWithSubscribers", candidate.NamespaceIndexes[0]),
                    DisplayName = new LocalizedText("LateRootWithSubscribers"),
                    TypeDefinitionId = Ua.ObjectTypeIds.BaseObjectType,
                    EventNotifier = EventNotifiers.SubscribeToEvents
                };
                await candidate.AddPredefinedNodeAsync(lateRoot, ct).ConfigureAwait(false);

                NodeManagerBatchResult committed = await prepared.CommitAsync(async token =>
                {
                    await candidate.RegisterRootNotifierAsync(lateRoot, token).ConfigureAwait(false);
                    Assert.That(lateRoot.AreEventsMonitored, Is.False);
                    Assert.That(candidate.Server.ServerObject.ReferenceExists(
                        Ua.ReferenceTypeIds.HasNotifier, false, lateRoot.NodeId), Is.False);
                }, ct).ConfigureAwait(false);
                Assert.That(committed.CleanupFailure, Is.Null);
                Assert.That(committed.Issues.Count, Is.Zero);
                Assert.That(lateRoot.AreEventsMonitored, Is.True);
                Assert.That(candidate.Server.ServerObject.ReferenceExists(
                    Ua.ReferenceTypeIds.HasNotifier, false, lateRoot.NodeId), Is.True);
                Unit addedAfterPublication = await candidate.Runtime.AddUnitAsync(
                    new QualifiedName("AfterPublicationWithSubscribers", candidate.NamespaceIndexes[0]),
                    new EquipmentProvider(),
                    cancellationToken: ct).ConfigureAwait(false);
                units = [.. units, addedAfterPublication];
                foreach (Unit unit in units)
                {
                    Assert.That(unit.Node.AreEventsMonitored, Is.True);
                    HashSet<NodeId> sources = CollectSubtree(candidate.SystemContext, unit.Node);
                    ServiceResult reset = await unit.RequestAsync(
                        candidate.SystemContext,
                        new PackMLRequest { Operation = PackMLOperation.Command, Command = PackMLCommand.Reset },
                        ct).ConfigureAwait(false);
                    Assert.That(ServiceResult.IsGood(reset), Is.True, reset.ToString());
                    NodeId source = await PublishUnitEventAsync(subscriptionId, sources, ct).ConfigureAwait(false);
                    Assert.That(sources, Does.Contain(source));
                }
                await RetirePackMLManagerAsync(lifecycle, committed, ct).ConfigureAwait(false);
            }
            finally
            {
                await m_session.DeleteSubscriptionsAsync(null, [subscriptionId], CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Keeps aborted prepared units detached from existing Server-event subscribers.
        /// </summary>
        /// <param name="facade">The facade returned by the candidate factory.</param>
        [TestCase(ManagerFacadeKind.Direct)]
        [TestCase(ManagerFacadeKind.Adapted)]
        [TestCase(ManagerFacadeKind.Forwarded)]
        public async Task AbortedPreparedPackMLRootsDoNotAttachExistingServerSubscribers(ManagerFacadeKind facade)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            CancellationToken ct = deadline.Token;
            INodeManagerLifecycle lifecycle = m_services.GetRequiredService<INodeManagerLifecycle>();
            uint subscriptionId = await SubscribeToServerEventsAsync(ct).ConfigureAwait(false);
            try
            {
                var factory = new PreparedPackMLManagerFactory(failFirstUnit: false, facade: facade);
                Unit[] units;
                await using (IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Add(factory)], ct).ConfigureAwait(false))
                {
                    PreparedPackMLManager candidate = factory.Manager
                        ?? throw new InvalidOperationException("The candidate manager was not created.");
                    Unit addedAfterPreparation = await candidate.Runtime.AddUnitAsync(
                        new QualifiedName("BeforeAbortWithSubscribers", candidate.NamespaceIndexes[0]),
                        new EquipmentProvider(),
                        cancellationToken: ct).ConfigureAwait(false);
                    units = [candidate.CreatedUnit, addedAfterPreparation];
                    foreach (Unit unit in units)
                    {
                        Assert.That(unit.Node.AreEventsMonitored, Is.False);
                        Assert.That(candidate.Server.ServerObject.ReferenceExists(
                            Ua.ReferenceTypeIds.HasNotifier, false, unit.Node.NodeId), Is.False);
                    }
                }

                foreach (Unit unit in units)
                {
                    Assert.That(unit.Node.AreEventsMonitored, Is.False);
                    Assert.That(unit.Node.OnReportEventAsync, Is.Null);
                }
            }
            finally
            {
                await m_session.DeleteSubscriptionsAsync(null, [subscriptionId], CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Does not publish references from a unit rolled back during manager preparation.
        /// </summary>
        [Test]
        public async Task RolledBackPreparedPackMLUnitDoesNotPublishExternalReferences()
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            CancellationToken ct = deadline.Token;
            INodeManagerLifecycle lifecycle = m_services.GetRequiredService<INodeManagerLifecycle>();
            var core = m_manager.Server.NodeManager.CoreNodeManager as CoreNodeManager
                ?? throw new InvalidOperationException("The core node manager is unavailable.");
            NodeState objects = core.FindPredefinedNode<NodeState>(Ua.ObjectIds.ObjectsFolder)
                ?? throw new InvalidOperationException("Objects folder is unavailable.");
            NodeState enumeration = core.FindPredefinedNode<NodeState>(Ua.DataTypeIds.Enumeration)
                ?? throw new InvalidOperationException("Enumeration type is unavailable.");
            var factory = new PreparedPackMLManagerFactory(failFirstUnit: true);

            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(factory)], ct).ConfigureAwait(false);
            PreparedPackMLManager candidate = factory.Manager
                ?? throw new InvalidOperationException("The candidate manager was not created.");
            Assert.That(candidate.FailedUnitId.IsNull, Is.False);
            Assert.That(candidate.FailedModeId.IsNull, Is.False);
            Assert.That(candidate.Find(candidate.FailedUnitId), Is.Null);
            Assert.That(candidate.Find(candidate.FailedModeId), Is.Null);
            AssertPackMLExternalReferences(
                objects, enumeration, candidate.FailedUnitId, candidate.FailedModeId, expected: false);

            NodeManagerBatchResult committed = await prepared.CommitAsync(_ => default, ct).ConfigureAwait(false);
            Assert.That(committed.CleanupFailure, Is.Null);
            AssertPackMLExternalReferences(
                objects, enumeration, candidate.FailedUnitId, candidate.FailedModeId, expected: false);
            AssertPackMLExternalReferences(
                objects, enumeration, candidate.CreatedUnit.Node.NodeId, candidate.CreatedUnit.ModeDataType.NodeId,
                expected: true);
            await RetirePackMLManagerAsync(lifecycle, committed, ct).ConfigureAwait(false);
            AssertPackMLExternalReferences(
                objects, enumeration, candidate.FailedUnitId, candidate.FailedModeId, expected: false);
        }

        /// <summary>
        /// Keeps the serving references when a private replacement rolls back matching node identifiers.
        /// </summary>
        [Test]
        public async Task RolledBackPreparedPackMLUnitKeepsServingReferencesWithMatchingIds()
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            CancellationToken ct = deadline.Token;
            INodeManagerLifecycle lifecycle = m_services.GetRequiredService<INodeManagerLifecycle>();
            var core = m_manager.Server.NodeManager.CoreNodeManager as CoreNodeManager
                ?? throw new InvalidOperationException("The core node manager is unavailable.");
            NodeState objects = core.FindPredefinedNode<NodeState>(Ua.ObjectIds.ObjectsFolder)
                ?? throw new InvalidOperationException("Objects folder is unavailable.");
            NodeState enumeration = core.FindPredefinedNode<NodeState>(Ua.DataTypeIds.Enumeration)
                ?? throw new InvalidOperationException("Enumeration type is unavailable.");
            var originalFactory = new PreparedPackMLManagerFactory(failFirstUnit: false);
            await using IPreparedNodeManagerBatch originalPreparation = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(originalFactory)], ct).ConfigureAwait(false);
            NodeManagerBatchResult original = await originalPreparation.CommitAsync(_ => default, ct)
                .ConfigureAwait(false);
            Assert.That(original.CleanupFailure, Is.Null);
            PreparedPackMLManager serving = originalFactory.Manager
                ?? throw new InvalidOperationException("The serving manager was not created.");
            NodeId unitId = serving.CreatedUnit.Node.NodeId;
            NodeId modeId = serving.CreatedUnit.ModeDataType.NodeId;
            AssertPackMLExternalReferences(objects, enumeration, unitId, modeId, expected: true);

            var replacementFactory = new PreparedPackMLManagerFactory(failFirstUnit: true, failServingUnit: true);
            await using (IPreparedNodeManagerBatch replacement = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Replace(original.Registrations[0], replacementFactory)], ct)
                .ConfigureAwait(false))
            {
                PreparedPackMLManager candidate = replacementFactory.Manager
                    ?? throw new InvalidOperationException("The candidate manager was not created.");
                Assert.That(candidate.FailedUnitId, Is.EqualTo(unitId));
                Assert.That(candidate.FailedModeId, Is.EqualTo(modeId));
                AssertPackMLExternalReferences(objects, enumeration, unitId, modeId, expected: true);
            }

            AssertPackMLExternalReferences(objects, enumeration, unitId, modeId, expected: true);
            await RetirePackMLManagerAsync(lifecycle, original, ct).ConfigureAwait(false);
            AssertPackMLExternalReferences(objects, enumeration, unitId, modeId, expected: false);
        }

        /// <summary>
        /// Discards a unit's captured references when it is deleted before publication.
        /// </summary>
        [Test]
        public async Task PreparedPackMLUnitDeletedBeforeCommitDoesNotPublishReferences()
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            CancellationToken ct = deadline.Token;
            INodeManagerLifecycle lifecycle = m_services.GetRequiredService<INodeManagerLifecycle>();
            var core = m_manager.Server.NodeManager.CoreNodeManager as CoreNodeManager
                ?? throw new InvalidOperationException("The core node manager is unavailable.");
            NodeState objects = core.FindPredefinedNode<NodeState>(Ua.ObjectIds.ObjectsFolder)
                ?? throw new InvalidOperationException("Objects folder is unavailable.");
            NodeState enumeration = core.FindPredefinedNode<NodeState>(Ua.DataTypeIds.Enumeration)
                ?? throw new InvalidOperationException("Enumeration type is unavailable.");
            var factory = new PreparedPackMLManagerFactory(failFirstUnit: false);
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(factory)], ct).ConfigureAwait(false);
            PreparedPackMLManager candidate = factory.Manager
                ?? throw new InvalidOperationException("The candidate manager was not created.");
            NodeId unitId = candidate.CreatedUnit.Node.NodeId;
            NodeId modeId = candidate.CreatedUnit.ModeDataType.NodeId;

            Assert.That(await candidate.DeleteNodeAsync(candidate.SystemContext, unitId, ct).ConfigureAwait(false),
                Is.True);
            Assert.That(await candidate.DeleteNodeAsync(candidate.SystemContext, modeId, ct).ConfigureAwait(false),
                Is.True);
            NodeManagerBatchResult committed = await prepared.CommitAsync(_ => default, ct).ConfigureAwait(false);
            Assert.That(committed.CleanupFailure, Is.Null);
            AssertPackMLExternalReferences(objects, enumeration, unitId, modeId, expected: false);
            Assert.That(candidate.Server.ServerObject.ReferenceExists(
                Ua.ReferenceTypeIds.HasNotifier, false, unitId), Is.False);
            await RetirePackMLManagerAsync(lifecycle, committed, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Publishes and later removes references added after preparation has returned.
        /// </summary>
        [Test]
        public async Task PreparedPackMLUnitAddedAfterPreparationPublishesReferences()
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            CancellationToken ct = deadline.Token;
            INodeManagerLifecycle lifecycle = m_services.GetRequiredService<INodeManagerLifecycle>();
            var core = m_manager.Server.NodeManager.CoreNodeManager as CoreNodeManager
                ?? throw new InvalidOperationException("The core node manager is unavailable.");
            NodeState objects = core.FindPredefinedNode<NodeState>(Ua.ObjectIds.ObjectsFolder)
                ?? throw new InvalidOperationException("Objects folder is unavailable.");
            NodeState enumeration = core.FindPredefinedNode<NodeState>(Ua.DataTypeIds.Enumeration)
                ?? throw new InvalidOperationException("Enumeration type is unavailable.");
            var factory = new PreparedPackMLManagerFactory(failFirstUnit: false);
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(factory)], ct).ConfigureAwait(false);
            PreparedPackMLManager candidate = factory.Manager
                ?? throw new InvalidOperationException("The candidate manager was not created.");
            Unit added = await candidate.Runtime.AddUnitAsync(
                new QualifiedName("AfterPreparation", candidate.NamespaceIndexes[0]),
                new EquipmentProvider(),
                cancellationToken: ct).ConfigureAwait(false);
            NodeId unitId = added.Node.NodeId;
            NodeId modeId = added.ModeDataType.NodeId;

            AssertPackMLExternalReferences(objects, enumeration, unitId, modeId, expected: false);
            Assert.That(candidate.Server.ServerObject.ReferenceExists(
                Ua.ReferenceTypeIds.HasNotifier, false, unitId), Is.False);
            NodeManagerBatchResult committed = await prepared.CommitAsync(_ => default, ct).ConfigureAwait(false);
            Assert.That(committed.CleanupFailure, Is.Null);
            AssertPackMLExternalReferences(objects, enumeration, unitId, modeId, expected: true);
            Assert.That(candidate.Server.ServerObject.ReferenceExists(
                Ua.ReferenceTypeIds.HasNotifier, false, unitId), Is.True);

            await RetirePackMLManagerAsync(lifecycle, committed, ct).ConfigureAwait(false);
            AssertPackMLExternalReferences(objects, enumeration, unitId, modeId, expected: false);
            Assert.That(candidate.Server.ServerObject.ReferenceExists(
                Ua.ReferenceTypeIds.HasNotifier, false, unitId), Is.False);
        }

        /// <summary>
        /// Rejects a staged reference whose external source does not exist when publishing.
        /// </summary>
        [Test]
        public async Task PreparedExternalReferenceRequiresAnAvailableSource()
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            CancellationToken ct = deadline.Token;
            INodeManagerLifecycle lifecycle = m_services.GetRequiredService<INodeManagerLifecycle>();
            var core = m_manager.Server.NodeManager.CoreNodeManager as CoreNodeManager
                ?? throw new InvalidOperationException("The core node manager is unavailable.");
            NodeState objects = core.FindPredefinedNode<NodeState>(Ua.ObjectIds.ObjectsFolder)
                ?? throw new InvalidOperationException("Objects folder is unavailable.");
            NodeState enumeration = core.FindPredefinedNode<NodeState>(Ua.DataTypeIds.Enumeration)
                ?? throw new InvalidOperationException("Enumeration type is unavailable.");
            var factory = new PreparedPackMLManagerFactory(failFirstUnit: false);
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(factory)], ct).ConfigureAwait(false);
            PreparedPackMLManager candidate = factory.Manager
                ?? throw new InvalidOperationException("The candidate manager was not created.");
            Unit unit = candidate.CreatedUnit;
            var missingSource = new NodeId("MissingPreparedSource", candidate.NamespaceIndexes[0]);
            await candidate.AddExternalReferenceAsync(
                unit.Node, missingSource, Ua.ReferenceTypeIds.Organizes, false, ct).ConfigureAwait(false);
            bool decided = false;

            ServiceResultException? failure = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await prepared.CommitAsync(
                    _ =>
                    {
                        decided = true;
                        return default;
                    },
                    ct).ConfigureAwait(false)).ConfigureAwait(false);

            Assert.That(failure!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNodeIdUnknown));
            Assert.That(decided, Is.False);
            AssertPackMLExternalReferences(
                objects, enumeration, unit.Node.NodeId, unit.ModeDataType.NodeId, expected: false);
            Assert.That(candidate.Server.ServerObject.ReferenceExists(
                Ua.ReferenceTypeIds.HasNotifier, false, unit.Node.NodeId), Is.False);
        }

        /// <summary>
        /// Publishes both directions of an external reference and removes it at retirement.
        /// </summary>
        /// <param name="isInverse">Whether the reference on the external source is inverse.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task PreparedExternalReferencePreservesDirection(bool isInverse)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            CancellationToken ct = deadline.Token;
            INodeManagerLifecycle lifecycle = m_services.GetRequiredService<INodeManagerLifecycle>();
            var core = m_manager.Server.NodeManager.CoreNodeManager as CoreNodeManager
                ?? throw new InvalidOperationException("The core node manager is unavailable.");
            NodeState objects = core.FindPredefinedNode<NodeState>(Ua.ObjectIds.ObjectsFolder)
                ?? throw new InvalidOperationException("Objects folder is unavailable.");
            var factory = new PreparedPackMLManagerFactory(failFirstUnit: false);
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(factory)], ct).ConfigureAwait(false);
            PreparedPackMLManager candidate = factory.Manager
                ?? throw new InvalidOperationException("The candidate manager was not created.");
            NodeState target = candidate.CreatedUnit.Node;
            await candidate.AddExternalReferenceAsync(
                target, Ua.ObjectIds.ObjectsFolder, Ua.ReferenceTypeIds.HasComponent, isInverse, ct)
                .ConfigureAwait(false);
            await candidate.AddExternalReferenceAsync(
                target, Ua.ObjectIds.ObjectsFolder, Ua.ReferenceTypeIds.HasComponent, isInverse, ct)
                .ConfigureAwait(false);
            Assert.That(target.ReferenceExists(
                Ua.ReferenceTypeIds.HasComponent, !isInverse, Ua.ObjectIds.ObjectsFolder), Is.True);
            Assert.That(objects.ReferenceExists(
                Ua.ReferenceTypeIds.HasComponent, isInverse, target.NodeId), Is.False);

            NodeManagerBatchResult committed = await prepared.CommitAsync(_ => default, ct).ConfigureAwait(false);
            Assert.That(committed.CleanupFailure, Is.Null);
            Assert.That(objects.ReferenceExists(
                Ua.ReferenceTypeIds.HasComponent, isInverse, target.NodeId), Is.True);
            await RetirePackMLManagerAsync(lifecycle, committed, ct).ConfigureAwait(false);
            Assert.That(objects.ReferenceExists(
                Ua.ReferenceTypeIds.HasComponent, isInverse, target.NodeId), Is.False);
        }

        /// <summary>
        /// Removes a later unit's external references when its manager is retired.
        /// </summary>
        /// <param name="deleteBeforeAddressSpace">Whether teardown deletes the unit before the address space.</param>
        /// <param name="facade">The facade returned by the candidate factory.</param>
        [TestCase(false, ManagerFacadeKind.Direct)]
        [TestCase(true, ManagerFacadeKind.Direct)]
        [TestCase(false, ManagerFacadeKind.Adapted)]
        [TestCase(true, ManagerFacadeKind.Adapted)]
        [TestCase(false, ManagerFacadeKind.Forwarded)]
        [TestCase(true, ManagerFacadeKind.Forwarded)]
        public async Task PublishedPackMLUnitReferencesAreRemovedOnRetirement(
            bool deleteBeforeAddressSpace,
            ManagerFacadeKind facade)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            CancellationToken ct = deadline.Token;
            INodeManagerLifecycle lifecycle = m_services.GetRequiredService<INodeManagerLifecycle>();
            var core = m_manager.Server.NodeManager.CoreNodeManager as CoreNodeManager
                ?? throw new InvalidOperationException("The core node manager is unavailable.");
            NodeState objects = core.FindPredefinedNode<NodeState>(Ua.ObjectIds.ObjectsFolder)
                ?? throw new InvalidOperationException("Objects folder is unavailable.");
            NodeState enumeration = core.FindPredefinedNode<NodeState>(Ua.DataTypeIds.Enumeration)
                ?? throw new InvalidOperationException("Enumeration type is unavailable.");
            var factory = new PreparedPackMLManagerFactory(failFirstUnit: false, facade: facade);
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(factory)], ct).ConfigureAwait(false);
            NodeManagerBatchResult committed = await prepared.CommitAsync(_ => default, ct).ConfigureAwait(false);
            Assert.That(committed.CleanupFailure, Is.Null);
            PreparedPackMLManager candidate = factory.Manager
                ?? throw new InvalidOperationException("The candidate manager was not created.");
            Assert.That(committed.Registrations[0].NodeManager, Is.SameAs(factory.RegisteredManager));
            Assert.That(ReferenceEquals(factory.RegisteredManager, candidate),
                Is.EqualTo(facade == ManagerFacadeKind.Direct));
            Assert.That(factory.RegisteredManager is AsyncNodeManagerAdapter,
                Is.EqualTo(facade == ManagerFacadeKind.Adapted));
            Assert.That(factory.RegisteredManager!.SyncNodeManager, Is.SameAs(candidate.SyncNodeManager));
            Unit added = await candidate.Runtime.AddUnitAsync(
                new QualifiedName("AfterPublication", candidate.NamespaceIndexes[0]),
                new EquipmentProvider(),
                cancellationToken: ct).ConfigureAwait(false);
            NodeId unitId = added.Node.NodeId;
            NodeId modeId = added.ModeDataType.NodeId;
            if (deleteBeforeAddressSpace)
            {
                candidate.UnitToDeleteOnRetirement = added;
            }
            AssertPackMLExternalReferences(objects, enumeration, unitId, modeId, expected: true);
            Assert.That(candidate.Server.ServerObject.ReferenceExists(
                Ua.ReferenceTypeIds.HasNotifier, false, unitId), Is.True);

            await RetirePackMLManagerAsync(lifecycle, committed, ct).ConfigureAwait(false);
            AssertPackMLExternalReferences(objects, enumeration, unitId, modeId, expected: false);
            Assert.That(candidate.Server.ServerObject.ReferenceExists(
                Ua.ReferenceTypeIds.HasNotifier, false, unitId), Is.False);
        }

        /// <summary>
        /// Removes a serving unit's references through its exact registered facade.
        /// </summary>
        /// <param name="facade">The facade returned by the candidate factory.</param>
        /// <param name="addedAfterPublication">Whether the unit is added after its manager is published.</param>
        [TestCase(ManagerFacadeKind.Direct, false)]
        [TestCase(ManagerFacadeKind.Direct, true)]
        [TestCase(ManagerFacadeKind.Adapted, false)]
        [TestCase(ManagerFacadeKind.Adapted, true)]
        [TestCase(ManagerFacadeKind.Forwarded, false)]
        [TestCase(ManagerFacadeKind.Forwarded, true)]
        public async Task LivePackMLUnitDeletionRemovesExternalReferences(
            ManagerFacadeKind facade,
            bool addedAfterPublication)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            CancellationToken ct = deadline.Token;
            INodeManagerLifecycle lifecycle = m_services.GetRequiredService<INodeManagerLifecycle>();
            var core = m_manager.Server.NodeManager.CoreNodeManager as CoreNodeManager
                ?? throw new InvalidOperationException("The core node manager is unavailable.");
            NodeState objects = core.FindPredefinedNode<NodeState>(Ua.ObjectIds.ObjectsFolder)
                ?? throw new InvalidOperationException("Objects folder is unavailable.");
            NodeState enumeration = core.FindPredefinedNode<NodeState>(Ua.DataTypeIds.Enumeration)
                ?? throw new InvalidOperationException("Enumeration type is unavailable.");
            var factory = new PreparedPackMLManagerFactory(failFirstUnit: false, facade: facade);
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(factory)], ct).ConfigureAwait(false);
            NodeManagerBatchResult committed = await prepared.CommitAsync(_ => default, ct).ConfigureAwait(false);
            Assert.That(committed.CleanupFailure, Is.Null);
            PreparedPackMLManager candidate = factory.Manager
                ?? throw new InvalidOperationException("The candidate manager was not created.");
            Assert.That(committed.Registrations[0].NodeManager, Is.SameAs(factory.RegisteredManager));
            Assert.That(ReferenceEquals(factory.RegisteredManager, candidate),
                Is.EqualTo(facade == ManagerFacadeKind.Direct));
            Assert.That(factory.RegisteredManager is AsyncNodeManagerAdapter,
                Is.EqualTo(facade == ManagerFacadeKind.Adapted));
            Assert.That(factory.RegisteredManager!.SyncNodeManager, Is.SameAs(candidate.SyncNodeManager));
            Unit unit = addedAfterPublication
                ? await candidate.Runtime.AddUnitAsync(
                    new QualifiedName("LiveDeletion", candidate.NamespaceIndexes[0]),
                    new EquipmentProvider(),
                    cancellationToken: ct).ConfigureAwait(false)
                : candidate.CreatedUnit;
            NodeId unitId = unit.Node.NodeId;
            NodeId modeId = unit.ModeDataType.NodeId;
            AssertPackMLExternalReferences(objects, enumeration, unitId, modeId, expected: true);
            Assert.That(candidate.Server.ServerObject.ReferenceExists(
                Ua.ReferenceTypeIds.HasNotifier, false, unitId), Is.True);
            Assert.That(unit.Node.OnReportEventAsync, Is.Not.Null);

            Assert.That(await candidate.DeleteNodeAsync(candidate.SystemContext, unitId, ct).ConfigureAwait(false),
                Is.True);
            Assert.That(await candidate.DeleteNodeAsync(candidate.SystemContext, modeId, ct).ConfigureAwait(false),
                Is.True);

            Assert.That(candidate.Find(unitId), Is.Null);
            Assert.That(candidate.Find(modeId), Is.Null);
            AssertPackMLExternalReferences(objects, enumeration, unitId, modeId, expected: false);
            Assert.That(candidate.Server.ServerObject.ReferenceExists(
                Ua.ReferenceTypeIds.HasNotifier, false, unitId), Is.False);
            Assert.That(unit.Node.OnReportEventAsync, Is.Null);
            await RetirePackMLManagerAsync(lifecycle, committed, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Keeps a deleted prepared unit's references removed and cleans up a later unit with the same identifiers.
        /// </summary>
        /// <param name="removeRootFirst">Whether the root's references were already removed.</param>
        /// <param name="facade">The facade returned by the candidate factory.</param>
        [TestCase(false, ManagerFacadeKind.Direct)]
        [TestCase(true, ManagerFacadeKind.Direct)]
        [TestCase(false, ManagerFacadeKind.Adapted)]
        [TestCase(true, ManagerFacadeKind.Adapted)]
        [TestCase(false, ManagerFacadeKind.Forwarded)]
        [TestCase(true, ManagerFacadeKind.Forwarded)]
        public async Task DeletedPreparedUnitReferencesDoNotReturnOrMaskLaterUnit(
            bool removeRootFirst,
            ManagerFacadeKind facade)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            CancellationToken ct = deadline.Token;
            INodeManagerLifecycle lifecycle = m_services.GetRequiredService<INodeManagerLifecycle>();
            var core = m_manager.Server.NodeManager.CoreNodeManager as CoreNodeManager
                ?? throw new InvalidOperationException("The core node manager is unavailable.");
            NodeState objects = core.FindPredefinedNode<NodeState>(Ua.ObjectIds.ObjectsFolder)
                ?? throw new InvalidOperationException("Objects folder is unavailable.");
            NodeState enumeration = core.FindPredefinedNode<NodeState>(Ua.DataTypeIds.Enumeration)
                ?? throw new InvalidOperationException("Enumeration type is unavailable.");
            var factory = new PreparedPackMLManagerFactory(failFirstUnit: false, facade: facade);
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(factory)], ct).ConfigureAwait(false);
            NodeManagerBatchResult committed = await prepared.CommitAsync(_ => default, ct).ConfigureAwait(false);
            Assert.That(committed.CleanupFailure, Is.Null);
            PreparedPackMLManager candidate = factory.Manager
                ?? throw new InvalidOperationException("The candidate manager was not created.");
            Assert.That(committed.Registrations[0].NodeManager, Is.SameAs(factory.RegisteredManager));
            Assert.That(ReferenceEquals(factory.RegisteredManager, candidate),
                Is.EqualTo(facade == ManagerFacadeKind.Direct));
            Assert.That(factory.RegisteredManager is AsyncNodeManagerAdapter,
                Is.EqualTo(facade == ManagerFacadeKind.Adapted));
            Assert.That(factory.RegisteredManager!.SyncNodeManager, Is.SameAs(candidate.SyncNodeManager));
            NodeId unitId = candidate.CreatedUnit.Node.NodeId;
            NodeId modeId = candidate.CreatedUnit.ModeDataType.NodeId;

            Assert.That(candidate.Server.ServerObject.ReferenceExists(
                Ua.ReferenceTypeIds.HasNotifier, false, unitId), Is.True);
            if (removeRootFirst)
            {
                candidate.Server.ServerObject.RemoveReference(Ua.ReferenceTypeIds.HasNotifier, false, unitId);
                candidate.CreatedUnit.Node.RemoveReference(
                    Ua.ReferenceTypeIds.HasNotifier, true, Ua.ObjectIds.Server);
            }

            Assert.That(await candidate.DeleteNodeAsync(candidate.SystemContext, unitId, ct).ConfigureAwait(false),
                Is.True);
            Assert.That(await candidate.DeleteNodeAsync(candidate.SystemContext, modeId, ct).ConfigureAwait(false),
                Is.True);
            AssertPackMLExternalReferences(objects, enumeration, unitId, modeId, expected: false);
            Assert.That(candidate.Server.ServerObject.ReferenceExists(
                Ua.ReferenceTypeIds.HasNotifier, false, unitId), Is.False);

            var otherFactory = new PreparedPackMLManagerFactory(
                failFirstUnit: false, namespaceUri: "urn:opcfoundation:tests:packml:second-prepared");
            await using IPreparedNodeManagerBatch otherPreparation = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(otherFactory)], ct).ConfigureAwait(false);
            NodeManagerBatchResult other = await otherPreparation.CommitAsync(_ => default, ct).ConfigureAwait(false);
            Assert.That(other.CleanupFailure, Is.Null);
            AssertPackMLExternalReferences(objects, enumeration, unitId, modeId, expected: false);
            Assert.That(candidate.Server.ServerObject.ReferenceExists(
                Ua.ReferenceTypeIds.HasNotifier, false, unitId), Is.False);

            await using (var replacementRuntime = new PackMLRuntime(candidate))
            {
                Unit replacement = await replacementRuntime.AddUnitAsync(
                    new QualifiedName("PreparedUnit", candidate.NamespaceIndexes[0]),
                    new EquipmentProvider(),
                    cancellationToken: ct).ConfigureAwait(false);
                Assert.That(replacement.Node.NodeId, Is.EqualTo(unitId));
                Assert.That(replacement.ModeDataType.NodeId, Is.EqualTo(modeId));
                AssertPackMLExternalReferences(objects, enumeration, unitId, modeId, expected: true);
                Assert.That(candidate.Server.ServerObject.ReferenceExists(
                    Ua.ReferenceTypeIds.HasNotifier, false, unitId), Is.True);
            }

            await RetirePackMLManagerAsync(lifecycle, other, ct).ConfigureAwait(false);
            AssertPackMLExternalReferences(objects, enumeration, unitId, modeId, expected: true);
            Assert.That(candidate.Server.ServerObject.ReferenceExists(
                Ua.ReferenceTypeIds.HasNotifier, false, unitId), Is.True);
            await RetirePackMLManagerAsync(lifecycle, committed, ct).ConfigureAwait(false);
            AssertPackMLExternalReferences(objects, enumeration, unitId, modeId, expected: false);
            Assert.That(candidate.Server.ServerObject.ReferenceExists(
                Ua.ReferenceTypeIds.HasNotifier, false, unitId), Is.False);
        }

        /// <summary>
        /// Completes a serving unit's deletion during another batch's decision and removes its type
        /// after the decision commits or aborts.
        /// </summary>
        /// <param name="facade">The facade returned by the candidate factory.</param>
        /// <param name="publish">Whether the deciding batch publishes.</param>
        [TestCase(ManagerFacadeKind.Direct, false)]
        [TestCase(ManagerFacadeKind.Direct, true)]
        [TestCase(ManagerFacadeKind.Adapted, false)]
        [TestCase(ManagerFacadeKind.Adapted, true)]
        [TestCase(ManagerFacadeKind.Forwarded, false)]
        [TestCase(ManagerFacadeKind.Forwarded, true)]
        public async Task LiveDeletionDuringLifecycleDecisionDoesNotWaitForAdmission(
            ManagerFacadeKind facade,
            bool publish)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            CancellationToken ct = deadline.Token;
            INodeManagerLifecycle lifecycle = m_services.GetRequiredService<INodeManagerLifecycle>();
            var core = m_manager.Server.NodeManager.CoreNodeManager as CoreNodeManager
                ?? throw new InvalidOperationException("The core node manager is unavailable.");
            NodeState objects = core.FindPredefinedNode<NodeState>(Ua.ObjectIds.ObjectsFolder)
                ?? throw new InvalidOperationException("Objects folder is unavailable.");
            NodeState enumeration = core.FindPredefinedNode<NodeState>(Ua.DataTypeIds.Enumeration)
                ?? throw new InvalidOperationException("Enumeration type is unavailable.");
            var factory = new PreparedPackMLManagerFactory(
                failFirstUnit: false,
                namespaceUri: "urn:opcfoundation:tests:packml:decision-deletion",
                facade: facade);
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(factory)], ct).ConfigureAwait(false);
            NodeManagerBatchResult committed = await prepared.CommitAsync(_ => default, ct).ConfigureAwait(false);
            Assert.That(committed.CleanupFailure, Is.Null);
            PreparedPackMLManager candidate = factory.Manager
                ?? throw new InvalidOperationException("The candidate manager was not created.");
            Assert.That(committed.Registrations[0].NodeManager, Is.SameAs(factory.RegisteredManager));
            Assert.That(factory.RegisteredManager!.SyncNodeManager, Is.SameAs(candidate.SyncNodeManager));
            NodeId unitId = candidate.CreatedUnit.Node.NodeId;
            NodeId modeId = candidate.CreatedUnit.ModeDataType.NodeId;
            AssertPackMLExternalReferences(objects, enumeration, unitId, modeId, expected: true);
            Assert.That(candidate.Server.ServerObject.ReferenceExists(
                Ua.ReferenceTypeIds.HasNotifier, false, unitId), Is.True);

            var decidingFactory = new ComposedNodeManagerFactory(
                _ => { },
                "urn:opcfoundation:tests:packml:deciding");
            await using IPreparedNodeManagerBatch deciding = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(decidingFactory)], ct).ConfigureAwait(false);
            async ValueTask DecideAsync(CancellationToken token)
            {
                Task<bool> deletion;
                // The deletion runs on a flow without the decision context, as a WoT mutation would.
                using (ExecutionContext.SuppressFlow())
                {
                    deletion = Task.Run(async () =>
                        await candidate.DeleteNodeAsync(candidate.SystemContext, unitId, token)
                            .ConfigureAwait(false) &&
                        await candidate.DeleteNodeAsync(candidate.SystemContext, modeId, token)
                            .ConfigureAwait(false), token);
                }
                Assert.That(
                    await deletion.WaitAsync(TimeSpan.FromSeconds(10), token).ConfigureAwait(false),
                    Is.True);
                AssertPackMLExternalReferences(objects, enumeration, unitId, modeId, expected: false);
                Assert.That(candidate.Server.ServerObject.ReferenceExists(
                    Ua.ReferenceTypeIds.HasNotifier, false, unitId), Is.False);
                Assert.That(candidate.Server.TypeTree.IsKnown(modeId), Is.True);
                if (!publish)
                {
                    throw new IOException("Reject the deciding batch.");
                }
            }

            NodeManagerBatchResult? decided = null;
            if (publish)
            {
                decided = await deciding.CommitAsync(DecideAsync, ct).ConfigureAwait(false);
                Assert.That(decided.CleanupFailure, Is.Null);
            }
            else
            {
                await Assert.ThrowsAsync<IOException>(
                    async () => await deciding.CommitAsync(DecideAsync, ct).ConfigureAwait(false))
                    .ConfigureAwait(false);
            }
            Assert.That(candidate.Find(unitId), Is.Null);
            Assert.That(candidate.Find(modeId), Is.Null);
            Assert.That(() => candidate.Server.TypeTree.IsKnown(modeId), Is.False.After(10000, 20));
            AssertPackMLExternalReferences(objects, enumeration, unitId, modeId, expected: false);
            Assert.That(candidate.Server.ServerObject.ReferenceExists(
                Ua.ReferenceTypeIds.HasNotifier, false, unitId), Is.False);

            if (decided is not null)
            {
                await RetirePackMLManagerAsync(lifecycle, decided, ct).ConfigureAwait(false);
            }
            AssertPackMLExternalReferences(objects, enumeration, unitId, modeId, expected: false);

            await using (var replacementRuntime = new PackMLRuntime(candidate))
            {
                Unit replacement = await replacementRuntime.AddUnitAsync(
                    new QualifiedName("PreparedUnit", candidate.NamespaceIndexes[0]),
                    new EquipmentProvider(),
                    cancellationToken: ct).ConfigureAwait(false);
                Assert.That(replacement.Node.NodeId, Is.EqualTo(unitId));
                AssertPackMLExternalReferences(objects, enumeration, unitId, modeId, expected: true);
            }

            await RetirePackMLManagerAsync(lifecycle, committed, ct).ConfigureAwait(false);
            AssertPackMLExternalReferences(objects, enumeration, unitId, modeId, expected: false);
            Assert.That(candidate.Server.ServerObject.ReferenceExists(
                Ua.ReferenceTypeIds.HasNotifier, false, unitId), Is.False);
        }

        /// <summary>
        /// Removes a deleted unit's references from source nodes that a pending publication reserved,
        /// once that publication's decision completes.
        /// </summary>
        /// <param name="facade">The facade returned by the candidate factory.</param>
        /// <param name="publish">Whether the deciding batch publishes.</param>
        [TestCase(ManagerFacadeKind.Direct, false)]
        [TestCase(ManagerFacadeKind.Direct, true)]
        [TestCase(ManagerFacadeKind.Adapted, false)]
        [TestCase(ManagerFacadeKind.Adapted, true)]
        [TestCase(ManagerFacadeKind.Forwarded, false)]
        [TestCase(ManagerFacadeKind.Forwarded, true)]
        public async Task LiveDeletionOfReservedSourceReferencesCompletesAfterDecision(
            ManagerFacadeKind facade,
            bool publish)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            CancellationToken ct = deadline.Token;
            INodeManagerLifecycle lifecycle = m_services.GetRequiredService<INodeManagerLifecycle>();
            var core = m_manager.Server.NodeManager.CoreNodeManager as CoreNodeManager
                ?? throw new InvalidOperationException("The core node manager is unavailable.");
            NodeState objects = core.FindPredefinedNode<NodeState>(Ua.ObjectIds.ObjectsFolder)
                ?? throw new InvalidOperationException("Objects folder is unavailable.");
            NodeState enumeration = core.FindPredefinedNode<NodeState>(Ua.DataTypeIds.Enumeration)
                ?? throw new InvalidOperationException("Enumeration type is unavailable.");
            var factory = new PreparedPackMLManagerFactory(
                failFirstUnit: false,
                namespaceUri: "urn:opcfoundation:tests:packml:reserved-deletion",
                facade: facade);
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(factory)], ct).ConfigureAwait(false);
            NodeManagerBatchResult committed = await prepared.CommitAsync(_ => default, ct).ConfigureAwait(false);
            Assert.That(committed.CleanupFailure, Is.Null);
            PreparedPackMLManager candidate = factory.Manager
                ?? throw new InvalidOperationException("The candidate manager was not created.");
            NodeId unitId = candidate.CreatedUnit.Node.NodeId;
            NodeId modeId = candidate.CreatedUnit.ModeDataType.NodeId;
            AssertPackMLExternalReferences(objects, enumeration, unitId, modeId, expected: true);

            // The deciding candidate adds its own unit to the Objects folder and Enumeration type,
            // so both source nodes are reserved while its decision is pending.
            var decidingFactory = new PreparedPackMLManagerFactory(
                failFirstUnit: false,
                namespaceUri: "urn:opcfoundation:tests:packml:reserving");
            await using IPreparedNodeManagerBatch deciding = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(decidingFactory)], ct).ConfigureAwait(false);
            async ValueTask DecideAsync(CancellationToken token)
            {
                Task<bool> deletion;
                using (ExecutionContext.SuppressFlow())
                {
                    deletion = Task.Run(async () =>
                        await candidate.DeleteNodeAsync(candidate.SystemContext, unitId, token)
                            .ConfigureAwait(false) &&
                        await candidate.DeleteNodeAsync(candidate.SystemContext, modeId, token)
                            .ConfigureAwait(false), token);
                }
                Assert.That(
                    await deletion.WaitAsync(TimeSpan.FromSeconds(10), token).ConfigureAwait(false),
                    Is.True);
                Assert.That(candidate.Server.TypeTree.IsKnown(modeId), Is.True);
                if (!publish)
                {
                    throw new IOException("Reject the deciding batch.");
                }
            }

            NodeManagerBatchResult? decided = null;
            if (publish)
            {
                decided = await deciding.CommitAsync(DecideAsync, ct).ConfigureAwait(false);
                Assert.That(decided.CleanupFailure, Is.Null);
            }
            else
            {
                await Assert.ThrowsAsync<IOException>(
                    async () => await deciding.CommitAsync(DecideAsync, ct).ConfigureAwait(false))
                    .ConfigureAwait(false);
            }
            Assert.That(candidate.Find(unitId), Is.Null);
            Assert.That(candidate.Find(modeId), Is.Null);
            Assert.That(() => candidate.Server.TypeTree.IsKnown(modeId), Is.False.After(10000, 20));
            Assert.That(
                () => objects.ReferenceExists(Ua.ReferenceTypeIds.Organizes, false, unitId),
                Is.False.After(10000, 20));
            Assert.That(
                () => enumeration.ReferenceExists(Ua.ReferenceTypeIds.HasSubtype, false, modeId),
                Is.False.After(10000, 20));
            Assert.That(candidate.Server.ServerObject.ReferenceExists(
                Ua.ReferenceTypeIds.HasNotifier, false, unitId), Is.False);

            if (decided is not null)
            {
                await RetirePackMLManagerAsync(lifecycle, decided, ct).ConfigureAwait(false);
            }
            AssertPackMLExternalReferences(objects, enumeration, unitId, modeId, expected: false);
            await RetirePackMLManagerAsync(lifecycle, committed, ct).ConfigureAwait(false);
            AssertPackMLExternalReferences(objects, enumeration, unitId, modeId, expected: false);
        }

        /// <summary>
        /// Reports a provider failure that is not a reservation conflict to the deleting caller and keeps
        /// the contribution for retirement instead of dropping it.
        /// </summary>
        [Test]
        public async Task LiveDeletionWithUnrelatedSourceFailureKeepsContributionForRetirement()
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            CancellationToken ct = deadline.Token;
            INodeManagerLifecycle lifecycle = m_services.GetRequiredService<INodeManagerLifecycle>();
            FolderState source = await AddComposedSourceFolderAsync("UnrelatedFailureSource", ct)
                .ConfigureAwait(false);
            var factory = new ExternalProbeManagerFactory(
                "urn:opcfoundation:tests:packml:unrelated-failure", source.NodeId);
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(factory)], ct).ConfigureAwait(false);
            NodeManagerBatchResult committed = await prepared.CommitAsync(_ => default, ct).ConfigureAwait(false);
            Assert.That(committed.CleanupFailure, Is.Null);
            ExternalProbeManager probe = factory.Manager
                ?? throw new InvalidOperationException("The probe manager was not created.");
            NodeId probeId = probe.ProbeId;
            Assert.That(source.ReferenceExists(Ua.ReferenceTypeIds.Organizes, false, probeId), Is.True);

            var decidingFactory = new ComposedNodeManagerFactory(
                _ => { },
                "urn:opcfoundation:tests:packml:unrelated-deciding");
            await using IPreparedNodeManagerBatch deciding = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(decidingFactory)], ct).ConfigureAwait(false);
            NodeManagerBatchResult decided = await deciding.CommitAsync(async token =>
            {
                m_composed.FailReferenceDeletes = true;
                try
                {
                    Task<bool> deletion;
                    using (ExecutionContext.SuppressFlow())
                    {
                        deletion = Task.Run(async () =>
                            await probe.DeleteNodeAsync(probe.SystemContext, probeId, token)
                                .ConfigureAwait(false), token);
                    }
                    InvalidOperationException? failure = null;
                    try
                    {
                        await deletion.WaitAsync(TimeSpan.FromSeconds(10), token).ConfigureAwait(false);
                    }
                    catch (InvalidOperationException ex)
                    {
                        failure = ex;
                    }
                    Assert.That(failure, Is.SameAs(m_composed.ReferenceDeleteFault));
                }
                finally
                {
                    m_composed.FailReferenceDeletes = false;
                }
                Assert.That(source.ReferenceExists(Ua.ReferenceTypeIds.Organizes, false, probeId), Is.True);
            }, ct).ConfigureAwait(false);
            Assert.That(decided.CleanupFailure, Is.Null);
            Assert.That(probe.Find(probeId), Is.Null);
            Assert.That(m_composed.FailedReferenceDeletes, Is.EqualTo(1));
            Assert.That(source.ReferenceExists(Ua.ReferenceTypeIds.Organizes, false, probeId), Is.True);

            await RetirePackMLManagerAsync(lifecycle, committed, ct).ConfigureAwait(false);
            Assert.That(source.ReferenceExists(Ua.ReferenceTypeIds.Organizes, false, probeId), Is.False);
            await RetirePackMLManagerAsync(lifecycle, decided, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Keeps a deferred removal queued when its drain fails and completes it after a later
        /// publication restarts the drain.
        /// </summary>
        [Test]
        public async Task DeferredReservedRemovalRetriesAfterDrainFault()
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            CancellationToken ct = deadline.Token;
            INodeManagerLifecycle lifecycle = m_services.GetRequiredService<INodeManagerLifecycle>();
            FolderState source = await AddComposedSourceFolderAsync("DrainFaultSource", ct)
                .ConfigureAwait(false);
            var factory = new ExternalProbeManagerFactory(
                "urn:opcfoundation:tests:packml:drain-fault-1", source.NodeId);
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(factory)], ct).ConfigureAwait(false);
            NodeManagerBatchResult committed = await prepared.CommitAsync(_ => default, ct).ConfigureAwait(false);
            Assert.That(committed.CleanupFailure, Is.Null);
            ExternalProbeManager probe = factory.Manager
                ?? throw new InvalidOperationException("The probe manager was not created.");
            NodeId probeId = probe.ProbeId;
            Assert.That(source.ReferenceExists(Ua.ReferenceTypeIds.Organizes, false, probeId), Is.True);

            // The deciding candidate references the same source folder, so the folder is reserved
            // while its decision is pending and the deletion's removal is queued.
            var decidingFactory = new ExternalProbeManagerFactory(
                "urn:opcfoundation:tests:packml:drain-fault-2", source.NodeId);
            await using IPreparedNodeManagerBatch deciding = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(decidingFactory)], ct).ConfigureAwait(false);
            NodeManagerBatchResult decided = await deciding.CommitAsync(async token =>
            {
                Task<bool> deletion;
                using (ExecutionContext.SuppressFlow())
                {
                    deletion = Task.Run(async () =>
                        await probe.DeleteNodeAsync(probe.SystemContext, probeId, token)
                            .ConfigureAwait(false), token);
                }
                Assert.That(
                    await deletion.WaitAsync(TimeSpan.FromSeconds(10), token).ConfigureAwait(false),
                    Is.True);
                m_composed.FailReferenceDeletes = true;
            }, ct).ConfigureAwait(false);
            try
            {
                Assert.That(decided.CleanupFailure, Is.Null);
                Assert.That(probe.Find(probeId), Is.Null);
                Assert.That(() => m_composed.FailedReferenceDeletes, Is.GreaterThan(0).After(10000, 20));
                Assert.That(source.ReferenceExists(Ua.ReferenceTypeIds.Organizes, false, probeId), Is.True);
            }
            finally
            {
                m_composed.FailReferenceDeletes = false;
            }

            // Retiring the deciding candidate publishes a view, which restarts the faulted drain.
            await RetirePackMLManagerAsync(lifecycle, decided, ct).ConfigureAwait(false);
            Assert.That(
                () => source.ReferenceExists(Ua.ReferenceTypeIds.Organizes, false, probeId),
                Is.False.After(10000, 20));
            await RetirePackMLManagerAsync(lifecycle, committed, ct).ConfigureAwait(false);
            Assert.That(source.ReferenceExists(Ua.ReferenceTypeIds.Organizes, false, probeId), Is.False);
        }

        /// <summary>
        /// Services a deletion's drain request made while another removal is about to fail.
        /// </summary>
        [Test]
        public async Task DeferredRemovalRequestedDuringFaultedPassIsRetriedWithoutAnotherMutation()
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            CancellationToken ct = deadline.Token;
            INodeManagerLifecycle lifecycle = m_services.GetRequiredService<INodeManagerLifecycle>();
            FolderState source = await AddComposedSourceFolderAsync("DrainHandoffSource", ct).ConfigureAwait(false);
            var firstFactory = new ExternalProbeManagerFactory(
                "urn:opcfoundation:tests:packml:drain-handoff-1", source.NodeId);
            await using IPreparedNodeManagerBatch firstPrepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(firstFactory)], ct).ConfigureAwait(false);
            NodeManagerBatchResult first = await firstPrepared.CommitAsync(_ => default, ct).ConfigureAwait(false);
            Assert.That(first.CleanupFailure, Is.Null);
            ExternalProbeManager firstProbe = firstFactory.Manager
                ?? throw new InvalidOperationException("The first probe manager was not created.");

            var secondFactory = new ExternalProbeManagerFactory(
                "urn:opcfoundation:tests:packml:drain-handoff-2", source.NodeId);
            await using IPreparedNodeManagerBatch secondPrepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(secondFactory)], ct).ConfigureAwait(false);
            NodeManagerBatchResult second = await secondPrepared.CommitAsync(_ => default, ct).ConfigureAwait(false);
            Assert.That(second.CleanupFailure, Is.Null);
            ExternalProbeManager secondProbe = secondFactory.Manager
                ?? throw new InvalidOperationException("The second probe manager was not created.");
            Assert.That(source.ReferenceExists(Ua.ReferenceTypeIds.Organizes, false, firstProbe.ProbeId), Is.True);
            Assert.That(source.ReferenceExists(Ua.ReferenceTypeIds.Organizes, false, secondProbe.ProbeId), Is.True);

            var entered = PackMLTestSupport.Signal();
            var release = PackMLTestSupport.Signal();
            var removed = PackMLTestSupport.Signal();
            int attempts = 0;
            var decidingFactory = new ExternalProbeManagerFactory(
                "urn:opcfoundation:tests:packml:drain-handoff-deciding", source.NodeId);
            await using IPreparedNodeManagerBatch deciding = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(decidingFactory)], ct).ConfigureAwait(false);
            NodeManagerBatchResult decided;
            try
            {
                decided = await deciding.CommitAsync(async token =>
                {
                    Task<bool> deletion;
                    using (ExecutionContext.SuppressFlow())
                    {
                        deletion = Task.Run(async () =>
                            await firstProbe.DeleteNodeAsync(firstProbe.SystemContext, firstProbe.ProbeId, token)
                                .ConfigureAwait(false), token);
                    }
                    Assert.That(await deletion.WaitAsync(token).ConfigureAwait(false), Is.True);
                    Assert.That(
                        source.ReferenceExists(Ua.ReferenceTypeIds.Organizes, false, firstProbe.ProbeId), Is.True);

                    // The initial deletion hit the reserved source. Only its post-publication retry
                    // reaches this barrier, with mutation admission held by the drain.
                    m_composed.BeforeReferenceDeleteAsync = async (target, cancellationToken) =>
                    {
                        if (target == firstProbe.ProbeId && Interlocked.Increment(ref attempts) == 1)
                        {
                            entered.TrySetResult(true);
                            await release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                            throw m_composed.ReferenceDeleteFault;
                        }
                    };
                    m_composed.ReferenceDeleted = target =>
                    {
                        if (target == firstProbe.ProbeId)
                        {
                            removed.TrySetResult(true);
                        }
                    };
                }, ct).ConfigureAwait(false);
                Assert.That(decided.CleanupFailure, Is.Null);
                await entered.Task.WaitAsync(ct).ConfigureAwait(false);

                // This deletion cannot enter admission. Its finally requests a drain while the
                // existing drain is still inside the barrier, before that pass returns Faulted.
                Task<bool> secondDeletion;
                using (ExecutionContext.SuppressFlow())
                {
                    secondDeletion = Task.Run(async () =>
                        await secondProbe.DeleteNodeAsync(secondProbe.SystemContext, secondProbe.ProbeId, ct)
                            .ConfigureAwait(false), ct);
                }
                Assert.That(await secondDeletion.WaitAsync(ct).ConfigureAwait(false), Is.True);
                Assert.That(source.ReferenceExists(
                    Ua.ReferenceTypeIds.Organizes, false, secondProbe.ProbeId), Is.False);
                Assert.That(removed.Task.IsCompleted, Is.False);
                Assert.That(Volatile.Read(ref attempts), Is.EqualTo(1));

                release.TrySetResult(true);
                await removed.Task.WaitAsync(ct).ConfigureAwait(false);
                Assert.That(Volatile.Read(ref attempts), Is.EqualTo(2));
                Assert.That(source.ReferenceExists(
                    Ua.ReferenceTypeIds.Organizes, false, firstProbe.ProbeId), Is.False);
                Assert.That(firstProbe.Find(firstProbe.ProbeId), Is.Null);
                Assert.That(secondProbe.Find(secondProbe.ProbeId), Is.Null);
            }
            finally
            {
                release.TrySetResult(true);
                m_composed.BeforeReferenceDeleteAsync = null;
                m_composed.ReferenceDeleted = null;
            }

            await RetirePackMLManagerAsync(lifecycle, decided, ct).ConfigureAwait(false);
            await RetirePackMLManagerAsync(lifecycle, second, ct).ConfigureAwait(false);
            await RetirePackMLManagerAsync(lifecycle, first, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Does not transfer a staged external reference to an unrelated replacement with the same NodeId.
        /// </summary>
        [Test]
        public async Task ReplacedPreparedPackMLNodeDoesNotInheritStagedReference()
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            CancellationToken ct = deadline.Token;
            INodeManagerLifecycle lifecycle = m_services.GetRequiredService<INodeManagerLifecycle>();
            var core = m_manager.Server.NodeManager.CoreNodeManager as CoreNodeManager
                ?? throw new InvalidOperationException("The core node manager is unavailable.");
            NodeState objects = core.FindPredefinedNode<NodeState>(Ua.ObjectIds.ObjectsFolder)
                ?? throw new InvalidOperationException("Objects folder is unavailable.");
            NodeState enumeration = core.FindPredefinedNode<NodeState>(Ua.DataTypeIds.Enumeration)
                ?? throw new InvalidOperationException("Enumeration type is unavailable.");
            var factory = new PreparedPackMLManagerFactory(failFirstUnit: false);
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(factory)], ct).ConfigureAwait(false);
            PreparedPackMLManager candidate = factory.Manager
                ?? throw new InvalidOperationException("The candidate manager was not created.");
            NodeState original = candidate.CreatedUnit.Node;
            NodeId modeId = candidate.CreatedUnit.ModeDataType.NodeId;
            var replacement = new BaseObjectState(null)
            {
                NodeId = original.NodeId,
                BrowseName = original.BrowseName,
                DisplayName = original.DisplayName,
                TypeDefinitionId = Ua.ObjectTypeIds.BaseObjectType
            };
            await candidate.AddPredefinedNodeAsync(replacement, ct).ConfigureAwait(false);
            Assert.That(candidate.Find(original.NodeId), Is.SameAs(replacement));

            NodeManagerBatchResult committed = await prepared.CommitAsync(_ => default, ct).ConfigureAwait(false);
            Assert.That(committed.CleanupFailure, Is.Null);
            Assert.That(objects.ReferenceExists(
                Ua.ReferenceTypeIds.Organizes, false, original.NodeId), Is.False);
            Assert.That(enumeration.ReferenceExists(
                Ua.ReferenceTypeIds.HasSubtype, false, modeId), Is.True);
            await RetirePackMLManagerAsync(lifecycle, committed, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Checks the serving Objects-folder and Enumeration references for one unit.
        /// </summary>
        private static void AssertPackMLExternalReferences(
            NodeState objects,
            NodeState enumeration,
            NodeId unitId,
            NodeId modeId,
            bool expected)
        {
            Assert.That(objects.ReferenceExists(Ua.ReferenceTypeIds.Organizes, false, unitId), Is.EqualTo(expected));
            Assert.That(
                enumeration.ReferenceExists(Ua.ReferenceTypeIds.HasSubtype, false, modeId), Is.EqualTo(expected));
        }

        /// <summary>
        /// Retires a committed manager and waits for its address space to be deleted.
        /// </summary>
        private static async Task RetirePackMLManagerAsync(
            INodeManagerLifecycle lifecycle,
            NodeManagerBatchResult committed,
            CancellationToken ct)
        {
            await using IPreparedNodeManagerBatch removal = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Remove(committed.Registrations[0])], ct).ConfigureAwait(false);
            NodeManagerBatchResult removed = await removal.CommitAsync(_ => default, ct).ConfigureAwait(false);
            Assert.That(removed.CleanupFailure, Is.Null);
            await removed.Retirement.WaitAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Adds a serving folder to the composed manager for use as an external reference source.
        /// </summary>
        private async Task<FolderState> AddComposedSourceFolderAsync(string name, CancellationToken ct)
        {
            ushort namespaceIndex = m_composed.NamespaceIndexes[0];
            var source = new FolderState(null)
            {
                NodeId = new NodeId(name, namespaceIndex),
                BrowseName = new QualifiedName(name, namespaceIndex),
                DisplayName = new LocalizedText(name),
                TypeDefinitionId = Ua.ObjectTypeIds.FolderType
            };
            await m_composed.AddPredefinedNodeAsync(source, ct).ConfigureAwait(false);
            return source;
        }

        private async Task<uint> SubscribeToServerEventsAsync(CancellationToken ct)
        {
            CreateSubscriptionResponse subscription = await m_session.CreateSubscriptionAsync(
                null, 100, 100, 10, 0, true, 0, ct).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(subscription.ResponseHeader.ServiceResult), Is.True);
            CreateMonitoredItemsResponse response = await m_session.CreateMonitoredItemsAsync(
                null,
                subscription.SubscriptionId,
                TimestampsToReturn.Both,
                [
                    new MonitoredItemCreateRequest
                    {
                        ItemToMonitor = new ReadValueId
                        {
                            NodeId = Ua.ObjectIds.Server,
                            AttributeId = Attributes.EventNotifier
                        },
                        MonitoringMode = MonitoringMode.Reporting,
                        RequestedParameters = new MonitoringParameters
                        {
                            ClientHandle = 1,
                            SamplingInterval = 0,
                            QueueSize = 100,
                            DiscardOldest = true,
                            Filter = new ExtensionObject(new EventFilter
                            {
                                SelectClauses =
                                [
                                    new SimpleAttributeOperand
                                    {
                                        TypeDefinitionId = Ua.ObjectTypeIds.BaseEventType,
                                        BrowsePath = [new QualifiedName(Ua.BrowseNames.SourceNode)],
                                        AttributeId = Attributes.Value
                                    }
                                ]
                            })
                        }
                    }
                ],
                ct).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            return subscription.SubscriptionId;
        }

        private async Task<NodeId> PublishUnitEventAsync(
            uint subscriptionId, HashSet<NodeId> sources, CancellationToken ct)
        {
            ArrayOf<SubscriptionAcknowledgement> acknowledgements = default;
            while (true)
            {
                PublishResponse published = await m_session.PublishAsync(null, acknowledgements, ct)
                    .ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(published.ResponseHeader.ServiceResult), Is.True);
                acknowledgements = published.NotificationMessage.NotificationData.Count == 0
                    ? default
                    : [
                        new SubscriptionAcknowledgement
                        {
                            SubscriptionId = published.SubscriptionId,
                            SequenceNumber = published.NotificationMessage.SequenceNumber
                        }
                    ];
                if (published.SubscriptionId != subscriptionId)
                {
                    continue;
                }
                foreach (ExtensionObject extension in published.NotificationMessage.NotificationData)
                {
                    if (!extension.TryGetValue(out EventNotificationList? events) || events == null)
                    {
                        continue;
                    }
                    foreach (EventFieldList fields in events.Events)
                    {
                        if (fields.EventFields.Count > 0 &&
                            fields.EventFields[0].TryGetValue(out NodeId source) &&
                            sources.Contains(source))
                        {
                            return source;
                        }
                    }
                }
            }
        }

        private static HashSet<NodeId> CollectSubtree(ISystemContext context, NodeState root)
        {
            var nodes = new List<NodeState> { root };
            var ids = new HashSet<NodeId>();
            for (int index = 0; index < nodes.Count; index++)
            {
                ids.Add(nodes[index].NodeId);
                var children = new List<BaseInstanceState>();
                nodes[index].GetChildren(context, children);
                nodes.AddRange(children);
            }
            return ids;
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
                    ServerConfiguration serverConfiguration =
                        configuration.ApplicationConfiguration.ServerConfiguration ??= new ServerConfiguration();
                    serverConfiguration.MaxMonitoredItemCount = 102;
                    serverConfiguration.MaxMonitoredItemsPerSubscription = 102;
                    serverConfiguration.MaxNotificationQueueSize = 5;
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

        /// <summary>
        /// Waits until the hosted server has run its startup tasks and PackML configuration has completed,
        /// surfacing a faulted host instead of waiting for the full timeout.
        /// </summary>
        private static async Task WaitForServerStartAsync(
            IHostedService host,
            ServerStartedSignal started,
            TaskCompletionSource<bool> configured)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            Task startup = host is BackgroundService { ExecuteTask: { } execute }
                ? await Task.WhenAny(started.Task, execute).WaitAsync(timeout.Token)
                : started.Task;
            await startup.WaitAsync(timeout.Token);
            await started.Task.WaitAsync(timeout.Token);
            await configured.Task.WaitAsync(timeout.Token);
        }

        /// <summary>
        /// Completes once the hosted server has started and serves requests.
        /// </summary>
        private sealed class ServerStartedSignal : IServerStartupTask
        {
            /// <summary>
            /// Gets the task that completes when the server has started.
            /// </summary>
            internal Task Task => m_started.Task;

            /// <inheritdoc/>
            public ValueTask OnServerStartedAsync(IServerContext server, CancellationToken cancellationToken = default)
            {
                m_started.TrySetResult(true);
                return default;
            }

            /// <summary>
            /// The started signal.
            /// </summary>
            private readonly TaskCompletionSource<bool> m_started =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        /// <summary>
        /// Creates the ordinary node manager that composes PackML units without owning PackML types.
        /// </summary>
        private sealed class ComposedNodeManagerFactory : IAsyncNodeManagerFactory
        {
            /// <summary>
            /// Initializes the factory with the callback that receives the created manager.
            /// </summary>
            public ComposedNodeManagerFactory(
                Action<ComposedNodeManager> created,
                string namespaceUri = "urn:opcfoundation:tests:packml:composed")
            {
                m_created = created;
                NamespacesUris = [namespaceUri];
            }

            /// <summary>
            /// Gets the namespace owned by the composed manager.
            /// </summary>
            public ArrayOf<string> NamespacesUris { get; }

            /// <inheritdoc/>
            public ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server,
                ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                var manager = new ComposedNodeManager(server, configuration, NamespacesUris[0]);
                m_created(manager);
                return new ValueTask<IAsyncNodeManager>(manager);
            }

            private readonly Action<ComposedNodeManager> m_created;
        }

        /// <summary>
        /// Ordinary asynchronous node manager that can inject a root registration failure
        /// after registration, or coordinate and fail reference deletion.
        /// </summary>
        private sealed class ComposedNodeManager : AsyncCustomNodeManager
        {
            /// <summary>
            /// Initializes the manager for one namespace.
            /// </summary>
            public ComposedNodeManager(
                IServerInternal server,
                ApplicationConfiguration configuration,
                string namespaceUri)
                : base(server, configuration, namespaceUri)
            {
            }

            /// <summary>
            /// Gets or sets the failure thrown after a root notifier is registered.
            /// </summary>
            public Exception? RegistrationFault { get; set; }

            /// <summary>
            /// Gets or sets the callback observing each registered root notifier.
            /// </summary>
            public Action<NodeId>? Registered { get; set; }

            /// <summary>
            /// Gets the failure thrown by reference deletions while <see cref="FailReferenceDeletes"/> is set.
            /// </summary>
            public InvalidOperationException ReferenceDeleteFault { get; } =
                new("Injected reference deletion failure.");

            /// <summary>
            /// Gets or sets whether deleting a reference from one of this manager's nodes fails.
            /// </summary>
            public bool FailReferenceDeletes
            {
                get => Volatile.Read(ref m_failReferenceDeletes);
                set => Volatile.Write(ref m_failReferenceDeletes, value);
            }

            /// <summary>
            /// Gets the number of reference deletions that failed by injection.
            /// </summary>
            public int FailedReferenceDeletes => Volatile.Read(ref m_failedReferenceDeletes);

            /// <summary>
            /// Gets or sets a callback that can pause or fail a reference deletion before it is applied.
            /// </summary>
            public Func<ExpandedNodeId, CancellationToken, ValueTask>? BeforeReferenceDeleteAsync { get; set; }

            /// <summary>
            /// Gets or sets a callback observing a successfully applied reference deletion.
            /// </summary>
            public Action<ExpandedNodeId>? ReferenceDeleted { get; set; }

            /// <inheritdoc/>
            public override async ValueTask<ServiceResult> DeleteReferenceAsync(
                object sourceHandle,
                NodeId referenceTypeId,
                bool isInverse,
                ExpandedNodeId targetId,
                bool deleteBidirectional,
                CancellationToken cancellationToken = default)
            {
                if (BeforeReferenceDeleteAsync is { } beforeDelete)
                {
                    await beforeDelete(targetId, cancellationToken).ConfigureAwait(false);
                }
                if (FailReferenceDeletes)
                {
                    Interlocked.Increment(ref m_failedReferenceDeletes);
                    throw ReferenceDeleteFault;
                }
                ServiceResult result = await base.DeleteReferenceAsync(
                    sourceHandle,
                    referenceTypeId,
                    isInverse,
                    targetId,
                    deleteBidirectional,
                    cancellationToken).ConfigureAwait(false);
                if (ServiceResult.IsGood(result))
                {
                    ReferenceDeleted?.Invoke(targetId);
                }
                return result;
            }

            /// <inheritdoc/>
            protected override async ValueTask AddRootNotifierAsync(
                NodeState notifier,
                CancellationToken cancellationToken = default)
            {
                await base.AddRootNotifierAsync(notifier, cancellationToken).ConfigureAwait(false);
                Registered?.Invoke(notifier.NodeId);
                if (RegistrationFault is { } fault)
                {
                    throw fault;
                }
            }

            private bool m_failReferenceDeletes;
            private int m_failedReferenceDeletes;
        }

        /// <summary>
        /// Creates a candidate whose only node is referenced from a node of another manager.
        /// </summary>
        private sealed class ExternalProbeManagerFactory : IAsyncNodeManagerFactory
        {
            /// <summary>
            /// Initializes the factory with the candidate namespace and the external source node.
            /// </summary>
            /// <param name="namespaceUri">The namespace owned by the candidate.</param>
            /// <param name="sourceId">The external node that organizes the candidate's probe.</param>
            public ExternalProbeManagerFactory(string namespaceUri, NodeId sourceId)
            {
                m_sourceId = sourceId;
                NamespacesUris = [namespaceUri];
            }

            /// <summary>
            /// Gets the candidate manager.
            /// </summary>
            public ExternalProbeManager? Manager { get; private set; }

            /// <inheritdoc/>
            public ArrayOf<string> NamespacesUris { get; }

            /// <inheritdoc/>
            public ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server,
                ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Manager = new ExternalProbeManager(server, configuration, NamespacesUris[0], m_sourceId);
                return new ValueTask<IAsyncNodeManager>(Manager);
            }

            private readonly NodeId m_sourceId;
        }

        /// <summary>
        /// Stages one probe node and its reference from an external source node during preparation.
        /// </summary>
        private sealed class ExternalProbeManager : AsyncCustomNodeManager
        {
            /// <summary>
            /// Initializes the manager for one namespace and the external source node.
            /// </summary>
            public ExternalProbeManager(
                IServerInternal server,
                ApplicationConfiguration configuration,
                string namespaceUri,
                NodeId sourceId)
                : base(server, configuration, namespaceUri)
            {
                m_sourceId = sourceId;
            }

            /// <summary>
            /// Gets the identifier of the probe node.
            /// </summary>
            public NodeId ProbeId => new("Probe", NamespaceIndexes[0]);

            /// <inheritdoc/>
            public override async ValueTask CreateAddressSpaceAsync(
                IDictionary<NodeId, IList<IReference>> externalReferences,
                CancellationToken cancellationToken = default)
            {
                await base.CreateAddressSpaceAsync(externalReferences, cancellationToken).ConfigureAwait(false);
                var probe = new BaseObjectState(null)
                {
                    NodeId = ProbeId,
                    BrowseName = new QualifiedName("Probe", NamespaceIndexes[0]),
                    DisplayName = new LocalizedText("Probe"),
                    TypeDefinitionId = Ua.ObjectTypeIds.BaseObjectType
                };
                await AddPredefinedNodeAsync(probe, cancellationToken).ConfigureAwait(false);
                await AddExternalReferenceAsync(
                    probe, m_sourceId, Ua.ReferenceTypeIds.Organizes, false, cancellationToken).ConfigureAwait(false);
                await AddReverseReferencesAsync(externalReferences, cancellationToken).ConfigureAwait(false);
            }

            private readonly NodeId m_sourceId;
        }

        /// <summary>
        /// Selects the facade returned by a PackML candidate factory.
        /// </summary>
        public enum ManagerFacadeKind
        {
            /// <summary>
            /// Returns the asynchronous manager itself.
            /// </summary>
            Direct,

            /// <summary>
            /// Returns an asynchronous adapter over the manager's synchronous adapter.
            /// </summary>
            Adapted,

            /// <summary>
            /// Returns a custom forwarding facade with the same synchronous resource.
            /// </summary>
            Forwarded
        }

        /// <summary>
        /// Creates a candidate that composes PackML during address-space preparation.
        /// </summary>
        private sealed class PreparedPackMLManagerFactory : IAsyncNodeManagerFactory
        {
            /// <summary>
            /// Selects whether the candidate first rolls back a unit.
            /// </summary>
            /// <param name="failFirstUnit">Whether to inject a failure during the first unit's registration.</param>
            /// <param name="failServingUnit">Whether the failed unit uses the serving unit's name.</param>
            /// <param name="namespaceUri">The namespace owned by the candidate.</param>
            /// <param name="facade">The facade returned for the candidate.</param>
            public PreparedPackMLManagerFactory(
                bool failFirstUnit,
                bool failServingUnit = false,
                string namespaceUri = "urn:opcfoundation:tests:packml:prepared",
                ManagerFacadeKind facade = ManagerFacadeKind.Direct)
            {
                m_failFirstUnit = failFirstUnit;
                m_failServingUnit = failServingUnit;
                m_facade = facade;
                NamespacesUris = [namespaceUri];
            }

            /// <summary>
            /// Gets the candidate manager.
            /// </summary>
            public PreparedPackMLManager? Manager { get; private set; }

            /// <summary>
            /// Gets the exact facade returned for registration.
            /// </summary>
            public IAsyncNodeManager? RegisteredManager { get; private set; }

            /// <inheritdoc/>
            public ArrayOf<string> NamespacesUris { get; }

            /// <inheritdoc/>
            public ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server,
                ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Manager = new PreparedPackMLManager(
                    server, configuration, NamespacesUris[0], m_failFirstUnit, m_failServingUnit);
                RegisteredManager = m_facade switch
                {
                    ManagerFacadeKind.Direct => Manager,
                    ManagerFacadeKind.Adapted => new AsyncNodeManagerAdapter(Manager.SyncNodeManager),
                    ManagerFacadeKind.Forwarded => CreateForwardingFacade(Manager),
                    _ => throw new InvalidOperationException("The candidate facade is not supported.")
                };
                return new ValueTask<IAsyncNodeManager>(RegisteredManager);
            }

            /// <summary>
            /// Forwards lifecycle and reference operations while retaining a distinct registration facade.
            /// </summary>
            private static IAsyncNodeManager CreateForwardingFacade(PreparedPackMLManager manager)
            {
                var facade = new Mock<IAsyncNodeManager>();
                facade.SetupGet(value => value.NamespaceUris).Returns(() => manager.NamespaceUris);
                facade.SetupGet(value => value.SyncNodeManager).Returns(manager.SyncNodeManager);
                facade.Setup(value => value.CreateAddressSpaceAsync(
                    It.IsAny<IDictionary<NodeId, IList<IReference>>>(), It.IsAny<CancellationToken>()))
                    .Returns((IDictionary<NodeId, IList<IReference>> references, CancellationToken ct) =>
                        manager.CreateAddressSpaceAsync(references, ct));
                facade.Setup(value => value.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()))
                    .Returns((CancellationToken ct) => manager.DeleteAddressSpaceAsync(ct));
                facade.Setup(value => value.GetManagerHandleAsync(
                    It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                    .Returns((NodeId nodeId, CancellationToken ct) => manager.GetManagerHandleAsync(nodeId, ct));
                facade.Setup(value => value.GetNodeMetadataAsync(
                    It.IsAny<OperationContext>(), It.IsAny<object>(), It.IsAny<BrowseResultMask>(),
                    It.IsAny<CancellationToken>()))
                    .Returns((OperationContext context, object handle, BrowseResultMask mask, CancellationToken ct) =>
                        manager.GetNodeMetadataAsync(context, handle, mask, ct));
                facade.Setup(value => value.SubscribeToAllEventsAsync(
                    It.IsAny<OperationContext>(), It.IsAny<uint>(), It.IsAny<IEventMonitoredItem>(), It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                    .Returns((OperationContext context, uint subscriptionId, IEventMonitoredItem monitoredItem,
                        bool unsubscribe, CancellationToken ct) => manager.SubscribeToAllEventsAsync(
                            context, subscriptionId, monitoredItem, unsubscribe, ct));
                facade.Setup(value => value.AddReferencesAsync(
                    It.IsAny<IDictionary<NodeId, IList<IReference>>>(), It.IsAny<CancellationToken>()))
                    .Returns((IDictionary<NodeId, IList<IReference>> references, CancellationToken ct) =>
                        manager.AddReferencesAsync(references, ct));
                facade.Setup(value => value.DeleteReferenceAsync(
                    It.IsAny<object>(), It.IsAny<NodeId>(), It.IsAny<bool>(), It.IsAny<ExpandedNodeId>(),
                    It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                    .Returns((object handle, NodeId referenceType, bool inverse, ExpandedNodeId target,
                        bool bidirectional, CancellationToken ct) =>
                        manager.DeleteReferenceAsync(handle, referenceType, inverse, target, bidirectional, ct));
                facade.As<IAsyncDisposable>().Setup(value => value.DisposeAsync()).Returns(manager.DisposeAsync);
                return facade.Object;
            }

            /// <summary>Whether to fail the first unit's registration.</summary>
            private readonly bool m_failFirstUnit;

            /// <summary>Whether the failed unit uses the serving unit's name.</summary>
            private readonly bool m_failServingUnit;

            /// <summary>The facade returned for registration.</summary>
            private readonly ManagerFacadeKind m_facade;
        }

        /// <summary>
        /// Composes units in a private candidate and can inject a registration failure.
        /// </summary>
        private sealed class PreparedPackMLManager : AsyncCustomNodeManager, INodeManagerReloadParticipant
        {
            /// <summary>
            /// Creates the candidate and its PackML runtime.
            /// </summary>
            public PreparedPackMLManager(
                IServerInternal server,
                ApplicationConfiguration configuration,
                string namespaceUri,
                bool failFirstUnit,
                bool failServingUnit)
                : base(server, configuration, namespaceUri)
            {
                m_failFirstUnit = failFirstUnit;
                m_failServingUnit = failServingUnit;
                Runtime = new PackMLRuntime(this);
            }

            /// <summary>
            /// Gets the candidate's PackML runtime.
            /// </summary>
            public PackMLRuntime Runtime { get; }

            /// <summary>
            /// Gets the unit retained by the candidate.
            /// </summary>
            public Unit CreatedUnit { get; private set; } = null!;

            /// <summary>
            /// Gets or sets a unit to delete before the address space during retirement.
            /// </summary>
            public Unit? UnitToDeleteOnRetirement { get; set; }

            /// <summary>
            /// Gets the identifier of the rolled-back unit.
            /// </summary>
            public NodeId FailedUnitId { get; private set; }

            /// <summary>
            /// Gets the identifier of the rolled-back unit's mode type.
            /// </summary>
            public NodeId FailedModeId { get; private set; }

            /// <inheritdoc/>
            public override async ValueTask CreateAddressSpaceAsync(
                IDictionary<NodeId, IList<IReference>> externalReferences,
                CancellationToken cancellationToken = default)
            {
                await base.CreateAddressSpaceAsync(externalReferences, cancellationToken).ConfigureAwait(false);
                if (m_failFirstUnit)
                {
                    m_failRegistration = true;
                    try
                    {
                        InvalidOperationException? failure = await Assert.ThrowsAsync<InvalidOperationException>(
                            async () => await Runtime.AddUnitAsync(
                                new QualifiedName(
                                    m_failServingUnit ? "PreparedUnit" : "FailedUnit", NamespaceIndexes[0]),
                                new EquipmentProvider(),
                                cancellationToken: cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
                        Assert.That(failure, Is.SameAs(m_registrationFailure));
                    }
                    finally
                    {
                        m_failRegistration = false;
                    }
                }
                CreatedUnit = await Runtime.AddUnitAsync(
                    new QualifiedName(m_failServingUnit ? "ReplacementUnit" : "PreparedUnit", NamespaceIndexes[0]),
                    new EquipmentProvider(),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                await AddReverseReferencesAsync(externalReferences, cancellationToken).ConfigureAwait(false);
            }

            /// <inheritdoc/>
            public ValueTask<ArrayOf<LocalReference>> PrepareReloadAsync(
                IAsyncNodeManager replacement,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ValueTask<ArrayOf<LocalReference>>([]);
            }

            /// <inheritdoc/>
            public override async ValueTask DeleteAddressSpaceAsync(CancellationToken cancellationToken = default)
            {
                await Runtime.DisposeAsync().ConfigureAwait(false);
                if (UnitToDeleteOnRetirement is { } unit)
                {
                    await DeleteNodeAsync(SystemContext, unit.Node.NodeId, cancellationToken).ConfigureAwait(false);
                    await DeleteNodeAsync(SystemContext, unit.ModeDataType.NodeId, cancellationToken)
                        .ConfigureAwait(false);
                }
                await base.DeleteAddressSpaceAsync(cancellationToken).ConfigureAwait(false);
            }

            /// <inheritdoc/>
            protected override async ValueTask AddRootNotifierAsync(
                NodeState notifier,
                CancellationToken cancellationToken = default)
            {
                await base.AddRootNotifierAsync(notifier, cancellationToken).ConfigureAwait(false);
                if (m_failRegistration && notifier is PackMLBaseObjectState unit)
                {
                    FailedUnitId = unit.NodeId;
                    FailedModeId = unit.Status.Required().UnitModeCurrent.Required().DataType;
                    throw m_registrationFailure;
                }
            }

            /// <inheritdoc/>
            protected override async ValueTask DisposeAsyncCore()
            {
                await Runtime.DisposeAsync().ConfigureAwait(false);
                await base.DisposeAsyncCore().ConfigureAwait(false);
            }

            /// <summary>Whether to fail the first unit's registration.</summary>
            private readonly bool m_failFirstUnit;

            /// <summary>Whether the failed unit uses the serving unit's name.</summary>
            private readonly bool m_failServingUnit;

            /// <summary>The failure raised after the first root is registered.</summary>
            private readonly InvalidOperationException m_registrationFailure =
                new("Injected root registration failure.");

            /// <summary>Whether the next root registration fails.</summary>
            private bool m_failRegistration;
        }
    }
}
