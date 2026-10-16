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
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.MachineTool.Client;
using Opc.Ua.MachineTool.Server;
using Opc.Ua.MachineTool.WMTP;
using Opc.Ua.Tests;
using ClientSession = Opc.Ua.Client.ISession;

namespace Opc.Ua.MachineTool.Tests
{
    /// <summary>
    /// Exercises released WMTP semantics through actual TCP sessions and generated client proxies.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class WmtpSessionTests
    {
        /// <summary>
        /// All 33 released CUs have real behavior in WMTP-only and mixed, reordered-namespace address spaces.
        /// </summary>
        [TestCase(MachineToolParts.Wmtp, false)]
        [TestCase(MachineToolParts.All, true)]
        public async Task FullReleasedBehaviorWorksOverTcpWithIndependentModelSelectionAsync(
            MachineToolParts parts, bool reordered)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var fixture = new WmtpTestFixture();
            await fixture.StartAsync(parts, reordered).ConfigureAwait(false);
            var equipment = new WmtpTestEquipment();
            equipment.FullSnapshot();
            await using var work = new InMemoryWmtpRecordStore(telemetry);
            await using var service = new InMemoryWmtpRecordStore(telemetry);
            for (uint index = 1; index <= 3; index++)
            {
                await work.AppendAsync(WmtpRecordStoreTests.Record(index)).ConfigureAwait(false);
                WMTPOutputDataType serviceRecord = WmtpRecordStoreTests.Record(index);
                serviceRecord.ActualValue += 100;
                await service.AppendAsync(serviceRecord).ConfigureAwait(false);
            }
            WmtpPeripheralBuilder builder = equipment.Builder(fixture.Context, "FullPeripheral")
                .WithFeatures(WmtpFeatures.All).WithWorkRecords(work, WmtpRecordFeatures.All)
                .WithServiceRecords(service, WmtpRecordFeatures.All);
            for (uint kind = 0; kind < 8; kind++)
            {
                WmtpMeasurementDefinition definition = WmtpTestEquipment.Definition(
                    (WmtpMeasurementKind)kind, WmtpMeasurementFeatures.All);
                equipment.Samples[definition.Name] = equipment.Sample(Variant.From(20d + kind), kind + 1);
                builder.AddMeasurement(definition);
            }
            await using WmtpPeripheral peripheral = await builder.BuildAsync().ConfigureAwait(false);
            await using WmtpPeripheral other = await equipment.Builder(fixture.Context, "OtherPeripheral")
                .BuildAsync().ConfigureAwait(false);
            MachineToolRuntime<MachineToolState>? machine = null;
            if ((parts & MachineToolParts.MachineTool) != 0)
            {
                machine = await fixture.Context.AddMachineTool("IndependentMachine")
                    .WithProvider(new InMemoryMachineToolProvider(new MachineToolSnapshot()))
                    .WithIdentification(identity =>
                    {
                        identity.Manufacturer = new LocalizedText("Mixed test");
                        identity.SerialNumber = "Mixed";
                        identity.ProductInstanceUri = "urn:test:wmtp:mixed";
                    }).BuildAsync().ConfigureAwait(false);
            }
            fixture.Context.DeviceSet.AddReference(Opc.Ua.ReferenceTypeIds.Organizes, false, peripheral.NodeId);
            await using var transport = new ClientFixture(telemetry);
            await transport.LoadClientConfigurationAsync(fixture.Root, "WmtpClient").ConfigureAwait(false);
            using ClientSession session = await transport.ConnectAsync(fixture.Endpoint, SecurityPolicies.None)
                .ConfigureAwait(false);
            try
            {
                await using var owner = new MachineToolClient(session, telemetry);
                ArrayOf<MachineToolDeviceEntry> discovered = await owner.DiscoverAsync().ConfigureAwait(false);
                Assert.That(discovered.Count, Is.EqualTo(machine == null ? 2 : 3));
                Assert.That(owner.AvailableParts, Is.EqualTo(parts));
                WmtpPeripheralClient client = owner.GetWirelessPeripheral(peripheral.NodeId);
                WmtpPeripheralSnapshot snapshot = await client.ReadSnapshotAsync().ConfigureAwait(false);
                Assert.That(snapshot.SerialNumber.WrappedValue.TryGetValue(out string serial), Is.True);
                Assert.That(serial, Is.EqualTo("FullPeripheral"));
                Assert.That(snapshot.Battery.WrappedValue.TryGetValue(out double battery), Is.True);
                Assert.That(battery, Is.EqualTo(75));
                Assert.That(snapshot.OperationHours.WrappedValue.TryGetValue(out double hours), Is.True);
                Assert.That(hours, Is.EqualTo(72));
                Assert.That(snapshot.LicenseValidity.WrappedValue.TryGetValue(out double remaining), Is.True);
                Assert.That(remaining, Is.EqualTo(80));
                Assert.That(snapshot.LifeCycleStatus.WrappedValue.TryGetValue(out uint lifecycle), Is.True);
                Assert.That(lifecycle, Is.EqualTo(1));
                Assert.That(snapshot.OperationSinceService.WrappedValue.TryGetValue(out double sinceService), Is.True);
                Assert.That(sinceService, Is.EqualTo(5000));
                ArrayOf<WmtpMeasurementReading> readings = await client.ReadMeasurementsAsync().ConfigureAwait(false);
                Assert.That(readings.Count, Is.EqualTo(8));
                for (int index = 0; index < readings.Count; index++)
                {
                    WmtpMeasurementReading reading = readings[index];
                    Assert.That(reading.TypeOfMeasurement.WrappedValue.TryGetValue(out uint kind), Is.True);
                    Assert.That(reading.Sample.WrappedValue.TryGetValue(out double value), Is.True);
                    Assert.That(value, Is.EqualTo(20 + kind));
                    Assert.That(reading.EngineeringUnits.WrappedValue.TryGetStructure<EUInformation>(
                        out EUInformation? unit), Is.True);
                    Assert.That(unit!.UnitId, Is.EqualTo(
                        WmtpTestEquipment.Definition((WmtpMeasurementKind)kind).EngineeringUnits.UnitId));
                    Assert.That(reading.Sample.SourceTimestamp,
                        Is.EqualTo(equipment.Samples[((WmtpMeasurementKind)kind).ToString()].Value.SourceTimestamp));
                    Assert.That(reading.InternalUpdateInterval.WrappedValue.TryGetValue(out double interval), Is.True);
                    Assert.That(interval, Is.EqualTo(25));
                    Assert.That(reading.ValuePrecision.WrappedValue.TryGetValue(out double precision), Is.True);
                    Assert.That(precision, Is.EqualTo(0.125));
                }
                Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await client.ReadMeasurementsAsync(7).ConfigureAwait(false));
                WMTPMeasurementTypeClient sensor = client.GetMeasurementProxy(readings[0].NodeId);
                await sensor.SetDeltaConditionAsync(Variant.From(2d)).ConfigureAwait(false);
                await sensor.SetTriggerSettingsAsync(0).ConfigureAwait(false);
                ServiceResultException invariant = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await sensor.SetDeltaConditionAsync(Variant.From(0d)).ConfigureAwait(false))!;
                Assert.That(invariant.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
                await client.SetCalibrationAsync(true).ConfigureAwait(false);
                Assert.That(peripheral.Nodes.CalibrationMode!.Value, Is.True);
                DateTimeUtc setTime = equipment.Clock.GetUtcNow();
                await client.SetDeviceTimeAsync(setTime,
                    new TimeZoneDataType { Offset = -120, DaylightSavingInOffset = true }).ConfigureAwait(false);
                equipment.DeviceTime.Verify(provider => provider.SetTimeAsync(setTime,
                    It.Is<TimeZoneDataType>(zone => zone.Offset == -120 && zone.DaylightSavingInOffset),
                    It.IsAny<CancellationToken>()), Times.Once);
                ServiceResultException refused = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await client.TransferAsync(WmtpTransferKind.Firmware, ByteString.From(4, 5, 6))
                        .ConfigureAwait(false))!;
                Assert.That(refused.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
                byte[]? received = null;
                equipment.Transfers.Setup(provider => provider.TransferAsync(
                        It.IsAny<WmtpTransferKind>(), It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                    .Callback((WmtpTransferKind _, ByteString bytes, CancellationToken _) => received = bytes.ToArray())
                    .Returns(new ValueTask<StatusCode>(StatusCodes.Good));
                await client.TransferAsync(WmtpTransferKind.Firmware, ByteString.From(4, 5, 6))
                    .ConfigureAwait(false);
                Assert.That(received, Is.EqualTo(new byte[] { 4, 5, 6 }));
                await client.TransferAsync(WmtpTransferKind.License, ByteString.From(7, 8)).ConfigureAwait(false);
                Assert.That(received, Is.EqualTo(new byte[] { 7, 8 }));
                Assert.That(peripheral.Nodes.LicenseInformation!.Value, Is.EqualTo("test authorization description"));
                const string sensitiveMarker = "private-transfer-diagnostic";
                equipment.Transfers.Setup(provider => provider.TransferAsync(
                        It.IsAny<WmtpTransferKind>(), It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                    .Throws(new InvalidOperationException(sensitiveMarker));
                ServiceResultException hidden = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await client.TransferAsync(WmtpTransferKind.License, ByteString.From(7, 8))
                        .ConfigureAwait(false))!;
                Assert.That(hidden.StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
                Assert.That(hidden.ToString(), Does.Not.Contain(sensitiveMarker));
                equipment.Transfers.Setup(provider => provider.TransferAsync(
                        It.IsAny<WmtpTransferKind>(), It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                    .Throws(new ServiceResultException(StatusCodes.BadNotSupported, sensitiveMarker));
                ServiceResultException sanitized = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await client.TransferAsync(WmtpTransferKind.Firmware, ByteString.From(7, 8))
                        .ConfigureAwait(false))!;
                Assert.That(sanitized.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
                Assert.That(sanitized.ToString(), Does.Not.Contain(sensitiveMarker));
                Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                    await client.TransferAsync(WmtpTransferKind.Firmware, ByteString.From(4, 5, 6), 2)
                        .ConfigureAwait(false));

                WmtpRecordClient workClient = await client.GetWorkRecordsAsync().ConfigureAwait(false);
                WmtpRecordClient serviceClient = await client.GetServiceRecordsAsync().ConfigureAwait(false);
                await ExerciseRecordsAsync(workClient, 0).ConfigureAwait(false);
                Assert.That(await serviceClient.CountAsync(new WmtpRecordQuery()).ConfigureAwait(false), Is.EqualTo(3));
                await ExerciseRecordsAsync(serviceClient, 100).ConfigureAwait(false);
                Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await owner.GetWirelessPeripheral(other.NodeId).GetWorkRecordsAsync().ConfigureAwait(false));
                WmtpPeripheralSnapshot minimal = await owner.GetWirelessPeripheral(other.NodeId).ReadSnapshotAsync()
                    .ConfigureAwait(false);
                Assert.That(minimal.Battery.IsNull, Is.True);
                Assert.That(minimal.CalibrationMode.IsNull, Is.True);
                Assert.That(minimal.LicenseInformation.IsNull, Is.True);

                WriteResponse response = await session.WriteAsync(null,
                    [new WriteValue
                    {
                        NodeId = peripheral.Nodes.CalibrationMode.NodeId, AttributeId = Attributes.Value,
                        Value = new DataValue(Variant.From(false))
                    }], CancellationToken.None).ConfigureAwait(false);
                Assert.That(response.Results[0], Is.EqualTo(StatusCodes.BadNotWritable));
                Assert.That(peripheral.Nodes.CalibrationMode.Value, Is.True);
            }
            finally
            {
                await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                if (machine != null)
                {
                    await machine.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Local cancellation sends no Abort, explicit cross-session abort is authorized, and stale reads are honest.
        /// </summary>
        [Test]
        public async Task CompetingTcpClientsCannotImplicitlyAbortEachOtherAndStaleReadsRemainUncertainAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var fixture = new WmtpTestFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            var equipment = new WmtpTestEquipment();
            equipment.Samples["Temperature"] = equipment.Sample(Variant.From(4d));
            equipment.Authorization.Setup(provider => provider.AuthorizeAsync(
                    It.IsAny<WmtpRequest>(), WmtpOperation.Abort, It.IsAny<CancellationToken>()))
                .Returns((WmtpRequest request, WmtpOperation _, CancellationToken _) =>
                    new ValueTask<StatusCode>(request.SessionId == request.ActiveSessionId ?
                        StatusCodes.Good : StatusCodes.BadUserAccessDenied));
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var blocking = new Mock<IWmtpRecordStore>(MockBehavior.Strict);
            blocking.Setup(store => store.ReadAsync(It.IsAny<WmtpRecordQuery>(), It.IsAny<CancellationToken>()))
                .Returns(async (WmtpRecordQuery _, CancellationToken token) =>
                {
                    using CancellationTokenRegistration registration =
                        token.Register(() => cancelled.TrySetResult(true));
                    entered.TrySetResult(true);
                    await release.Task.ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    return new[] { WmtpRecordStoreTests.Record(4) }.ToArrayOf();
                });
            await using var service = new InMemoryWmtpRecordStore(telemetry);
            await using WmtpPeripheral peripheral = await equipment.Builder(fixture.Context)
                .AddMeasurement(WmtpTestEquipment.Definition() with { StaleAfter = TimeSpan.FromSeconds(1) })
                .WithWorkRecords(blocking.Object).WithServiceRecords(service).BuildAsync().ConfigureAwait(false);
            await using var firstTransport = new ClientFixture(telemetry);
            await using var secondTransport = new ClientFixture(telemetry);
            await firstTransport.LoadClientConfigurationAsync(fixture.Root, "WmtpFirst").ConfigureAwait(false);
            await secondTransport.LoadClientConfigurationAsync(fixture.Root, "WmtpSecond").ConfigureAwait(false);
            using ClientSession first = await firstTransport.ConnectAsync(fixture.Endpoint, SecurityPolicies.None)
                .ConfigureAwait(false);
            using ClientSession second = await secondTransport.ConnectAsync(fixture.Endpoint, SecurityPolicies.None)
                .ConfigureAwait(false);
            Task<ArrayOf<WMTPOutputDataType>>? pending = null;
            try
            {
                await using var firstOwner = new MachineToolClient(first, telemetry);
                await using var secondOwner = new MachineToolClient(second, telemetry);
                WmtpPeripheralClient firstClient = firstOwner.GetWirelessPeripheral(peripheral.NodeId);
                WmtpPeripheralClient secondClient = secondOwner.GetWirelessPeripheral(peripheral.NodeId);
                WmtpRecordClient firstWork = await firstClient.GetWorkRecordsAsync().ConfigureAwait(false);
                WmtpRecordClient firstService = await firstClient.GetServiceRecordsAsync().ConfigureAwait(false);
                WmtpRecordClient secondWork = await secondClient.GetWorkRecordsAsync().ConfigureAwait(false);
                pending = firstWork.ReadAsync(new WmtpRecordQuery()).AsTask();
                await entered.Task.ConfigureAwait(false);
                using var localCancellation = new CancellationTokenSource();
                localCancellation.Cancel();
                Assert.CatchAsync<OperationCanceledException>(async () => await secondWork.ReadAsync(
                    new WmtpRecordQuery(), cancellationToken: localCancellation.Token).ConfigureAwait(false));
                equipment.Authorization.Verify(provider => provider.AuthorizeAsync(
                    It.IsAny<WmtpRequest>(), WmtpOperation.Abort, It.IsAny<CancellationToken>()), Times.Never);
                Assert.That(pending.IsCompleted, Is.False);
                ServiceResultException denied = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await secondWork.AbortAsync().ConfigureAwait(false))!;
                Assert.That(denied.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(cancelled.Task.IsCompleted, Is.False);
                equipment.Clock.Advance(TimeSpan.FromSeconds(2));
                ArrayOf<WmtpMeasurementReading> readings = await secondClient.ReadMeasurementsAsync()
                    .ConfigureAwait(false);
                Assert.That(readings.Count, Is.EqualTo(1));
                Assert.That(readings[0].Sample.StatusCode, Is.EqualTo(StatusCodes.UncertainLastUsableValue));
                Assert.That(readings[0].Sample.WrappedValue, Is.EqualTo(Variant.From(4d)));
                await firstService.AbortAsync().ConfigureAwait(false);
                await cancelled.Task.ConfigureAwait(false);
                release.TrySetResult(true);
                ServiceResultException aborted = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await pending.ConfigureAwait(false))!;
                Assert.That(aborted.StatusCode, Is.EqualTo(StatusCodes.BadRequestCancelledByClient));
                Assert.That((await firstWork.ReadAsync(new WmtpRecordQuery()).ConfigureAwait(false))[0].ActualValue,
                    Is.EqualTo(4.5));
                await firstOwner.DisposeAsync().ConfigureAwait(false);
                Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                    await firstWork.ReadAsync(new WmtpRecordQuery()).ConfigureAwait(false));
            }
            finally
            {
                release.TrySetResult(true);
                if (pending != null)
                {
                    try
                    {
                        await pending.ConfigureAwait(false);
                    }
                    catch (ServiceResultException)
                    {
                        // The explicit abort result is asserted above; cleanup still joins the request.
                    }
                }
                await first.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                await second.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// UInt64 samples and Number inputs retain their exact wire type; lossy Double publication is rejected.
        /// </summary>
        [Test]
        public async Task UInt64WireSamplesAndDeltaInputsRetainPrecisionAndRejectWrongTypesAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var fixture = new WmtpTestFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            var equipment = new WmtpTestEquipment();
            equipment.Samples["Temperature"] = equipment.Sample(Variant.From(9007199254740993ul));
            await using WmtpPeripheral peripheral = await equipment.Builder(fixture.Context)
                .AddMeasurement(WmtpTestEquipment.Definition() with
                {
                    DataType = BuiltInType.UInt64, Delta = Variant.From(0ul),
                    InstrumentRange = new Range { Low = 0, High = 1e19 }
                }).BuildAsync().ConfigureAwait(false);
            await using var transport = new ClientFixture(telemetry);
            await transport.LoadClientConfigurationAsync(fixture.Root, "WmtpInteger").ConfigureAwait(false);
            using ClientSession session = await transport.ConnectAsync(fixture.Endpoint, SecurityPolicies.None)
                .ConfigureAwait(false);
            try
            {
                await using var owner = new MachineToolClient(session, telemetry);
                WmtpPeripheralClient client = owner.GetWirelessPeripheral(peripheral.NodeId);
                WmtpMeasurementReading reading = (await client.ReadMeasurementsAsync().ConfigureAwait(false))[0];
                Assert.That(reading.Sample.WrappedValue.TryGetValue(out ulong sample), Is.True);
                Assert.That(sample, Is.EqualTo(9007199254740993ul));
                WMTPMeasurementTypeClient sensor = client.GetMeasurementProxy(reading.NodeId);
                ServiceResultException wrongType = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await sensor.SetDeltaConditionAsync(Variant.From(1d)).ConfigureAwait(false))!;
                Assert.That(wrongType.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
                ServiceResultException lost = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await sensor.SetDeltaConditionAsync(Variant.From(9007199254740993ul)).ConfigureAwait(false))!;
                Assert.That(lost.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
                equipment.Measurement.Verify(provider => provider.ConfigureAsync(
                    It.IsAny<string>(), It.IsAny<uint>(), It.IsAny<Variant>(), It.IsAny<CancellationToken>()),
                    Times.Never);
                await sensor.SetDeltaConditionAsync(Variant.From(9007199254740992ul)).ConfigureAwait(false);
                equipment.Measurement.Verify(provider => provider.ConfigureAsync(
                    "Temperature", 1000, Variant.From(9007199254740992ul), It.IsAny<CancellationToken>()), Times.Once);
                Assert.That(peripheral.Measurements[0].State.DeltaCondition!.Value, Is.EqualTo(9007199254740992d));
                Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await sensor.SetTriggerSettingsAsync(0.5).ConfigureAwait(false));
                Assert.That(peripheral.Measurements[0].TriggerMilliseconds, Is.EqualTo(1000));
            }
            finally
            {
                await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Executes every mandatory and optional record method with exact endpoint, ordering and copy assertions.
        /// </summary>
        private static async ValueTask ExerciseRecordsAsync(WmtpRecordClient client, double offset)
        {
            var all = new WmtpRecordQuery();
            var middle = new WmtpRecordQuery { Selection = WmtpRecordSelection.Index, FromIndex = 2, ToIndex = 2 };
            var range = new WmtpRecordQuery
            {
                Selection = WmtpRecordSelection.Time,
                FromTime = WmtpRecordStoreTests.Record(2).Timestamp, ToTime = WmtpRecordStoreTests.Record(3).Timestamp
            };
            Assert.That(await client.CountAsync(all).ConfigureAwait(false), Is.EqualTo(3));
            ArrayOf<WMTPOutputDataType> records = await client.ReadAsync(all).ConfigureAwait(false);
            Assert.That(records.Count, Is.EqualTo(3));
            Assert.That(records[0].ActualValue, Is.EqualTo(1.5 + offset));
            Assert.That(records[0].EngineeringUnits.UnitId, Is.EqualTo(4408652));
            Assert.That(records[0].InstrumentRange.Low, Is.EqualTo(-100));
            Assert.That(records[0].ValuePrecision, Is.EqualTo(0.125));
            Assert.That(records[0].SignalTag, Is.EqualTo("sensor-a"));
            Assert.That(records[0].MeasurementPeriod, Is.EqualTo(50));
            Assert.That((await client.ReadAsync(middle).ConfigureAwait(false))[0].Index, Is.EqualTo(2));
            Assert.That((await client.ReadAsync(range).ConfigureAwait(false)).Count, Is.EqualTo(2));
            Assert.That(await client.CountAsync(range).ConfigureAwait(false), Is.EqualTo(2));
            Assert.That((await client.ReadAsync(new WmtpRecordQuery { Selection = WmtpRecordSelection.First })
                .ConfigureAwait(false))[0].Index, Is.EqualTo(1));
            Assert.That((await client.ReadAsync(new WmtpRecordQuery { Selection = WmtpRecordSelection.Last })
                .ConfigureAwait(false))[0].Index, Is.EqualTo(3));
            Assert.That(await client.CountAsync(all).ConfigureAwait(false), Is.EqualTo(3));
            Assert.ThrowsAsync<ServiceResultException>(async () =>
                await client.ReadAsync(all, 2).ConfigureAwait(false));
            await client.AbortAsync().ConfigureAwait(false);
            await client.DeleteAsync(middle).ConfigureAwait(false);
            Assert.That(await client.CountAsync(all).ConfigureAwait(false), Is.EqualTo(2));
            await client.DeleteAsync(range).ConfigureAwait(false);
            Assert.That(await client.CountAsync(all).ConfigureAwait(false), Is.EqualTo(1));
            await client.DeleteAsync(all).ConfigureAwait(false);
            Assert.That(await client.CountAsync(all).ConfigureAwait(false), Is.Zero);
            ServiceResultException empty = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await client.ReadAsync(new WmtpRecordQuery { Selection = WmtpRecordSelection.Last })
                    .ConfigureAwait(false))!;
            Assert.That(empty.StatusCode, Is.EqualTo(StatusCodes.BadNoData));
        }
    }
}
