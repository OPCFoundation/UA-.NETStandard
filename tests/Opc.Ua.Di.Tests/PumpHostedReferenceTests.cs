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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
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
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Tests;
using Pumps;

namespace Opc.Ua.Di.Tests
{
    [TestFixture]
    [Category("Pumps")]
    [Category("Hosting")]
    [NonParallelizable]
    public sealed class PumpHostedReferenceTests
    {
        [Test]
        public async Task HostedPumpsBrowseSimulateAndReportEventsAsync()
        {
            CaptureServer.Reset();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpcUa()
                .AddServer<CaptureServer>(ConfigureServer)
                .AddNodeManager<PumpNodeManagerFactory>();

            await using ServiceProvider provider = services.BuildServiceProvider();
            IHostedService hostedService = provider.GetServices<IHostedService>().Single();
            await hostedService.StartAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                Assert.That(
                    await WaitForAsync(
                        () => CaptureServer.StartedInstance != null,
                        TimeSpan.FromSeconds(30)).ConfigureAwait(false),
                    Is.True);
                IServerInternal server = CaptureServer.StartedInstance!;
                ushort diNamespaceIndex = (ushort)server.NamespaceUris.GetIndex(
                    Opc.Ua.Di.Namespaces.OpcUaDi);
                var deviceSetId = new NodeId(Opc.Ua.Di.Objects.DeviceSet, diNamespaceIndex);
                ArrayOf<BrowseDescription> nodesToBrowse =
                [
                    new BrowseDescription
                    {
                        NodeId = deviceSetId,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = Opc.Ua.Types.ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                ];

                (ArrayOf<BrowseResult> results, _) = await server.NodeManager.BrowseAsync(
                    new OperationContext(
                        new RequestHeader(),
                        null,
                        RequestType.Browse,
                        RequestLifetime.None),
                    new ViewDescription(),
                    0,
                    nodesToBrowse,
                    CancellationToken.None).ConfigureAwait(false);

                var pumpReferences = new List<ReferenceDescription>();
                for (int ii = 0; ii < results[0].References.Count; ii++)
                {
                    ReferenceDescription reference = results[0].References[ii];
                    if (reference.BrowseName.Name is "Pump_1" or "Pump_2")
                    {
                        pumpReferences.Add(reference);
                    }
                }

                Assert.That(pumpReferences, Has.Count.EqualTo(2));
                Assert.That(
                    pumpReferences,
                    Has.All.Matches<ReferenceDescription>(reference =>
                        ExpandedNodeId.ToNodeId(
                            reference.ReferenceTypeId,
                            server.NamespaceUris) == Opc.Ua.Types.ReferenceTypeIds.Organizes));

                using var clientFixture = new ClientFixture(
                    NUnitTelemetryContext.Create());
                string clientPkiRoot = System.IO.Path.Combine(
                    TestContext.CurrentContext.WorkDirectory,
                    nameof(HostedPumpsBrowseSimulateAndReportEventsAsync),
                    "client-pki");
                await clientFixture.LoadClientConfigurationAsync(clientPkiRoot)
                    .ConfigureAwait(false);
                using Opc.Ua.Client.ISession session = await clientFixture.ConnectAsync(
                    new Uri(s_endpointUrl),
                    SecurityPolicies.None).ConfigureAwait(false);
                BrowseResponse clientBrowse = await session.BrowseAsync(
                    null,
                    null,
                    0,
                    nodesToBrowse,
                    CancellationToken.None).ConfigureAwait(false);
                var clientPumpReferences = new List<ReferenceDescription>();
                for (int ii = 0; ii < clientBrowse.Results[0].References.Count; ii++)
                {
                    ReferenceDescription reference =
                        clientBrowse.Results[0].References[ii];
                    if (reference.BrowseName.Name is "Pump_1" or "Pump_2")
                    {
                        clientPumpReferences.Add(reference);
                    }
                }
                Assert.That(clientPumpReferences, Has.Count.EqualTo(2));
                Assert.That(
                    clientPumpReferences,
                    Has.All.Matches<ReferenceDescription>(reference =>
                        ExpandedNodeId.ToNodeId(
                            reference.ReferenceTypeId,
                            session.NamespaceUris) == Opc.Ua.Types.ReferenceTypeIds.Organizes));

                NodeId pump1Id = ExpandedNodeId.ToNodeId(
                    clientPumpReferences.Single(reference => reference.BrowseName.Name == "Pump_1").NodeId,
                    session.NamespaceUris);
                NodeId pump2Id = ExpandedNodeId.ToNodeId(
                    clientPumpReferences.Single(reference => reference.BrowseName.Name == "Pump_2").NodeId,
                    session.NamespaceUris);
                var pump1PressureId = new NodeId(
                    pump1Id.IdentifierAsString + "_Operational_Measurements_DifferentialPressure",
                    pump1Id.NamespaceIndex);
                var pump2PressureId = new NodeId(
                    pump2Id.IdentifierAsString + "_Operational_Measurements_DifferentialPressure",
                    pump2Id.NamespaceIndex);

                DataValue initialPump1 = await ReadGoodValueAsync(
                    session,
                    pump1PressureId,
                    TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                DataValue initialPump2 = await ReadGoodValueAsync(
                    session,
                    pump2PressureId,
                    TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(initialPump1.WrappedValue.TryGetValue(out double pump1Value), Is.True);
                    Assert.That(initialPump2.WrappedValue.TryGetValue(out double pump2Value), Is.True);
                    Assert.That(pump1Value, Is.GreaterThan(0));
                    Assert.That(pump2Value, Is.GreaterThan(0));
                    Assert.That(pump2Value, Is.Not.EqualTo(pump1Value));
                });

                using var subscription = new Opc.Ua.Client.Subscription(
                    session.DefaultSubscription)
                {
                    DisplayName = "Pump simulation and events",
                    PublishingEnabled = true,
                    PublishingInterval = 250,
                    KeepAliveCount = 10
                };
                session.AddSubscription(subscription);
                await subscription.CreateAsync(CancellationToken.None)
                    .ConfigureAwait(false);

                var pump1Values = new ConcurrentDictionary<double, bool>();
                var pump2Values = new ConcurrentDictionary<double, bool>();
                var pump1Changed = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var pump2Changed = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var eventReceived = new TaskCompletionSource<EventFieldList>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

                var pump1Item = new Opc.Ua.Client.MonitoredItem(subscription.DefaultItem)
                {
                    StartNodeId = pump1PressureId,
                    AttributeId = Attributes.Value,
                    DisplayName = "Pump #1 pressure",
                    SamplingInterval = 100,
                    QueueSize = 16
                };
                pump1Item.Notification += (item, _) =>
                {
                    foreach (DataValue value in item.DequeueValues())
                    {
                        if (value.WrappedValue.TryGetValue(out double current))
                        {
                            pump1Values.TryAdd(current, !value.SourceTimestamp.IsNull);
                            if (pump1Values.Count >= 2)
                            {
                                pump1Changed.TrySetResult(true);
                            }
                        }
                    }
                };

                var pump2Item = new Opc.Ua.Client.MonitoredItem(subscription.DefaultItem)
                {
                    StartNodeId = pump2PressureId,
                    AttributeId = Attributes.Value,
                    DisplayName = "Pump #2 pressure",
                    SamplingInterval = 100,
                    QueueSize = 16
                };
                pump2Item.Notification += (item, _) =>
                {
                    foreach (DataValue value in item.DequeueValues())
                    {
                        if (value.WrappedValue.TryGetValue(out double current))
                        {
                            pump2Values.TryAdd(current, !value.SourceTimestamp.IsNull);
                            if (pump2Values.Count >= 2)
                            {
                                pump2Changed.TrySetResult(true);
                            }
                        }
                    }
                };

                var eventFilter = new EventFilter();
                eventFilter.AddSelectClause(
                    Opc.Ua.Types.ObjectTypeIds.BaseEventType,
                    QualifiedName.From("Message"));
                var eventItem = new Opc.Ua.Client.MonitoredItem(subscription.DefaultItem)
                {
                    StartNodeId = pump2Id,
                    AttributeId = Attributes.EventNotifier,
                    DisplayName = "Pump #2 events",
                    Filter = eventFilter,
                    QueueSize = 16
                };
                eventItem.Notification += (_, args) =>
                {
                    if (args.NotificationValue is EventFieldList fields)
                    {
                        eventReceived.TrySetResult(fields);
                    }
                };

                subscription.AddItem(pump1Item);
                subscription.AddItem(pump2Item);
                subscription.AddItem(eventItem);
                await subscription.ApplyChangesAsync(CancellationToken.None)
                    .ConfigureAwait(false);

                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                using (timeout.Token.Register(() =>
                {
                    pump1Changed.TrySetCanceled(timeout.Token);
                    pump2Changed.TrySetCanceled(timeout.Token);
                    eventReceived.TrySetCanceled(timeout.Token);
                }))
                {
                    await Task.WhenAll(
                        pump1Changed.Task,
                        pump2Changed.Task,
                        eventReceived.Task).ConfigureAwait(false);
                }
                EventFieldList observedEvent = await eventReceived.Task.ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(
                        pump1Values.Values,
                        Has.All.True);
                    Assert.That(
                        pump2Values.Values,
                        Has.All.True);
                    Assert.That(observedEvent.EventFields, Has.Count.EqualTo(1));
                    Assert.That(
                        observedEvent.EventFields[0].GetLocalizedText().Text,
                        Does.StartWith("Alarm "));
                });

