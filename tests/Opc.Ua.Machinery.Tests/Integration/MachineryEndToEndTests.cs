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
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.FileSystem;
using Opc.Ua.Client.StateMachines;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Hosting;
using Opc.Ua.ISA95.Client;
using Opc.Ua.ISA95.Server.Providers;
using Opc.Ua.Machinery.Client;
using Opc.Ua.Machinery.ProcessValues;
using Opc.Ua.Machinery.Result;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Machinery.Server.Builders;
using Opc.Ua.Machinery.Server.Results;
using Opc.Ua.Machinery.Server.StateMachines;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using ClientSession = Opc.Ua.Client.ISession;
using ClientSubscriptionManager = Opc.Ua.Client.Subscriptions.ISubscriptionManager;
using V2 = Opc.Ua.ISA95.JobControl.V2;

namespace Opc.Ua.Machinery.Tests.Integration
{
    /// <summary>
    /// Drives a real OPC 40001 server over <c>opc.tcp</c> with the real
    /// client.
    /// </summary>
    /// <remarks>
    /// Everything else in this project exercises the address space in
    /// process. This fixture is the only one that proves the whole stack —
    /// encoding, browse-path translation, method calls, file transfer and
    /// event subscription — works over the wire for all four parts the
    /// library claims: process values, jobs, results and energy.
    /// </remarks>
    [TestFixture]
    [Category("Machinery")]
    [Category("Integration")]
    [NonParallelizable]
    public sealed class MachineryEndToEndTests
    {
        private const string kNotificationMessage = "E2E notification.";

        [Test]
        public Task TheClientDrivesEveryPartOverTheWireAsync()
        {
            return RunAgainstPressAsync(async (machinery, configurator, _, session, ct) =>
            {
                NodeId machine = await AssertDiscoveryAsync(machinery, ct)
                    .ConfigureAwait(false);
                await AssertBuildingBlocksAsync(machinery, machine, ct).ConfigureAwait(false);
                await AssertStateAsync(machinery, machine, configurator, ct)
                    .ConfigureAwait(false);
                await AssertCountersAndEquipmentAsync(machinery, machine, ct)
                    .ConfigureAwait(false);
                NodeId processValue = await AssertProcessValuesAsync(
                    machinery,
                    machine,
                    configurator,
                    ct).ConfigureAwait(false);
                await AssertZeroPointAdjustmentAsync(machinery, processValue, session, ct)
                    .ConfigureAwait(false);
                await AssertEnergyAsync(machinery, machine, ct).ConfigureAwait(false);
                await AssertJobsAsync(machinery, machine, ct).ConfigureAwait(false);
                await AssertResultsAsync(machinery, machine, configurator, session, ct)
                    .ConfigureAwait(false);
                await AssertResultQueriesAndWriteRefusalAsync(
                    machinery,
                    machine,
                    configurator,
                    session,
                    ct).ConfigureAwait(false);

                await AssertNotificationEventAsync(
                    machinery, machine, configurator, session, ct)
                    .ConfigureAwait(false);
                await AssertConformanceAsync(session, ct).ConfigureAwait(false);
            });
        }

        /// <summary>
        /// Drives Pause, Resume and RevokeStart through a machine's
        /// <c>JobManagement</c> over the wire, together with the state the
        /// published <c>JobOrderList</c> reports after each, and reads the
        /// predefined job parameters back with the client helpers.
        /// </summary>
        [Test]
        public Task JobControlPausesResumesAndRevokesStartThroughAMachineAsync()
        {
            return RunAgainstPressAsync((machinery, _, jobProvider, _, ct) =>
                AssertJobControlLifecycleAsync(machinery, jobProvider, ct));
        }

        /// <summary>
        /// Subscribes to a machine's job lists over the wire and checks that
        /// every change of the lists reaches the monitored items.
        /// </summary>
        [Test]
        public Task TheJobListsNotifySubscribedClientsAsync()
        {
            return RunAgainstPressAsync((machinery, _, jobProvider, session, ct) =>
                AssertJobListNotificationsAsync(machinery, jobProvider, session, ct));
        }

        private static async Task RunAgainstPressAsync(
            Func<MachineryClient,
                PressConfigurator,
                InMemoryIsa95JobControlProvider,
                ClientSession,
                CancellationToken,
                Task> scenario)
        {
            string testRoot = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                nameof(MachineryEndToEndTests),
                Guid.NewGuid().ToString("N"));
            using var jobProvider = new InMemoryIsa95JobControlProvider();
            var configurator = new PressConfigurator(jobProvider);

            var serverFixture = new ServerFixture<MachineryIntegrationServer>(
                telemetry => new MachineryIntegrationServer(telemetry, configurator))
            {
                AutoAccept = true,
                SecurityNone = true
            };
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var clientFixture = new ClientFixture(telemetry)
            {
                OperationTimeout = 30_000,
                SessionTimeout = 60_000
            };
            clientFixture.UseSubscriptionEngineFactory(
                DefaultSubscriptionEngineFactory.Instance);
            ClientSession? session = null;

            try
            {
                await serverFixture.LoadConfigurationAsync(testRoot).ConfigureAwait(false);
                await serverFixture.StartAsync().ConfigureAwait(false);
                var endpointUrl = new Uri(
                    $"{Utils.UriSchemeOpcTcp}://localhost:{serverFixture.Port}");

                await clientFixture
                    .LoadClientConfigurationAsync(testRoot, "MachineryIntegrationClient")
                    .ConfigureAwait(false);
                session = await clientFixture
                    .ConnectAsync(endpointUrl, SecurityPolicies.None)
                    .ConfigureAwait(false);

                using var testCts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
                var machinery = new MachineryClient(session, telemetry);
                await scenario(machinery, configurator, jobProvider, session, testCts.Token)
                    .ConfigureAwait(false);
            }
            finally
            {
                if (session != null)
                {
                    await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                    session.Dispose();
                }
                await serverFixture.StopAsync().ConfigureAwait(false);
                try
                {
                    if (Directory.Exists(testRoot))
                    {
                        Directory.Delete(testRoot, recursive: true);
                    }
                }
                catch (IOException)
                {
                    // A leftover test directory must not fail the run.
                }
            }
        }

        private static async Task<NodeId> AssertDiscoveryAsync(
            MachineryClient machinery,
            CancellationToken ct)
        {
            Assert.That(
                machinery.MachinesFolderId.IsNull,
                Is.False,
                "The server publishes the OPC 40001-1 Machines folder.");

            var machines = new List<MachineEntry>();
            await foreach (MachineEntry entry in machinery
                .EnumerateMachinesAsync(ct)
                .ConfigureAwait(false))
            {
                machines.Add(entry);
            }
            Assert.That(machines, Has.Count.EqualTo(1));
            NodeId machine = machines[0].NodeId;

            // The NodeId-only convenience over the same walk.
            ArrayOf<NodeId> discovered = await machinery
                .DiscoverMachinesAsync(ct)
                .ConfigureAwait(false);
            Assert.That(
                discovered.ToArray(),
                Is.EqualTo(new[] { machine }),
                "DiscoverMachinesAsync has to report the same machines as the walk.");

            // OPC 40001-1 §7.1 and Table 12 require the folder; eight
            // conformance units hang off it.
            NodeId buildingBlocks = await machinery
                .ResolveBuildingBlocksAsync(machine, ct)
                .ConfigureAwait(false);
            Assert.That(
                buildingBlocks.IsNull,
                Is.False,
                "The MachineryBuildingBlocks folder has to be resolvable.");

            MachineIdentification? identification = await machinery
                .ReadIdentificationAsync(machine, ct)
                .ConfigureAwait(false);
            Assert.That(identification, Is.Not.Null);
            Assert.That(identification!.Manufacturer.Text, Is.EqualTo("Acme"));
            Assert.That(identification.SerialNumber, Is.EqualTo("SN-E2E"));
            Assert.That(identification.ProductInstanceUri, Is.EqualTo("urn:acme:press:e2e"));

            var components = new List<MachineEntry>();
            await foreach (MachineEntry entry in machinery
                .EnumerateComponentsAsync(machine, ct)
                .ConfigureAwait(false))
            {
                components.Add(entry);
            }
            Assert.That(components, Has.Count.EqualTo(1));
            Assert.That(components[0].BrowseName.Name, Is.EqualTo("HydraulicUnit"));

            Opc.Ua.Di.DeviceHealthEnumeration? health = await machinery
                .ReadDeviceHealthAsync(machine, ct)
                .ConfigureAwait(false);
            Assert.That(
                health,
                Is.EqualTo(Opc.Ua.Di.DeviceHealthEnumeration.NORMAL),
                "WithHealth has to materialise the DI DeviceHealth variable.");
            return machine;
        }

