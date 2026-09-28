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
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Pumps.Client;
using Opc.Ua.Pumps.Server;
using Opc.Ua.Pumps.Server.Builders;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Tests;

namespace Opc.Ua.Pumps.Tests
{
    /// <summary>
    /// Drives a hosted OPC 40223 server with the Pumps client over a real
    /// session. This is the test that decides whether the two packages agree:
    /// everything else checks one side in isolation.
    /// </summary>
    [TestFixture]
    [Category("Pumps")]
    [Category("Hosting")]
    [NonParallelizable]
    public sealed class PumpsClientServerE2eTests
    {
        private static string s_endpointUrl = string.Empty;

        [Test]
        public async Task ClientReadsBackEverythingTheServerPublishesAsync()
        {
            IPumpGroupBuilder? liveMeasurements = null;
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpcUa()
                .AddServer<StandardServer>(ConfigureServer)
                .AddPumps()
                .ConfigurePumps(async context =>
                {
                    IPumpBuilder pump = await context.Manager.CreatePumpAsync(
                        new QualifiedName(
                            "Pump_1",
                            context.Manager.InstanceNamespaceIndex),
                        context.CancellationToken);

                    pump.WithNameplate(new PumpNameplate
                    {
                        NodeId = NodeId.Null,
                        Manufacturer = new LocalizedText("Acme Pumps"),
                        SerialNumber = "SN-001",
                        Model = new LocalizedText("PumpX-2000"),
                        ManufacturerUri = "https://example.invalid/acme",
                        Location = "Plant 1 / Utility Skid",
                        YearOfConstruction = 2025,
                        MonthOfConstruction = 4,
                        CountryOfOrigin = "DE",
                        ArticleNumber = "ART-4711",
                        TypeOfProduct = "Centrifugal pump"
                    });

                    pump.Measurements
                        .SetAnalog(
                            BrowseNames.DifferentialPressure,
                            350_000.0,
                            new EUInformation { DisplayName = new LocalizedText("Pa") },
                            new Range { Low = 0, High = 1_000_000 })
                        .SetAnalog(BrowseNames.MassFlow, 12.5)
                        .SetAnalog(BrowseNames.PumpEfficiency, 0.78);
                    liveMeasurements = pump.Measurements;

                    // <Vibration> is an OptionalPlaceholder (OPC 40223 §7.32).
                    pump.Measurements.AddVibration("DriveEndBearing")
                        .SetAnalog(
                            BrowseNames.OverallVibrationVelocityRMS,
                            2.8,
                            new EUInformation { DisplayName = new LocalizedText("mm/s") });

                    pump.MultiPump
                        .Set(BrowseNames.PumpRole, Variant.From(PumpRoleEnum.Master))
                        .Set(BrowseNames.NumberOfPumps, Variant.From(2u))
                        .Set(
                            BrowseNames.RedundantPumpIDs,
                            Variant.From((ArrayOf<string>)["Pump_2"]));

                    pump.Signals.SetDiscrete(BrowseNames.PumpOperation, true);

                    pump.Design.SetAnalog(BrowseNames.MaximumAllowableWorkingPressure, 1_600_000.0);
                    pump.SystemRequirements.SetAnalog(BrowseNames.MaximumFlow, 20.0);

                    pump.Supervision(BrowseNames.SupervisionProcessFluid)
                        .SetDiscrete(BrowseNames.Cavitation, true)
                        .SetDiscrete(BrowseNames.Dry, false);
                    pump.Supervision(BrowseNames.SupervisionPumpOperation)
                        .SetDiscrete(BrowseNames.MotorOverheat, false);

                    pump.MaintenanceCategory(BrowseNames.GeneralMaintenance)
                        .Set(
                            BrowseNames.StateOfTheItem,
                            Variant.From(StateOfTheItemEnum.OperatingState))
                        .SetAnalog(BrowseNames.OperatingTime, 1234.0);

                    pump.AddPort(PumpPortKind.InletConnection, "Suction")
                        .Set(BrowseNames.Direction, Variant.From(PortDirectionEnum.In));
                    pump.AddPort(PumpPortKind.Drive, "Motor");

                });

            await using ServiceProvider provider = services.BuildServiceProvider();
            IHostedService hostedService = provider.GetServices<IHostedService>().Single();
            await hostedService.StartAsync(CancellationToken.None);
            try
            {
                using var clientFixture = new ClientFixture(NUnitTelemetryContext.Create());
                await clientFixture.LoadClientConfigurationAsync(
                    System.IO.Path.Combine(
                        TestContext.CurrentContext.WorkDirectory,
                        nameof(ClientReadsBackEverythingTheServerPublishesAsync),
                        "client-pki"));
                using Opc.Ua.Client.ISession session = await clientFixture.ConnectAsync(
                    new Uri(s_endpointUrl),
                    SecurityPolicies.None);

                PumpsClient pumps = session.Pumps(NUnitTelemetryContext.Create());
                Assert.That(pumps.IsSupported, Is.True, "the server publishes the Pumps namespace");

                var discovered = new List<PumpEntry>();
                await foreach (PumpEntry entry in pumps.EnumeratePumpsAsync())
                {
                    discovered.Add(entry);
                }

                Assert.That(discovered, Has.Count.EqualTo(1),
                    "the pump is organized into both DeviceSet and Machines, and must " +
                    "still be reported once");
                PumpEntry found = discovered[0];
                Assert.That(found.BrowseName.Name, Is.EqualTo("Pump_1"));

                PumpNameplate? nameplate = await pumps.ReadNameplateAsync(found.NodeId);
                Assert.That(nameplate, Is.Not.Null);
                Assert.Multiple(() =>
                {
                    Assert.That(nameplate!.Manufacturer.Text, Is.EqualTo("Acme Pumps"));
                    Assert.That(nameplate.SerialNumber, Is.EqualTo("SN-001"));
                    Assert.That(nameplate.Model.Text, Is.EqualTo("PumpX-2000"));
                    Assert.That(nameplate.Location, Is.EqualTo("Plant 1 / Utility Skid"));
                    Assert.That(nameplate.YearOfConstruction, Is.EqualTo(2025));
                    Assert.That(nameplate.MonthOfConstruction, Is.EqualTo(4));
                    Assert.That(nameplate.CountryOfOrigin, Is.EqualTo("DE"));
                    Assert.That(nameplate.ArticleNumber, Is.EqualTo("ART-4711"));
                    Assert.That(nameplate.TypeOfProduct, Is.EqualTo("Centrifugal pump"));

                    // Not configured, so not published.
                    Assert.That(nameplate.Supplier, Is.Null);
                });

                PumpValueSet measurements = await pumps.ReadMeasurementsAsync(found.NodeId);
                Assert.Multiple(() =>
                {
                    Assert.That(
                        measurements.GetDouble(BrowseNames.DifferentialPressure),
                        Is.EqualTo(350_000.0));
                    Assert.That(measurements.GetDouble(BrowseNames.MassFlow), Is.EqualTo(12.5));
                    Assert.That(
                        measurements.GetDouble(BrowseNames.PumpEfficiency),
                        Is.EqualTo(0.78));
                    Assert.That(
                        measurements[BrowseNames.DifferentialPressure]!
                            .EngineeringUnits!.DisplayName.Text,
                        Is.EqualTo("Pa"));
                    Assert.That(
                        measurements[BrowseNames.DifferentialPressure]!.EuRange!.High,
                        Is.EqualTo(1_000_000.0));
                });

                PumpValueSet? vibration = measurements.Group("DriveEndBearing");
                Assert.That(vibration, Is.Not.Null,
                    "a <Vibration> instance must be read as a nested group");
                Assert.Multiple(() =>
                {
                    Assert.That(
                        vibration!.GetDouble(BrowseNames.OverallVibrationVelocityRMS),
                        Is.EqualTo(2.8));
                    Assert.That(
                        vibration[BrowseNames.OverallVibrationVelocityRMS]!
                            .EngineeringUnits!.DisplayName.Text,
                        Is.EqualTo("mm/s"));
                    Assert.That(measurements.Contains("DriveEndBearing"), Is.False,
                        "the vibration object is a group, not a value");
                });

                MultiPumpConfiguration? multiPump = await pumps.ReadMultiPumpAsync(found.NodeId);
                Assert.That(multiPump, Is.Not.Null);
                string[] redundantPumpIds = ["Pump_2"];
                Assert.Multiple(() =>
                {
                    Assert.That(multiPump!.PumpRole, Is.EqualTo(PumpRoleEnum.Master));
                    Assert.That(multiPump.NumberOfPumps, Is.EqualTo(2u));
                    Assert.That(multiPump.RedundantPumpIDs, Is.Not.Null);
                    Assert.That(multiPump.RedundantPumpIDs!.Value.ToArray(), Is.EqualTo(redundantPumpIds));
                });

                PumpValueSet signals = await pumps.ReadSignalsAsync(found.NodeId);
                Assert.That(signals.GetBoolean(BrowseNames.PumpOperation), Is.True);

                PumpSupervisionStatus? supervision =
                    await pumps.ReadSupervisionAsync(found.NodeId);
                Assert.That(supervision, Is.Not.Null);
                List<KeyValuePair<string, PumpValue>> raised = [.. supervision!.ActiveSignals];
                Assert.Multiple(() =>
                {
                    Assert.That(supervision.HasActiveSignals, Is.True);
                    Assert.That(raised, Has.Count.EqualTo(1),
                        "only Cavitation is raised; Dry and MotorOverheat are false");
                    Assert.That(raised[0].Value.Name, Is.EqualTo(BrowseNames.Cavitation));
                    Assert.That(
                        raised[0].Key,
                        Is.EqualTo(BrowseNames.SupervisionProcessFluid));
                });

                PumpConfigurationData? configuration =
                    await pumps.ReadConfigurationAsync(found.NodeId);
                Assert.That(configuration, Is.Not.Null);
                Assert.Multiple(() =>
                {
                    Assert.That(
                        configuration!.Design.GetDouble(
                            BrowseNames.MaximumAllowableWorkingPressure),
                        Is.EqualTo(1_600_000.0));
                    Assert.That(
                        configuration.SystemRequirements.GetDouble(BrowseNames.MaximumFlow),
                        Is.EqualTo(20.0));
                });

                PumpMaintenanceData? maintenance =
                    await pumps.ReadMaintenanceAsync(found.NodeId);
                Assert.That(maintenance, Is.Not.Null);
                Assert.Multiple(() =>
                {
                    Assert.That(
                        maintenance!.StateOfTheItem,
                        Is.EqualTo(StateOfTheItemEnum.OperatingState));
                    Assert.That(
                        maintenance.General.GetDouble(BrowseNames.OperatingTime),
                        Is.EqualTo(1234.0));
                });

                ArrayOf<PumpPortDescriptor> ports = await pumps.ReadPortsAsync(found.NodeId);
                Assert.That(ports.Count, Is.EqualTo(2));
                PumpPortDescriptor inlet = FindPort(ports, "Suction");
                PumpPortDescriptor drive = FindPort(ports, "Motor");
                Assert.Multiple(() =>
                {
                    Assert.That(inlet.Kind, Is.EqualTo(PumpPortKind.InletConnection));
                    Assert.That(inlet.Direction, Is.EqualTo(PortDirectionEnum.In));
                    Assert.That(drive.Kind, Is.EqualTo(PumpPortKind.Drive));
                });

                PumpSnapshot snapshot = await pumps.ReadPumpAsync(found.NodeId);
                Assert.Multiple(() =>
                {
                    Assert.That(snapshot.NodeId, Is.EqualTo(found.NodeId));
                    Assert.That(snapshot.BrowseName.Name, Is.EqualTo("Pump_1"));
                    Assert.That(snapshot.Nameplate, Is.Not.Null);
                    Assert.That(snapshot.Operational, Is.Not.Null);
                    Assert.That(snapshot.Configuration, Is.Not.Null);
                    Assert.That(snapshot.Supervision, Is.Not.Null);
                    Assert.That(snapshot.Maintenance, Is.Not.Null);
                    Assert.That(snapshot.Ports.Count, Is.EqualTo(2));
                    Assert.That(
                        snapshot.Operational!.Measurements.GetDouble(BrowseNames.MassFlow),
                        Is.EqualTo(12.5));
                });

                await AssertRuntimeUpdateIsPublishedAsync(
                    clientFixture.Config,
                    measurements[BrowseNames.MassFlow]!.NodeId,
                    () => liveMeasurements!.SetAnalog(BrowseNames.MassFlow, 13.5));
            }
            finally
            {
                await hostedService.StopAsync(CancellationToken.None);
            }
        }

