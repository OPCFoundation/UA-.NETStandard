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

#if NET10_0
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Machinery;
using Opc.Ua.Machinery.Server;
using Opc.Ua.MachineTool;
using Opc.Ua.MachineTool.Server;
using Opc.Ua.MachineTool.WMTP;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using ISession = Opc.Ua.Client.ISession;
using ISubscriptionManager = Opc.Ua.Client.Subscriptions.ISubscriptionManager;

namespace Opc.Ua.Tools.Tests
{
    /// <summary>
    /// Owns an actual WMTP-only TCP server, exact numeric sensors, independent stores and explicit fake equipment.
    /// </summary>
    internal sealed class WmtpMcpFixture : IAsyncDisposable
    {
        /// <summary>
        /// Creates deterministic equipment observers and initially refusing transfer providers.
        /// </summary>
        internal WmtpMcpFixture()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            Work = new InMemoryWmtpRecordStore(telemetry);
            Service = new InMemoryWmtpRecordStore(telemetry);
            OtherWork = new InMemoryWmtpRecordStore(telemetry);
            m_server = new ServerFixture<RuntimeServer>(context => new RuntimeServer(context))
            {
                AutoAccept = true, SecurityNone = true
            };
            Device.Setup(provider => provider.ReadAsync(It.IsAny<CancellationToken>()))
                .Returns((CancellationToken _) => new ValueTask<WmtpDeviceSnapshot>(Snapshot()));
            Measurement.Setup(provider => provider.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string name, CancellationToken _) => new ValueTask<WmtpMeasurementSample>(m_samples[name]));
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
            WorkProvider.Setup(provider => provider.CountAsync(
                    It.IsAny<WmtpRecordQuery>(), It.IsAny<CancellationToken>()))
                .Returns((WmtpRecordQuery query, CancellationToken token) => Work.CountAsync(query, token));
            WorkProvider.Setup(provider => provider.ReadAsync(
                    It.IsAny<WmtpRecordQuery>(), It.IsAny<CancellationToken>()))
                .Returns((WmtpRecordQuery query, CancellationToken token) => Work.ReadAsync(query, token));
            WorkProvider.Setup(provider => provider.DeleteAsync(
                    It.IsAny<WmtpRecordQuery>(), It.IsAny<CancellationToken>()))
                .Returns((WmtpRecordQuery query, CancellationToken token) => Work.DeleteAsync(query, token));
            OtherWorkProvider.Setup(provider => provider.CountAsync(
                    It.IsAny<WmtpRecordQuery>(), It.IsAny<CancellationToken>()))
                .Returns((WmtpRecordQuery query, CancellationToken token) => OtherWork.CountAsync(query, token));
            OtherWorkProvider.Setup(provider => provider.ReadAsync(
                    It.IsAny<WmtpRecordQuery>(), It.IsAny<CancellationToken>()))
                .Returns((WmtpRecordQuery query, CancellationToken token) => OtherWork.ReadAsync(query, token));
            OtherWorkProvider.Setup(provider => provider.DeleteAsync(
                    It.IsAny<WmtpRecordQuery>(), It.IsAny<CancellationToken>()))
                .Returns((WmtpRecordQuery query, CancellationToken token) => OtherWork.DeleteAsync(query, token));
        }

        /// <summary>
        /// Gets the deterministic acquisition timestamp, independent of wall-clock receipt.
        /// </summary>
        internal static DateTimeUtc SourceTime { get; } =
            new(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));

        /// <summary>
        /// Gets a live session with the existing subscription engine.
        /// </summary>
        internal ISession Session { get; private set; } = null!;

        /// <summary>
        /// Gets the independently connected second caller.
        /// </summary>
        internal ISession SecondSession { get; private set; } = null!;

        /// <summary>
        /// Gets the live subscription manager whose existing resources must survive borrowed-facade cleanup.
        /// </summary>
        internal ISubscriptionManager Subscriptions => Session.TryGetSubscriptionManager(
            out ISubscriptionManager? manager) ? manager :
            throw new InvalidOperationException("The managed test session has no subscription manager.");

        /// <summary>
        /// Gets the full provider-backed peripheral.
        /// </summary>
        internal WmtpPeripheral Peripheral { get; private set; } = null!;

        /// <summary>
        /// Gets a second peripheral with its own work records.
        /// </summary>
        internal WmtpPeripheral Other { get; private set; } = null!;

        /// <summary>
        /// Gets a mandatory-only peripheral with absent optional metadata and records.
        /// </summary>
        internal WmtpPeripheral Minimal { get; private set; } = null!;

        /// <summary>
        /// Gets the independent retained work dataset.
        /// </summary>
        internal InMemoryWmtpRecordStore Work { get; }

        /// <summary>
        /// Gets the independent retained service dataset.
        /// </summary>
        internal InMemoryWmtpRecordStore Service { get; }

        /// <summary>
        /// Gets a different peripheral's retained records.
        /// </summary>
        internal InMemoryWmtpRecordStore OtherWork { get; }

        /// <summary>
        /// Gets the explicit device observer.
        /// </summary>
        internal Mock<IWmtpDeviceProvider> Device { get; } = new(MockBehavior.Strict);

        /// <summary>
        /// Gets the strict sensor adapter used to assert exact numeric types and negative effects.
        /// </summary>
        internal Mock<IWmtpMeasurementProvider> Measurement { get; } = new(MockBehavior.Strict);

        /// <summary>
        /// Gets the explicit server-side authorization policy.
        /// </summary>
        internal Mock<IWmtpAuthorization> Authorization { get; } = new(MockBehavior.Strict);

        /// <summary>
        /// Gets the strict calibration provider.
        /// </summary>
        internal Mock<IWmtpCalibrationProvider> Calibration { get; } = new(MockBehavior.Strict);

        /// <summary>
        /// Gets the strict device-time provider.
        /// </summary>
        internal Mock<IWmtpDeviceTimeProvider> DeviceTime { get; } = new(MockBehavior.Strict);

        /// <summary>
        /// Gets the explicit initially refusing transfer provider.
        /// </summary>
        internal Mock<IWmtpTransferProvider> Transfers { get; } = new(MockBehavior.Strict);

        /// <summary>
        /// Gets the actual work-store adapter, replaceable with coordinated blocking/refusal behavior.
        /// </summary>
        internal Mock<IWmtpRecordStore> WorkProvider { get; } = new(MockBehavior.Strict);

        /// <summary>
        /// Gets the independent second peripheral's store adapter for concurrent-operation isolation tests.
        /// </summary>
        internal Mock<IWmtpRecordStore> OtherWorkProvider { get; } = new(MockBehavior.Strict);

        /// <summary>
        /// Starts the stock server with WMTP only, deliberately reordering model namespace indices.
        /// </summary>
        internal async Task StartAsync()
        {
            await m_server.LoadConfigurationAsync(Path.Combine(m_root, "s")).ConfigureAwait(false);
            RuntimeServer server = await m_server.StartAsync().ConfigureAwait(false);
            IMachineryBuildContext context = server.Manager.CreateMachineryBuildContext();
            for (uint index = 1; index <= 3; index++)
            {
                await Work.AppendAsync(Record(index)).ConfigureAwait(false);
                await Service.AppendAsync(Record(index, 100)).ConfigureAwait(false);
            }
            await OtherWork.AppendAsync(Record(20, 200)).ConfigureAwait(false);
            WmtpPeripheralBuilder builder = Builder(context, "WmtpMcpFull")
                .WithFeatures(WmtpFeatures.All)
                .WithWorkRecords(WorkProvider.Object, WmtpRecordFeatures.All)
                .WithServiceRecords(Service, WmtpRecordFeatures.All);
            foreach (BuiltInType type in NumericTypes())
            {
                Variant sample = Scalar(type, type == BuiltInType.UInt64 ? 9007199254740993ul : 8ul);
                m_samples[type.ToString()] = new WmtpMeasurementSample
                {
                    Value = new DataValue(sample, StatusCodes.UncertainSensorNotAccurate, SourceTime),
                    Index = 7, MeasurementPeriod = 50
                };
                builder.AddMeasurement(Definition(type));
            }
            Peripheral = await builder.BuildAsync().ConfigureAwait(false);
            Other = await Builder(context, "WmtpMcpOther")
                .WithWorkRecords(OtherWorkProvider.Object, WmtpRecordFeatures.All)
                .AddMeasurement(Definition(BuiltInType.Double)).BuildAsync().ConfigureAwait(false);
            Minimal = await Builder(context, "WmtpMcpMinimal").BuildAsync().ConfigureAwait(false);
            Session = await ConnectAsync("a").ConfigureAwait(false);
            SecondSession = await ConnectAsync("b").ConfigureAwait(false);
        }

        /// <summary>
        /// Gets an exact sensor object rather than guessing namespace indices or NodeId suffixes.
        /// </summary>
        internal WmtpMeasurement Sensor(BuiltInType type)
        {
            foreach (WmtpMeasurement measurement in Peripheral.Measurements)
            {
                if (measurement.State.BrowseName.Name == type.ToString())
                {
                    return measurement;
                }
            }
            throw new InvalidOperationException("The requested test sensor is not configured.");
        }

        /// <summary>
        /// Creates independent report structures with nondefault released metadata.
        /// </summary>
        internal static WMTPOutputDataType Record(uint index, double offset = 0)
        {
            return new WMTPOutputDataType
            {
                EngineeringUnits = Units(),
                ActualValue = index + 0.5 + offset,
                TypeOfMeasurement = 0,
                TypeOfSample = 1,
                InstrumentRange = new Range { Low = -100, High = 1000 },
                EURange = new Range { Low = 0, High = 100 },
                ValuePrecision = 0.125,
                Definition = "Provider acquisition",
                SignalTag = "wireless-sensor",
                Timestamp = SourceTime.Add(TimeSpan.FromSeconds(index)),
                Index = index,
                MeasurementPeriod = 50,
                InternalUpdateInterval = 25,
                RelativeUncertainty = 0.5,
                AbsoluteUncertainty = 0.25
            };
        }

        /// <summary>
        /// Supplies all supported native numeric wire types.
        /// </summary>
        internal static ArrayOf<BuiltInType> NumericTypes()
        {
            return
            [
                BuiltInType.SByte, BuiltInType.Byte, BuiltInType.Int16, BuiltInType.UInt16,
                BuiltInType.Int32, BuiltInType.UInt32, BuiltInType.Int64, BuiltInType.UInt64,
                BuiltInType.Float, BuiltInType.Double
            ];
        }

        /// <summary>
        /// Builds exact typed values without boxing or double-rounding an integral sample.
        /// </summary>
        internal static Variant Scalar(BuiltInType type, ulong value)
        {
            return type switch
            {
                BuiltInType.SByte => Variant.From(checked((sbyte)value)),
                BuiltInType.Byte => Variant.From(checked((byte)value)),
                BuiltInType.Int16 => Variant.From(checked((short)value)),
                BuiltInType.UInt16 => Variant.From(checked((ushort)value)),
                BuiltInType.Int32 => Variant.From(checked((int)value)),
                BuiltInType.UInt32 => Variant.From(checked((uint)value)),
                BuiltInType.Int64 => Variant.From(checked((long)value)),
                BuiltInType.UInt64 => Variant.From(value),
                BuiltInType.Float => Variant.From((float)value),
                _ => Variant.From((double)value)
            };
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            foreach (ISession session in m_sessions)
            {
                if (session.Connected)
                {
                    await session.CloseAsync(1000, true).ConfigureAwait(false);
                }
                if (session is IAsyncDisposable disposable)
                {
                    await disposable.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    session.Dispose();
                }
            }
            foreach (ClientFixture client in m_clients)
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }
            if (Minimal != null)
            {
                await Minimal.DisposeAsync().ConfigureAwait(false);
            }
            if (Other != null)
            {
                await Other.DisposeAsync().ConfigureAwait(false);
            }
            if (Peripheral != null)
            {
                await Peripheral.DisposeAsync().ConfigureAwait(false);
            }
            await Work.DisposeAsync().ConfigureAwait(false);
            await Service.DisposeAsync().ConfigureAwait(false);
            await OtherWork.DisposeAsync().ConfigureAwait(false);
            await m_server.StopAsync().ConfigureAwait(false);
            if (Directory.Exists(m_root))
            {
                Directory.Delete(m_root, true);
            }
        }

        /// <summary>
        /// Configures only public provider-backed builder seams.
        /// </summary>
        private WmtpPeripheralBuilder Builder(IMachineryBuildContext context, string name)
        {
            return context.AddWirelessPeripheral(name)
                .WithProvider(Device.Object).WithMeasurementProvider(Measurement.Object)
                .WithAuthorization(Authorization.Object).WithCalibrationProvider(Calibration.Object)
                .WithDeviceTimeProvider(DeviceTime.Object).WithTransferProvider(Transfers.Object)
                .WithClock(new WmtpMcpClock()).WithIdentification(identity =>
                {
                    identity.Manufacturer = new LocalizedText("Explicit WMTP test equipment");
                    identity.SerialNumber = name;
                });
        }

        /// <summary>
        /// Connects a real borrowed-capable managed session with its own short PKI store.
        /// </summary>
        private async Task<ISession> ConnectAsync(string name)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var client = new ClientFixture(telemetry);
            m_clients.Add(client);
            client.UseSubscriptionEngineFactory(DefaultSubscriptionEngineFactory.Instance);
            client.SessionFactory = new ManagedSessionFactory(telemetry)
                .WithSubscriptionEngine(DefaultSubscriptionEngineFactory.Instance);
            await client.LoadClientConfigurationAsync(Path.Combine(m_root, name), "WmtpMcp" + name)
                .ConfigureAwait(false);
            ISession session = await client.ConnectAsync(
                new Uri($"opc.tcp://localhost:{m_server.Port}"), SecurityPolicies.None).ConfigureAwait(false);
            m_sessions.Add(session);
            return session;
        }

        /// <summary>
        /// Defines complete measurement metadata with an exact initial delta type.
        /// </summary>
        private static WmtpMeasurementDefinition Definition(BuiltInType type)
        {
            return new WmtpMeasurementDefinition
            {
                Name = type.ToString(), Kind = WmtpMeasurementKind.Temperature,
                Features = WmtpMeasurementFeatures.All, DataType = type, Delta = Scalar(type, 0),
                EngineeringUnits = Units(), EURange = new Range { Low = 0, High = 100 },
                InstrumentRange = new Range { Low = -100, High = 1000 },
                ValuePrecision = 0.125, InternalUpdateInterval = 25,
                RelativeUncertainty = 0.5, AbsoluteUncertainty = 0.25, High = 80, HighHigh = 90
            };
        }

        /// <summary>
        /// Supplies explicit acquired device information, never synthetic clock-derived equipment lifetime.
        /// </summary>
        private static WmtpDeviceSnapshot Snapshot()
        {
            var lifetime = new WmtpLifetime
            {
                Remaining = Variant.From(80d), Start = Variant.From(100d), Limit = Variant.From(0d),
                EngineeringUnits = new EUInformation
                {
                    NamespaceUri = "http://www.opcfoundation.org/UA/units/un/cefact",
                    UnitId = 4740434, DisplayName = new LocalizedText("h")
                }
            };
            return new WmtpDeviceSnapshot
            {
                Observation = new DataValue(Variant.From(1u), StatusCodes.Good, SourceTime),
                CalibrationMode = false,
                Battery = new DataValue(Variant.From(75d), StatusCodes.UncertainSensorNotAccurate, SourceTime),
                LicenseInformation = "Test entitlement description",
                LicenseValidity = lifetime,
                LastService = SourceTime,
                NextServiceDate = SourceTime.Add(TimeSpan.FromDays(30)),
                LifeCycleStatus = 1,
                RemainingLifetime = lifetime with { Remaining = Variant.From(60d) },
                RemainingCycles = lifetime with { Remaining = Variant.From(40d) },
                NextService = lifetime with { Remaining = Variant.From(20d) },
                OperationHours = 72,
                OperationSinceService = 5000
            };
        }

        /// <summary>
        /// Supplies standard temperature units for independent samples and record copies.
        /// </summary>
        private static EUInformation Units()
        {
            return new EUInformation
            {
                NamespaceUri = "http://www.opcfoundation.org/UA/units/un/cefact",
                UnitId = 4408652, DisplayName = new LocalizedText("°C")
            };
        }

        /// <summary>
        /// Anchors test certificates inside the source worktree rather than a system temporary directory.
        /// </summary>
        private static string PkiRoot([CallerFilePath] string sourcePath = "")
        {
            string root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourcePath)!, "..", ".."));
            return Path.Combine(root, ".wm", Guid.NewGuid().ToString("N")[..8]);
        }

        /// <summary>
        /// Hosts only the WMTP closure through the existing Machinery namespace owner.
        /// </summary>
        private sealed class RuntimeServer : StandardServer
        {
            /// <summary>
            /// Creates the real transport host.
            /// </summary>
            internal RuntimeServer(ITelemetryContext telemetry) : base(telemetry)
            {
                var options = new MachineryServerOptions
                {
                    Parts = MachineryParts.BuildingBlocks | MachineryParts.ProcessValues
                };
                AddNodeManager(new MachineryNodeManagerFactory(
                    [new MachineryModelProvider(options.Parts), new MachineToolModelProvider(MachineToolParts.Wmtp)],
                    options));
            }

            /// <summary>
            /// Gets the shared namespace owner.
            /// </summary>
            internal MachineryNodeManager Manager { get; private set; } = null!;

            /// <inheritdoc/>
            protected override ValueTask<IMasterNodeManager> CreateMasterNodeManagerAsync(
                IServerInternal server, ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                server.NamespaceUris.GetIndexOrAppend("urn:wmtp-mcp:unrelated:first");
                server.NamespaceUris.GetIndexOrAppend(Opc.Ua.MachineTool.WMTP.Namespaces.WMTP);
                return base.CreateMasterNodeManagerAsync(server, configuration, cancellationToken);
            }

            /// <inheritdoc/>
            protected override async ValueTask OnNodeManagerStartedAsync(
                IServerInternal server,
                CancellationToken cancellationToken = default)
            {
                Manager = server.NodeManager.AsyncNodeManagers.OfType<MachineryNodeManager>().Single();
                await base.OnNodeManagerStartedAsync(server, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Keeps equipment age and counter calculations deterministic without a background clock helper.
        /// </summary>
        private sealed class WmtpMcpClock : TimeProvider
        {
            /// <inheritdoc/>
            public override DateTimeOffset GetUtcNow()
            {
                return new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
            }

            /// <inheritdoc/>
            public override long GetTimestamp()
            {
                return 0;
            }
        }

        /// <summary>The owned transport server.</summary>
        private readonly ServerFixture<RuntimeServer> m_server;

        /// <summary>The exact next sensor acquisitions.</summary>
        private readonly Dictionary<string, WmtpMeasurementSample> m_samples = new(StringComparer.Ordinal);

        /// <summary>The owned test client transports.</summary>
        private readonly List<ClientFixture> m_clients = [];

        /// <summary>The test-owned sessions borrowed by MCP.</summary>
        private readonly List<ISession> m_sessions = [];

        /// <summary>The short source-local certificate directory.</summary>
        private readonly string m_root = PkiRoot();
    }
}
#endif