                await session.RemoveSubscriptionAsync(subscription)
                    .ConfigureAwait(false);
            }
            finally
            {
                await hostedService.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A pump materialised after the server has started - through the
        /// declarative <c>ConfigureDevicesFor</c> hosting hook rather than the
        /// configured pump count - must join the shared simulation loop, not
        /// just appear in the address space. This pins the post-setup
        /// registration path in <c>CreatePumpAsync</c>.
        /// </summary>
        [Test]
        public async Task PumpCreatedAfterStartupJoinsTheLiveSimulationAsync()
        {
            const string dynamicPumpName = "Pump_Dynamic";

            CaptureServer.Reset();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpcUa()
                .AddServer<CaptureServer>(ConfigureServer)
                .AddNodeManager<PumpNodeManagerFactory>()
                .ConfigureDevicesFor<PumpNodeManager>(async context =>
                {
                    var manager = (PumpNodeManager)context.Manager;
                    await manager.CreatePumpAsync(
                        new QualifiedName(dynamicPumpName, manager.InstanceNamespaceIndex),
                        context.CancellationToken).ConfigureAwait(false);
                });

            await using ServiceProvider provider = services.BuildServiceProvider();
            IHostedService hostedService = provider.GetServices<IHostedService>().Single();
            await hostedService.StartAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                Assert.That(
                    await WaitForAsync(
                        () => CaptureServer.StartedInstance != null,
                        TimeSpan.FromSeconds(30)).ConfigureAwait(false),
                    Is.True);
                IServerInternal server = CaptureServer.StartedInstance!;

                ushort diNamespaceIndex = (ushort)server.NamespaceUris.GetIndex(
                    Opc.Ua.Di.Namespaces.OpcUaDi);
                var deviceSetId = new NodeId(Opc.Ua.Di.Objects.DeviceSet, diNamespaceIndex);
                ArrayOf<BrowseDescription> nodesToBrowse =
                [
                    new BrowseDescription
                    {
                        NodeId = deviceSetId,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = Opc.Ua.Types.ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                ];

                using var clientFixture = new ClientFixture(NUnitTelemetryContext.Create());
                string clientPkiRoot = System.IO.Path.Combine(
                    TestContext.CurrentContext.WorkDirectory,
                    nameof(PumpCreatedAfterStartupJoinsTheLiveSimulationAsync),
                    "client-pki");
                await clientFixture.LoadClientConfigurationAsync(clientPkiRoot)
                    .ConfigureAwait(false);
                using Opc.Ua.Client.ISession session = await clientFixture.ConnectAsync(
                    new Uri(s_endpointUrl),
                    SecurityPolicies.None).ConfigureAwait(false);

                BrowseResponse clientBrowse = await session.BrowseAsync(
                    null,
                    null,
                    0,
                    nodesToBrowse,
                    CancellationToken.None).ConfigureAwait(false);

                ReferenceDescription? dynamicPump = null;
                for (int ii = 0; ii < clientBrowse.Results[0].References.Count; ii++)
                {
                    ReferenceDescription reference = clientBrowse.Results[0].References[ii];
                    if (reference.BrowseName.Name == dynamicPumpName)
                    {
                        dynamicPump = reference;
                    }
                }

                Assert.That(
                    dynamicPump,
                    Is.Not.Null,
                    "The pump created after startup was not organized by DeviceSet.");

                NodeId dynamicPumpId = ExpandedNodeId.ToNodeId(
                    dynamicPump!.NodeId,
                    session.NamespaceUris);
                var pressureId = new NodeId(
                    dynamicPumpId.IdentifierAsString +
                        "_Operational_Measurements_DifferentialPressure",
                    dynamicPumpId.NamespaceIndex);

                // Joining the simulation is what turns the initial
                // BadWaitingForInitialData into a published value.
                DataValue first = await ReadGoodValueAsync(
                    session,
                    pressureId,
                    TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.That(
                    StatusCode.IsGood(first.StatusCode),
                    Is.True,
                    "The pump created after startup never published a value, so it did " +
                    "not join the simulation.");

                bool changed = false;
                DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
                first.WrappedValue.TryGetValue(out double before);
                while (DateTime.UtcNow < deadline)
                {
                    DataValue current = await session
                        .ReadValueAsync(pressureId, CancellationToken.None)
                        .ConfigureAwait(false);
                    if (current.WrappedValue.TryGetValue(out double now) &&
                        !now.Equals(before))
                    {
                        changed = true;
                        break;
                    }
                    await Task.Delay(100).ConfigureAwait(false);
                }

                Assert.That(
                    changed,
                    Is.True,
                    "The value of the pump created after startup never changed, so the " +
                    "simulation is not advancing it.");
            }
            finally
            {
                await hostedService.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }

        private static void ConfigureServer(OpcUaServerOptions options)
        {
            string applicationName = nameof(PumpHostedReferenceTests);
            string testRoot = System.IO.Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                applicationName,
                Guid.NewGuid().ToString("N"));
            options.ApplicationName = applicationName;
            options.ApplicationUri = "urn:localhost:" + applicationName;
            options.ProductUri = "urn:localhost:" + applicationName + ":product";
            options.PkiRoot = System.IO.Path.Combine(testRoot, "pki");
            options.AutoAcceptUntrustedCertificates = true;
            options.IncludeUnsecurePolicyNone = true;
            options.EndpointUrls.Clear();
            s_endpointUrl =
                "opc.tcp://localhost:" +
                GetAvailablePort().ToString(CultureInfo.InvariantCulture) +
                "/" +
                applicationName;
            options.EndpointUrls.Add(s_endpointUrl);
        }

        private static int GetAvailablePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        private static async Task<bool> WaitForAsync(
            Func<bool> condition,
            TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                {
                    return true;
                }
                await Task.Delay(100).ConfigureAwait(false);
            }
            return condition();
        }

        private static async Task<DataValue> ReadGoodValueAsync(
            Opc.Ua.Client.ISession session,
            NodeId nodeId,
            TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            DataValue value;
            do
            {
                try
                {
                    value = await session.ReadValueAsync(nodeId, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (ServiceResultException ex) when (ex.StatusCode == StatusCodes.BadWaitingForInitialData)
                {
                    value = DataValue.FromStatusCode(StatusCodes.BadWaitingForInitialData);
                }
                if (StatusCode.IsGood(value.StatusCode))
                {
                    return value;
                }
                await Task.Delay(100).ConfigureAwait(false);
            } while (DateTime.UtcNow < deadline);

            return value;
        }

        private static string s_endpointUrl = string.Empty;

        public sealed class CaptureServer : DependencyInjectionStandardServer
        {
            public CaptureServer(
                IServiceProvider services,
                ITelemetryContext telemetry,
                TimeProvider timeProvider)
                : base(services, telemetry, timeProvider)
            {
            }

            public static IServerInternal? StartedInstance =>
                Volatile.Read(ref s_startedInstance);

            public static void Reset()
            {
                Volatile.Write(ref s_startedInstance, null);
            }

            protected override void OnServerStarted(IServerInternal server)
            {
                Volatile.Write(ref s_startedInstance, server);
                base.OnServerStarted(server);
            }

            private static IServerInternal? s_startedInstance;
        }
    }
}