        /// <summary>
        /// Subscribes to a measurement, writes a new value through the builder
        /// and waits for the data change - the path a live pump server takes on
        /// every process update.
        /// </summary>
        private static async Task AssertRuntimeUpdateIsPublishedAsync(
            ApplicationConfiguration configuration,
            NodeId measurement,
            Action write)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            EndpointDescription? endpoint = await CoreClientUtils.SelectEndpointAsync(
                configuration,
                s_endpointUrl,
                useSecurity: false,
                discoverTimeout: 15000,
                telemetry);
            Assert.That(endpoint, Is.Not.Null);
            await using ManagedSession session = await new ManagedSessionBuilder(configuration, telemetry)
                .UseEndpoint(new ConfiguredEndpoint(null, endpoint, EndpointConfiguration.Create(configuration)))
                .WithSessionName(nameof(AssertRuntimeUpdateIsPublishedAsync))
                .ConnectAsync();

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            bool written = false;
            double? received = null;
            await foreach (DataValueChange change in session.DefaultStreaming
                .SubscribeDataChangesAsync(measurement, ct: timeout.Token))
            {
                // The first notification carries the current value; write only
                // once the monitored item is established.
                if (!written)
                {
                    write();
                    written = true;
                    continue;
                }
                if (change.Value.WrappedValue.TryGetValue(out double value) && value == 13.5)
                {
                    received = value;
                    break;
                }
            }
            await session.DefaultStreaming.DisposeAsync();
            Assert.That(received, Is.EqualTo(13.5),
                "a monitored item must see a value written through the builder after it subscribed");
        }

        private static PumpPortDescriptor FindPort(
            ArrayOf<PumpPortDescriptor> ports,
            string name)
        {
            for (int ii = 0; ii < ports.Count; ii++)
            {
                if (ports[ii].Name == name)
                {
                    return ports[ii];
                }
            }
            throw new AssertionException("No port named '" + name + "'.");
        }

        private static void ConfigureServer(OpcUaServerOptions options)
        {
            string applicationName = nameof(PumpsClientServerE2eTests);
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
    }
}
