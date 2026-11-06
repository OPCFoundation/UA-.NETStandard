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

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Opc.Ua.Di;
using Opc.Ua.ECM;
using Opc.Ua.ECM.Client;
using Opc.Ua.ECM.Server;
using Opc.Ua.Server;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// Roots generated ECM encodings, simulated power callbacks and hosting in the NativeAOT test binary.
    /// </summary>
    public sealed class EcmAotTests
    {
        /// <summary>
        /// Distinct phase values and high-precision timings survive generated binary encoding.
        /// </summary>
        [Test]
        public async Task GeneratedElectricalAndStandbyStructuresRetainValuesAsync()
        {
            ITelemetryContext telemetry = DefaultTelemetry.Create(
                logging => logging.SetMinimumLevel(LogLevel.Warning));
            var message = ServiceMessageContext.Create(telemetry);
            message.NamespaceUris.GetIndexOrAppend(ECM.Namespaces.ECM);
            message.Factory.Builder.AddOpcUaECM().Commit();

            var pe = new AcPeDataType { L1 = 1.25f, L2 = 2.5f, L3 = 3.75f };
            AcPeDataType decodedPe = BinaryDecoder.DecodeMessage<AcPeDataType>(
                BinaryEncoder.EncodeMessage(pe, message), message);
            await Assert.That(decodedPe.L1).IsEqualTo(1.25f);
            await Assert.That(decodedPe.L2).IsEqualTo(2.5f);
            await Assert.That(decodedPe.L3).IsEqualTo(3.75f);

            var pp = new AcPpDataType { L1L2 = 220.5f, L2L3 = 221.25f, L3L1 = 222.75f };
            AcPpDataType decodedPp = BinaryDecoder.DecodeMessage<AcPpDataType>(
                BinaryEncoder.EncodeMessage(pp, message), message);
            await Assert.That(decodedPp.L1L2).IsEqualTo(220.5f);
            await Assert.That(decodedPp.L2L3).IsEqualTo(221.25f);
            await Assert.That(decodedPp.L3L1).IsEqualTo(222.75f);

            var period = new MeasurementPeriodDataType
            {
                Duration = 900000,
                Definition = MeasurementPeriodEnum.FixedBlockCompleted
            };
            MeasurementPeriodDataType decodedPeriod = BinaryDecoder.DecodeMessage<MeasurementPeriodDataType>(
                BinaryEncoder.EncodeMessage(period, message), message);
            await Assert.That(decodedPeriod.Duration).IsEqualTo(900000d);
            await Assert.That(decodedPeriod.Definition).IsEqualTo(MeasurementPeriodEnum.FixedBlockCompleted);

            var state = new EnergyStateInformationDataType
            {
                IDSource = 1,
                IDDestination = 2,
                RegularTimeToOperate = 3000.125,
                ModePowerConsumption = 0.75f
            };
            EnergyStateInformationDataType decodedState =
                BinaryDecoder.DecodeMessage<EnergyStateInformationDataType>(
                    BinaryEncoder.EncodeMessage(state, message), message);
            await Assert.That(decodedState.IDSource).IsEqualTo((byte)1);
            await Assert.That(decodedState.IDDestination).IsEqualTo((byte)2);
            await Assert.That(decodedState.RegularTimeToOperate).IsEqualTo(3000.125);
            await Assert.That(decodedState.ModePowerConsumption).IsEqualTo(0.75f);

            var transition = new StandbyModeTransitionDataType
            {
                IDDestination = 2,
                CurrentTimeToDestination = 1000.125,
                CurrentTimeToOperate = 5000.25,
                EnergyConsumptionToDestination = 0.125f
            };
            StandbyModeTransitionDataType decodedTransition =
                BinaryDecoder.DecodeMessage<StandbyModeTransitionDataType>(
                    BinaryEncoder.EncodeMessage(transition, message), message);
            await Assert.That(decodedTransition.IDDestination).IsEqualTo((byte)2);
            await Assert.That(decodedTransition.CurrentTimeToDestination).IsEqualTo(1000.125);
            await Assert.That(decodedTransition.CurrentTimeToOperate).IsEqualTo(5000.25);
            await Assert.That(decodedTransition.EnergyConsumptionToDestination).IsEqualTo(0.125f);
        }

        /// <summary>
        /// Generated callbacks and fluent registrations remain reachable without power or network side effects.
        /// </summary>
        [Test]
        public async Task PowerCallbacksAndFluentCompositionAreReachableAsync()
        {
            ITelemetryContext telemetry = DefaultTelemetry.Create(
                logging => logging.SetMinimumLevel(LogLevel.Warning));
            var message = ServiceMessageContext.Create(telemetry);
            message.NamespaceUris.GetIndexOrAppend(Di.Namespaces.OpcUaDi);
            message.NamespaceUris.GetIndexOrAppend(IA.Namespaces.IA);
            message.NamespaceUris.GetIndexOrAppend(ECM.Namespaces.ECM);
            ushort instances = message.NamespaceUris.GetIndexOrAppend("urn:ecm-aot:instances");
            message.Factory.Builder.AddOpcUaECM().Commit();
            var context = new SystemContext(telemetry)
            {
                NamespaceUris = message.NamespaceUris,
                EncodeableFactory = message.Factory,
                NodeIdFactory = new DefaultNodeIdFactory(NodeIdAssignmentMode.String, instances),
                TypeTable = CreateTypeTable(message.NamespaceUris)
            };
            var nodes = new NodeStateCollection();
            nodes.AddOpcUaDi(context);
            new EcmModelProvider().AddPredefinedNodes(nodes, context);
            EnergyStandbyManagementState standby = context.CreateInstanceOfEnergyStandbyManagementType(
                null, new QualifiedName("Power", instances));
            EnergyDevicePowerOffState wol = context.CreateInstanceOfEnergyDevicePowerOffType(
                standby, new QualifiedName("Sleep", instances));
            var clock = new DeviceClock();
            var provider = new SimulatedEcmPowerProvider(
                [new EcmEnergySavingMode(1, "Pause", 7000, 2000, 2000, 60000, 3000, 1, 0.1f, 0.2f)],
                ByteString.From(new byte[] { 2, 0, 0, 0, 0, 1 }.AsSpan()),
                timeProvider: clock);
            await using var binding = new EcmPowerBinding(context, standby, provider, wol, provider,
                authorize: static (_, _) => new ValueTask<bool>(true));
            await binding.RefreshAsync().ConfigureAwait(false);
            await Assert.That(standby.StandbyManagementStatus.Value)
                .IsEqualTo((byte)EcmStandbyStatus.ReadyToOperate);
            await Assert.That(wol.WOLMagicPacket.Value.Length).IsEqualTo(6);

            var pause = await standby.StartPause.OnCallAsync!(
                context, standby.StartPause, standby.NodeId, 10000, CancellationToken.None).ConfigureAwait(false);
            await Assert.That(pause.ServiceResult.StatusCode).IsEqualTo(StatusCodes.Good);
            await Assert.That(pause.ReturnCode).IsEqualTo((byte)0);
            await Assert.That(pause.ModeID).IsEqualTo((byte)1);
            await Assert.That(pause.CurrentTimeToDestination).IsEqualTo(2000d);
            await Assert.That(pause.RegularTimeToOperate).IsEqualTo(3000d);
            await Assert.That(pause.TimeMinLengthOfStay).IsEqualTo(2000d);
            await Assert.That(standby.StandbyManagementStatus.Value)
                .IsEqualTo((byte)EcmStandbyStatus.MovingToEnergySavingMode);
            clock.Advance(2000);
            await binding.RefreshAsync().ConfigureAwait(false);
            var sleep = await wol.SwitchOffWOL.OnCallAsync!(
                context, wol.SwitchOffWOL, wol.NodeId, CancellationToken.None).ConfigureAwait(false);
            await Assert.That(sleep.ServiceResult.StatusCode).IsEqualTo(StatusCodes.Good);
            await Assert.That(sleep.ReturnCode).IsEqualTo((byte)0);
            await Assert.That(sleep.ModeID).IsEqualTo((byte)0xFE);
            await Assert.That(sleep.CurrentTimeToDestination).IsEqualTo(1000d);
            await Assert.That(sleep.RegularTimeToOperate).IsEqualTo(2000d);
            await Assert.That(sleep.TimeMinLengthOfStay).IsEqualTo(1000d);
            await Assert.That(standby.StandbyManagementStatus.Value)
                .IsEqualTo((byte)EcmStandbyStatus.MovingToSleepWol);
            clock.Advance(2000);
            await binding.RefreshAsync().ConfigureAwait(false);
            EcmControlResult wake = await provider.SimulateWakeAsync().ConfigureAwait(false);
            await Assert.That(wake.Status).IsEqualTo(StatusCodes.Good);
            await Assert.That(wake.CurrentTimeToOperate).IsEqualTo(2000d);
            await binding.RefreshAsync().ConfigureAwait(false);
            await Assert.That(standby.StandbyManagementStatus.Value)
                .IsEqualTo((byte)EcmStandbyStatus.WakeUpWol);
            clock.Advance(2000);
            await binding.RefreshAsync().ConfigureAwait(false);
            await Assert.That(standby.StandbyManagementStatus.Value)
                .IsEqualTo((byte)EcmStandbyStatus.ReadyToOperate);
            var rejected = await standby.StartPause.OnCallAsync!(
                context, standby.StartPause, standby.NodeId, 1, CancellationToken.None).ConfigureAwait(false);
            await Assert.That(rejected.ServiceResult.StatusCode).IsEqualTo(StatusCodes.Uncertain);
            await Assert.That(rejected.ReturnCode).IsEqualTo((byte)0x50);
            await Assert.That(rejected.ModeID).IsEqualTo((byte)0);
            await Assert.That(rejected.CurrentTimeToDestination).IsEqualTo(0d);
            await Assert.That(rejected.RegularTimeToOperate).IsEqualTo(0d);
            await Assert.That(rejected.TimeMinLengthOfStay).IsEqualTo(0d);
            await Assert.That(standby.StandbyManagementStatus.Value)
                .IsEqualTo((byte)EcmStandbyStatus.ReadyToOperate);
            await binding.DisposeAsync().ConfigureAwait(false);
            await Assert.That(standby.StartPause.OnCallAsync == null).IsTrue();
            await Assert.That(standby.PauseTime.OnSimpleWriteValueAsync == null).IsTrue();
            await Assert.That(wol.SwitchOffWOL.OnCallAsync == null).IsTrue();

            EnergyDevicePowerOffState standalone = context.CreateInstanceOfEnergyDevicePowerOffType(
                null, new QualifiedName("StandaloneSleep", instances));
            await using var standaloneBinding = new EcmWolBinding(context, standalone, provider,
                authorize: static (_, _) => new ValueTask<bool>(true));
            var standaloneCall = await standalone.SwitchOffWOL.OnCallAsync!(
                context, standalone.SwitchOffWOL, standalone.NodeId, CancellationToken.None).ConfigureAwait(false);
            await Assert.That(standaloneCall.ServiceResult.StatusCode).IsEqualTo(StatusCodes.Good);
            await Assert.That(standaloneCall.ModeID).IsEqualTo((byte)0xFE);
            await Assert.That(standaloneCall.ReturnCode).IsEqualTo((byte)0);
            await Assert.That(standalone.Parent).IsNull();

            var services = new ServiceCollection();
            services.AddOpcUa().AddServer(options => options.ApplicationName = "EcmAotServer")
                .AddEcm().ConfigureEcm(static (_, _) => default);
            services.AddOpcUa().AddClient(options => options.ApplicationName = "EcmAotClient").AddEcmClient();
            using ServiceProvider container = services.BuildServiceProvider();
            await Assert.That(container.GetRequiredService<EcmNodeManagerFactory>()).IsNotNull();
            await Assert.That(container.GetRequiredService<EcmClientFactory>()).IsNotNull();
        }

        /// <summary>
        /// Measurement profiles, shared statistics reset, detached localized metadata and rejection of unsupported
        /// formatting arguments remain reachable without reflection-based discovery.
        /// </summary>
        [Test]
        public async Task MeasurementStatisticsAndLocalizedCopyAreReachableAsync()
        {
            ITelemetryContext telemetry = DefaultTelemetry.Create(
                logging => logging.SetMinimumLevel(LogLevel.Warning));
            var message = ServiceMessageContext.Create(telemetry);
            message.NamespaceUris.GetIndexOrAppend(Di.Namespaces.OpcUaDi);
            message.NamespaceUris.GetIndexOrAppend(IA.Namespaces.IA);
            ushort ecm = message.NamespaceUris.GetIndexOrAppend(ECM.Namespaces.ECM);
            ushort instances = message.NamespaceUris.GetIndexOrAppend("urn:ecm-aot:instances");
            message.Factory.Builder.AddOpcUaECM().Commit();
            var context = new SystemContext(telemetry)
            {
                NamespaceUris = message.NamespaceUris,
                EncodeableFactory = message.Factory,
                NodeIdFactory = new DefaultNodeIdFactory(NodeIdAssignmentMode.String, instances),
                TypeTable = CreateTypeTable(message.NamespaceUris)
            };
            var nodes = new NodeStateCollection();
            nodes.AddOpcUaDi(context);
            new EcmModelProvider().AddPredefinedNodes(nodes, context);
            EnergyMeasurementState measurement = context.CreateInstanceOfEnergyMeasurementType(
                null, new QualifiedName("Meter", instances));
            var acquired = new DateTime(2026, 8, 14, 9, 10, 11, DateTimeKind.Utc);
            var restarted = new DateTime(2026, 8, 14, 10, 0, 0, DateTimeKind.Utc);
            var provider = new StatisticsProvider(acquired, restarted);
            await using var binding = new EcmMeasurementBinding(
                context, measurement, provider,
                authorize: static (_, _) => new ValueTask<bool>(true));
            await binding.RefreshAsync().ConfigureAwait(false);

            var export = (BaseVariableState)measurement.FindChild(
                context, new QualifiedName(ECM.BrowseNames.AcActiveEnergyTotalExportLp, ecm))!;
            await Assert.That(export.WrappedValue.TryGetValue(out float exported)).IsTrue();
            await Assert.That(exported).IsEqualTo(100f);
            var children = new List<BaseInstanceState>();
            measurement.GetChildren(context, children);
            MethodState reset = children.OfType<MethodState>().Single(m => m.BrowseName.Name == "ResetStatistics");
            ServiceResult result = await reset.OnCallMethod2Async!(
                context, reset, measurement.NodeId, default, new List<Variant>(), CancellationToken.None)
                .ConfigureAwait(false);
            await Assert.That(ServiceResult.IsGood(result)).IsTrue();
            await Assert.That(provider.ResetCount).IsEqualTo(1);
            await Assert.That(export.WrappedValue.TryGetValue(out float zeroed)).IsTrue();
            await Assert.That(zeroed).IsEqualTo(0f);
            var power = (BaseVariableState)measurement.FindChild(
                context, new QualifiedName(ECM.BrowseNames.AcActivePowerTotal, ecm))!;
            await Assert.That(power.WrappedValue.TryGetValue(out float retainedPower)).IsTrue();
            await Assert.That(retainedPower).IsEqualTo(15.5f);
            await Assert.That(measurement.ApplicationTag!.WrappedValue.TryGetValue(out string? tag)).IsTrue();
            await Assert.That(tag).IsEqualTo("aot/tag");
            await Assert.That(export.StatusCode).IsEqualTo(StatusCodes.Good);
            await binding.DisposeAsync().ConfigureAwait(false);
            await Assert.That(reset.OnCallMethod2Async == null).IsTrue();

            var template = new LocalizedText("key", "en", "Value {0}", 42);
            LocalizedText copy = template.Copy();
            await Assert.That(copy.Text).IsEqualTo(template.Text);
            var rejected = new LocalizedText("key", "en", "Value {0}", new object());
            ServiceResultException copyError = await Assert.That(() => rejected.Copy())
                .Throws<ServiceResultException>() ??
                throw new InvalidOperationException("The exception assertion returned no exception.");
            await Assert.That(copyError.StatusCode).IsEqualTo(StatusCodes.BadNotSupported);
        }

        /// <summary>
        /// An E0 AcPe companion on a statistics point is detached and retained through the shared reset.
        /// </summary>
        [Test]
        public async Task E0CompanionIsDetachedAndRetainedThroughResetAsync()
        {
            ITelemetryContext telemetry = DefaultTelemetry.Create(
                logging => logging.SetMinimumLevel(LogLevel.Warning));
            var message = ServiceMessageContext.Create(telemetry);
            message.NamespaceUris.GetIndexOrAppend(Di.Namespaces.OpcUaDi);
            message.NamespaceUris.GetIndexOrAppend(IA.Namespaces.IA);
            ushort ecm = message.NamespaceUris.GetIndexOrAppend(ECM.Namespaces.ECM);
            ushort instances = message.NamespaceUris.GetIndexOrAppend("urn:ecm-aot:instances");
            message.Factory.Builder.AddOpcUaECM().Commit();
            var context = new SystemContext(telemetry)
            {
                NamespaceUris = message.NamespaceUris,
                EncodeableFactory = message.Factory,
                NodeIdFactory = new DefaultNodeIdFactory(NodeIdAssignmentMode.String, instances),
                TypeTable = CreateTypeTable(message.NamespaceUris)
            };
            var nodes = new NodeStateCollection();
            nodes.AddOpcUaDi(context);
            new EcmModelProvider().AddPredefinedNodes(nodes, context);
            EnergyMeasurementState measurement = context.CreateInstanceOfEnergyMeasurementType(
                null, new QualifiedName("Meter", instances));
            var acquired = new DateTime(2026, 8, 14, 9, 10, 11, DateTimeKind.Utc);
            var restarted = new DateTime(2026, 8, 14, 10, 0, 0, DateTimeKind.Utc);
            var phases = new AcPeDataType { L1 = 1f, L2 = 2f, L3 = 3f };
            var provider = new StatisticsProvider(acquired, restarted) { Current = phases };
            await using var binding = new EcmMeasurementBinding(
                context, measurement, provider,
                authorize: static (_, _) => new ValueTask<bool>(true));
            await binding.RefreshAsync().ConfigureAwait(false);
            phases.L1 = 99f;

            var current = (BaseVariableState)measurement.FindChild(
                context, new QualifiedName(ECM.BrowseNames.AcCurrentPe, ecm))!;
            await Assert.That(current.WrappedValue.TryGetStructure(out AcPeDataType? published)).IsTrue();
            await Assert.That(published!.L1).IsEqualTo(1f);
            var children = new List<BaseInstanceState>();
            measurement.GetChildren(context, children);
            MethodState reset = children.OfType<MethodState>().Single(m => m.BrowseName.Name == "ResetStatistics");
            ServiceResult result = await reset.OnCallMethod2Async!(
                context, reset, measurement.NodeId, default, new List<Variant>(), CancellationToken.None)
                .ConfigureAwait(false);

            await Assert.That(ServiceResult.IsGood(result)).IsTrue();
            await Assert.That(current.WrappedValue.TryGetStructure(out AcPeDataType? retained)).IsTrue();
            await Assert.That(retained!.L1).IsEqualTo(1f);
            await Assert.That(current.StatusCode).IsEqualTo(StatusCodes.Good);

            var hostile = new HostileAcPe { L1 = 5f };
            provider.Current = hostile;
            hostile.Arm();
            InvalidOperationException? failure = null;
            try
            {
                await binding.RefreshAsync().ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                failure = ex;
            }

            await Assert.That(failure).IsNotNull();
            await Assert.That(hostile.Touched).IsEqualTo(0);
            await Assert.That(hostile.TypeIdReads).IsEqualTo(0);
            await Assert.That(current.WrappedValue.TryGetStructure(out AcPeDataType? kept)).IsTrue();
            await Assert.That(kept!.L1).IsEqualTo(1f);
            await Assert.That(current.StatusCode).IsEqualTo(StatusCodes.BadNoCommunication);
        }

        /// <summary>
        /// Creates the ECM data type hierarchy needed to validate generated structure values.
        /// </summary>
        private static TypeTable CreateTypeTable(NamespaceTable namespaces)
        {
            var typeTable = new TypeTable(namespaces);
            typeTable.AddSubtype(
                ExpandedNodeId.ToNodeId(Opc.Ua.ECM.DataTypeIds.AcPeDataType, namespaces),
                Opc.Ua.DataTypeIds.Structure);
            typeTable.AddSubtype(
                ExpandedNodeId.ToNodeId(Opc.Ua.ECM.DataTypeIds.AcPpDataType, namespaces),
                Opc.Ua.DataTypeIds.Structure);
            return typeTable;
        }

        /// <summary>
        /// An AcPe subclass that counts the virtual members the validation path must not invoke.
        /// </summary>
        private sealed class HostileAcPe : AcPeDataType
        {
            private bool m_armed;

            public int Touched { get; private set; }

            public int TypeIdReads { get; private set; }

            public override ExpandedNodeId TypeId
            {
                get
                {
                    if (m_armed)
                    {
                        TypeIdReads++;
                    }
                    return base.TypeId;
                }
            }

            public void Arm()
            {
                m_armed = true;
            }

            public override string ToString()
            {
                Record();
                return "hostile";
            }

            public override bool Equals(object? obj)
            {
                Record();
                return ReferenceEquals(this, obj);
            }

            public override int GetHashCode()
            {
                Record();
                return 0;
            }

            public override void Encode(IEncoder encoder)
            {
                Record();
                throw new InvalidOperationException("Encode must not be invoked.");
            }

            public override object Clone()
            {
                Record();
                throw new InvalidOperationException("Clone must not be invoked.");
            }

            public override bool IsEqual(IEncodeable? encodeable)
            {
                Record();
                throw new InvalidOperationException("IsEqual must not be invoked.");
            }

            private void Record()
            {
                if (m_armed)
                {
                    Touched++;
                }
            }
        }

        /// <summary>
        /// Supplies E2 readings and a reset that zeroes the two energy statistics.
        /// </summary>
        private sealed class StatisticsProvider(DateTime acquired, DateTime restarted) : IEcmStatisticsProvider
        {
            public AcPeDataType? Current { get; set; }

            public int ResetCount { get; private set; }

            public ValueTask<EcmMeasurementSnapshot> ReadAsync(CancellationToken cancellationToken)
            {
                var readings = new List<EcmMeasurementReading>
                {
                    Float(ECM.BrowseNames.AcActivePowerTotal, 15.5f),
                    Float(ECM.BrowseNames.AcActiveEnergyTotalExportLp, ResetCount == 0 ? 100f : 0f),
                    Float(ECM.BrowseNames.AcActiveEnergyTotalImportLp, ResetCount == 0 ? 200f : 0f)
                };
                if (Current != null)
                {
                    readings.Add(new EcmMeasurementReading(
                        ECM.BrowseNames.AcCurrentPe,
                        new Variant(new ExtensionObject(ECM.DataTypeIds.AcPeDataType, Current)),
                        ECM.DataTypeIds.AcPeDataType,
                        StatusCodes.Good,
                        acquired));
                }
                return new ValueTask<EcmMeasurementSnapshot>(new EcmMeasurementSnapshot(
                    "aot/tag",
                    readings.ToArray().ToArrayOf())
                {
                    StatisticsStartTime = (DateTimeUtc)(ResetCount == 0 ? acquired : restarted)
                });
            }

            public ValueTask WriteApplicationTagAsync(string applicationTag, CancellationToken cancellationToken)
            {
                return default;
            }

            public ValueTask<ServiceResult> AuthorizeStatisticsResetAsync(
                ISystemContext requestContext,
                CancellationToken cancellationToken)
            {
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            }

            public ValueTask<EcmStatisticsSnapshot> ResetStatisticsAsync(CancellationToken cancellationToken)
            {
                ResetCount++;
                return new ValueTask<EcmStatisticsSnapshot>(new EcmStatisticsSnapshot(
                    (DateTimeUtc)restarted,
                    new[]
                    {
                        Float(ECM.BrowseNames.AcActiveEnergyTotalExportLp, 0f),
                        Float(ECM.BrowseNames.AcActiveEnergyTotalImportLp, 0f)
                    }.ToArrayOf()));
            }

            private EcmMeasurementReading Float(string name, float value)
            {
                return new EcmMeasurementReading(
                    name,
                    Variant.From(value),
                    Opc.Ua.DataTypeIds.Float,
                    StatusCodes.Good,
                    acquired);
            }
        }
        /// <summary>
        /// Advances simulated transitions without wall-clock timing.
        /// </summary>
        private sealed class DeviceClock : TimeProvider
        {
            /// <inheritdoc/>
            public override DateTimeOffset GetUtcNow()
            {
                return m_now;
            }

            /// <summary>
            /// Advances the simulated acquisition time in milliseconds.
            /// </summary>
            public void Advance(double milliseconds)
            {
                m_now = m_now.AddMilliseconds(milliseconds);
            }

            /// <summary>The controlled device time.</summary>
            private DateTimeOffset m_now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        }
    }
}
