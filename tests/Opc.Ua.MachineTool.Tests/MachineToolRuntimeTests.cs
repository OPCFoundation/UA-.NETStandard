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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Machinery.Server;
using Opc.Ua.MachineTool.Server;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.MachineTool.Tests
{
    /// <summary>
    /// Exercises MachineTool behavior through its public provider, builder and runtime handles.
    /// </summary>
    [TestFixture]
    [Category("MachineTool")]
    [NonParallelizable]
    public sealed class MachineToolRuntimeTests
    {
        /// <summary>
        /// Mandatory-only hosting preserves the nameplate, real sample quality, and legacy active program.
        /// </summary>
        [Test]
        public async Task MandatoryMachinePublishesProviderSamplesWithoutOptionalFacetsAsync()
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            DateTimeUtc acquired = new DateTime(2026, 2, 3, 4, 5, 6, DateTimeKind.Utc);
            var snapshot = new MachineToolSnapshot
            {
                Operation = new MachineToolOperation
                {
                    Mode = MachineOperationMode.Automatic,
                    Sample = new MachineToolSampleInfo(acquired, StatusCodes.UncertainLastUsableValue)
                },
                ActiveProgram = new MachineToolProgram
                {
                    Name = "MAIN.NC",
                    Phase = MachineToolProductionPhase.Running,
                    Sample = new MachineToolSampleInfo(acquired, StatusCodes.Good)
                }
            };
            var provider = new InMemoryMachineToolProvider(snapshot);
            await using MachineToolRuntime<MachineToolState> runtime = await fixture.Context
                .AddMachineTool("Mill")
                .WithProvider(provider)
                .WithIdentification(identity =>
                {
                    identity.Manufacturer = new LocalizedText("Test builder");
                    identity.SerialNumber = "MT-1";
                    identity.ProductInstanceUri = "urn:machinetool-tests:mt-1";
                })
                .BuildAsync().ConfigureAwait(false);

            Assert.That(runtime.State.Identification!.SerialNumber!.Value, Is.EqualTo("MT-1"));
            Assert.That(runtime.State.Identification.TypeDefinitionId,
                Is.EqualTo(NodeId.Create(11, Namespaces.MachineTool, fixture.Context.Context.NamespaceUris)));
            Assert.That(runtime.State.Monitoring!.MachineTool!.OperationMode!.Value,
                Is.EqualTo(MachineOperationMode.Automatic));
            Assert.That(runtime.State.Monitoring.MachineTool.OperationMode.StatusCode,
                Is.EqualTo(StatusCodes.UncertainLastUsableValue));
            Assert.That(runtime.State.Monitoring.MachineTool.OperationMode.Timestamp, Is.EqualTo(acquired));
            Assert.That(runtime.State.Production!.ActiveProgram!.Name!.Value, Is.EqualTo("MAIN.NC"));
            Assert.That(runtime.State.Production.ActiveProgram.NumberInList!.Value, Is.Zero);
            Assert.That(runtime.State.Production.ActiveProgram.State!.CurrentState!.Value.Text, Is.EqualTo("Running"));
            Assert.That(runtime.State.Equipment!.Tools, Is.Null);
            Assert.That(runtime.State.Notification!.Messages, Is.Null);
            Assert.That(runtime.State.Notification.Prognoses, Is.Null);
            Assert.That(runtime.State.Production.ProductionPlan, Is.Null);
            Assert.That(runtime.State.FileSystem, Is.Null);
            Assert.That(fixture.Context.Context.NamespaceUris.GetIndex(WMTP.Namespaces.WMTP), Is.EqualTo(-1));
        }

        /// <summary>
        /// Failed nameplate validation releases the staged name and identifiers so corrected setup can retry.
        /// </summary>
        [Test]
        public async Task InvalidIdentificationDoesNotReserveTheMachineNameAsync()
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            MachineToolBuilder<MachineToolState> builder = fixture.Context.AddMachineTool("Retry")
                .WithProvider(new InMemoryMachineToolProvider(new MachineToolSnapshot()))
                .WithIdentification(identity => identity.Manufacturer = new LocalizedText("Manufacturer"));

            ServiceResultException invalid = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await builder.BuildAsync().ConfigureAwait(false))!;
            Assert.That(invalid.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            await using MachineToolRuntime<MachineToolState> runtime = await builder
                .WithIdentification(identity =>
                {
                    identity.Manufacturer = new LocalizedText("Manufacturer");
                    identity.SerialNumber = "Retry-1";
                    identity.ProductInstanceUri = "urn:test:retry-1";
                }).BuildAsync().ConfigureAwait(false);

            Assert.That(runtime.State.Identification!.SerialNumber!.Value, Is.EqualTo("Retry-1"));
        }

        /// <summary>
        /// Combined channels use the specified state priority and modifier union while keeping HMI overrides.
        /// </summary>
        [Test]
        public async Task CombinedChannelsAggregateReportedStateWithoutCommandingEquipmentAsync()
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            DateTimeUtc acquired = new DateTime(2026, 2, 3, 4, 5, 6, DateTimeKind.Utc);
            var sample = new MachineToolSampleInfo(acquired, StatusCodes.Good);
            var provider = new InMemoryMachineToolProvider(new MachineToolSnapshot
            {
                Channels =
                [
                    new MachineToolChannel
                    {
                        Name = "Left", State = ChannelState.Active, Mode = ChannelMode.Automatic,
                        FeedOverride = 90, Modifiers = new(true, false, false), Sample = sample
                    },
                    new MachineToolChannel
                    {
                        Name = "Right", State = ChannelState.Reset, Mode = ChannelMode.JogManual,
                        FeedOverride = 100, Modifiers = new(false, true, true), Sample = sample
                    },
                    new MachineToolChannel
                    {
                        Name = "Combined", CombinedChannels = ["Left", "Right"],
                        FeedOverride = 75, RapidOverride = 80, Sample = sample
                    }
                ]
            });
            await using MachineToolRuntime<MachineToolState> runtime = await fixture.Context.AddMachineTool("Channels")
                .WithProvider(provider).WithFeatures(MachineToolFeatures.Channels)
                .WithIdentification(identity =>
                {
                    identity.Manufacturer = new LocalizedText("Manufacturer");
                    identity.SerialNumber = "Channel-1";
                    identity.ProductInstanceUri = "urn:test:channel-1";
                }).BuildAsync().ConfigureAwait(false);
            var children = new List<BaseInstanceState>();
            runtime.State.Monitoring!.GetChildren(fixture.Context.Context, children);
            var combined = children.Find(child => child.BrowseName.Name == "Combined") as CombinedChannelMonitoringState;

            Assert.That(combined, Is.Not.Null);
            Assert.That(combined!.ChannelState!.Value, Is.EqualTo(ChannelState.Reset));
            Assert.That(combined.ChannelMode!.Value, Is.EqualTo(ChannelMode.JogManual));
            Assert.That(combined.FeedOverride!.Value, Is.EqualTo(75));
            Assert.That(combined.FeedOverride.EngineeringUnits!.Value!.UnitId, Is.EqualTo(20529));
            Assert.That(combined.RapidOverride!.Value, Is.EqualTo(80));
            Assert.That(combined.ChannelModifiers!.DryRun!.Value, Is.True);
            Assert.That(combined.ChannelModifiers.OptionalStop!.Value, Is.True);
            Assert.That(combined.ChannelModifiers.SingleStep!.Value, Is.True);
        }

        /// <summary>
        /// Optional working units, counters and responsibility retain their modeled types and observations.
        /// </summary>
        [Test]
        public async Task WorkingUnitsCountersAndObligationUseProviderObservationsAsync()
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            var sample = new MachineToolSampleInfo(
                new DateTime(2026, 2, 3, 4, 5, 6, DateTimeKind.Utc), StatusCodes.Good);
            var provider = new InMemoryMachineToolProvider(new MachineToolSnapshot
            {
                Operation = new MachineToolOperation
                {
                    Mode = MachineOperationMode.Service,
                    Sample = sample,
                    PowerOnDurationMilliseconds = 9_000_000,
                    PartsProducedInLifetime = 412,
                    Obligation = new MachineToolObligation(false, true),
                    ItemState = MachineToolItemState.NotExecuting,
                    MachineryMode = MachineToolMachineryMode.Maintenance,
                    MaintenanceMode = MachineToolMaintenanceMode.Repair
                },
                Software = [new MachineToolSoftware("NC", "1.2.3")],
                Spindles = [new MachineToolSpindle("Spindle", true, sample, 95, false)],
                Lasers = [new MachineToolLaser("Laser", true, LaserState.Ready, sample)],
                EdmGenerators = [new MachineToolEdmGenerator("Generator", false, EDMGeneratorState.Ready, sample)]
            });
            await using MachineToolRuntime<MachineToolState> runtime = await fixture.Context.AddMachineTool("Observed")
                .WithProvider(provider).WithFeatures(
                    MachineToolFeatures.SoftwareIdentification | MachineToolFeatures.WorkingUnits |
                    MachineToolFeatures.LegacyPowerOnDuration | MachineToolFeatures.OperationCounters |
                    MachineToolFeatures.PartsProducedInLifetime | MachineToolFeatures.Obligation |
                    MachineToolFeatures.MachineryState | MachineToolFeatures.MaintenanceMode)
                .WithIdentification(identity =>
                {
                    identity.Manufacturer = new LocalizedText("Manufacturer");
                    identity.SerialNumber = "Observed-1";
                    identity.ProductInstanceUri = "urn:test:observed-1";
                }).BuildAsync().ConfigureAwait(false);

            MachineOperationMonitoringState operation = runtime.State.Monitoring!.MachineTool!;
            Assert.That(operation.PowerOnDuration, Is.Not.Null);
            Assert.That(operation.PowerOnDuration!.Value, Is.EqualTo(2));
            Assert.That(operation.OperationCounters!.PowerOnDuration!.Value, Is.EqualTo(9_000_000));
            Assert.That(operation.Obligation!.EndUserObligated!.Value, Is.False);
            Assert.That(operation.Obligation.MachineBuilderObligated!.Value, Is.True);
            Assert.That(operation.MachineryOperationMode!.CurrentState!.Value.Text, Is.EqualTo("Maintenance"));
            Assert.That(operation.MachineryOperationMode.MaintenanceMode!.CurrentState!.Value.Text, Is.EqualTo("Repair"));
            var children = new List<BaseInstanceState>();
            runtime.State.Monitoring.GetChildren(fixture.Context.Context, children);
            Assert.That(children.Find(child => child.BrowseName.Name == "Spindle"), Is.TypeOf<SpindleMonitoringState>());
            Assert.That(children.Find(child => child.BrowseName.Name == "Laser"), Is.TypeOf<LaserMonitoringState>());
            Assert.That(children.Find(child => child.BrowseName.Name == "Generator"), Is.TypeOf<EDMGeneratorMonitoringState>());
        }

        /// <summary>
        /// Stacklights use segmented IA nodes with ordered lights and propagate their provider quality.
        /// </summary>
        [Test]
        public async Task StacklightsUseSegmentedOrderedLightElementsAsync()
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            var sample = new MachineToolSampleInfo(
                new DateTime(2026, 2, 3, 4, 5, 6, DateTimeKind.Utc), StatusCodes.UncertainLastUsableValue);
            var provider = new InMemoryMachineToolProvider(new MachineToolSnapshot
            {
                Stacklights =
                [
                    new MachineToolStacklight("Main",
                    [
                        new MachineToolLight(true, IA.SignalColor.Red, IA.SignalModeLight.Flashing, sample),
                        new MachineToolLight(false, IA.SignalColor.Green, IA.SignalModeLight.Continuous, sample)
                    ])
                ]
            });
            await using MachineToolRuntime<MachineToolState> runtime = await fixture.Context.AddMachineTool("Lights")
                .WithProvider(provider).WithFeatures(MachineToolFeatures.Stacklights)
                .WithIdentification(identity =>
                {
                    identity.Manufacturer = new LocalizedText("Manufacturer");
                    identity.SerialNumber = "Lights-1";
                    identity.ProductInstanceUri = "urn:test:lights-1";
                }).BuildAsync().ConfigureAwait(false);

            IA.BasicStacklightState? stacklight = runtime.State.Monitoring!.Stacklight;
            Assert.That(stacklight, Is.Not.Null);
            Assert.That(stacklight!.StacklightMode!.Value, Is.EqualTo(IA.StacklightOperationMode.Segmented));
            var children = new List<BaseInstanceState>();
            stacklight.GetChildren(fixture.Context.Context, children);
            var lights = children.FindAll(static child => child is IA.StackElementLightState);
            Assert.That(lights, Has.Count.EqualTo(2));
            var first = (IA.StackElementLightState)lights[0];
            Assert.That(first.NumberInList!.WrappedValue.TryGetValue(out ushort order), Is.True);
            Assert.That(order, Is.Zero);
            Assert.That(first.SignalOn!.Value, Is.True);
            Assert.That(first.SignalColor!.Value, Is.EqualTo(IA.SignalColor.Red));
            Assert.That(first.SignalMode!.Value, Is.EqualTo(IA.SignalModeLight.Flashing));
            Assert.That(first.SignalOn.StatusCode, Is.EqualTo(StatusCodes.UncertainLastUsableValue));
        }

        /// <summary>
        /// Selecting the additional Machinery nameplate unit requires its data rather than empty successful nodes.
        /// </summary>
        [Test]
        public async Task AdditionalNameplateSelectionRequiresAllAdditionalValuesAsync()
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            MachineToolBuilder<MachineToolState> builder = fixture.Context.AddMachineTool("Additional")
                .WithProvider(new InMemoryMachineToolProvider(new MachineToolSnapshot()))
                .WithFeatures(MachineToolFeatures.AdditionalIdentification)
                .WithIdentification(identity =>
                {
                    identity.Manufacturer = new LocalizedText("Manufacturer");
                    identity.SerialNumber = "Additional-1";
                    identity.ProductInstanceUri = "urn:test:additional-1";
                });

            ServiceResultException missing = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await builder.BuildAsync().ConfigureAwait(false))!;
            Assert.That(missing.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            await using MachineToolRuntime<MachineToolState> runtime = await builder.WithIdentification(identity =>
            {
                identity.Manufacturer = new LocalizedText("Manufacturer");
                identity.SerialNumber = "Additional-1";
                identity.ProductInstanceUri = "urn:test:additional-1";
                identity.Model = new LocalizedText("Milling centre");
                identity.ComponentName = new LocalizedText("Mill A");
                identity.DeviceClass = "Milling machine";
                identity.YearOfConstruction = 2025;
                identity.MonthOfConstruction = 7;
            }).BuildAsync().ConfigureAwait(false);
            Assert.That(runtime.State.Identification!.DeviceClass!.Value, Is.EqualTo("Milling machine"));
            Assert.That(runtime.State.Identification.MonthOfConstruction!.Value, Is.EqualTo(7));
        }
    }

    /// <summary>
    /// Hosts the real MachineTool model on the stock Machinery owner for runtime and wire tests.
    /// </summary>
    internal sealed class MachineToolRuntimeFixture : IAsyncDisposable
    {
        /// <summary>
        /// Selects additional models before server construction while preserving the default MachineTool fixture.
        /// </summary>
        internal MachineToolRuntimeFixture(ArrayOf<IMachineryModelProvider> additionalProviders = default)
        {
            m_additionalProviders = additionalProviders;
        }

        /// <summary>
        /// Gets the live MachineTool server.
        /// </summary>
        public RuntimeServer Server { get; private set; } = null!;

        /// <summary>
        /// Gets the shared build context.
        /// </summary>
        public IMachineryBuildContext Context { get; private set; } = null!;

        /// <summary>
        /// Gets the endpoint chosen by the server fixture.
        /// </summary>
        public Uri Endpoint => new($"opc.tcp://localhost:{m_fixture!.Port}");

        /// <summary>
        /// Gets the artifact root used for this fixture's certificate stores.
        /// </summary>
        public string Root { get; } = Path.Combine(
            Path.GetTempPath(), "opc-mt", Guid.NewGuid().ToString("N"));

        /// <summary>
        /// Starts an isolated real server and a direct build context.
        /// </summary>
        public async ValueTask StartAsync()
        {
            m_fixture = new ServerFixture<RuntimeServer>(
                telemetry => new RuntimeServer(telemetry, m_additionalProviders))
            {
                AutoAccept = true,
                SecurityNone = true
            };
            await m_fixture.LoadConfigurationAsync(Root).ConfigureAwait(false);
            Server = await m_fixture.StartAsync().ConfigureAwait(false);
            Context = Server.Manager.CreateMachineryBuildContext();
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (m_fixture != null)
            {
                await m_fixture.StopAsync().ConfigureAwait(false);
            }
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        /// <summary>
        /// Exposes the shared node manager to the fixture.
        /// </summary>
        internal sealed class RuntimeServer : StandardServer
        {
            /// <summary>
            /// Creates the server.
            /// </summary>
            public RuntimeServer(
                ITelemetryContext telemetry, ArrayOf<IMachineryModelProvider> additionalProviders = default)
                : base(telemetry)
            {
                m_additionalProviders = additionalProviders;
            }

            /// <summary>
            /// Gets the namespace owner.
            /// </summary>
            public MachineryNodeManager Manager { get; private set; } = null!;

            /// <inheritdoc/>
            protected override ValueTask<IMasterNodeManager> CreateMasterNodeManagerAsync(
                IServerInternal server,
                ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                var options = new MachineryServerOptions
                {
                    Parts = MachineryParts.BuildingBlocks | MachineryParts.Jobs
                };
                Manager = new MachineryNodeManager(server, configuration,
                    [new MachineryModelProvider(options.Parts), new MachineToolModelProvider(), .. m_additionalProviders],
                    options);
                return new ValueTask<IMasterNodeManager>(
                    new MasterNodeManager(server, configuration, null, [Manager]));
            }

            /// <summary>Models selected before namespace registration.</summary>
            private readonly ArrayOf<IMachineryModelProvider> m_additionalProviders;
        }

        /// <summary>Additional model providers supplied before server construction.</summary>
        private readonly ArrayOf<IMachineryModelProvider> m_additionalProviders;

        /// <summary>The real server fixture, disposed asynchronously.</summary>
        private ServerFixture<RuntimeServer>? m_fixture;
    }
}
