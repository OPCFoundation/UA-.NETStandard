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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Machinery.ProcessValues;
using Opc.Ua.MachineTool.Server;
using Opc.Ua.MachineTool.WMTP;
using Opc.Ua.Tests;

namespace Opc.Ua.MachineTool.Tests
{
    /// <summary>
    /// Tests mandatory setup, measurement semantics, provider acceptance and the runtime lifetime.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class WmtpRuntimeTests
    {
        /// <summary>
        /// Starts the real WMTP namespace owner once; each test creates and disposes independent peripherals.
        /// </summary>
        [OneTimeSetUp]
        public async Task StartAsync()
        {
            await m_fixture.StartAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Drains all namespace-owned resources and removes the isolated PKI.
        /// </summary>
        [OneTimeTearDown]
        public async Task StopAsync()
        {
            await m_fixture.DisposeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// WMTP-only mandatory defaults preserve the independent root, nested identification and discovery reference.
        /// </summary>
        [Test]
        public async Task MandatoryPeripheralHasNoMachineToolParentOrImplicitOptionalFeaturesAsync()
        {
            var equipment = new WmtpTestEquipment();
            await using WmtpPeripheral peripheral = await equipment.Builder(m_fixture.Context).BuildAsync()
                .ConfigureAwait(false);
            Assert.That(peripheral.State.TypeDefinitionId, Is.EqualTo(NodeId.Create(
                1003, WMTP.Namespaces.WMTP, m_fixture.Context.Context.NamespaceUris)));
            Assert.That(peripheral.State.Parent, Is.SameAs(m_fixture.Context.MachinesFolder));
            Assert.That(peripheral.State.DeviceInformation, Is.Not.Null);
            Assert.That(peripheral.State.Measurements, Is.Not.Null);
            Assert.That(peripheral.State.MachineryBuildingBlocks, Is.Not.Null);
            Assert.That(peripheral.Nodes.Identification.Parent, Is.SameAs(peripheral.State.DeviceInformation));
            Assert.That(peripheral.Nodes.Identification.SerialNumber!.Value, Is.EqualTo("Peripheral"));
            Assert.That(peripheral.Nodes.Identification.Manufacturer!.Value.Text, Is.EqualTo("WMTP test equipment"));
            Assert.That(peripheral.State.MachineryBuildingBlocks!.ReferenceExists(
                Opc.Ua.ReferenceTypeIds.HasAddIn, false, peripheral.Nodes.Identification.NodeId), Is.True);
            Assert.That(peripheral.State.DeviceConfiguration, Is.Null);
            Assert.That(peripheral.State.WMTPWorkCycleData, Is.Null);
            Assert.That(peripheral.State.WMTPServiceCycleData, Is.Null);
            Assert.That(peripheral.State.LifetimeCounters, Is.Null);
            Assert.That(peripheral.Measurements.IsEmpty, Is.True);
            Assert.That(m_fixture.Context.Context.NamespaceUris.GetIndex(Namespaces.MachineTool), Is.EqualTo(-1));
            await peripheral.RefreshQualityAsync().ConfigureAwait(false);
            Assert.That(peripheral.QualityRefreshStatus, Is.EqualTo(StatusCodes.Good));
        }

        /// <summary>
        /// All released physical kinds retain exact enums, units, range, precision, uncertainty and sample metadata.
        /// </summary>
        [TestCase(WmtpMeasurementKind.Temperature)]
        [TestCase(WmtpMeasurementKind.RotationSpeed)]
        [TestCase(WmtpMeasurementKind.Humidity)]
        [TestCase(WmtpMeasurementKind.Acceleration)]
        [TestCase(WmtpMeasurementKind.Force)]
        [TestCase(WmtpMeasurementKind.Torque)]
        [TestCase(WmtpMeasurementKind.Position)]
        [TestCase(WmtpMeasurementKind.Dimension)]
        public async Task EveryMeasurementKindPreservesExactMetadataAndProcessValueIdentityAsync(
            WmtpMeasurementKind kind)
        {
            var equipment = new WmtpTestEquipment();
            WmtpMeasurementDefinition definition = WmtpTestEquipment.Definition(kind, WmtpMeasurementFeatures.All);
            equipment.Samples[definition.Name] = equipment.Sample(Variant.From(82.5));
            await using WmtpPeripheral peripheral = await equipment.Builder(m_fixture.Context)
                .AddMeasurement(definition).BuildAsync().ConfigureAwait(false);
            WmtpMeasurement sensor = peripheral.Measurements[0];
            var signal = sensor.State.AnalogSignal as ProcessValueVariableState;
            Assert.That(signal, Is.Not.Null);
            Assert.That(sensor.State.TypeDefinitionId, Is.EqualTo(NodeId.Create(
                1005, WMTP.Namespaces.WMTP, m_fixture.Context.Context.NamespaceUris)));
            Assert.That(sensor.State.TypeOfMeasurement!.Value, Is.EqualTo((uint)kind));
            Assert.That(sensor.State.TypeOfSample!.Value, Is.EqualTo(1));
            Assert.That(signal!.BrowseName.NamespaceIndex, Is.EqualTo(
                m_fixture.Context.Context.NamespaceUris.GetIndex(Opc.Ua.PADIM.Namespaces.PADIM)));
            Assert.That(signal.EngineeringUnits!.Value!.UnitId, Is.EqualTo(definition.EngineeringUnits.UnitId));
            Assert.That(signal.EURange!.Value!.High, Is.EqualTo(100));
            Assert.That(signal.InstrumentRange!.Value!.Low, Is.EqualTo(-100));
            Assert.That(signal.ValuePrecision!.Value, Is.EqualTo(0.125));
            Assert.That(sensor.State.RelativeUncertainty!.Value, Is.EqualTo(0.5));
            Assert.That(sensor.State.AbsoluteUncertainty!.Value, Is.EqualTo(0.25));
            Assert.That(sensor.State.InternalUpdateInterval!.Value, Is.EqualTo(25));
            Assert.That(sensor.State.Index!.Value, Is.EqualTo(17));
            Assert.That(sensor.State.MeasurementPeriod!.Value, Is.EqualTo(50));
            Assert.That(sensor.State.Timestamp!.Value,
                Is.EqualTo(equipment.Samples[definition.Name].Value.SourceTimestamp));
            Assert.That(sensor.State.LimitAlarm, Is.Not.Null);
            Assert.That(sensor.State.Status!.Value, Is.EqualTo(9));
            Assert.That(peripheral.State.MachineryBuildingBlocks!.ReferenceExists(
                Opc.Ua.ReferenceTypeIds.HasAddIn, false, sensor.State.LimitCounters!.NodeId), Is.True);
            Assert.That(sensor.Sample.WrappedValue.TryGetValue(out double actual), Is.True);
            Assert.That(actual, Is.EqualTo(82.5));
        }

        /// <summary>
        /// All optional device features publish only actual supplied data and independent method-backed datasets.
        /// </summary>
        [Test]
        public async Task FullOptionalDeviceFeaturesPublishActualMetadataAndGeneratedMethodsAsync()
        {
            var equipment = new WmtpTestEquipment();
            equipment.FullSnapshot();
            await using var work = new InMemoryWmtpRecordStore(NUnitTelemetryContext.Create());
            await using var service = new InMemoryWmtpRecordStore(NUnitTelemetryContext.Create());
            await using WmtpPeripheral peripheral = await equipment.Builder(m_fixture.Context)
                .WithFeatures(WmtpFeatures.All).WithWorkRecords(work, WmtpRecordFeatures.All)
                .WithServiceRecords(service, WmtpRecordFeatures.All).BuildAsync().ConfigureAwait(false);
            Assert.That(peripheral.Nodes.CalibrationMode!.Value, Is.False);
            Assert.That(peripheral.Nodes.Battery!.AnalogSignal!.WrappedValue.TryGetValue(out double battery), Is.True);
            Assert.That(battery, Is.EqualTo(75));
            Assert.That(peripheral.Nodes.LicenseInformation!.Value, Is.EqualTo("test authorization description"));
            Assert.That(peripheral.Nodes.LicenseValidity!.StartValue!.WrappedValue.TryGetValue(out double start),
                Is.True);
            Assert.That(start, Is.EqualTo(100));
            Assert.That(peripheral.Nodes.RemainingCycles!.WrappedValue.TryGetValue(out double remaining), Is.True);
            Assert.That(remaining, Is.EqualTo(80));
            Assert.That(peripheral.Nodes.NextService!.EngineeringUnits!.Value!.UnitId, Is.EqualTo(4740434));
            Assert.That(peripheral.Nodes.LifeCycleStatus!.Value, Is.EqualTo(1));
            Assert.That(peripheral.Nodes.LastService!.Value, Is.EqualTo(equipment.Snapshot.LastService));
            Assert.That(peripheral.Nodes.NextServiceDate!.Value, Is.EqualTo(equipment.Snapshot.NextServiceDate));
            Assert.That(peripheral.State.OperationCounters!.OperationDuration!.Value, Is.EqualTo(72));
            Assert.That(peripheral.Nodes.OperationSinceService!.Value, Is.EqualTo(5000));
            Assert.That(peripheral.State.WMTPWorkCycleData!.CombinedReportIndex!.OnCallAsync, Is.Not.Null);
            Assert.That(peripheral.State.WMTPServiceCycleData!.DeleteStoredRecordsTime!.OnCallAsync, Is.Not.Null);
            Assert.That(peripheral.Nodes.CalibrationMode.AccessLevel, Is.EqualTo(AccessLevels.CurrentRead));
            await peripheral.RefreshAsync().ConfigureAwait(false);
            Assert.That(peripheral.Nodes.LicenseValidity.StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        /// <summary>
        /// Raw integer measurements, both timestamps and unavailable/stale quality are never coerced to good-zero.
        /// </summary>
        [Test]
        public async Task RawQualityAcquisitionTimeAndStalenessArePreservedWithoutFictitiousSamplesAsync()
        {
            var equipment = new WmtpTestEquipment();
            WmtpMeasurementDefinition definition = WmtpTestEquipment.Definition() with
            {
                DataType = BuiltInType.UInt16, Delta = Variant.From((ushort)2), StaleAfter = TimeSpan.FromSeconds(5)
            };
            equipment.Samples[definition.Name] = equipment.Sample(Variant.From((ushort)42));
            DateTimeUtc acquired = equipment.Samples[definition.Name].Value.SourceTimestamp;
            equipment.Clock.Advance(TimeSpan.FromSeconds(1));
            await using WmtpPeripheral peripheral = await equipment.Builder(m_fixture.Context)
                .AddMeasurement(definition).BuildAsync().ConfigureAwait(false);
            WmtpMeasurement sensor = peripheral.Measurements[0];
            Assert.That(sensor.Sample.WrappedValue.TryGetValue(out ushort value), Is.True);
            Assert.That(value, Is.EqualTo(42));
            Assert.That(sensor.Sample.SourceTimestamp, Is.EqualTo(acquired));
            Assert.That(sensor.ReceivedAt, Is.EqualTo(acquired + TimeSpan.FromSeconds(1)));
            equipment.Clock.Advance(TimeSpan.FromSeconds(5));
            await peripheral.RefreshQualityAsync().ConfigureAwait(false);
            Assert.That(sensor.Sample.StatusCode, Is.EqualTo(StatusCodes.UncertainLastUsableValue));
            Assert.That(sensor.Sample.SourceTimestamp, Is.EqualTo(acquired));
            Assert.That(sensor.Acquisition!.Value.StatusCode, Is.EqualTo(StatusCodes.Good));
            equipment.Samples[definition.Name] = new WmtpMeasurementSample
            {
                Value = new DataValue(Variant.Null, StatusCodes.BadNoCommunication, equipment.Clock.GetUtcNow())
            };
            await peripheral.RefreshAsync().ConfigureAwait(false);
            Assert.That(sensor.Sample.WrappedValue.IsNull, Is.True);
            Assert.That(sensor.Sample.StatusCode, Is.EqualTo(StatusCodes.BadNoCommunication));
        }

        /// <summary>
        /// Limit duration counters use monotonic time, include high-high in high, and stop at stale/unknown data.
        /// </summary>
        [Test]
        public async Task LimitCountersAccrueKnownStatusDurationsAndKeepDeclaredReferencesAsync()
        {
            var equipment = new WmtpTestEquipment();
            WmtpMeasurementDefinition definition = WmtpTestEquipment.Definition(
                features: WmtpMeasurementFeatures.LimitCounters) with { StaleAfter = TimeSpan.FromSeconds(10) };
            equipment.Samples[definition.Name] = equipment.Sample(Variant.From(85d));
            await using WmtpPeripheral peripheral = await equipment.Builder(m_fixture.Context)
                .AddMeasurement(definition).BuildAsync().ConfigureAwait(false);
            WmtpMeasurement sensor = peripheral.Measurements[0];
            ISystemContext context = m_fixture.Context.Context;
            var high = (PropertyState<double>)sensor.State.Status!.FindChild(context,
                new QualifiedName("OperationAboveHigh", context.NamespaceUris.GetIndexOrAppend(WMTP.Namespaces.WMTP)))!;
            var highHigh = (PropertyState<double>)sensor.State.Status.FindChild(context,
                new QualifiedName("OperationAboveHighHigh",
                    context.NamespaceUris.GetIndexOrAppend(WMTP.Namespaces.WMTP)))!;
            Assert.That(sensor.State.LimitCounters!.ReferenceExists(
                Opc.Ua.ReferenceTypeIds.HasProperty, false, high.NodeId), Is.True);
            Assert.That(high.ReferenceExists(
                Opc.Ua.ReferenceTypeIds.HasProperty, true, sensor.State.LimitCounters.NodeId), Is.True);
            equipment.Clock.Advance(TimeSpan.FromSeconds(2));
            equipment.Samples[definition.Name] = equipment.Sample(Variant.From(95d));
            await peripheral.RefreshAsync().ConfigureAwait(false);
            equipment.Clock.Advance(TimeSpan.FromSeconds(3));
            await peripheral.RefreshQualityAsync().ConfigureAwait(false);
            Assert.That(high.Value, Is.EqualTo(5000));
            Assert.That(highHigh.Value, Is.EqualTo(3000));
            equipment.Clock.Advance(TimeSpan.FromSeconds(20));
            await peripheral.RefreshQualityAsync().ConfigureAwait(false);
            Assert.That(high.Value, Is.EqualTo(12000));
            Assert.That(highHigh.Value, Is.EqualTo(10000));
            Assert.That(sensor.State.Status.Value, Is.EqualTo(1));
            equipment.Clock.Advance(TimeSpan.FromSeconds(2));
            await peripheral.RefreshQualityAsync().ConfigureAwait(false);
            Assert.That(high.Value, Is.EqualTo(12000));
            equipment.Samples[definition.Name] = equipment.Sample(Variant.From(90d));
            await peripheral.RefreshAsync().ConfigureAwait(false);
            Assert.That(sensor.State.Status.Value, Is.EqualTo(9));
        }

        /// <summary>
        /// Invalid Duration inputs are refused before equipment callbacks and leave both settings unchanged.
        /// </summary>
        [TestCase(-1d)]
        [TestCase(0.5d)]
        [TestCase(4294967296d)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public async Task TriggerDurationRejectsUnrepresentableInputsWithoutProviderMutationAsync(double duration)
        {
            var equipment = new WmtpTestEquipment();
            WmtpMeasurementDefinition definition = WmtpTestEquipment.Definition();
            equipment.Samples[definition.Name] = equipment.Sample(Variant.From(1d));
            await using WmtpPeripheral peripheral = await equipment.Builder(m_fixture.Context)
                .AddMeasurement(definition).BuildAsync().ConfigureAwait(false);
            ServiceResultException error = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await peripheral.Measurements[0].SetTriggerAsync(duration, new WmtpRequest()).ConfigureAwait(false)))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
            Assert.That(peripheral.Measurements[0].TriggerMilliseconds, Is.EqualTo(1000));
            equipment.Measurement.Verify(provider => provider.ConfigureAsync(
                It.IsAny<string>(), It.IsAny<uint>(), It.IsAny<Variant>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Both-zero and numeric-type violations are atomic; successful UInt32 boundaries remain exact.
        /// </summary>
        [Test]
        public async Task TriggerAndDeltaInvariantAndProviderRefusalAreAtomicAsync()
        {
            var equipment = new WmtpTestEquipment();
            WmtpMeasurementDefinition definition = WmtpTestEquipment.Definition();
            equipment.Samples[definition.Name] = equipment.Sample(Variant.From(12d));
            await using WmtpPeripheral peripheral = await equipment.Builder(m_fixture.Context)
                .AddMeasurement(definition).BuildAsync().ConfigureAwait(false);
            WmtpMeasurement sensor = peripheral.Measurements[0];
            var request = new WmtpRequest();
            await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await sensor.SetTriggerAsync(0, request).ConfigureAwait(false));
            await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await sensor.SetDeltaAsync(Variant.From(1u), request).ConfigureAwait(false));
            await sensor.SetDeltaAsync(Variant.From(2d), request).ConfigureAwait(false);
            await sensor.SetTriggerAsync(0, request).ConfigureAwait(false);
            await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await sensor.SetDeltaAsync(Variant.From(0d), request).ConfigureAwait(false));
            await sensor.SetTriggerAsync(uint.MaxValue, request).ConfigureAwait(false);
            equipment.Measurement.Setup(provider => provider.ConfigureAsync(
                    It.IsAny<string>(), It.IsAny<uint>(), It.IsAny<Variant>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<StatusCode>(StatusCodes.BadNotWritable));
            ServiceResultException refused = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await sensor.SetDeltaAsync(Variant.From(3d), request).ConfigureAwait(false)))!;
            Assert.That(refused.StatusCode, Is.EqualTo(StatusCodes.BadNotWritable));
            Assert.That(sensor.TriggerMilliseconds, Is.EqualTo(uint.MaxValue));
            Assert.That(sensor.Delta.TryGetValue(out double delta), Is.True);
            Assert.That(delta, Is.EqualTo(2d));
        }

        /// <summary>
        /// UInt64 Number input is preserved exactly and only exactly representable published deltas are accepted.
        /// </summary>
        [Test]
        public async Task UInt64DeltaRejectsPrecisionLossAndRetainsExactProviderArgumentAsync()
        {
            var equipment = new WmtpTestEquipment();
            WmtpMeasurementDefinition definition = WmtpTestEquipment.Definition() with
            {
                DataType = BuiltInType.UInt64, Delta = Variant.From(0ul)
            };
            equipment.Samples[definition.Name] = equipment.Sample(Variant.From(7ul));
            await using WmtpPeripheral peripheral = await equipment.Builder(m_fixture.Context)
                .AddMeasurement(definition).BuildAsync().ConfigureAwait(false);
            WmtpMeasurement sensor = peripheral.Measurements[0];
            foreach (ulong invalid in new[] { 9007199254740993ul, ulong.MaxValue })
            {
                await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await sensor.SetDeltaAsync(Variant.From(invalid), new WmtpRequest()).ConfigureAwait(false));
            }
            await sensor.SetDeltaAsync(Variant.From(9007199254740992ul), new WmtpRequest()).ConfigureAwait(false);
            Assert.That(sensor.Delta.TryGetValue(out ulong exact), Is.True);
            Assert.That(exact, Is.EqualTo(9007199254740992ul));
            equipment.Measurement.Verify(provider => provider.ConfigureAsync(definition.Name, 1000,
                Variant.From(9007199254740992ul), It.IsAny<CancellationToken>()), Times.Once);
            Assert.That(sensor.State.DeltaCondition!.Value, Is.EqualTo(9007199254740992d));
        }

        /// <summary>
        /// Calibration/time/transfer workflows honor authorization, bounds and explicit equipment acceptance.
        /// </summary>
        [Test]
        public async Task ConfigurationAndTransfersRefuseInvalidOrUnacceptedChangesAsync()
        {
            var equipment = new WmtpTestEquipment();
            equipment.FullSnapshot();
            await using WmtpPeripheral peripheral = await equipment.Builder(m_fixture.Context)
                .WithFeatures(WmtpFeatures.Calibration | WmtpFeatures.DeviceTime | WmtpFeatures.Firmware |
                    WmtpFeatures.License).WithByteLimits(3, 256).BuildAsync().ConfigureAwait(false);
            var request = new WmtpRequest();
            await peripheral.SetCalibrationAsync(1, request).ConfigureAwait(false);
            Assert.That(peripheral.Nodes.CalibrationMode!.Value, Is.True);
            await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await peripheral.SetCalibrationAsync(2, request).ConfigureAwait(false));
            var zone = new TimeZoneDataType { Offset = 120, DaylightSavingInOffset = true };
            DateTimeUtc time = equipment.Clock.GetUtcNow();
            await peripheral.SetDeviceTimeAsync(time, zone, request).ConfigureAwait(false);
            equipment.DeviceTime.Verify(provider => provider.SetTimeAsync(time,
                It.Is<TimeZoneDataType>(actual => actual.Offset == 120 && actual.DaylightSavingInOffset),
                It.IsAny<CancellationToken>()), Times.Once);
            await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await peripheral.SetDeviceTimeAsync(time, new TimeZoneDataType { Offset = 841 }, request)
                    .ConfigureAwait(false));
            ServiceResultException refused = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await peripheral.TransferAsync(WmtpTransferKind.Firmware, ByteString.From(1, 2, 3), request)
                    .ConfigureAwait(false)))!;
            Assert.That(refused.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            ServiceResultException oversized = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await peripheral.TransferAsync(WmtpTransferKind.Firmware, ByteString.From(1, 2, 3, 4), request)
                    .ConfigureAwait(false)))!;
            Assert.That(oversized.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            ByteString captured = default;
            equipment.Transfers.Setup(provider => provider.TransferAsync(
                    It.IsAny<WmtpTransferKind>(), It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                .Callback((WmtpTransferKind _, ByteString bytes, CancellationToken _) => captured = bytes)
                .Returns(new ValueTask<StatusCode>(StatusCodes.Good));
            await peripheral.TransferAsync(WmtpTransferKind.License, ByteString.From(1, 2, 3), request)
                .ConfigureAwait(false);
            Assert.That(captured.ToArray(), Is.EqualTo(new byte[] { 0, 0, 0 }));
            Assert.That(peripheral.Nodes.LicenseInformation!.Value, Is.EqualTo("test authorization description"));
            Assert.That(peripheral.Nodes.CalibrationMode.Value, Is.True);
            equipment.Authorization.Setup(provider => provider.AuthorizeAsync(
                    It.IsAny<WmtpRequest>(), WmtpOperation.Calibrate, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<StatusCode>(StatusCodes.BadUserAccessDenied));
            await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await peripheral.SetCalibrationAsync(0, request).ConfigureAwait(false));
            Assert.That(peripheral.Nodes.CalibrationMode.Value, Is.True);
        }

        /// <summary>
        /// Missing selected providers fail before reading/staging, and failed nameplates release the reserved name.
        /// </summary>
        [Test]
        public async Task MissingProvidersAndInvalidIdentityFailBeforePublicationAndCanRetryAsync()
        {
            var equipment = new WmtpTestEquipment();
            WmtpPeripheralBuilder missing = m_fixture.Context.AddWirelessPeripheral("Retry")
                .WithProvider(equipment.Device.Object).WithFeatures(WmtpFeatures.Firmware)
                .WithIdentification(identity => identity.Manufacturer = new LocalizedText("Missing"));
            ServiceResultException required = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await missing.BuildAsync().ConfigureAwait(false)))!;
            Assert.That(required.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            equipment.Device.Verify(provider => provider.ReadAsync(It.IsAny<CancellationToken>()), Times.Never);
            WmtpPeripheralBuilder invalid = equipment.Builder(m_fixture.Context, "Retry")
                .WithIdentification(identity => identity.Manufacturer = new LocalizedText("Missing serial"));
            await Assert.ThrowsAsync<ServiceResultException>(async () => await invalid.BuildAsync().ConfigureAwait(false));
            await using WmtpPeripheral retried = await invalid.WithIdentification(identity =>
            {
                identity.Manufacturer = new LocalizedText("Retry");
                identity.SerialNumber = "Valid";
            }).BuildAsync().ConfigureAwait(false);
            Assert.That(retried.Nodes.Identification.SerialNumber!.Value, Is.EqualTo("Valid"));
            Assert.That(() => invalid.WithFeatures(WmtpFeatures.All), Throws.TypeOf<ServiceResultException>());
        }

        /// <summary>
        /// The owned injectable timer degrades stored quality without inventing a new sensor acquisition.
        /// </summary>
        [Test]
        public async Task FreshnessTimerChangesQualityWithoutReadingEquipmentAgainAsync()
        {
            var equipment = new WmtpTestEquipment();
            equipment.Samples["Temperature"] = equipment.Sample(Variant.From(17d));
            WmtpPeripheral peripheral = await equipment.Builder(m_fixture.Context)
                .AddMeasurement(WmtpTestEquipment.Definition() with { StaleAfter = TimeSpan.FromSeconds(1) })
                .BuildAsync().ConfigureAwait(false);
            await using (peripheral.ConfigureAwait(false))
            {
                DateTimeUtc acquired = peripheral.Measurements[0].Sample.SourceTimestamp;
                equipment.Clock.Advance(TimeSpan.FromSeconds(2));
                equipment.Clock.Pulse();
                Assert.That(peripheral.Measurements[0].State.AnalogSignal!.StatusCode,
                    Is.EqualTo(StatusCodes.UncertainLastUsableValue));
                Assert.That(peripheral.Measurements[0].State.AnalogSignal!.WrappedValue, Is.EqualTo(Variant.From(17d)));
                Assert.That(peripheral.Measurements[0].Sample.SourceTimestamp, Is.EqualTo(acquired));
                Assert.That(peripheral.QualityRefreshStatus, Is.EqualTo(StatusCodes.Good));
                equipment.Measurement.Verify(provider => provider.ReadAsync(
                    "Temperature", It.IsAny<CancellationToken>()), Times.Once);
            }
            equipment.Clock.Advance(TimeSpan.FromSeconds(1));
            equipment.Clock.Pulse();
            Assert.That(m_fixture.Context.Manager.FindPredefinedNode(peripheral.NodeId), Is.Null);
        }

        /// <summary>
        /// A caller arriving during a freshness tick waits for it instead of seeing BadServerTooBusy.
        /// </summary>
        [Test]
        public async Task CallerArrivingDuringFreshnessTickWaitsInsteadOfBusyAsync()
        {
            var equipment = new WmtpTestEquipment();
            equipment.Samples["Temperature"] = equipment.Sample(Variant.From(17d));
            WmtpPeripheral peripheral = await equipment.Builder(m_fixture.Context)
                .AddMeasurement(WmtpTestEquipment.Definition() with { StaleAfter = TimeSpan.FromSeconds(1) })
                .BuildAsync().ConfigureAwait(false);
            await using (peripheral.ConfigureAwait(false))
            {
                var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                peripheral.Measurements[0].State.AnalogSignal!.StateChangedAsync += async (_, _, _, _) =>
                {
                    entered.TrySetResult(true);
                    await release.Task.ConfigureAwait(false);
                };
                equipment.Clock.Advance(TimeSpan.FromSeconds(2));
                // A synchronous state-changed raise blocks the pulsing thread until release.
                Task tick = Task.Run(equipment.Clock.Pulse);
                try
                {
                    await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                    Task configure = peripheral.Measurements[0].SetDeltaAsync(
                        Variant.From(2d), new WmtpRequest { SessionId = new NodeId("caller", 1) }).AsTask();
                    Assert.That(configure.IsCompleted, Is.False, configure.Exception?.ToString());
                    release.TrySetResult(true);
                    await configure.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                }
                finally
                {
                    release.TrySetResult(true);
                    await tick.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                }
                Assert.That(peripheral.Measurements[0].State.DeltaCondition!.Value, Is.EqualTo(2d));
            }
        }

        /// <summary>
        /// A call that re-enters a ticking peripheral through another peripheral's callback is refused
        /// instead of waiting for the tick that encloses it (A tick, then B authorization, then A).
        /// </summary>
        [Test]
        public async Task IndirectReentryIntoTickingPeripheralIsRefusedInsteadOfDeadlockingAsync()
        {
            var first = new WmtpTestEquipment();
            first.Samples["Temperature"] = first.Sample(Variant.From(17d));
            var second = new WmtpTestEquipment();
            second.Samples["Temperature"] = second.Sample(Variant.From(18d));
            WmtpPeripheral a = await first.Builder(m_fixture.Context, "ReentryA")
                .AddMeasurement(WmtpTestEquipment.Definition() with { StaleAfter = TimeSpan.FromSeconds(1) })
                .BuildAsync().ConfigureAwait(false);
            await using (a.ConfigureAwait(false))
            {
                WmtpPeripheral b = await second.Builder(m_fixture.Context, "ReentryB")
                    .AddMeasurement(WmtpTestEquipment.Definition()).BuildAsync().ConfigureAwait(false);
                await using (b.ConfigureAwait(false))
                {
                    var nested = new TaskCompletionSource<Exception?>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    second.Authorization.Setup(provider => provider.AuthorizeAsync(
                            It.IsAny<WmtpRequest>(), It.IsAny<WmtpOperation>(), It.IsAny<CancellationToken>()))
                        .Returns(async (WmtpRequest _, WmtpOperation _, CancellationToken _) =>
                        {
                            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                            try
                            {
                                await a.Measurements[0].SetDeltaAsync(Variant.From(3d),
                                    new WmtpRequest { SessionId = new NodeId("nested", 1) }, timeout.Token)
                                    .ConfigureAwait(false);
                                nested.TrySetResult(null);
                            }
                            catch (Exception error)
                            {
                                nested.TrySetResult(error);
                            }
                            return StatusCodes.Good;
                        });
                    int calls = 0;
                    a.Measurements[0].State.AnalogSignal!.StateChangedAsync += async (_, _, _, token) =>
                    {
                        if (Interlocked.Exchange(ref calls, 1) == 0)
                        {
                            await b.Measurements[0].SetDeltaAsync(Variant.From(2d),
                                new WmtpRequest { SessionId = new NodeId("outer", 1) }, token).ConfigureAwait(false);
                        }
                    };
                    first.Clock.Advance(TimeSpan.FromSeconds(2));
                    await Task.Run(first.Clock.Pulse).WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                    Exception? error = await nested.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                    Assert.That(error, Is.TypeOf<ServiceResultException>(), error?.ToString());
                    Assert.That(((ServiceResultException)error!).StatusCode, Is.EqualTo(StatusCodes.BadServerTooBusy));
                    Assert.That(b.Measurements[0].State.DeltaCondition!.Value, Is.EqualTo(2d));
                    Assert.That(a.Measurements[0].State.DeltaCondition!.Value, Is.Zero);
                }
            }
        }

        /// <summary>
        /// Refusal and cancellation before provider acceptance cannot commit either proposed trigger setting.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task RefusedOrCancelledConfigurationBeforeAcceptancePreservesBothSettingsAsync(
            bool delta, bool cancelled)
        {
            var equipment = new WmtpTestEquipment();
            equipment.Samples["Temperature"] = equipment.Sample(Variant.From(4d));
            await using WmtpPeripheral peripheral = await equipment.Builder(m_fixture.Context)
                .AddMeasurement(WmtpTestEquipment.Definition()).BuildAsync().ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            equipment.Measurement.Setup(provider => provider.ConfigureAsync(
                    It.IsAny<string>(), It.IsAny<uint>(), It.IsAny<Variant>(), It.IsAny<CancellationToken>()))
                .Returns((string _, uint _, Variant _, CancellationToken token) =>
                {
                    if (cancelled)
                    {
                        cancellation.Cancel();
                        token.ThrowIfCancellationRequested();
                    }
                    return new ValueTask<StatusCode>(StatusCodes.BadNotSupported);
                });
            Task operation = delta ? peripheral.Measurements[0].SetDeltaAsync(
                Variant.From(2d), new WmtpRequest(), cancellation.Token).AsTask() :
                peripheral.Measurements[0].SetTriggerAsync(20, new WmtpRequest(), cancellation.Token).AsTask();
            if (cancelled)
            {
                await Assert.CatchAsync<OperationCanceledException>(async () => await operation.ConfigureAwait(false));
            }
            else
            {
                ServiceResultException refused = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await operation.ConfigureAwait(false)))!;
                Assert.That(refused.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            }
            Assert.That(peripheral.Measurements[0].TriggerMilliseconds, Is.EqualTo(1000));
            Assert.That(peripheral.Measurements[0].State.DeltaCondition!.Value, Is.Zero);
            Assert.That(peripheral.Measurements[0].Delta, Is.EqualTo(Variant.From(0d)));
        }

        /// <summary>
        /// Calibration and time methods propagate explicit refusal/cancellation without publishing a proposed state.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task DeviceConfigurationAcceptanceBoundaryDoesNotPublishProposedStateAsync(
            bool setTime, bool cancelled)
        {
            var equipment = new WmtpTestEquipment();
            equipment.FullSnapshot();
            await using WmtpPeripheral peripheral = await equipment.Builder(m_fixture.Context)
                .WithFeatures(WmtpFeatures.Calibration | WmtpFeatures.DeviceTime).BuildAsync().ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            equipment.Calibration.Setup(provider => provider.SetCalibrationAsync(
                    It.IsAny<bool>(), It.IsAny<CancellationToken>())).Returns((bool _, CancellationToken _) =>
                {
                    if (cancelled)
                    {
                        cancellation.Cancel();
                    }
                    return new ValueTask<StatusCode>(cancelled ? StatusCodes.Good : StatusCodes.BadNotWritable);
                });
            equipment.DeviceTime.Setup(provider => provider.SetTimeAsync(
                    It.IsAny<DateTimeUtc>(), It.IsAny<TimeZoneDataType>(), It.IsAny<CancellationToken>()))
                .Returns((DateTimeUtc _, TimeZoneDataType _, CancellationToken _) =>
                {
                    if (cancelled)
                    {
                        cancellation.Cancel();
                    }
                    return new ValueTask<StatusCode>(cancelled ? StatusCodes.Good : StatusCodes.BadNotWritable);
                });
            Task operation = setTime ? peripheral.SetDeviceTimeAsync(equipment.Clock.GetUtcNow(),
                new TimeZoneDataType { Offset = 840 }, new WmtpRequest(), cancellation.Token).AsTask() :
                peripheral.SetCalibrationAsync(1, new WmtpRequest(), cancellation.Token).AsTask();
            if (cancelled)
            {
                await Assert.CatchAsync<OperationCanceledException>(async () => await operation.ConfigureAwait(false));
            }
            else
            {
                ServiceResultException refused = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await operation.ConfigureAwait(false)))!;
                Assert.That(refused.StatusCode, Is.EqualTo(StatusCodes.BadNotWritable));
            }
            Assert.That(peripheral.Nodes.CalibrationMode!.Value, Is.False);
            Assert.That(peripheral.Nodes.CalibrationMode.StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        /// <summary>
        /// Read-only authorization refuses every independent mutation class before its provider is invoked.
        /// </summary>
        [Test]
        public async Task ReadOnlyCallerCannotConfigureDeleteTransferOrAbortAsync()
        {
            var equipment = new WmtpTestEquipment();
            equipment.FullSnapshot();
            equipment.Samples["Temperature"] = equipment.Sample(Variant.From(10d));
            WmtpOperation? requested = null;
            equipment.Authorization.Setup(provider => provider.AuthorizeAsync(
                    It.IsAny<WmtpRequest>(), It.IsAny<WmtpOperation>(), It.IsAny<CancellationToken>()))
                .Returns((WmtpRequest _, WmtpOperation operation, CancellationToken _) =>
                {
                    requested = operation;
                    return new ValueTask<StatusCode>(operation == WmtpOperation.ReadRecords ?
                        StatusCodes.Good : StatusCodes.BadUserAccessDenied);
                });
            await using var work = new InMemoryWmtpRecordStore(NUnitTelemetryContext.Create());
            await using var service = new InMemoryWmtpRecordStore(NUnitTelemetryContext.Create());
            await work.AppendAsync(WmtpRecordStoreTests.Record(1)).ConfigureAwait(false);
            await using WmtpPeripheral peripheral = await equipment.Builder(m_fixture.Context)
                .WithFeatures(WmtpFeatures.All).WithWorkRecords(work, WmtpRecordFeatures.All)
                .WithServiceRecords(service, WmtpRecordFeatures.All).AddMeasurement(WmtpTestEquipment.Definition())
                .BuildAsync().ConfigureAwait(false);
            var request = new WmtpRequest();
            foreach (WmtpOperation operation in new[]
            {
                WmtpOperation.ConfigureMeasurement, WmtpOperation.Calibrate, WmtpOperation.SetTime,
                WmtpOperation.TransferFirmware, WmtpOperation.TransferLicense,
                WmtpOperation.DeleteRecords, WmtpOperation.Abort
            })
            {
                requested = null;
                ServiceResultException denied = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                {
                    switch (operation)
                    {
                        case WmtpOperation.ConfigureMeasurement:
                            await peripheral.Measurements[0].SetDeltaAsync(Variant.From(2d), request)
                                .ConfigureAwait(false);
                            break;
                        case WmtpOperation.Calibrate:
                            await peripheral.SetCalibrationAsync(1, request).ConfigureAwait(false);
                            break;
                        case WmtpOperation.SetTime:
                            await peripheral.SetDeviceTimeAsync(
                                equipment.Clock.GetUtcNow(), new TimeZoneDataType(), request).ConfigureAwait(false);
                            break;
                        case WmtpOperation.TransferFirmware:
                        case WmtpOperation.TransferLicense:
                            await peripheral.TransferAsync(operation == WmtpOperation.TransferFirmware ?
                                WmtpTransferKind.Firmware : WmtpTransferKind.License, ByteString.From(1), request)
                                .ConfigureAwait(false);
                            break;
                        case WmtpOperation.DeleteRecords:
                            await peripheral.WorkRecords!.DeleteAsync(new WmtpRecordQuery(), request)
                                .ConfigureAwait(false);
                            break;
                        default:
                            await peripheral.AbortAsync(request).ConfigureAwait(false);
                            break;
                    }
                }))!;
                Assert.That(denied.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(requested, Is.EqualTo(operation));
            }
            Assert.That(await peripheral.WorkRecords!.CountAsync(new WmtpRecordQuery(), request)
                .ConfigureAwait(false), Is.EqualTo(1));
            Assert.That(peripheral.Nodes.CalibrationMode!.Value, Is.False);
            Assert.That(peripheral.Measurements[0].State.DeltaCondition!.Value, Is.Zero);
            equipment.Measurement.Verify(provider => provider.ConfigureAsync(
                It.IsAny<string>(), It.IsAny<uint>(), It.IsAny<Variant>(), It.IsAny<CancellationToken>()), Times.Never);
            equipment.Calibration.Verify(provider => provider.SetCalibrationAsync(
                It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
            equipment.DeviceTime.Verify(provider => provider.SetTimeAsync(
                It.IsAny<DateTimeUtc>(), It.IsAny<TimeZoneDataType>(), It.IsAny<CancellationToken>()), Times.Never);
            equipment.Transfers.Verify(provider => provider.TransferAsync(
                It.IsAny<WmtpTransferKind>(), It.IsAny<ByteString>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>The actual WMTP-only model/transport fixture.</summary>
        private readonly WmtpTestFixture m_fixture = new();
    }
}
