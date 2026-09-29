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
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Di.Server.Hosting;
using Opc.Ua.Pumps.Client;
using Opc.Ua.Pumps.Server;
using Opc.Ua.Pumps.Server.Builders;
using Opc.Ua.Pumps.Server.Hosting;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Tests;

namespace Opc.Ua.Pumps.Tests
{
    /// <summary>
    /// Closes the runtime gaps the feature suites leave cold: the client
    /// discovery and group reads not exercised elsewhere, the maintenance
    /// contract's computed members, the client factory over a real managed
    /// session, and the post-setup pipeline that dispatches
    /// <c>ConfigurePumps</c> to a live node manager.
    /// </summary>
    [TestFixture]
    [Category("Pumps")]
    [NonParallelizable]
    public sealed class PumpsCoverageTests
    {
        [Test]
        public void MaintenanceDataExposesItsCategoriesAndComputedState()
        {
            var maintenance = new PumpMaintenanceData
            {
                NodeId = new NodeId(1, 1),
                General = new PumpValueSet(
                    new NodeId(2, 1),
                    [
                        Value(BrowseNames.StateOfTheItem,
                            Variant.From(StateOfTheItemEnum.OperatingState)),
                        Value(BrowseNames.MaintenanceLevel,
                            Variant.From(FirstMaintenanceLevel())),
                        Value(BrowseNames.OperatingTime, Variant.From(1234.0))
                    ]),
                Breakdown = new PumpValueSet(
                    new NodeId(3, 1),
                    [Value(BrowseNames.Failure, Variant.From(true))])
            };

            List<KeyValuePair<string, PumpValueSet>> groups = [.. maintenance.Groups];

            Assert.Multiple(() =>
            {
                Assert.That(maintenance.StateOfTheItem,
                    Is.EqualTo(StateOfTheItemEnum.OperatingState));
                Assert.That(maintenance.MaintenanceLevel,
                    Is.EqualTo(FirstMaintenanceLevel()));
                Assert.That(maintenance.HasFailure, Is.True);
                Assert.That(maintenance.General.GetDouble(BrowseNames.OperatingTime),
                    Is.EqualTo(1234.0));

                // General, Preventive, ConditionBased, Breakdown - in order.
                Assert.That(groups, Has.Count.EqualTo(4));
                Assert.That(groups[0].Key, Is.EqualTo(BrowseNames.GeneralMaintenance));
                Assert.That(groups[3].Key, Is.EqualTo(BrowseNames.BreakdownMaintenance));
            });
        }

        [Test]
        public void MaintenanceDataOfAHealthyPumpReportsNoFailureOrState()
        {
            var maintenance = new PumpMaintenanceData { NodeId = new NodeId(1, 1) };

            Assert.Multiple(() =>
            {
                Assert.That(maintenance.StateOfTheItem, Is.Null);
                Assert.That(maintenance.MaintenanceLevel, Is.Null);
                Assert.That(maintenance.HasFailure, Is.Null);
                Assert.That(maintenance.Groups, Is.Not.Empty);
            });
        }

        [Test]
        public void PumpValueCoercionsCoverEveryTypedAccessor()
        {
            var utc = new DateTime(2025, 4, 1, 8, 30, 0, DateTimeKind.Utc);

            Assert.Multiple(() =>
            {
                // AsDouble accepts double, float and int; AsUInt32 widens a
                // non-negative int; AsString falls back through LocalizedText.
                Assert.That(Val(Variant.From(1.5f)).AsDouble(), Is.EqualTo(1.5));
                Assert.That(Val(Variant.From(7)).AsDouble(), Is.EqualTo(7.0));
                Assert.That(Val(Variant.From(9)).AsUInt32(), Is.EqualTo(9u));
                Assert.That(Val(Variant.From(-1)).AsUInt32(), Is.Null);
                Assert.That(Val(Variant.From(3u)).AsUInt32(), Is.EqualTo(3u));
                Assert.That(
                    Val(Variant.From(new LocalizedText("hi"))).AsString(),
                    Is.EqualTo("hi"));
                Assert.That(Val(Variant.From("plain")).AsString(), Is.EqualTo("plain"));
                Assert.That(Val(Variant.From(true)).AsBoolean(), Is.True);
                Assert.That(
                    Val(Variant.From(PumpRoleEnum.Master)).AsEnum<PumpRoleEnum>(),
                    Is.EqualTo(PumpRoleEnum.Master));
                Assert.That(
                    Val(Variant.From(new DateTimeUtc(utc))).AsDateTime(),
                    Is.EqualTo(utc));
                string[] strings = ["a", "b"];
                Assert.That(
                    Val(Variant.From((ArrayOf<string>)strings))
                        .AsStringArray().ToArray(),
                    Is.EqualTo(strings));

                // A bad status blanks every accessor.
                PumpValue bad = Val(Variant.From(1.0), StatusCodes.BadNoData);
                Assert.That(bad.AsDouble(), Is.Null);
                Assert.That(bad.AsBoolean(), Is.Null);
                Assert.That(bad.AsString(), Is.Null);
                Assert.That(bad.AsUInt32(), Is.Null);
                Assert.That(bad.AsStringArray().IsNull, Is.True);
                Assert.That(bad.AsEnum<PumpRoleEnum>(), Is.Null);
                Assert.That(bad.AsDateTime(), Is.Null);

                // A wrong-typed value coerces to null rather than throwing.
                Assert.That(Val(Variant.From("text")).AsDouble(), Is.Null);
                Assert.That(Val(Variant.From(1.0)).AsStringArray().IsNull, Is.True);
            });
        }