        private static async Task AssertBuildingBlocksAsync(
            MachineryClient machinery,
            NodeId machine,
            CancellationToken ct)
        {
            var blocks = new List<string>();
            await foreach (MachineEntry entry in machinery
                .EnumerateBuildingBlocksAsync(machine, ct)
                .ConfigureAwait(false))
            {
                blocks.Add(entry.BrowseName.Name ?? string.Empty);
            }

            // OPC 40001-1 Table 12: these seven shall be reachable through the
            // organizer, and every conformance unit for them is worded that
            // way.
            Assert.That(blocks, Contains.Item("Monitoring"));
            Assert.That(blocks, Contains.Item("MachineryItemState"));
            Assert.That(blocks, Contains.Item("MachineryOperationMode"));
            Assert.That(blocks, Contains.Item("OperationCounters"));
            Assert.That(blocks, Contains.Item("LifetimeCounters"));
            Assert.That(blocks, Contains.Item("MachineryEquipment"));
            Assert.That(blocks, Contains.Item("Notifications"));
        }

        private static async Task AssertStateAsync(
            MachineryClient machinery,
            NodeId machine,
            PressConfigurator configurator,
            CancellationToken ct)
        {
            FiniteStateSnapshot? state = await machinery.GetItemStateAsync(machine, ct)
                .ConfigureAwait(false);
            FiniteStateSnapshot? mode = await machinery.GetOperationModeAsync(machine, ct)
                .ConfigureAwait(false);
            Assert.That(state!.CurrentState.Text, Is.EqualTo("NotExecuting"));
            Assert.That(mode!.CurrentState.Text, Is.EqualTo("None"));

            await configurator.Machine!.ItemState!
                .SetStateAsync(MachineryItemStateValue.Executing, ct)
                .ConfigureAwait(false);
            state = await machinery.GetItemStateAsync(machine, ct).ConfigureAwait(false);
            Assert.That(state!.CurrentState.Text, Is.EqualTo("Executing"));
            Assert.That(
                state.LastTransition.Text,
                Is.EqualTo("FromNotExecutingToExecuting"));
        }

        private static async Task AssertCountersAndEquipmentAsync(
            MachineryClient machinery,
            NodeId machine,
            CancellationToken ct)
        {
            MachineryOperationCounters? counters = await machinery
                .ReadOperationCountersAsync(machine, ct)
                .ConfigureAwait(false);
            Assert.That(counters, Is.Not.Null);
            Assert.That(counters!.PowerOnDuration, Is.EqualTo(12_480.0));
            Assert.That(counters.OperationDuration, Is.EqualTo(9_215.5));

            var lifetimes = new List<MachineryLifetimeVariable>();
            await foreach (MachineryLifetimeVariable life in machinery
                .ReadLifetimeCountersAsync(machine, ct)
                .ConfigureAwait(false))
            {
                lifetimes.Add(life);
            }
            Assert.That(lifetimes, Has.Count.EqualTo(1));
            Assert.That(lifetimes[0].BrowseName.Name, Is.EqualTo("SealLife"));
            Assert.That(lifetimes[0].Remaining, Is.EqualTo(1_412_500.0));
            Assert.That(lifetimes[0].StartValue, Is.EqualTo(2_000_000.0));

            var equipment = new List<MachineryEquipmentItem>();
            await foreach (MachineryEquipmentItem item in machinery
                .EnumerateEquipmentAsync(machine, ct)
                .ConfigureAwait(false))
            {
                equipment.Add(item);
            }
            Assert.That(equipment, Has.Count.EqualTo(1));
            Assert.That(
                equipment[0].MachineryEquipmentTypeId,
                Is.EqualTo("urn:acme:equipment:die"));
            Assert.That(
                equipment[0].EquipmentLife,
                Is.Not.Null,
                "EquipmentLife comes from an interface and has to be materialised.");
            Assert.That(equipment[0].EquipmentLife!.Remaining, Is.EqualTo(118_400.0));
        }

        private static async Task<NodeId> AssertProcessValuesAsync(
            MachineryClient machinery,
            NodeId machine,
            PressConfigurator configurator,
            CancellationToken ct)
        {
            var processValues = new List<MachineEntry>();
            await foreach (MachineEntry entry in machinery
                .EnumerateProcessValuesAsync(machine, ct)
                .ConfigureAwait(false))
            {
                processValues.Add(entry);
            }
            Assert.That(processValues, Has.Count.EqualTo(1));
            NodeId processValue = processValues[0].NodeId;

            MachineryProcessValue? reading = await machinery
                .ReadProcessValueAsync(processValue, ct)
                .ConfigureAwait(false);
            Assert.That(reading, Is.Not.Null);
            Assert.That(reading!.Value, Is.EqualTo(45.0));
            Assert.That(reading.Setpoint, Is.EqualTo(45.0));
            Assert.That(reading.HighLimit, Is.EqualTo(65.0));
            Assert.That(reading.HighHighLimit, Is.EqualTo(80.0));
            Assert.That(reading.LowLimit, Is.EqualTo(15.0));
            Assert.That(reading.EngineeringUnits!.DisplayName.Text, Is.EqualTo("°C"));
            Assert.That(reading.EuRange!.High, Is.EqualTo(120.0));

            // Range -20 … 120, so 45 sits at 46.43 %.
            Assert.That(reading.PercentageValue, Is.EqualTo(46.428571).Within(0.001));

            await configurator.OilTemperature!.SetValueAsync(70.0, ct).ConfigureAwait(false);
            reading = await machinery.ReadProcessValueAsync(processValue, ct)
                .ConfigureAwait(false);
            Assert.That(reading!.Value, Is.EqualTo(70.0));

            // The OPC 40001-2 Status word is vendor-specific, so the only thing
            // the library owes is that what the server writes is what a client
            // reads back.
            await configurator.OilTemperature.SetStatusAsync(0x0042, ct)
                .ConfigureAwait(false);
            reading = await machinery.ReadProcessValueAsync(processValue, ct)
                .ConfigureAwait(false);
            Assert.That(reading!.Status, Is.EqualTo(0x0042));
            return processValue;
        }

