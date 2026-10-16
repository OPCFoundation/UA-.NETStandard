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
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.CranesHoists.Server;
using Opc.Ua.GMS;
using Opc.Ua.GMS.Contracts;
using Opc.Ua.GMS.Server;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Machinery.Server.Builders;
using Opc.Ua.MachineTool;
using Opc.Ua.MachineTool.Server;
using Opc.Ua.MachineTool.WMTP;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Opc.Ua.Vision;
using Opc.Ua.Vision.MachineVision;
using Opc.Ua.Vision.Server;
using ClientSession = Opc.Ua.Client.ISession;

namespace Opc.Ua.Machinery.Tests.Integration
{
    /// <summary>
    /// Runs all real companion configurators on the stock Machinery owner, over an encrypted TCP endpoint.
    /// </summary>
    internal sealed class CompanionSuiteFixture : IAsyncDisposable
    {
        /// <summary>
        /// Gets real telemetry for both server and clients.
        /// </summary>
        internal ITelemetryContext Telemetry { get; } = NUnitTelemetryContext.Create();

        /// <summary>
        /// Gets the registrations that selected the sole namespace owner.
        /// </summary>
        internal ServiceCollection Services { get; } = new();

        /// <summary>
        /// Gets independently issued build contexts, all backed by one manager.
        /// </summary>
        internal List<IMachineryBuildContext> Contexts { get; } = [];

        /// <summary>
        /// Gets the actual shared Machinery context.
        /// </summary>
        internal IMachineryBuildContext Context => m_liveContext ?? Contexts[0];

        /// <summary>
        /// Gets the actual per-instance MachineTool handles.
        /// </summary>
        internal List<MachineToolRuntime<MachineToolState>> Machines { get; } = [];

        /// <summary>
        /// Gets independent MachineTool observation providers.
        /// </summary>
        internal List<Mock<IMachineToolProvider>> MachineProviders { get; } = [];

        /// <summary>
        /// Gets native GMS handles and their own result publishers.
        /// </summary>
        internal List<GmsHandle> Systems { get; } = [];

        /// <summary>
        /// Gets independent GMS observation providers.
        /// </summary>
        internal List<Mock<IGmsProvider>> GmsProviders { get; } = [];

        /// <summary>
        /// Gets independently registered native crane systems.
        /// </summary>
        internal List<CraneSystemHandle> Cranes { get; } = [];

        /// <summary>
        /// Gets independent crane acquisitions.
        /// </summary>
        internal List<SimulatedCraneProvider> CraneProviders { get; } = [];

        /// <summary>
        /// Gets independent wireless peripheral instances.
        /// </summary>
        internal List<WmtpPeripheral> Peripherals { get; } = [];

        /// <summary>
        /// Gets the per-peripheral acquisition clocks supplied through the public builder.
        /// </summary>
        internal List<AcquisitionClock> PeripheralClocks { get; } = [];

        /// <summary>
        /// Gets separate nonconsuming record datasets.
        /// </summary>
        internal List<InMemoryWmtpRecordStore> Records { get; } = [];

        /// <summary>
        /// Gets the native MachineVision control runtimes.
        /// </summary>
        internal List<MachineVisionSystemHandle> Inspections { get; } = [];

        /// <summary>
        /// Gets their independently linked AMCM assets.
        /// </summary>
        internal List<MachineVisionAssetHandle> Assets { get; } = [];

        /// <summary>
        /// Gets explicitly observed vision provider invocations.
        /// </summary>
        internal List<Mock<IMachineVisionOperationsProvider>> VisionProviders { get; } = [];