        [Test]
        public void ValueSetTypedGettersAndNestedGroupsResolve()
        {
            var nested = new PumpValueSet(
                new NodeId(9, 1),
                [Val(Variant.From(2.8), name: BrowseNames.OverallVibrationVelocityRMS)]);
            var set = new PumpValueSet(
                new NodeId(1, 1),
                [
                    Val(Variant.From(PumpRoleEnum.Master), name: BrowseNames.PumpRole),
                    Val(Variant.From((ArrayOf<string>)["P2"]),
                        name: BrowseNames.RedundantPumpIDs),
                    Val(Variant.From(new DateTimeUtc(
                        new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc))),
                        name: BrowseNames.ExchangeTime)
                ],
                [new("DriveEndBearing", nested)]);

            string[] redundantPumpIds = ["P2"];
            Assert.Multiple(() =>
            {
                Assert.That(set.GetEnum<PumpRoleEnum>(BrowseNames.PumpRole),
                    Is.EqualTo(PumpRoleEnum.Master));
                Assert.That(
                    set.GetStringArray(BrowseNames.RedundantPumpIDs).ToArray(),
                    Is.EqualTo(redundantPumpIds));
                Assert.That(set.GetDateTime(BrowseNames.ExchangeTime), Is.Not.Null);
                Assert.That(set.Names, Has.Count.EqualTo(3));
                Assert.That(set.Groups, Has.Count.EqualTo(1));
                Assert.That(set.Group("DriveEndBearing"), Is.SameAs(nested));
                Assert.That(set.Group("Absent"), Is.Null);
                Assert.That(
                    set.GetEnum<PumpRoleEnum>("Absent"), Is.Null,
                    "an absent key reads as null rather than throwing");

                // Enumeration yields every value once.
                int enumerated = 0;
                foreach (PumpValue _ in set)
                {
                    enumerated++;
                }
                Assert.That(enumerated, Is.EqualTo(3));
            });
        }

        [Test]
        public void ValueSetRejectsNullConstructorArguments()
        {
            Assert.Multiple(() =>
            {
                Assert.Throws<ArgumentNullException>(
                    () => new PumpValueSet(new NodeId(1, 1), null!));
                Assert.Throws<ArgumentNullException>(
                    () => new PumpValueSet(new NodeId(1, 1), [], null!));
                Assert.Throws<ArgumentNullException>(() => _ = PumpValueSet.Empty[null!]);
                Assert.Throws<ArgumentNullException>(
                    () => PumpValueSet.Empty.Group(null!));
                Assert.That(PumpValueSet.Empty.Contains(null!), Is.False);
            });
        }