        private static async Task AssertZeroPointAdjustmentAsync(
            MachineryClient machinery,
            NodeId processValue,
            ClientSession session,
            CancellationToken ct)
        {
            Assert.That(
                session.TryGetSubscriptionManager(
                    out ClientSubscriptionManager? subscriptionManager),
                Is.True);
            await using var streaming = new StreamingSubscription(subscriptionManager!);

            using var eventCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            eventCts.CancelAfter(TimeSpan.FromSeconds(30));

            // OPC 40001-2's conformance unit says every instance supporting the
            // method generates the event, so the call and the event belong in
            // the same assertion: the method alone proves only half of it.
            IAsyncEnumerator<ZeroPointAdjustmentEventTypeRecord> events = machinery
                .ObserveZeroPointAdjustmentsAsync(
                    processValue, streaming, cancellationToken: eventCts.Token)
                .GetAsyncEnumerator(eventCts.Token);
            try
            {
                Task<bool> pending = events.MoveNextAsync().AsTask();
                Assert.That(
                    await WaitForAsync(
                        () => subscriptionManager!.Items.Any(subscription =>
                            subscription.Created &&
                            subscription.MonitoredItems.Items.Any(item =>
                                item.Created && ServiceResult.IsGood(item.Error))),
                        TimeSpan.FromSeconds(20),
                        eventCts.Token).ConfigureAwait(false),
                    Is.True,
                    "The zero-point event subscription has to come up.");

                StatusCode status = await machinery
                    .ZeroPointAdjustmentAsync(processValue, ct)
                    .ConfigureAwait(false);
                Assert.That(
                    StatusCode.IsGood(status),
                    Is.True,
                    "OPC 40001-2's ZeroPointAdjustment method has to be callable.");

                Assert.That(
                    await Task.WhenAny(pending, Task.Delay(TimeSpan.FromSeconds(25), ct))
                        .ConfigureAwait(false),
                    Is.SameAs(pending),
                    "Calling ZeroPointAdjustment has to report the event to a "
                        + "subscribed client.");
                Assert.That(await pending.ConfigureAwait(false), Is.True);
            }
            finally
            {
                await events.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Subscribes to the machine's OPC 40001-1 <c>Notifications</c> add-in
        /// and reports an event on it while the subscription is live.
        /// </summary>
        /// <remarks>
        /// OPC 40001-1 fixes the notifier but not the event types, so what is
        /// asserted is that an event reported on the add-in reaches a client
        /// that subscribed to it; the stock <c>SystemEventType</c> stands in
        /// for a vendor event, exactly as the sample does.
        /// </remarks>
        private static async Task AssertNotificationEventAsync(
            MachineryClient machinery,
            NodeId machine,
            PressConfigurator configurator,
            ClientSession session,
            CancellationToken ct)
        {
            Assert.That(
                session.TryGetSubscriptionManager(
                    out ClientSubscriptionManager? subscriptionManager),
                Is.True);
            await using var streaming = new StreamingSubscription(subscriptionManager!);

            using var eventCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            eventCts.CancelAfter(TimeSpan.FromSeconds(30));
            IAsyncEnumerator<BaseEventTypeRecord> events = machinery
                .ObserveNotificationsAsync(
                    machine, streaming, cancellationToken: eventCts.Token)
                .GetAsyncEnumerator(eventCts.Token);
            try
            {
                Task<bool> pending = events.MoveNextAsync().AsTask();
                Assert.That(
                    await WaitForAsync(
                        () => subscriptionManager!.Items.Any(subscription =>
                            subscription.Created &&
                            subscription.MonitoredItems.Items.Any(item =>
                                item.Created && ServiceResult.IsGood(item.Error))),
                        TimeSpan.FromSeconds(20),
                        eventCts.Token).ConfigureAwait(false),
                    Is.True,
                    "The notification subscription has to come up.");

                IMachineryNotificationPublisher notifications =
                    configurator.Machine!.Notifications!;
                var notification = new SystemEventState(null);
                notification.Initialize(
                    notifications.Context,
                    notifications.Notifier,
                    EventSeverity.Low,
                    new LocalizedText(kNotificationMessage));
                notification.TypeDefinitionId = Opc.Ua.ObjectTypeIds.SystemEventType;
                notification.EventType!.Value = Opc.Ua.ObjectTypeIds.SystemEventType;
                await notifications.PublishAsync(notification, ct).ConfigureAwait(false);

                Assert.That(
                    await Task.WhenAny(pending, Task.Delay(TimeSpan.FromSeconds(25), ct))
                        .ConfigureAwait(false),
                    Is.SameAs(pending),
                    "An event reported on the Notifications add-in has to reach a "
                        + "subscribed client.");
                Assert.That(await pending.ConfigureAwait(false), Is.True);
                Assert.That(
                    events.Current.Message.Text,
                    Is.EqualTo(kNotificationMessage));
            }
            finally
            {
                await events.DisposeAsync().ConfigureAwait(false);
            }
        }

        private static async Task AssertEnergyAsync(
            MachineryClient machinery,
            NodeId machine,
            CancellationToken ct)
        {
            var resources = new List<MachineEntry>();
            await foreach (MachineEntry entry in machinery
                .EnumerateEnergyResourcesAsync(machine, ct)
                .ConfigureAwait(false))
            {
                resources.Add(entry);
            }

            // OPC 40001-4 §9.1 fixes the resource browse names, and the
            // resources hang below Monitoring/Consumption rather than off the
            // machine.
            Assert.That(
                resources.ConvertAll(entry => entry.BrowseName.Name),
                Is.EquivalentTo(s_expectedResources));

            MachineEntry air = resources.Find(
                entry => entry.BrowseName.Name == "CompressedAir")!;
            MachineryMeteringPoint? main = await machinery
                .ReadMainMeteringPointAsync(air.NodeId, ct)
                .ConfigureAwait(false);
            Assert.That(
                main,
                Is.Not.Null,
                "OPC 40001-4 requires a Main metering point per resource.");
            Assert.That(main!.ApplicationTag, Is.EqualTo("press/CompressedAir"));

            // The same point read directly by its own NodeId, which is the
            // path a client takes for a sub-metering point below Main.
            MachineryMeteringPoint? byNodeId = await machinery
                .ReadMeteringPointAsync(main.NodeId, ct)
                .ConfigureAwait(false);
            Assert.That(byNodeId, Is.Not.Null);
            Assert.That(byNodeId!.NodeId, Is.EqualTo(main.NodeId));
            Assert.That(byNodeId.ApplicationTag, Is.EqualTo(main.ApplicationTag));

            var measurements = new Dictionary<string, Variant>(StringComparer.Ordinal);
            foreach (MachineryMeasurementValue measurement in main.Measurements)
            {
                measurements[measurement.BrowseName.Name ?? string.Empty] =
                    measurement.Value;
            }
            Assert.That(measurements, Contains.Key("NeEnergyImportHp"));
            Assert.That(measurements, Contains.Key("Pressure"));
            Assert.That(measurements, Contains.Key("VolumeFlowRate"));
            Assert.That(
                measurements["Pressure"].TryGetValue(out float pressure),
                Is.True);
            Assert.That(pressure, Is.EqualTo(620_000.0f));

            await AssertSubMetersAsync(machinery, air.NodeId, main, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// OPC 40001-4 §8.1: a resource publishes further metering points next
        /// to <c>Main</c>, and <c>Contains</c> runs from <c>Main</c> to them.
        /// </summary>
        private static async Task AssertSubMetersAsync(
            MachineryClient machinery,
            NodeId resource,
            MachineryMeteringPoint main,
            CancellationToken ct)
        {
            var points = new List<string>();
            await foreach (MachineEntry point in machinery
                .EnumerateMeteringPointsAsync(resource, ct)
                .ConfigureAwait(false))
            {
                points.Add(point.BrowseName.Name ?? string.Empty);
            }
            Assert.That(points, Is.EquivalentTo(s_expectedMeteringPoints));

            var subMeters = new List<MachineEntry>();
            await foreach (MachineEntry subMeter in machinery
                .EnumerateSubMetersAsync(main.NodeId, ct)
                .ConfigureAwait(false))
            {
                subMeters.Add(subMeter);
            }
            Assert.That(subMeters, Has.Count.EqualTo(1));
            Assert.That(subMeters[0].BrowseName.Name, Is.EqualTo("ClampingCylinders"));

            var readings = new List<MachineryMeteringPoint>();
            await foreach (MachineryMeteringPoint reading in machinery
                .ReadSubMetersAsync(main.NodeId, ct)
                .ConfigureAwait(false))
            {
                readings.Add(reading);
            }
            Assert.That(readings, Has.Count.EqualTo(1));
            Assert.That(readings[0].ApplicationTag, Is.EqualTo("press/CompressedAir/Clamping"));
            MachineryMeasurementValue[] subMeterReadings = [.. readings[0].Measurements];
            Assert.That(
                subMeterReadings.Any(measurement => measurement.BrowseName.Name == "VolumeFlowRate"),
                Is.True);

            // Contains is directed: the sub-meter contains nothing.
            var nested = new List<MachineEntry>();
            await foreach (MachineEntry entry in machinery
                .EnumerateSubMetersAsync(subMeters[0].NodeId, ct)
                .ConfigureAwait(false))
            {
                nested.Add(entry);
            }
            Assert.That(nested, Is.Empty);
        }

        private static async Task AssertJobsAsync(
            MachineryClient machinery,
            NodeId machine,
            CancellationToken ct)
        {
            Isa95JobControlV2Client? jobs = await machinery
                .JobManagementAsync(machine, ct)
                .ConfigureAwait(false);
            Assert.That(
                jobs,
                Is.Not.Null,
                "OPC 40001-3 composes the ISA-95 Job Control V2 surface.");

            var order = new V2.ISA95JobOrderDataType
            {
                JobOrderID = "E2E-1",
                Description = new[] { new LocalizedText("End-to-end job") }.ToArrayOf()
            };
            ulong returnStatus = await jobs!.StoreAndStartAsync(order, ct: ct)
                .ConfigureAwait(false);
            Assert.That(
                returnStatus,
                Is.EqualTo(Isa95JobReturnStatus.Success),
                "StoreAndStart must be accepted by the provider behind the machine.");

            // OPC 40001-3 adds no verbs of its own: what has to work is that
            // the ISA-95 receiver below the machine is the one that answered,
            // which the downloadable job-order list shows.
            ArrayOf<V2.ISA95JobOrderAndStateDataType> list = await ReadJobOrderListAsync(
                jobs,
                ct).ConfigureAwait(false);
            bool stored = false;
            foreach (V2.ISA95JobOrderAndStateDataType entry in list)
            {
                stored |= entry.JobOrder?.JobOrderID == "E2E-1";
            }
            Assert.That(
                stored,
                Is.True,
                "The downloadable job-order list has to show the stored order.");
        }

        /// <summary>
        /// Reads the <c>JobOrderList</c> the receiver publishes.
        /// </summary>
        private static async Task<ArrayOf<V2.ISA95JobOrderAndStateDataType>>
            ReadJobOrderListAsync(Isa95JobControlV2Client jobs, CancellationToken ct)
        {
            NodeId list = await ResolveJobListAsync(
                jobs.Session,
                jobs.JobOrderReceiverId,
                V2.BrowseNames.JobOrderList,
                ct).ConfigureAwait(false);
            ReadResponse read = await jobs.Session.ReadAsync(
                requestHeader: null,
                maxAge: 0,
                timestampsToReturn: TimestampsToReturn.Neither,
                nodesToRead: new[]
                {
                    new ReadValueId
                    {
                        NodeId = list,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                ct: ct).ConfigureAwait(false);
            Assert.That(
                read.Results[0].WrappedValue.TryGetStructure(
                    out ArrayOf<V2.ISA95JobOrderAndStateDataType> orders),
                Is.True);
            return orders;
        }

        /// <summary>
        /// Resolves a list the receiver or the response provider publishes.
        /// The typed proxy exposes the verbs but not the list variables, so
        /// they are resolved by browse path.
        /// </summary>
        private static async Task<NodeId> ResolveJobListAsync(
            ClientSession session,
            NodeId parent,
            string browseName,
            CancellationToken ct)
        {
            int namespaceIndex = session.NamespaceUris.GetIndex(V2.Namespaces.ISA95JobControlV2);
            Assert.That(namespaceIndex, Is.GreaterThanOrEqualTo(0));
            var path = new BrowsePath
            {
                StartingNode = parent,
                RelativePath = new RelativePath
                {
                    Elements =
                    [
                        new RelativePathElement
                        {
                            ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                            IncludeSubtypes = true,
                            TargetName = new QualifiedName(browseName, (ushort)namespaceIndex)
                        }
                    ]
                }
            };
            TranslateBrowsePathsToNodeIdsResponse translated = await session
                .TranslateBrowsePathsToNodeIdsAsync(null, new[] { path }.ToArrayOf(), ct)
                .ConfigureAwait(false);
            Assert.That(translated.Results[0].Targets, Is.Not.Empty);
            return ExpandedNodeId.ToNodeId(
                translated.Results[0].Targets[0].TargetId,
                session.NamespaceUris);
        }

        /// <summary>
        /// Subscribes to the machine's <c>JobOrderList</c> and
        /// <c>JobOrderResponseList</c> and changes both, once and then twice in
        /// quick succession. Every change has to reach the monitored items:
        /// the lists serve their value from a snapshot, so a change is only
        /// published when the machine reports it, and the second of two quick
        /// refreshes can stamp the same source timestamp as the first.
        /// </summary>
        private static async Task AssertJobListNotificationsAsync(
            MachineryClient machinery,
            InMemoryIsa95JobControlProvider jobProvider,
            ClientSession session,
            CancellationToken ct)
        {
            ArrayOf<NodeId> machines = await machinery.DiscoverMachinesAsync(ct)
                .ConfigureAwait(false);
            Isa95JobControlV2Client? jobs = await machinery
                .JobManagementAsync(machines[0], ct)
                .ConfigureAwait(false);
            Assert.That(jobs, Is.Not.Null);
            NodeId orderListId = await ResolveJobListAsync(
                session,
                jobs!.JobOrderReceiverId,
                V2.BrowseNames.JobOrderList,
                ct).ConfigureAwait(false);
            NodeId responseListId = await ResolveJobListAsync(
                session,
                jobs.JobResponseProviderId,
                V2.BrowseNames.JobOrderResponseList,
                ct).ConfigureAwait(false);

            Assert.That(
                session.TryGetSubscriptionManager(
                    out ClientSubscriptionManager? subscriptionManager),
                Is.True);
            var orderLists = new ConcurrentQueue<DataValue>();
            var responseLists = new ConcurrentQueue<DataValue>();
            var streaming = new StreamingSubscription(subscriptionManager!);
            using var streamCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            Task orderStream = CollectAsync(
                streaming.SubscribeDataChangesAsync(orderListId, ct: streamCts.Token),
                orderLists);
            Task responseStream = CollectAsync(
                streaming.SubscribeDataChangesAsync(responseListId, ct: streamCts.Token),
                responseLists);
            try
            {
                // A monitored item reports the current value first; only what
                // follows shows that the machine reports changes.
                Assert.That(
                    await WaitForAsync(
                        () => !orderLists.IsEmpty && !responseLists.IsEmpty,
                        TimeSpan.FromSeconds(20),
                        ct).ConfigureAwait(false),
                    Is.True,
                    "The job list monitored items have to report their current value.");

                Assert.That(
                    await jobs.StoreAsync(
                        new V2.ISA95JobOrderDataType { JobOrderID = "E2E-N1" },
                        ct: ct).ConfigureAwait(false),
                    Is.EqualTo(Isa95JobReturnStatus.Success));
                await AssertNotifiedAsync(orderLists, ListsJobOrder, "E2E-N1", ct)
                    .ConfigureAwait(false);
                await ReceiveResponseAsync(jobProvider, "E2E-N1", ct).ConfigureAwait(false);
                await AssertNotifiedAsync(responseLists, ListsJobResponse, "E2E-N1-R", ct)
                    .ConfigureAwait(false);

                // Back to back, so both refreshes can fall into one clock tick.
                await StoreExternallyAsync(jobProvider, "E2E-N2", ct).ConfigureAwait(false);
                await StoreExternallyAsync(jobProvider, "E2E-N3", ct).ConfigureAwait(false);
                await AssertNotifiedAsync(orderLists, ListsJobOrder, "E2E-N3", ct)
                    .ConfigureAwait(false);
                await ReceiveResponseAsync(jobProvider, "E2E-N2", ct).ConfigureAwait(false);
                await ReceiveResponseAsync(jobProvider, "E2E-N3", ct).ConfigureAwait(false);
                await AssertNotifiedAsync(responseLists, ListsJobResponse, "E2E-N3-R", ct)
                    .ConfigureAwait(false);
            }
            finally
            {
                streamCts.Cancel();
                await streaming.DisposeAsync().ConfigureAwait(false);
                await Task.WhenAll(orderStream, responseStream).ConfigureAwait(false);
            }
        }

        private static async Task StoreExternallyAsync(
            InMemoryIsa95JobControlProvider jobProvider,
            string jobOrderId,
            CancellationToken ct)
        {
            Isa95JobOrderReceiptV2 receipt = await jobProvider.ReceiveJobOrderAsync(
                Isa95JobOrderOperationV2.Store,
                new V2.ISA95JobOrderDataType { JobOrderID = jobOrderId },
                cancellationToken: ct).ConfigureAwait(false);
            Assert.That(receipt.ReturnStatus, Is.EqualTo(Isa95JobReturnStatus.Success));
        }

        private static async Task ReceiveResponseAsync(
            InMemoryIsa95JobControlProvider jobProvider,
            string jobOrderId,
            CancellationToken ct)
        {
            Isa95JobResponseReceiptV2 receipt = await jobProvider.ReceiveJobResponseAsync(
                new V2.ISA95JobResponseDataType
                {
                    JobResponseID = jobOrderId + "-R",
                    JobOrderID = jobOrderId,
                    JobState = new[]
                    {
                        new V2.ISA95StateDataType
                        {
                            BrowsePath = new RelativePath(),
                            StateText = new LocalizedText("Running"),
                            StateNumber = 3
                        }
                    }.ToArrayOf()
                },
                ct).ConfigureAwait(false);
            Assert.That(receipt.ReturnStatus, Is.EqualTo(Isa95JobReturnStatus.Success));
        }

        private static async Task AssertNotifiedAsync(
            ConcurrentQueue<DataValue> values,
            Func<DataValue, string, bool> lists,
            string id,
            CancellationToken ct)
        {
            Assert.That(
                await WaitForAsync(
                    () => values.Any(value => lists(value, id)),
                    TimeSpan.FromSeconds(10),
                    ct).ConfigureAwait(false),
                Is.True,
                $"'{id}' has to reach the monitored item of the job list.");
        }

        private static bool ListsJobOrder(DataValue value, string jobOrderId)
        {
            if (!value.WrappedValue.TryGetStructure(
                out ArrayOf<V2.ISA95JobOrderAndStateDataType> orders))
            {
                return false;
            }
            foreach (V2.ISA95JobOrderAndStateDataType order in orders)
            {
                if (order?.JobOrder?.JobOrderID == jobOrderId)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool ListsJobResponse(DataValue value, string jobResponseId)
        {
            if (!value.WrappedValue.TryGetStructure(
                out ArrayOf<V2.ISA95JobResponseDataType> responses))
            {
                return false;
            }
            foreach (V2.ISA95JobResponseDataType response in responses)
            {
                if (response?.JobResponseID == jobResponseId)
                {
                    return true;
                }
            }
            return false;
        }

        private static async Task CollectAsync(
            IAsyncEnumerable<DataValueChange> changes,
            ConcurrentQueue<DataValue> values)
        {
            try
            {
                await foreach (DataValueChange change in changes.ConfigureAwait(false))
                {
                    values.Enqueue(change.Value);
                }
            }
            catch (OperationCanceledException)
            {
                // The test cancels the stream once it is done with it.
            }
        }

        private static async Task AssertJobControlLifecycleAsync(
            MachineryClient machinery,
            InMemoryIsa95JobControlProvider jobProvider,
            CancellationToken ct)
        {
            ArrayOf<NodeId> machines = await machinery.DiscoverMachinesAsync(ct)
                .ConfigureAwait(false);
            NodeId machine = machines[0];
            Isa95JobControlV2Client? jobs = await machinery
                .JobManagementAsync(machine, ct)
                .ConfigureAwait(false);
            Assert.That(jobs, Is.Not.Null);

            const string jobOrderId = "E2E-JC";
            // JobOrderParameters and Subparameters are optional fields: only the
            // encoding mask puts them on the wire.
            var order = new V2.ISA95JobOrderDataType
            {
                JobOrderID = jobOrderId,
                EncodingMask = (uint)V2.ISA95JobOrderDataTypeFields.JobOrderParameters,
                JobOrderParameters = new[]
                {
                    Parameter(
                        "JobName",
                        Variant.From(new[] { new LocalizedText("en", "Bracket batch") }.ToArrayOf())),
                    Parameter("RunsPlanned", Variant.From(3u)),
                    new V2.ISA95ParameterDataType
                    {
                        ID = "PlannedOrderQuantity",
                        Value = Variant.From(120.0),
                        EncodingMask = (uint)V2.ISA95ParameterDataTypeFields.Subparameters,
                        Subparameters = new[]
                        {
                            Parameter("Overproduction", Variant.From(true))
                        }.ToArrayOf()
                    },
                    Parameter("PlannedTimePerRun", Variant.From(60_000.0)),
                    Parameter(
                        "JobExecutionMode",
                        Variant.From((int)Opc.Ua.Machinery.Jobs.JobExecutionMode.ProductionMode)),
                    Parameter("CustomerSpecific", Variant.From("kept"))
                }.ToArrayOf()
            };
            Assert.That(
                await jobs!.StoreAsync(order, ct: ct).ConfigureAwait(false),
                Is.EqualTo(Isa95JobReturnStatus.Success));
            await AssertJobStateAsync(machinery, machine, jobOrderId, "NotAllowedToStart", null, ct)
                .ConfigureAwait(false);

            MachineryJobOrderParameters? planned = await machinery
                .ReadJobOrderParametersAsync(machine, jobOrderId, ct)
                .ConfigureAwait(false);
            Assert.That(planned, Is.Not.Null);
            Assert.That(planned!.JobName[0].Text, Is.EqualTo("Bracket batch"));
            Assert.That(planned.RunsPlanned, Is.EqualTo(3u));
            Assert.That(planned.PlannedOrderQuantity, Is.EqualTo(120.0));
            Assert.That(
                planned.Overproduction,
                Is.True,
                "OPC 40001-3 Table 1 makes Overproduction a sub-parameter of PlannedOrderQuantity.");
            Assert.That(planned.PlannedTimePerRun, Is.EqualTo(60_000.0));
            Assert.That(
                planned.JobExecutionMode,
                Is.EqualTo(Opc.Ua.Machinery.Jobs.JobExecutionMode.ProductionMode));
            Assert.That(planned.PlannedSetupTime, Is.Null);
            Assert.That(planned.Parameters.Count, Is.EqualTo(6), "Nothing is dropped.");
            Assert.That(
                await machinery.ReadJobOrderParametersAsync(machine, "unknown", ct)
                    .ConfigureAwait(false),
                Is.Null);

            // RevokeStart only takes an order back that is allowed to start.
            Assert.That(
                await jobs.RevokeStartAsync(jobOrderId, ct: ct).ConfigureAwait(false),
                Is.Not.EqualTo(Isa95JobReturnStatus.Success),
                "An order that was never allowed to start cannot be revoked.");
            Assert.That(
                await jobs.StartAsync(jobOrderId, ct: ct).ConfigureAwait(false),
                Is.EqualTo(Isa95JobReturnStatus.Success));
            await AssertJobStateAsync(machinery, machine, jobOrderId, "AllowedToStart", null, ct)
                .ConfigureAwait(false);
            Assert.That(
                await jobs.PauseAsync(jobOrderId, ct: ct).ConfigureAwait(false),
                Is.Not.EqualTo(Isa95JobReturnStatus.Success),
                "Only a running order can be paused.");
            Assert.That(
                await jobs.RevokeStartAsync(
                    jobOrderId,
                    new[] { new LocalizedText("en", "Material missing") }.ToArrayOf(),
                    ct).ConfigureAwait(false),
                Is.EqualTo(Isa95JobReturnStatus.Success));
            await AssertJobStateAsync(machinery, machine, jobOrderId, "NotAllowedToStart", null, ct)
                .ConfigureAwait(false);

            // The machine picks the order up again and starts executing it -
            // a transition the machine makes, not the client.
            Assert.That(
                await jobs.StartAsync(jobOrderId, ct: ct).ConfigureAwait(false),
                Is.EqualTo(Isa95JobReturnStatus.Success));
            Isa95JobOrderReceiptV2 began = await jobProvider
                .TransitionAsync(jobOrderId, Isa95JobExecutionTransition.BeginExecution, ct)
                .ConfigureAwait(false);
            Assert.That(began.ReturnStatus, Is.EqualTo(Isa95JobReturnStatus.Success));
            await AssertJobStateAsync(machinery, machine, jobOrderId, "Running", null, ct)
                .ConfigureAwait(false);

            Assert.That(
                await jobs.RevokeStartAsync(jobOrderId, ct: ct).ConfigureAwait(false),
                Is.Not.EqualTo(Isa95JobReturnStatus.Success),
                "A running order cannot be revoked.");
            Assert.That(
                await jobs.ResumeAsync(jobOrderId, ct: ct).ConfigureAwait(false),
                Is.Not.EqualTo(Isa95JobReturnStatus.Success),
                "A running order has nothing to resume.");

            Assert.That(
                await jobs.PauseAsync(jobOrderId, ct: ct).ConfigureAwait(false),
                Is.EqualTo(Isa95JobReturnStatus.Success));
            await AssertJobStateAsync(
                machinery,
                machine,
                jobOrderId,
                "Interrupted",
                "Suspended",
                ct).ConfigureAwait(false);
            Assert.That(
                await jobs.PauseAsync(jobOrderId, ct: ct).ConfigureAwait(false),
                Is.Not.EqualTo(Isa95JobReturnStatus.Success),
                "A suspended order cannot be paused again.");

            Assert.That(
                await jobs.ResumeAsync(jobOrderId, ct: ct).ConfigureAwait(false),
                Is.EqualTo(Isa95JobReturnStatus.Success));
            await AssertJobStateAsync(machinery, machine, jobOrderId, "Running", null, ct)
                .ConfigureAwait(false);

            // The verbs for an order the machine does not know are refused
            // rather than faulting the call.
            Assert.That(
                await jobs.PauseAsync("unknown", ct: ct).ConfigureAwait(false),
                Is.Not.EqualTo(Isa95JobReturnStatus.Success));
            Assert.That(
                await jobs.ResumeAsync("unknown", ct: ct).ConfigureAwait(false),
                Is.Not.EqualTo(Isa95JobReturnStatus.Success));
            Assert.That(
                await jobs.RevokeStartAsync("unknown", ct: ct).ConfigureAwait(false),
                Is.Not.EqualTo(Isa95JobReturnStatus.Success));

            await AssertJobResponseParametersAsync(machinery, machine, jobProvider, jobOrderId, ct)
                .ConfigureAwait(false);
        }

        private static async Task AssertJobResponseParametersAsync(
            MachineryClient machinery,
            NodeId machine,
            InMemoryIsa95JobControlProvider jobProvider,
            string jobOrderId,
            CancellationToken ct)
        {
            Assert.That(
                await machinery.ReadJobResponseParametersAsync(machine, jobOrderId, ct)
                    .ConfigureAwait(false),
                Is.Null,
                "No response has been reported yet.");

            var response = new V2.ISA95JobResponseDataType
            {
                JobResponseID = jobOrderId + "-R1",
                JobOrderID = jobOrderId,
                JobState = new[]
                {
                    new V2.ISA95StateDataType
                    {
                        BrowsePath = new RelativePath(),
                        StateText = new LocalizedText("Running"),
                        StateNumber = 3
                    }
                }.ToArrayOf(),
                JobResponseData = new[]
                {
                    Parameter("RunsStarted", Variant.From(2u)),
                    Parameter("RunsCompleted", Variant.From(1u)),
                    Parameter("ProducedQuantity", Variant.From(41.0)),
                    Parameter("GoodQuantity", Variant.From(40.0)),
                    Parameter(
                        "JobResult",
                        Variant.From((int)Opc.Ua.Machinery.Jobs.JobResult.Successful))
                }.ToArrayOf()
            };
            Isa95JobResponseReceiptV2 received = await jobProvider
                .ReceiveJobResponseAsync(response, ct)
                .ConfigureAwait(false);
            Assert.That(received.ReturnStatus, Is.EqualTo(Isa95JobReturnStatus.Success));

            MachineryJobResponseParameters? actual = await machinery
                .ReadJobResponseParametersAsync(machine, jobOrderId, ct)
                .ConfigureAwait(false);
            Assert.That(actual, Is.Not.Null);
            Assert.That(actual!.RunsStarted, Is.EqualTo(2u));
            Assert.That(actual.RunsCompleted, Is.EqualTo(1u));
            Assert.That(actual.ProducedQuantity, Is.EqualTo(41.0));
            Assert.That(actual.GoodQuantity, Is.EqualTo(40.0));
            Assert.That(
                actual.JobResult,
                Is.EqualTo(Opc.Ua.Machinery.Jobs.JobResult.Successful));
            Assert.That(actual.EstimatedRemainingTime, Is.Null);
            Assert.That(
                await machinery.ReadJobResponseParametersAsync(machine, "unknown", ct)
                    .ConfigureAwait(false),
                Is.Null);

            // The published list follows once the machine refreshes it.
            Assert.That(
                await WaitForAsync(
                    async () =>
                    {
                        ArrayOf<V2.ISA95JobResponseDataType> responses = await machinery
                            .ReadJobResponsesAsync(machine, ct)
                            .ConfigureAwait(false);
                        V2.ISA95JobResponseDataType[] published = [.. responses];
                        return published.Any(
                            candidate => candidate?.JobResponseID == jobOrderId + "-R1");
                    },
                    TimeSpan.FromSeconds(10),
                    ct).ConfigureAwait(false),
                Is.True,
                "JobOrderResponseList has to show the reported response.");
        }

        /// <summary>
        /// Polls the published <c>JobOrderList</c> until the order reports the
        /// expected top-level state (and sub-state). The list follows a
        /// transition the machine makes itself asynchronously, through the
        /// provider's status stream, so a single read could race it.
        /// </summary>
        private static async Task AssertJobStateAsync(
            MachineryClient machinery,
            NodeId machine,
            string jobOrderId,
            string expectedState,
            string? expectedSubstate,
            CancellationToken ct)
        {
            string? observed = null;
            bool reached = await WaitForAsync(
                async () =>
                {
                    ArrayOf<V2.ISA95JobOrderAndStateDataType> orders = await machinery
                        .ReadJobOrdersAsync(machine, ct)
                        .ConfigureAwait(false);
                    V2.ISA95JobOrderAndStateDataType[] listed = [.. orders];
                    V2.ISA95JobOrderAndStateDataType? entry = listed.FirstOrDefault(
                        candidate => candidate?.JobOrder?.JobOrderID == jobOrderId);
                    if (entry == null || entry.State.Count == 0)
                    {
                        observed = "<absent>";
                        return false;
                    }
                    V2.ISA95StateDataType[] states = [.. entry.State];
                    observed = string.Join("/", states.Select(state => state.StateText.Text));
                    return states[0].StateText.Text == expectedState &&
                        (expectedSubstate == null ||
                            (states.Length > 1 && states[1].StateText.Text == expectedSubstate));
                },
                TimeSpan.FromSeconds(10),
                ct).ConfigureAwait(false);
            Assert.That(
                reached,
                Is.True,
                $"Job order '{jobOrderId}' should report {expectedState}" +
                (expectedSubstate == null ? string.Empty : "/" + expectedSubstate) +
                $" but reports {observed}.");
        }

        private static V2.ISA95ParameterDataType Parameter(string id, Variant value)
        {
            return new V2.ISA95ParameterDataType { ID = id, Value = value };
        }

        /// <summary>
        /// Runs a filtered, ordered <c>GetResultIdListFiltered</c> over the
        /// wire (OPC 40001-101 §7.1.3) and checks that the result transfer
        /// refuses the write side of <c>TemporaryFileTransferType</c>.
        /// </summary>
        private static async Task AssertResultQueriesAndWriteRefusalAsync(
            MachineryClient machinery,
            NodeId machine,
            PressConfigurator configurator,
            ClientSession session,
            CancellationToken ct)
        {
            var day = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
            await configurator.Machine!.Results!.PublishAsync(
                QueryResult("E2E-Q1", "J-A", day.AddHours(2)),
                ct).ConfigureAwait(false);
            await configurator.Machine.Results.PublishAsync(
                QueryResult("E2E-Q2", "J-B", day.AddHours(1)),
                ct).ConfigureAwait(false);
            await configurator.Machine.Results.PublishAsync(
                QueryResult("E2E-Q3", "J-A", day.AddHours(3)),
                ct).ConfigureAwait(false);

            ResultManagementTypeClient? results = await machinery
                .ResultManagementAsync(machine, ct)
                .ConfigureAwait(false);
            Assert.That(results, Is.Not.Null);

            ushort resultNamespace = (ushort)session.NamespaceUris.GetIndex(
                Opc.Ua.Machinery.Result.Namespaces.MachineryResult);
            var resultReadyEventType = new NodeId(
                Opc.Ua.Machinery.Result.ObjectTypes.ResultReadyEventType,
                resultNamespace);
            var filter = new ContentFilter();
            filter.Push(
                FilterOperator.Equals,
                Variant.FromStructure(new SimpleAttributeOperand(
                    resultReadyEventType,
                    new[]
                    {
                        new QualifiedName("Result", resultNamespace),
                        new QualifiedName("ResultMetaData", resultNamespace),
                        new QualifiedName("JobId", resultNamespace)
                    }.ToArrayOf())),
                Variant.From("J-A"));
            ArrayOf<RelativePath> byCreationTime = new[]
            {
                new RelativePath
                {
                    Elements = new[]
                    {
                        new RelativePathElement
                        {
                            ReferenceTypeId = ReferenceTypeIds.HasComponent,
                            IncludeSubtypes = true,
                            TargetName = new QualifiedName("ResultMetaData", resultNamespace)
                        },
                        new RelativePathElement
                        {
                            ReferenceTypeId = ReferenceTypeIds.HasComponent,
                            IncludeSubtypes = true,
                            TargetName = new QualifiedName("CreationTime", resultNamespace)
                        }
                    }.ToArrayOf()
                }
            }.ToArrayOf();

            (uint handle, ArrayOf<string> ids, int error) = await results!
                .GetResultIdListFilteredAsync(filter, byCreationTime, 0, 0, ct)
                .ConfigureAwait(false);
            Assert.That(error, Is.Zero);
            Assert.That(
                ids.ToArray(),
                Is.EqualTo(s_expectedJobAResults),
                "Only J-A, oldest first by CreationTime.");
            Assert.That(handle, Is.Not.Zero);
            await results.ReleaseResultHandleAsync(handle, ct: ct).ConfigureAwait(false);

            (_, ArrayOf<string> capped, _) = await results
                .GetResultIdListFilteredAsync(filter, byCreationTime, 1, 0, ct)
                .ConfigureAwait(false);
            Assert.That(capped.ToArray(), Is.EqualTo(s_expectedFirstJobAResult));

            ServiceResultException invalidOrder = Assert.ThrowsAsync<ServiceResultException>(
                async () => await results
                    .GetResultIdListFilteredAsync(
                        new ContentFilter(),
                        new[]
                        {
                            new RelativePath
                            {
                                Elements = new[]
                                {
                                    new RelativePathElement
                                    {
                                        TargetName = new QualifiedName("NoSuchField")
                                    }
                                }.ToArrayOf()
                            }
                        }.ToArrayOf(),
                        0,
                        0,
                        ct)
                    .ConfigureAwait(false))!;
            Assert.That(
                invalidOrder.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));

            NodeId management = await machinery.ResolveResultManagementAsync(machine, ct)
                .ConfigureAwait(false);
            NodeId transfer = await machinery.ResolveChildAsync(
                management,
                new QualifiedName("ResultTransfer", resultNamespace),
                ct).ConfigureAwait(false);
            var transferClient = new TemporaryFileTransferClient(session, transfer);
            ServiceResultException write = Assert.ThrowsAsync<ServiceResultException>(
                async () => await transferClient.Proxy
                    .GenerateFileForWriteAsync(Variant.Null, ct)
                    .ConfigureAwait(false))!;
            Assert.That(write.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNotWritable));
            ServiceResultException commit = Assert.ThrowsAsync<ServiceResultException>(
                async () => await transferClient.Proxy
                    .CloseAndCommitAsync(4711, ct)
                    .ConfigureAwait(false))!;
            Assert.That(
                commit.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
        }

        private static MachineryResult QueryResult(string resultId, string jobId, DateTime created)
        {
            // Optional fields only reach the wire when the encoding mask says so.
            return new MachineryResult(
                new ResultDataType
                {
                    ResultMetaData = new ResultMetaDataType
                    {
                        ResultId = resultId,
                        JobId = jobId,
                        CreationTime = created,
                        EncodingMask = (uint)(ResultMetaDataTypeFields.JobId |
                            ResultMetaDataTypeFields.CreationTime)
                    }
                });
        }

        private static readonly string[] s_expectedJobAResults = ["E2E-Q1", "E2E-Q3"];
        private static readonly string[] s_expectedFirstJobAResult = ["E2E-Q1"];

        private static async Task AssertResultsAsync(
            MachineryClient machinery,
            NodeId machine,
            PressConfigurator configurator,
            ClientSession session,
            CancellationToken ct)
        {
            byte[] payload = Encoding.UTF8.GetBytes("peakForceKN,4821.5\n");
            await configurator.Machine!.Results!.PublishAsync(
                new MachineryResult(
                    new ResultDataType
                    {
                        ResultMetaData = new ResultMetaDataType { ResultId = "E2E-R1" }
                    },
                    new ByteString(payload),
                    "text/csv"),
                ct).ConfigureAwait(false);

            ResultManagementTypeClient? results = await machinery
                .ResultManagementAsync(machine, ct)
                .ConfigureAwait(false);
            Assert.That(results, Is.Not.Null);

            (uint resultHandle, ResultDataType result, int error) latest = await results!
                .GetLatestResultAsync(timeout: 5000, ct: ct)
                .ConfigureAwait(false);
            Assert.That(latest.error, Is.Zero);
            Assert.That(latest.result?.ResultMetaData?.ResultId, Is.EqualTo("E2E-R1"));

            ByteString downloaded = await machinery
                .DownloadResultAsync(machine, "E2E-R1", ct)
                .ConfigureAwait(false);
            Assert.That(
                downloaded.Span.ToArray(),
                Is.EqualTo(payload),
                "The OPC 10000-5 GenerateFileForRead path has to deliver the payload.");

            await results.ReleaseResultHandleAsync(latest.resultHandle, ct: ct)
                .ConfigureAwait(false);

            var published = new List<ResultDataType>();
            await foreach (ResultDataType result in machinery
                .ReadPublishedResultsAsync(machine, ct)
                .ConfigureAwait(false))
            {
                published.Add(result);
            }
            Assert.That(
                published.ConvertAll(result => result.ResultMetaData?.ResultId),
                Contains.Item("E2E-R1"),
                "The Results folder has to carry the published result variables.");

            await AssertResultEventAsync(machinery, machine, configurator, session, ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Subscribes to the result-ready event and publishes a result while
        /// the subscription is live.
        /// </summary>
        /// <remarks>
        /// This is the assertion the abstract-event-type defect would fail:
        /// the filter selects <c>ResultReadyEventType</c>, and an event
        /// reported with the abstract type itself is what a conformant server
        /// must never send.
        /// </remarks>
        private static async Task AssertResultEventAsync(
            MachineryClient machinery,
            NodeId machine,
            PressConfigurator configurator,
            ClientSession session,
            CancellationToken ct)
        {
            Assert.That(
                session.TryGetSubscriptionManager(
                    out ClientSubscriptionManager? subscriptionManager),
                Is.True);
            await using var streaming = new StreamingSubscription(subscriptionManager!);

            using var eventCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            eventCts.CancelAfter(TimeSpan.FromSeconds(30));
            IAsyncEnumerator<ResultReadyEventTypeRecord> events = machinery
                .ObserveResultsAsync(machine, streaming, cancellationToken: eventCts.Token)
                .GetAsyncEnumerator(eventCts.Token);
            try
            {
                Task<bool> pending = events.MoveNextAsync().AsTask();
                Assert.That(
                    await WaitForAsync(
                        () => subscriptionManager!.Items.Any(subscription =>
                            subscription.Created &&
                            subscription.MonitoredItems.Items.Any(item =>
                                item.Created && ServiceResult.IsGood(item.Error))),
                        TimeSpan.FromSeconds(20),
                        eventCts.Token).ConfigureAwait(false),
                    Is.True,
                    "The event subscription has to come up.");

                await configurator.Machine!.Results!.PublishAsync(
                    new MachineryResult(
                        new ResultDataType
                        {
                            ResultMetaData = new ResultMetaDataType { ResultId = "E2E-R2" }
                        }),
                    ct).ConfigureAwait(false);

                Assert.That(
                    await Task.WhenAny(pending, Task.Delay(TimeSpan.FromSeconds(25), ct))
                        .ConfigureAwait(false),
                    Is.SameAs(pending),
                    "A result-ready event has to reach a subscribed client.");
                Assert.That(await pending.ConfigureAwait(false), Is.True);
                Assert.That(
                    events.Current.Result?.ResultMetaData?.ResultId,
                    Is.EqualTo("E2E-R2"));
            }
            finally
            {
                await events.DisposeAsync().ConfigureAwait(false);
            }
        }

        private static async Task AssertConformanceAsync(
            ClientSession session,
            CancellationToken ct)
        {
            ReadResponse response = await session.ReadAsync(
                requestHeader: null,
                maxAge: 0,
                timestampsToReturn: TimestampsToReturn.Neither,
                nodesToRead: new[]
                {
                    new ReadValueId
                    {
                        NodeId = Opc.Ua.VariableIds.Server_ServerCapabilities_ServerProfileArray,
                        AttributeId = Attributes.Value
                    },
                    new ReadValueId
                    {
                        NodeId = Opc.Ua.VariableIds.Server_ServerCapabilities_ConformanceUnits,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                ct: ct).ConfigureAwait(false);

            Assert.That(
                response.Results[0].WrappedValue.TryGetValue(out ArrayOf<string> profiles),
                Is.True);
            string[] published = [.. profiles];

            // The URIs are the published values from the OPC 40001 profile
            // tables, reproduced verbatim — a facet URI is matched literally.
            Assert.That(
                published,
                Contains.Item(
                    "http://opcfoundation.org/UA-Profile/Machinery/Server/MachineIdentification"));
            Assert.That(
                published,
                Contains.Item("http://opcfoundation.org/UA-Profile/Machinery/Server/State"));
            Assert.That(
                published,
                Contains.Item(
                    "http://opcfoundation.org/UA/Machinery/ProcessValues/Server/Base/"));
            Assert.That(
                published,
                Contains.Item(
                    "http://opcfoundation.org/UA-Profile/Machinery/Jobs/Server/Base"));
            Assert.That(
                published,
                Contains.Item(
                    "http://opcfoundation.org/UA-Profile/Machinery/Energy/Server/Base"));
            Assert.That(
                published,
                Contains.Item(
                    "http://opcfoundation.org/UA-Profile/Machinery/Result/Server/" +
                    "SimpleResultTransfer"));

            Assert.That(
                response.Results[1].WrappedValue.TryGetValue(
                    out ArrayOf<QualifiedName> units),
                Is.True);
            QualifiedName[] conformanceUnits = [.. units];
            Assert.That(
                conformanceUnits,
                Contains.Item(new QualifiedName("Machinery Building Block Organization")));
            Assert.That(
                conformanceUnits,
                Contains.Item(new QualifiedName("Machinery Energy Base Structure")));
            Assert.That(
                conformanceUnits,
                Contains.Item(new QualifiedName("Machinery-Result GetLatestResult")));
        }

        private static readonly string[] s_expectedResources =
            ["CompressedAir", "Electricity"];

        private static readonly string[] s_expectedMeteringPoints =
            ["Main", "ClampingCylinders"];

        private static async Task<bool> WaitForAsync(
            Func<Task<bool>> condition,
            TimeSpan timeout,
            CancellationToken ct)
        {
            DateTime deadline = DateTime.UtcNow.Add(timeout);
            while (DateTime.UtcNow < deadline)
            {
                if (await condition().ConfigureAwait(false))
                {
                    return true;
                }
                await Task.Delay(100, ct).ConfigureAwait(false);
            }
            return await condition().ConfigureAwait(false);
        }

        private static async Task<bool> WaitForAsync(
            Func<bool> condition,
            TimeSpan timeout,
            CancellationToken ct)
        {
            DateTime deadline = DateTime.UtcNow.Add(timeout);
            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                {
                    return true;
                }
                await Task.Delay(100, ct).ConfigureAwait(false);
            }
            return condition();
        }

        /// <summary>
        /// Hosts the stock Machinery node manager and builds the machine
        /// inside the manager's address-space creation, so the conformance
        /// snapshot the server publishes reflects what was actually built.
        /// </summary>
        private sealed class MachineryIntegrationServer : StandardServer
        {
            public MachineryIntegrationServer(
                ITelemetryContext telemetry,
                PressConfigurator configurator)
                : base(telemetry)
            {
                m_configurator = configurator;
            }

            protected override ValueTask<IMasterNodeManager> CreateMasterNodeManagerAsync(
                IServerInternal server,
                ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                var options = new MachineryServerOptions { Parts = MachineryParts.All };
                var manager = new MachineryNodeManager(
                    server,
                    configuration,
                    new IMachineryModelProvider[]
                    {
                        new MachineryModelProvider(options.Parts)
                    }.ToArrayOf(),
                    options,
                    new DelegatePostSetupRunner(m_configurator));
                Manager = manager;
                IMasterNodeManager master = new MasterNodeManager(
                    server,
                    configuration,
                    null,
                    [manager]);
                return new ValueTask<IMasterNodeManager>(master);
            }

            public MachineryNodeManager? Manager { get; private set; }

            private readonly PressConfigurator m_configurator;
        }

        /// <summary>
        /// The post-setup hook a hosted server gets from dependency
        /// injection. Running the build here rather than after startup is
        /// what puts the machine in place before the server snapshots its
        /// conformance units.
        /// </summary>
        private sealed class DelegatePostSetupRunner : IDiPostSetupRunner
        {
            public DelegatePostSetupRunner(PressConfigurator configurator)
            {
                m_configurator = configurator;
            }

            public async ValueTask RunAsync(
                DiNodeManager manager,
                CancellationToken cancellationToken)
            {
                if (manager is not MachineryNodeManager machinery)
                {
                    return;
                }
                IMachineryBuildContext context =
                    machinery.CreateMachineryBuildContext(cancellationToken);
                await m_configurator.ConfigureAsync(context, cancellationToken)
                    .ConfigureAwait(false);
                await context.SealAsync(cancellationToken).ConfigureAwait(false);
            }

            private readonly PressConfigurator m_configurator;
        }

        /// <summary>
        /// Builds the machine the client walks. Every part appears once.
        /// </summary>
        private sealed class PressConfigurator
        {
            public PressConfigurator(InMemoryIsa95JobControlProvider jobProvider)
            {
                m_jobProvider = jobProvider;
            }

            public IMachineHandle<BaseObjectState>? Machine { get; private set; }

            public IProcessValueHandle? OilTemperature { get; private set; }

            public async ValueTask ConfigureAsync(
                IMachineryBuildContext context,
                CancellationToken cancellationToken)
            {
                IProcessValueHandle? oilTemperature = null;
                Machine = await context
                    .AddMachine(new QualifiedName("Press-E2E"))
                    .WithIdentification(id =>
                    {
                        id.Manufacturer = new LocalizedText("Acme");
                        id.Model = new LocalizedText("HydraPress 500");
                        id.SerialNumber = "SN-E2E";
                        id.ProductInstanceUri = "urn:acme:press:e2e";
                        id.Location = "Hall 3";
                    })
                    .WithMonitoring(monitoring => monitoring
                        .WithMachineryItemState(MachineryItemStateValue.NotExecuting)
                        .WithOperationMode(MachineryOperationModeValue.None)
                        .WithHealth())
                    .WithComponents(components => components.AddComponent(
                        new QualifiedName("HydraulicUnit"),
                        component => component.WithIdentification(id =>
                        {
                            id.Manufacturer = new LocalizedText("Acme");
                            id.SerialNumber = "HU-1";
                        })))
                    .WithMachineryEquipment(equipment => equipment.AddEquipment(
                        new QualifiedName("UpperDie"),
                        "urn:acme:equipment:die",
                        die => die.WithEquipmentLife(
                            remaining: 118_400,
                            startValue: 250_000)))
                    .WithNotifications()
                    .WithOperationCounters(counters => counters
                        .WithPowerOnDuration(12_480.0)
                        .WithOperationDuration(9_215.5))
                    .WithLifetimeCounters(counters => counters.AddLifetimeVariable(
                        new QualifiedName("SealLife"),
                        2_000_000,
                        1_412_500))
                    .WithProcessValue(
                        new QualifiedName("HydraulicOilTemperature"),
                        processValue => processValue
                            .WithEngineeringUnits(
                                DegreeCelsius(),
                                new Opc.Ua.Range { Low = -20, High = 120 })
                            .WithLimits(lowLow: 5, low: 15, high: 65, highHigh: 80)
                            .WithSetpoint(45)
                            .WithDeviationLimits(low: -10, high: 10)
                            .WithPercentageValue()
                            .WithStatus()
                            .WithLimitAlarm()
                            .WithDeviationAlarm()
                            .WithZeroPointAdjustment((_, _) =>
                                new ValueTask<StatusCode>(StatusCodes.Good))
                            .WithValue(45)
                            .Bind(out oilTemperature))
                    .WithJobManagement(jobs => jobs
                        .WithJobOrderReceiver(m_jobProvider)
                        .WithJobResponseProvider(m_jobProvider)
                        .WithJobOrderCatalog(m_jobProvider))
                    .WithEnergy(energy => energy
                        .AddResource(MachineryEnergyCarrier.CompressedAir, air =>
                        {
                            air.Main
                                .WithApplicationTag("press/CompressedAir")
                                .WithNonElectricalEnergy(0, 0)
                                .WithVolumeFlow(184_320f, 18.4f)
                                .WithBaseFlow(620_000f, 294.65f);
                            air.AddMeteringPoint(
                                new QualifiedName("ClampingCylinders"),
                                point => point
                                    .WithApplicationTag("press/CompressedAir/Clamping")
                                    .WithNonElectricalEnergy(0, 0)
                                    .WithVolumeFlow(24_576f, 2.4f));
                        })
                        .AddResource(MachineryEnergyCarrier.Electricity, electricity =>
                            electricity.Main.WithApplicationTag("press/Electricity")))
                    .WithResultManagement(results => results
                        .WithInMemoryStore(8)
                        .WithResultsFolder(2)
                        .WithFileTransfer())
                    .BuildAsync(cancellationToken)
                    .ConfigureAwait(false);
                OilTemperature = oilTemperature;
            }

            private static EUInformation DegreeCelsius()
            {
                return new EUInformation
                {
                    NamespaceUri = "http://www.opcfoundation.org/UA/units/un/cefact",
                    UnitId = 4408652,
                    DisplayName = new LocalizedText("°C"),
                    Description = new LocalizedText("degree Celsius")
                };
            }

            private readonly InMemoryIsa95JobControlProvider m_jobProvider;
        }
    }
}
