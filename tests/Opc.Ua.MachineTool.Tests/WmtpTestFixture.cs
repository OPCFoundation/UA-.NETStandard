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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Opc.Ua.Machinery.Server;
using Opc.Ua.MachineTool.Server;
using Opc.Ua.MachineTool.WMTP;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.MachineTool.Tests
{
    /// <summary>
    /// Real WMTP-only or combined hosting through the existing Machinery namespace owner.
    /// </summary>
    internal sealed class WmtpTestFixture : IAsyncDisposable
    {
        /// <summary>
        /// Gets the live shared build context.
        /// </summary>
        internal IMachineryBuildContext Context { get; private set; } = null!;

        /// <summary>
        /// Gets the short worktree-local test PKI path; no system temporary directory is used.
        /// </summary>
        internal string Root { get; } = CreatePkiRoot();

        /// <summary>
        /// Gets the dynamically chosen actual TCP endpoint.
        /// </summary>
        internal Uri Endpoint => new($"opc.tcp://localhost:{m_fixture!.Port}");

        /// <summary>
        /// Starts the stock server with the exact selected model closure.
        /// </summary>
        internal async ValueTask StartAsync(MachineToolParts parts = MachineToolParts.Wmtp, bool reordered = false)
        {
            m_fixture = new ServerFixture<RuntimeServer>(telemetry => new RuntimeServer(telemetry, parts, reordered))
            {
                AutoAccept = true,
                SecurityNone = true
            };
            await m_fixture.LoadConfigurationAsync(Root).ConfigureAwait(false);
            RuntimeServer server = await m_fixture.StartAsync().ConfigureAwait(false);
            Context = server.Manager.CreateMachineryBuildContext();
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
                Directory.Delete(Root, true);
            }
        }

        /// <summary>
        /// Anchors PKI to the short source worktree, not the long external multi-target artifact directory.
        /// </summary>
        private static string CreatePkiRoot([CallerFilePath] string sourcePath = "")
        {
            string repository = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourcePath)!, "..", ".."));
            return Path.Combine(repository, ".wp", Guid.NewGuid().ToString("N")[..8]);
        }

        /// <summary>
        /// Hosts real generated models and their actual dependencies.
        /// </summary>
        private sealed class RuntimeServer : StandardServer
        {
            /// <summary>
            /// Creates an isolated model-selection server.
            /// </summary>
            internal RuntimeServer(ITelemetryContext telemetry, MachineToolParts parts, bool reordered)
                : base(telemetry)
            {
                m_parts = parts;
                m_reordered = reordered;
            }

            /// <summary>
            /// Gets the existing Machinery owner.
            /// </summary>
            internal MachineryNodeManager Manager { get; private set; } = null!;

            /// <inheritdoc/>
            protected override ValueTask<IMasterNodeManager> CreateMasterNodeManagerAsync(
                IServerInternal server, ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                if (m_reordered)
                {
                    server.NamespaceUris.GetIndexOrAppend("urn:wmtp-tests:unrelated:1");
                    server.NamespaceUris.GetIndexOrAppend("urn:wmtp-tests:unrelated:2");
                    server.NamespaceUris.GetIndexOrAppend(Opc.Ua.MachineTool.WMTP.Namespaces.WMTP);
                }
                var options = new MachineryServerOptions
                {
                    Parts = MachineryParts.BuildingBlocks | MachineryParts.ProcessValues |
                        ((m_parts & MachineToolParts.MachineTool) != 0 ? MachineryParts.Jobs : MachineryParts.None)
                };
                Manager = new MachineryNodeManager(server, configuration,
                    [new MachineryModelProvider(options.Parts), new MachineToolModelProvider(m_parts)], options);
                return new ValueTask<IMasterNodeManager>(
                    new MasterNodeManager(server, configuration, null, [Manager]));
            }

            /// <summary>The selected model parts.</summary>
            private readonly MachineToolParts m_parts;

            /// <summary>Whether unrelated namespace entries precede the models.</summary>
            private readonly bool m_reordered;
        }

        /// <summary>The actual transport fixture.</summary>
        private ServerFixture<RuntimeServer>? m_fixture;
    }

    /// <summary>
    /// Explicit Moq equipment adapters with independent, test-controlled observations and acceptance.
    /// </summary>
    internal sealed class WmtpTestEquipment
    {
        /// <summary>
        /// Creates strict equipment interfaces; no transfer is accepted by default.
        /// </summary>
        internal WmtpTestEquipment()
        {
            Device.Setup(provider => provider.ReadAsync(It.IsAny<CancellationToken>()))
                .Returns((CancellationToken _) => new ValueTask<WmtpDeviceSnapshot>(Snapshot));
            Measurement.Setup(provider => provider.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string name, CancellationToken _) => new ValueTask<WmtpMeasurementSample>(Samples[name]));
            Measurement.Setup(provider => provider.ConfigureAsync(
                    It.IsAny<string>(), It.IsAny<uint>(), It.IsAny<Variant>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<StatusCode>(StatusCodes.Good));
            Authorization.Setup(provider => provider.AuthorizeAsync(
                    It.IsAny<WmtpRequest>(), It.IsAny<WmtpOperation>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<StatusCode>(StatusCodes.Good));
            Calibration.Setup(provider => provider.SetCalibrationAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<StatusCode>(StatusCodes.Good));
            DeviceTime.Setup(provider => provider.SetTimeAsync(
                    It.IsAny<DateTimeUtc>(), It.IsAny<TimeZoneDataType>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<StatusCode>(StatusCodes.Good));
            Transfers.Setup(provider => provider.TransferAsync(
                    It.IsAny<WmtpTransferKind>(), It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<StatusCode>(StatusCodes.BadNotSupported));
        }

        /// <summary>
        /// Gets the strict device observer.
        /// </summary>
        internal Mock<IWmtpDeviceProvider> Device { get; } = new(MockBehavior.Strict);

        /// <summary>
        /// Gets the strict sensor adapter.
        /// </summary>
        internal Mock<IWmtpMeasurementProvider> Measurement { get; } = new(MockBehavior.Strict);

        /// <summary>
        /// Gets the explicit caller authorization policy.
        /// </summary>
        internal Mock<IWmtpAuthorization> Authorization { get; } = new(MockBehavior.Strict);

        /// <summary>
        /// Gets the strict calibration adapter.
        /// </summary>
        internal Mock<IWmtpCalibrationProvider> Calibration { get; } = new(MockBehavior.Strict);

        /// <summary>
        /// Gets the strict device-time adapter.
        /// </summary>
        internal Mock<IWmtpDeviceTimeProvider> DeviceTime { get; } = new(MockBehavior.Strict);

        /// <summary>
        /// Gets the strict, initially refusing transfer adapter.
        /// </summary>
        internal Mock<IWmtpTransferProvider> Transfers { get; } = new(MockBehavior.Strict);

        /// <summary>
        /// Gets the deterministic receipt and monotonic clock.
        /// </summary>
        internal WmtpTestClock Clock { get; } = new();

        /// <summary>
        /// Gets or sets the next actual device observation.
        /// </summary>
        internal WmtpDeviceSnapshot Snapshot { get; set; } = new();

        /// <summary>
        /// Gets the explicit next sensor acquisitions.
        /// </summary>
        internal Dictionary<string, WmtpMeasurementSample> Samples { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// Configures a real runtime with explicit adapters and a valid component nameplate.
        /// </summary>
        internal WmtpPeripheralBuilder Builder(IMachineryBuildContext context, string name = "Peripheral")
        {
            return context.AddWirelessPeripheral(name)
                .WithProvider(Device.Object).WithMeasurementProvider(Measurement.Object)
                .WithAuthorization(Authorization.Object).WithCalibrationProvider(Calibration.Object)
                .WithDeviceTimeProvider(DeviceTime.Object).WithTransferProvider(Transfers.Object)
                .WithClock(Clock).WithIdentification(identity =>
                {
                    identity.Manufacturer = new LocalizedText("WMTP test equipment");
                    identity.SerialNumber = name;
                });
        }

        /// <summary>
        /// Creates a precise explicit definition for each released physical measurement kind.
        /// </summary>
        internal static WmtpMeasurementDefinition Definition(
            WmtpMeasurementKind kind = WmtpMeasurementKind.Temperature,
            WmtpMeasurementFeatures features = WmtpMeasurementFeatures.None)
        {
            (int id, string symbol) = kind switch
            {
                WmtpMeasurementKind.Temperature => (4408652, "°C"),
                WmtpMeasurementKind.RotationSpeed => (5394509, "rpm"),
                WmtpMeasurementKind.Humidity => (20529, "%"),
                WmtpMeasurementKind.Acceleration => (5067595, "m/s²"),
                WmtpMeasurementKind.Force => (5129559, "N"),
                WmtpMeasurementKind.Torque => (20053, "N·m"),
                WmtpMeasurementKind.Position => (5067858, "m"),
                _ => (5066068, "mm")
            };
            return new WmtpMeasurementDefinition
            {
                Name = kind.ToString(), Kind = kind, Features = features,
                EngineeringUnits = new EUInformation
                {
                    NamespaceUri = "http://www.opcfoundation.org/UA/units/un/cefact",
                    UnitId = id, DisplayName = new LocalizedText(symbol)
                },
                EURange = new Range { Low = 0, High = 100 },
                InstrumentRange = new Range { Low = -100, High = 200 },
                ValuePrecision = 0.125, InternalUpdateInterval = 25, RelativeUncertainty = 0.5,
                AbsoluteUncertainty = 0.25, High = 80, HighHigh = 90
            };
        }

        /// <summary>
        /// Creates an actual acquisition with no implicit current-time fallback.
        /// </summary>
        internal WmtpMeasurementSample Sample(Variant value, uint index = 17, StatusCode status = default)
        {
            return new WmtpMeasurementSample
            {
                Value = new DataValue(value, status, Clock.GetUtcNow()), Index = index, MeasurementPeriod = 50
            };
        }

        /// <summary>
        /// Supplies all explicitly selected optional metadata for feature-layout tests.
        /// </summary>
        internal void FullSnapshot()
        {
            var life = new WmtpLifetime
            {
                Remaining = Variant.From(80d), Start = Variant.From(100d), Limit = Variant.From(0d),
                EngineeringUnits = new EUInformation
                {
                    NamespaceUri = "http://www.opcfoundation.org/UA/units/un/cefact",
                    UnitId = 4740434, DisplayName = new LocalizedText("h")
                }
            };
            WmtpLifetime cycles = life with
            {
                EngineeringUnits = new EUInformation
                {
                    NamespaceUri = "http://www.opcfoundation.org/UA/units/un/cefact",
                    UnitId = 4404786, DisplayName = new LocalizedText("cycle")
                }
            };
            Snapshot = new WmtpDeviceSnapshot
            {
                Observation = new DataValue(Variant.From(1u), StatusCodes.Good, Clock.GetUtcNow()),
                CalibrationMode = false, Battery = Sample(Variant.From(75d)).Value,
                LicenseInformation = "test authorization description", LicenseValidity = life,
                LastService = Clock.GetUtcNow(), NextServiceDate = Clock.GetUtcNow().AddDays(30),
                LifeCycleStatus = 1, RemainingLifetime = life, RemainingCycles = cycles, NextService = life,
                OperationHours = 72, OperationSinceService = 5000
            };
        }
    }

    /// <summary>
    /// A deterministic injectable clock with explicitly pulsed timer callbacks.
    /// </summary>
    internal sealed class WmtpTestClock : TimeProvider
    {
        /// <inheritdoc/>
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        /// <inheritdoc/>
        public override DateTimeOffset GetUtcNow()
        {
            return new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero).AddTicks(m_ticks);
        }

        /// <inheritdoc/>
        public override long GetTimestamp()
        {
            return m_ticks;
        }

        /// <inheritdoc/>
        public override ITimer CreateTimer(
            TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(callback, state);
            m_timers.Add(timer);
            return timer;
        }

        /// <summary>
        /// Advances both clocks without background nondeterminism.
        /// </summary>
        internal void Advance(TimeSpan elapsed)
        {
            m_ticks += elapsed.Ticks;
        }

        /// <summary>
        /// Fires active freshness timers explicitly.
        /// </summary>
        internal void Pulse()
        {
            foreach (ManualTimer timer in m_timers)
            {
                timer.Fire();
            }
        }

        /// <summary>
        /// Provides the standard timer lifetime without worker threads.
        /// </summary>
        private sealed class ManualTimer : ITimer
        {
            /// <summary>
            /// Retains the typed callback contract required by TimeProvider.
            /// </summary>
            internal ManualTimer(TimerCallback callback, object? state)
            {
                m_callback = callback;
                m_state = state;
            }

            /// <inheritdoc/>
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                return !m_disposed;
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                m_disposed = true;
            }

            /// <inheritdoc/>
            public ValueTask DisposeAsync()
            {
                Dispose();
                return default;
            }

            /// <summary>
            /// Fires only a currently owned timer.
            /// </summary>
            internal void Fire()
            {
                if (!m_disposed)
                {
                    m_callback(m_state);
                }
            }

            /// <summary>The BCL timer delegate.</summary>
            private readonly TimerCallback m_callback;

            /// <summary>The BCL timer context, never a synchronization root.</summary>
            private readonly object? m_state;

            /// <summary>Whether ownership ended.</summary>
            private bool m_disposed;
        }

        /// <summary>The owned timer callbacks.</summary>
        private readonly List<ManualTimer> m_timers = [];

        /// <summary>The elapsed monotonic time.</summary>
        private long m_ticks;
    }
}