        [Test]
        public async Task ConfigurePumpsPipelineDispatchesToTheLiveManagerAsync()
        {
            await using var fixture = new PumpsServerFixture();
            await fixture.StartAsync();

            IPumpsSetupContext? seen = null;
            PumpsPipelineMarker? resolved = null;
            var marker = new PumpsPipelineMarker();

            var services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa().AddServer(options =>
            {
                options.ApplicationName = "PumpsPipelineCoverage";
                options.ApplicationUri = "urn:localhost:OPCFoundation:PumpsPipelineCoverage";
            });
            builder.Services.AddSingleton(marker);
            builder.AddPumps();
            builder.ConfigurePumps(async context =>
            {
                seen = context;
                resolved = context.GetRequiredService<PumpsPipelineMarker>();
                await context.Manager.CreatePumpAsync(
                    new QualifiedName(
                        "Pipeline_Pump",
                        context.Manager.InstanceNamespaceIndex),
                    context.CancellationToken).ConfigureAwait(false);
            });

            await using ServiceProvider provider = services.BuildServiceProvider();
            IDiPostSetupRunner runner = provider.GetRequiredService<IDiPostSetupRunner>();
            await runner.RunAsync(fixture.Manager, CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(seen, Is.Not.Null);
                Assert.That(seen!.Manager, Is.SameAs(fixture.Manager));
                Assert.That(seen.DiContext, Is.Not.Null);
                Assert.That(seen.CancellationToken.IsCancellationRequested, Is.False);
                Assert.That(resolved, Is.SameAs(marker));
                Assert.That(fixture.Manager.Pumps, Has.Count.EqualTo(1));
            });
        }

        [Test]
        public async Task ClientRuntimeReadsCoverDiscoveryGroupsAndFactoryAsync()
        {
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
                        Model = new LocalizedText("PumpX-2000")
                    });

                    pump.Measurements.SetAnalog(BrowseNames.MassFlow, 12.5);

                    pump.MultiPump
                        .Set(BrowseNames.PumpRole, Variant.From(PumpRoleEnum.Master))
                        .Set(BrowseNames.NumberOfPumps, Variant.From(2u));

                    pump.Design.SetAnalog(
                        BrowseNames.MaximumAllowableWorkingPressure, 1_600_000.0);

                    pump.MaintenanceCategory(BrowseNames.GeneralMaintenance)
                        .Set(
                            BrowseNames.StateOfTheItem,
                            Variant.From(StateOfTheItemEnum.OperatingState))
                        .SetAnalog(BrowseNames.OperatingTime, 1234.0);
                    pump.MaintenanceCategory(BrowseNames.BreakdownMaintenance)
                        .SetDiscrete(BrowseNames.Failure, true);
                });

            await using ServiceProvider provider = services.BuildServiceProvider();
            var host = new HostedServiceRunner(provider);
            await host.StartAsync();
            try
            {
                using var clientFixture = new ClientFixture(NUnitTelemetryContext.Create());
                await clientFixture.LoadClientConfigurationAsync(
                    System.IO.Path.Combine(
                        TestContext.CurrentContext.WorkDirectory,
                        nameof(ClientRuntimeReadsCoverDiscoveryGroupsAndFactoryAsync),
                        "client-pki"));

                ITelemetryContext telemetry = NUnitTelemetryContext.Create();

                // The client fixture retries until the hosted server is
                // listening, so everything after it can connect in one shot.
                using Opc.Ua.Client.ISession session = await clientFixture.ConnectAsync(
                    new Uri(s_endpointUrl),
                    SecurityPolicies.None);

                Assert.Throws<ArgumentNullException>(() => session.Pumps(null!));
                PumpsClient pumps = session.Pumps(telemetry);
                Assert.That(pumps.IsSupported, Is.True);

                ArrayOf<NodeId> discovered = await pumps.DiscoverPumpsAsync();
                Assert.That(discovered.Count, Is.EqualTo(1));
                NodeId pumpId = discovered[0];

                PumpEntry entry = await FirstAsync(pumps.EnumeratePumpsAsync());
                Assert.That(entry.NodeId, Is.EqualTo(pumpId));
                Assert.That(await pumps.IsPumpAsync(entry.TypeDefinitionId), Is.True);
                Assert.That(await pumps.IsPumpAsync(NodeId.Null), Is.False);

                PumpOperationalData? operational = await pumps.ReadOperationalAsync(pumpId);
                Assert.That(operational, Is.Not.Null);
                Assert.Multiple(() =>
                {
                    Assert.That(
                        operational!.Measurements.GetDouble(BrowseNames.MassFlow),
                        Is.EqualTo(12.5));
                    Assert.That(operational.MultiPump, Is.Not.Null);
                    Assert.That(operational.MultiPump!.PumpRole,
                        Is.EqualTo(PumpRoleEnum.Master));
                });

                MultiPumpConfiguration? multiPump = await pumps.ReadMultiPumpAsync(pumpId);
                Assert.That(multiPump, Is.Not.Null);
                Assert.That(multiPump!.NumberOfPumps, Is.EqualTo(2u));

                PumpConfigurationData? configuration =
                    await pumps.ReadConfigurationAsync(pumpId);
                Assert.That(configuration, Is.Not.Null);
                Assert.That(
                    configuration!.Design.GetDouble(
                        BrowseNames.MaximumAllowableWorkingPressure),
                    Is.EqualTo(1_600_000.0));

                PumpMaintenanceData? maintenance = await pumps.ReadMaintenanceAsync(pumpId);
                Assert.That(maintenance, Is.Not.Null);
                Assert.Multiple(() =>
                {
                    Assert.That(maintenance!.StateOfTheItem,
                        Is.EqualTo(StateOfTheItemEnum.OperatingState));
                    Assert.That(maintenance.HasFailure, Is.True);
                    Assert.That(maintenance.General.GetDouble(BrowseNames.OperatingTime),
                        Is.EqualTo(1234.0));
                    Assert.That(
                        maintenance.Groups.ToList(), Has.Count.EqualTo(4));
                });

                PumpValueSet documentation = await pumps.ReadDocumentationAsync(pumpId);
                Assert.That(documentation.IsEmpty, Is.True);

                PumpNameplate? nameplate = await pumps.ReadNameplateAsync(pumpId);
                Assert.That(nameplate, Is.Not.Null);

                // The pump publishes no Markings folder, so the id is null and
                // the browse yields nothing rather than throwing.
                ArrayOf<PumpEntry> emptyMarkings =
                    await pumps.ReadMarkingsAsync(nameplate!.MarkingsFolderId);
                Assert.That(emptyMarkings.Count, Is.Zero);

                // Browsing a node that does have object children exercises the
                // enumeration and projection path.
                ArrayOf<PumpEntry> children = await pumps.ReadMarkingsAsync(pumpId);
                Assert.That(children.Count, Is.GreaterThan(0));

                NodeId noIdentification =
                    await pumps.ResolveIdentificationAsync(NodeId.Null);
                Assert.That(noIdentification.IsNull, Is.True);

                EndpointDescription? endpoint = await CoreClientUtils.SelectEndpointAsync(
                    clientFixture.Config,
                    s_endpointUrl,
                    useSecurity: false,
                    discoverTimeout: 15000,
                    telemetry);
                Assert.That(endpoint, Is.Not.Null);

                await using ManagedSession managedSession =
                    await new ManagedSessionBuilder(clientFixture.Config, telemetry)
                        .UseEndpoint(new ConfiguredEndpoint(
                            null,
                            endpoint,
                            EndpointConfiguration.Create(clientFixture.Config)))
                        .WithSessionName(
                            nameof(ClientRuntimeReadsCoverDiscoveryGroupsAndFactoryAsync))
                        .ConnectAsync();

                // CA2025: the factory hands out the session only while this
                // await-using scope is active; every task that uses it is
                // awaited before the session is disposed.
#pragma warning disable CA2025
                var factory = new PumpsClientFactory(
                    _ => Task.FromResult(managedSession),
                    telemetry);
#pragma warning restore CA2025
                PumpsClient viaFactory = await factory.CreateAsync();
                Assert.Multiple(() =>
                {
                    Assert.That(viaFactory.IsSupported, Is.True);
                    Assert.That(viaFactory.Session, Is.SameAs(managedSession));
                });
                Assert.That(
                    (await viaFactory.DiscoverPumpsAsync()).Count, Is.EqualTo(1));
            }
            finally
            {
                await host.StopAsync();
            }
        }

        private static MaintenanceLevelEnum FirstMaintenanceLevel()
        {
#if NET5_0_OR_GREATER
            return Enum.GetValues<MaintenanceLevelEnum>()[0];
#else
            return ((MaintenanceLevelEnum[])Enum.GetValues(typeof(MaintenanceLevelEnum)))[0];
#endif
        }

        private static PumpValue Value(string name, Variant value)
        {
            return new PumpValue
            {
                NodeId = new NodeId(1, 1),
                Name = name,
                Value = value,
                StatusCode = StatusCodes.Good
            };
        }

        private static PumpValue Val(
            Variant value,
            StatusCode? status = null,
            string name = "Value")
        {
            return new PumpValue
            {
                NodeId = new NodeId(1, 1),
                Name = name,
                Value = value,
                StatusCode = status ?? StatusCodes.Good
            };
        }

        private static async Task<T> FirstAsync<T>(IAsyncEnumerable<T> source)
        {
            await foreach (T item in source)
            {
                return item;
            }
            throw new AssertionException("The sequence was empty.");
        }

        private static void ConfigureServer(OpcUaServerOptions options)
        {
            string applicationName = nameof(PumpsCoverageTests);
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

        private static string s_endpointUrl = string.Empty;

        /// <summary>
        /// Marker resolved through <c>IPumpsSetupContext.GetRequiredService</c>
        /// to prove the context reaches the application's service provider.
        /// </summary>
        private sealed class PumpsPipelineMarker
        {
        }

        /// <summary>
        /// Thin wrapper over the single hosted service the server registration
        /// produces, matching the start/stop shape of the E2E suite.
        /// </summary>
        private sealed class HostedServiceRunner
        {
            private readonly Microsoft.Extensions.Hosting.IHostedService m_hosted;

            public HostedServiceRunner(IServiceProvider provider)
            {
                m_hosted = provider
                    .GetServices<Microsoft.Extensions.Hosting.IHostedService>()
                    .Single();
            }

            public Task StartAsync()
            {
                return m_hosted.StartAsync(CancellationToken.None);
            }

            public Task StopAsync()
            {
                return m_hosted.StopAsync(CancellationToken.None);
            }
        }
    }
}