        /// <summary>
        /// Gets the live provider used for factory-count assertions.
        /// </summary>
        internal ServiceProvider Provider => m_provider ??
            throw new InvalidOperationException("The composition fixture is not started.");

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            var failures = new List<Exception>();
            foreach (ClientSession session in m_sessions)
            {
                try
                {
                    if (session.Connected)
                    {
                        await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                    if (session is IAsyncDisposable asynchronous)
                    {
                        await asynchronous.DisposeAsync().ConfigureAwait(false);
                    }
                    else
                    {
                        session.Dispose();
                    }
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }
            if (m_fixture != null)
            {
                try
                {
                    await m_fixture.StopAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }
            foreach (ClientFixture client in m_clients)
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }
            foreach (SimulatedCraneProvider provider in CraneProviders)
            {
                await provider.DisposeAsync().ConfigureAwait(false);
            }
            foreach (InMemoryWmtpRecordStore records in Records)
            {
                await records.DisposeAsync().ConfigureAwait(false);
            }
            if (m_provider != null)
            {
                await m_provider.DisposeAsync().ConfigureAwait(false);
            }
            if (Directory.Exists(m_root))
            {
                Directory.Delete(m_root, recursive: true);
            }
            if (failures.Count != 0)
            {
                throw new AggregateException("Composition fixture cleanup failed.", failures);
            }
        }

        /// <summary>
        /// Starts the actual DI-configured single owner, optionally with independent WMTP-only model selection.
        /// </summary>
        internal async Task StartAsync(
            bool reordered = false, bool wirelessOnly = false, bool standalone = false, bool failStartup = false)
        {
            IOpcUaServerBuilder builder = Services.AddOpcUa().AddServer(options =>
                options.ApplicationName = "CompanionSuite");
            if (standalone && wirelessOnly)
            {
                builder.AddMachineTool(options =>
                {
                    options.Parts = MachineToolParts.Wmtp;
                    options.InstanceNamespaceUri = InstanceUri;
                });
            }
            else if (standalone)
            {
                builder.AddCranesHoists(options =>
                {
                    options.Parts = RequiredParts;
                    options.InstanceNamespaceUri = InstanceUri;
                }).AddMachineToolToMachinery(options => options.Parts = MachineToolParts.All);
            }
            else
            {
                builder.AddMachinery(options =>
                {
                    options.Parts = wirelessOnly
                        ? MachineryParts.BuildingBlocks | MachineryParts.ProcessValues : RequiredParts;
                    options.InstanceNamespaceUri = InstanceUri;
                }).AddMachineToolToMachinery(options =>
                    options.Parts = wirelessOnly ? MachineToolParts.Wmtp : MachineToolParts.All);
            }
            if (!wirelessOnly)
            {
                builder.AddGmsToMachinery().AddCranesHoistsToMachinery().AddVisionToMachinery();
            }
            for (int i = 1; i <= 2; i++)
            {
                int number = i;
                builder.ConfigureMachineToolFor<MachineryNodeManager>(async (context, ct) =>
                {
                    if (number == 2)
                    {
                        context = NewContext(context, wirelessOnly);
                    }
                    Contexts.Add(context);
                    await AddPeripheralAsync(context, number, ct).ConfigureAwait(false);
                    if (!wirelessOnly)
                    {
                        await AddMachineAsync(context, number, ct).ConfigureAwait(false);
                    }
                });
                if (!wirelessOnly)
                {
                    builder.ConfigureGmsFor<MachineryNodeManager>(async (context, ct) =>
                    {
                        if (number == 2)
                        {
                            context = NewContext(context, wirelessOnly: false);
                        }
                        Contexts.Add(context);
                        await AddGmsAsync(context, number, ct).ConfigureAwait(false);
                    });
                    builder.ConfigureCranesHoistsFor<MachineryNodeManager>(async (context, ct) =>
                    {
                        Contexts.Add(context.Machinery);
                        string name = "Crane" + number;
                        var provider = new SimulatedCraneProvider(CompanionSuiteData.CraneSamples(name, number));
                        CraneProviders.Add(provider);
                        Cranes.Add(await CompanionSuiteData.Crane(context, name, provider)
                            .BuildAsync(ct).ConfigureAwait(false));
                    });
                    builder.ConfigureVisionFor<MachineryNodeManager>(async (context, ct) =>
                    {
                        Mock<IMachineVisionOperationsProvider> provider = CompanionSuiteData.Vision(number);
                        VisionProviders.Add(provider);
                        MachineVisionSystemHandle system = await context.AddMachineVisionSystemAsync(
                            "Inspection" + number, provider.Object, new MachineVisionSystemOptions
                            {
                                InitiallyReady = true,
                                InitiallySimulated = true,
                                Features = MachineVisionFeatures.Results | MachineVisionFeatures.Folders |
                                    MachineVisionFeatures.FileTransfer
                            }, cancellationToken: ct).ConfigureAwait(false);
                        Inspections.Add(system);
                        var identity = new MachineryIdentificationData();
                        CompanionSuiteData.Identify(identity, "Camera" + number);
                        MachineVisionAssetBuilder asset = context.AddMachineVisionAsset("Camera" + number)
                            .WithIdentification(identity).RepresentsSameSystem(system.State);
                        asset.Nodes.WithHealth(health => health.Set("Temperature", Variant.From(20.0 + number)));
                        Assets.Add(await asset.BuildAsync(ct).ConfigureAwait(false));
                        await system.PublishResultAsync(new Opc.Ua.Vision.MachineVision.ResultDataType
                        {
                            ResultId = new ResultIdDataType { Id = "Inspection" + number },
                            ResultState = number,
                            ResultContent = [Variant.From(number)],
                            EncodingMask = (uint)ResultDataTypeFields.ResultContent
                        }, ByteString.From((byte)number), ct).ConfigureAwait(false);
                    });
                }
            }
            if (failStartup)
            {
                builder.ConfigureMachineToolFor<MachineryNodeManager>((_, _) =>
                    throw new ServiceResultException(
                        StatusCodes.BadConfigurationError, "Composition startup rejection."));
            }
            m_provider = Services.BuildServiceProvider();
            MachineryNodeManagerFactory owner = Provider.GetRequiredService<MachineryNodeManagerFactory>();
            m_fixture = new ServerFixture<CompositionServer>(telemetry =>
            {
                var server = new CompositionServer(telemetry, reordered);
                server.AddNodeManager(owner);
                return server;
            }) { AutoAccept = true, SecurityNone = false };
            await m_fixture.LoadConfigurationAsync(m_root).ConfigureAwait(false);
            await m_fixture.StartAsync().ConfigureAwait(false);
            m_liveContext = NewContext(Contexts[0], wirelessOnly);
        }

        /// <summary>
        /// Connects an independently owned real session using SignAndEncrypt.
        /// </summary>
        internal async Task<ClientSession> ConnectAsync(string name)
        {
            var transport = new ClientFixture(Telemetry);
            m_clients.Add(transport);
            transport.UseSubscriptionEngineFactory(DefaultSubscriptionEngineFactory.Instance);
            await transport.LoadClientConfigurationAsync(m_root, name).ConfigureAwait(false);
            ClientSession session = await transport.ConnectAsync(
                new Uri("opc.tcp://localhost:" + m_fixture!.Port), SecurityPolicies.Basic256Sha256)
                .ConfigureAwait(false);
            if (session.NamespaceUris.GetIndex(GmsModel.NamespaceUri) >= 0)
            {
                session.MessageContext.Factory.Builder.AddGmsTypeSystem().Commit();
            }
            m_sessions.Add(session);
            return session;
        }

        /// <summary>
        /// Configures the real generic runtime rather than statically scaffolding its generated model.
        /// </summary>
        private async ValueTask AddMachineAsync(IMachineryBuildContext context, int number, CancellationToken ct)
        {
            var provider = new Mock<IMachineToolProvider>(MockBehavior.Strict);
            provider.Setup(value => value.ReadAsync(It.IsAny<CancellationToken>()))
                .Returns(() => new ValueTask<MachineToolSnapshot>(CompanionSuiteData.Machine(number)));
            MachineProviders.Add(provider);
            Machines.Add(await context.AddMachineTool("Mill" + number)
                .WithProvider(provider.Object)
                .WithIdentification(identity => CompanionSuiteData.Identify(identity, "Mill" + number))
                .WithFeatures(MachineToolFeatures.Production | MachineToolFeatures.Tools |
                    MachineToolFeatures.MachineryState)
                .BuildAsync(ct).ConfigureAwait(false));
        }

        /// <summary>
        /// Supplies separate mandatory result storage and typed sensor observations for each native GMS.
        /// </summary>
        private async ValueTask AddGmsAsync(IMachineryBuildContext context, int number, CancellationToken ct)
        {
            var provider = new Mock<IGmsProvider>(MockBehavior.Strict);
            provider.Setup(value => value.ReadAsync(It.IsAny<CancellationToken>()))
                .Returns(() => new ValueTask<GmsSystemSnapshot>(CompanionSuiteData.Gms(number)));
            GmsProviders.Add(provider);
            var encoding = ServiceMessageContext.Create(Telemetry);
            encoding.NamespaceUris = context.Context.NamespaceUris;
            encoding.Factory.Builder.AddGmsTypeSystem().Commit();
            GmsHandle handle = await context.AddGms("Gms" + number)
                .WithProvider(provider.Object)
                .WithIdentification(identity => CompanionSuiteData.Identify(identity, "Gms" + number))
                .WithOptions(new GmsSystemOptions
                {
                    Features = GmsFeatures.Sensors | GmsFeatures.ToolMonitoring | GmsFeatures.Production
                })
                .WithResults(new InMemoryGmsResultStore(encoding), new GmsResultBindingOptions
                {
                    EnableFileTransfer = true,
                    PublishedResults = 1,
                    IncludeUsedTools = true
                }).BuildAsync(ct).ConfigureAwait(false);
            Systems.Add(handle);
            await handle.Results.PublishAsync(CompanionSuiteData.Result(number), ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Uses actual measurement and record services; only equipment observations and authorization are mocked.
        /// </summary>
        private async ValueTask AddPeripheralAsync(IMachineryBuildContext context, int number, CancellationToken ct)
        {
            var device = new Mock<IWmtpDeviceProvider>(MockBehavior.Strict);
            device.Setup(value => value.ReadAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<WmtpDeviceSnapshot>(new WmtpDeviceSnapshot
                {
                    Observation = CompanionSuiteData.Value(Variant.Null),
                    Battery = CompanionSuiteData.Value(Variant.From(70.0 + number))
                }));
            var sensor = new Mock<IWmtpMeasurementProvider>(MockBehavior.Strict);
            sensor.Setup(value => value.ReadAsync("Temperature", It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<WmtpMeasurementSample>(new WmtpMeasurementSample
                {
                    Value = CompanionSuiteData.Value(Variant.From(20.0 + number)), Index = (uint)number
                }));
            var authorization = new Mock<IWmtpAuthorization>(MockBehavior.Strict);
            authorization.Setup(value => value.AuthorizeAsync(
                It.IsAny<WmtpRequest>(), It.IsAny<WmtpOperation>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<StatusCode>(StatusCodes.Good));
            var records = new InMemoryWmtpRecordStore(Telemetry, capacity: 4);
            Records.Add(records);
            await records.AppendAsync(CompanionSuiteData.Record(number), ct).ConfigureAwait(false);
            var clock = new AcquisitionClock();
            PeripheralClocks.Add(clock);
            Peripherals.Add(await context.AddWirelessPeripheral("Peripheral" + number)
                .WithProvider(device.Object).WithMeasurementProvider(sensor.Object)
                .WithClock(clock)
                .WithAuthorization(authorization.Object).WithFeatures(WmtpFeatures.Battery)
                .WithIdentification(identity => CompanionSuiteData.Identify(identity, "Peripheral" + number))
                .AddMeasurement(CompanionSuiteData.Measurement())
                .WithWorkRecords(records).BuildAsync(ct).ConfigureAwait(false));
        }

        /// <summary>
        /// Exercises the public direct-construction fallback without creating another namespace owner or root.
        /// </summary>
        private static IMachineryBuildContext NewContext(IMachineryBuildContext existing, bool wirelessOnly)
        {
            return existing.Manager.CreateMachineryBuildContext(new MachineryServerOptions
            {
                Parts = wirelessOnly ? MachineryParts.BuildingBlocks | MachineryParts.ProcessValues : RequiredParts,
                InstanceNamespaceUri = InstanceUri
            });
        }

        /// <summary>
        /// Advances UTC, monotonic time and timers together only when the test explicitly requests it.
        /// </summary>
        internal sealed class AcquisitionClock : TimeProvider
        {
            /// <inheritdoc/>
            public override long TimestampFrequency => TimeSpan.TicksPerSecond;

            /// <inheritdoc/>
            public override DateTimeOffset GetUtcNow()
            {
                return new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero).AddTicks(GetTimestamp());
            }

            /// <inheritdoc/>
            public override long GetTimestamp()
            {
                lock (m_gate)
                {
                    return m_ticks;
                }
            }

            /// <inheritdoc/>
            public override ITimer CreateTimer(
                TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            {
                if (callback == null)
                {
                    throw new ArgumentNullException(nameof(callback));
                }
                var timer = new ScheduledTimer(this, () => callback(state), dueTime, period);
                lock (m_gate)
                {
                    m_timers.Add(timer);
                }
                return timer;
            }

            /// <summary>
            /// Runs due timer callbacks without introducing wall-clock work into an otherwise frozen acquisition.
            /// </summary>
            internal void Advance(TimeSpan elapsed)
            {
                if (elapsed < TimeSpan.Zero)
                {
                    throw new ArgumentOutOfRangeException(nameof(elapsed));
                }
                ScheduledTimer[] timers;
                long now;
                lock (m_gate)
                {
                    m_ticks = checked(m_ticks + elapsed.Ticks);
                    now = m_ticks;
                    timers = m_timers.ToArray();
                }
                foreach (ScheduledTimer timer in timers)
                {
                    timer.RunDue(now);
                }
            }

            /// <summary>
            /// Represents one clock-owned schedule with the standard change and disposal contract.
            /// </summary>
            private sealed class ScheduledTimer : ITimer
            {
                /// <summary>
                /// Stores a callback until its due logical time is reached.
                /// </summary>
                internal ScheduledTimer(AcquisitionClock clock, Action callback, TimeSpan dueTime, TimeSpan period)
                {
                    m_clock = clock;
                    m_callback = callback;
                    _ = Change(dueTime, period);
                }

                /// <inheritdoc/>
                public bool Change(TimeSpan dueTime, TimeSpan period)
                {
                    ValidateDelay(dueTime, nameof(dueTime));
                    ValidateDelay(period, nameof(period));
                    lock (m_gate)
                    {
                        if (m_disposed)
                        {
                            return false;
                        }
                        m_due = dueTime == Timeout.InfiniteTimeSpan
                            ? long.MaxValue : checked(m_clock.GetTimestamp() + dueTime.Ticks);
                        m_period = period > TimeSpan.Zero ? period.Ticks : 0;
                        return true;
                    }
                }

                /// <inheritdoc/>
                public void Dispose()
                {
                    lock (m_gate)
                    {
                        m_disposed = true;
                    }
                }

                /// <inheritdoc/>
                public ValueTask DisposeAsync()
                {
                    Dispose();
                    return default;
                }

                /// <summary>
                /// Executes elapsed ticks outside the schedule gate; disposed or disabled timers cannot fire.
                /// </summary>
                internal void RunDue(long now)
                {
                    while (true)
                    {
                        lock (m_gate)
                        {
                            if (m_disposed || m_due == long.MaxValue || m_due > now)
                            {
                                return;
                            }
                            m_due = m_period == 0 ? long.MaxValue : checked(m_due + m_period);
                        }
                        m_callback();
                    }
                }

                /// <summary>
                /// Accepts nonnegative durations and the standard disabled-timer sentinel only.
                /// </summary>
                private static void ValidateDelay(TimeSpan value, string parameterName)
                {
                    if (value < TimeSpan.Zero && value != Timeout.InfiniteTimeSpan)
                    {
                        throw new ArgumentOutOfRangeException(parameterName);
                    }
                }

                /// <summary>The scheduler whose logical timestamp determines the next due time.</summary>
                private readonly AcquisitionClock m_clock;

                /// <summary>The original runtime callback and state, retained as a typed delegate.</summary>
                private readonly Action m_callback;

                /// <summary>The private schedule-state gate.</summary>
                private readonly System.Threading.Lock m_gate = new();

                /// <summary>The next due monotonic timestamp, or the disabled sentinel.</summary>
                private long m_due;

                /// <summary>The positive repeat interval, or zero for one-shot execution.</summary>
                private long m_period;

                /// <summary>Whether the runtime has disposed this schedule.</summary>
                private bool m_disposed;
            }

            /// <summary>The private clock and timer-list gate.</summary>
            private readonly System.Threading.Lock m_gate = new();

            /// <summary>The schedules that can run only when this logical clock advances.</summary>
            private readonly List<ScheduledTimer> m_timers = [];

            /// <summary>The elapsed UTC and monotonic ticks from the deterministic acquisition epoch.</summary>
            private long m_ticks;
        }

        /// <summary>
        /// Perturbs namespace indexes before the unchanged stock owner is created.
        /// </summary>
        private sealed class CompositionServer : StandardServer
        {
            /// <summary>
            /// Preserves the real StandardServer service pipeline.
            /// </summary>
            internal CompositionServer(ITelemetryContext telemetry, bool reordered)
                : base(telemetry)
            {
                m_reordered = reordered;
            }

            /// <inheritdoc/>
            protected override ValueTask<IMasterNodeManager> CreateMasterNodeManagerAsync(
                IServerInternal server, ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                if (m_reordered)
                {
                    server.NamespaceUris.GetIndexOrAppend("urn:opcua:composition:unrelated");
                    server.NamespaceUris.GetIndexOrAppend(GmsModel.NamespaceUri);
                    server.NamespaceUris.GetIndexOrAppend(Opc.Ua.MachineTool.WMTP.Namespaces.WMTP);
                    server.NamespaceUris.GetIndexOrAppend(MachineVisionModels.AssetNamespaceUri);
                }
                return base.CreateMasterNodeManagerAsync(server, configuration, cancellationToken);
            }

            /// <summary>Whether to exercise a different valid namespace index order.</summary>
            private readonly bool m_reordered;
        }

        /// <summary>The common application-owned namespace.</summary>
        internal const string InstanceUri = "urn:opcua:composition:instances";

        /// <summary>The exact union needed by the real family model providers.</summary>
        internal const MachineryParts RequiredParts = MachineryParts.BuildingBlocks |
            MachineryParts.ProcessValues | MachineryParts.Jobs | MachineryParts.Result;

        /// <summary>The fixture-owned, short worktree-local certificate store.</summary>
        private readonly string m_root = Path.GetFullPath(Path.Combine(".csuite", Guid.NewGuid().ToString("N")[..8]));

        /// <summary>The independent client configurations.</summary>
        private readonly List<ClientFixture> m_clients = [];

        /// <summary>The independent actual sessions.</summary>
        private readonly List<ClientSession> m_sessions = [];

        /// <summary>The owner factory's service container.</summary>
        private ServiceProvider? m_provider;

        /// <summary>The explicit unsealed context used for live additions after startup configurators seal.</summary>
        private IMachineryBuildContext? m_liveContext;

        /// <summary>The real server and TCP listener.</summary>
        private ServerFixture<CompositionServer>? m_fixture;
    }
}
