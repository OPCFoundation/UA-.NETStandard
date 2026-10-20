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
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using Opc.Ua.MachineTool.Server;
using Opc.Ua.MachineTool.WMTP;
using Opc.Ua.Tests;

namespace Opc.Ua.Tools.Tests
{
    /// <summary>
    /// Exercises real MCP framing and generated tools through opc.tcp to explicit provider-backed WMTP equipment.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    [Category("Integration")]
    [NonParallelizable]
    public sealed class WmtpMcpIntegrationTests
    {
        /// <summary>
        /// Creates independent real equipment and two explicitly named borrowed sessions for each test.
        /// </summary>
        [SetUp]
        public async Task SetUpAsync()
        {
            m_fixture = new WmtpMcpFixture();
            await m_fixture.StartAsync().ConfigureAwait(false);
            m_mcp = await WmtpMcpHarness.CreateAsync(m_fixture.Session).ConfigureAwait(false);
            await m_mcp.Manager.RegisterExistingSessionAsync("second", m_fixture.SecondSession, "Anonymous")
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Joins the MCP server before closing the test-owned UA sessions and equipment.
        /// </summary>
        [TearDown]
        public async Task TearDownAsync()
        {
            try
            {
                if (m_mcp != null)
                {
                    await m_mcp.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                if (m_fixture != null)
                {
                    await m_fixture.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// WMTP-only discovery and reads retain exact UInt64 values, original quality/time, units and optional absence.
        /// </summary>
        [Test]
        public async Task WmtpOnlySnapshotsPreserveQualityPrecisionUnitsAndAbsence()
        {
            Assert.That(m_fixture.Session.NamespaceUris.GetIndex(Opc.Ua.MachineTool.Namespaces.MachineTool),
                Is.EqualTo(-1));
            JsonElement inventory = Success(await CallAsync("machinetool_list_devices").ConfigureAwait(false));
            Assert.That(inventory.GetProperty("machineToolModelPresent").GetBoolean(), Is.False);
            Assert.That(inventory.GetProperty("wmtpModelPresent").GetBoolean(), Is.True);
            Assert.That(inventory.GetProperty("devices").GetArrayLength(), Is.EqualTo(3));
            JsonElement result = Success(await CallAsync("wmtp_read_peripheral", Target()).ConfigureAwait(false));
            Assert.That(result.GetProperty("nodeId").GetString(), Is.EqualTo(m_fixture.Peripheral.NodeId.ToString()));
            JsonElement status = result.GetProperty("status");
            DataValue serial = Decode(status.GetProperty("serialNumber"));
            Assert.That(serial.WrappedValue.TryGetValue(out string serialText), Is.True);
            Assert.That(serialText, Is.EqualTo("WmtpMcpFull"));
            DataValue battery = Decode(status.GetProperty("battery"));
            Assert.That(battery.WrappedValue.TryGetValue(out double batteryLevel), Is.True);
            Assert.That(batteryLevel, Is.EqualTo(75));
            Assert.That(battery.StatusCode, Is.EqualTo(StatusCodes.UncertainSensorNotAccurate));
            Assert.That(battery.SourceTimestamp, Is.EqualTo(WmtpMcpFixture.SourceTime));
            Assert.That(battery.ServerTimestamp.IsNull, Is.False);
            foreach ((string name, double expected) in new (string, double)[]
            {
                ("licenseValidity", 80), ("remainingLifetime", 60), ("remainingCycles", 40),
                ("nextService", 20), ("operationHours", 72), ("operationSinceService", 5000)
            })
            {
                Assert.That(Decode(status.GetProperty(name)).WrappedValue.TryGetValue(out double value), Is.True);
                Assert.That(value, Is.EqualTo(expected), name);
            }
            Assert.That(Decode(status.GetProperty("lifeCycleStatus")).WrappedValue.TryGetValue(out uint lifecycle),
                Is.True);
            Assert.That(lifecycle, Is.EqualTo(1));
            Assert.That(Decode(status.GetProperty("lastService")).WrappedValue.TryGetValue(out DateTimeUtc lastService),
                Is.True);
            Assert.That(lastService, Is.EqualTo(WmtpMcpFixture.SourceTime));
            Assert.That(Decode(status.GetProperty("nextServiceDate")).WrappedValue.TryGetValue(out DateTimeUtc next),
                Is.True);
            Assert.That(next, Is.EqualTo(WmtpMcpFixture.SourceTime.Add(TimeSpan.FromDays(30))));
            JsonElement measurements = result.GetProperty("measurements");
            Assert.That(measurements.GetArrayLength(), Is.EqualTo(10));
            JsonElement sensor = measurements.EnumerateArray().Single(reading =>
                reading.GetProperty("nodeId").GetString() ==
                    m_fixture.Sensor(BuiltInType.UInt64).State.NodeId.ToString());
            JsonElement values = sensor.GetProperty("values");
            Assert.That(sensor.GetProperty("valueNodeId").GetString(),
                Is.EqualTo(m_fixture.Sensor(BuiltInType.UInt64).State.AnalogSignal!.NodeId.ToString()));
            Assert.That(sensor.GetProperty("valueNodeId").GetString(),
                Is.Not.EqualTo(sensor.GetProperty("nodeId").GetString()));
            DataValue sample = Decode(values.GetProperty("sample"));
            Assert.That(sample.WrappedValue.TryGetValue(out ulong exact), Is.True);
            Assert.That(exact, Is.EqualTo(9007199254740993ul));
            Assert.That(sample.SourceTimestamp, Is.EqualTo(WmtpMcpFixture.SourceTime));
            Assert.That(sample.StatusCode, Is.EqualTo(StatusCodes.UncertainSensorNotAccurate));
            Assert.That(values.GetProperty("sample").GetRawText(), Does.Contain("9007199254740993"));
            DataValue units = Decode(values.GetProperty("engineeringUnits"));
            Assert.That(units.WrappedValue.TryGetStructure<EUInformation>(out EUInformation? unit), Is.True);
            Assert.That(unit!.UnitId, Is.EqualTo(4408652));
            Assert.That(unit.DisplayName.Text, Is.EqualTo("°C"));
            Assert.That(Decode(values.GetProperty("valuePrecision")).WrappedValue.TryGetValue(out double precision),
                Is.True);
            Assert.That(precision, Is.EqualTo(0.125));
            Assert.That(Decode(values.GetProperty("index")).WrappedValue.TryGetValue(out uint index), Is.True);
            Assert.That(index, Is.EqualTo(7));
            JsonElement minimal = Success(await CallAsync("wmtp_read_peripheral",
                Target(m_fixture.Minimal.NodeId)).ConfigureAwait(false));
            Assert.That(minimal.GetProperty("measurements").GetArrayLength(), Is.Zero);
            Assert.That(minimal.GetProperty("status").GetProperty("battery").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(minimal.GetProperty("status").GetProperty("calibrationMode").ValueKind,
                Is.EqualTo(JsonValueKind.Null));
            Assert.That(minimal.GetProperty("status").GetProperty("licenseInformation").ValueKind,
                Is.EqualTo(JsonValueKind.Null));
            JsonObject statusOnly = Target();
            statusOnly["includeMeasurements"] = false;
            JsonElement only = Success(await CallAsync("wmtp_read_peripheral", statusOnly).ConfigureAwait(false));
            Assert.That(only.GetProperty("measurementsIncluded").GetBoolean(), Is.False);
            Assert.That(only.TryGetProperty("measurements", out _), Is.False);
            AssertNoDeviceActions();
        }

        /// <summary>
        /// A WMTP read supplies the exact value NodeId to the shared observer, which cleans up only its subscription.
        /// </summary>
        [Test]
        public async Task ReadValueNodeIdFeedsSharedObservationAndPreservesBorrowedSubscriptions()
        {
            JsonElement snapshot = Success(await CallAsync("wmtp_read_peripheral", Target()).ConfigureAwait(false));
            JsonElement sensor = snapshot.GetProperty("measurements").EnumerateArray().Single(measurement =>
                measurement.GetProperty("nodeId").GetString() ==
                    m_fixture.Sensor(BuiltInType.UInt64).State.NodeId.ToString());
            string valueNodeId = sensor.GetProperty("valueNodeId").GetString()!;
            Assert.That(valueNodeId,
                Is.EqualTo(m_fixture.Sensor(BuiltInType.UInt64).State.AnalogSignal!.NodeId.ToString()));
            Assert.That(valueNodeId, Is.Not.EqualTo(sensor.GetProperty("nodeId").GetString()));
            DataValue original = Decode(sensor.GetProperty("values").GetProperty("sample"));
            await using ISubscription unrelated = m_fixture.Subscriptions.Add(
                new Mock<ISubscriptionNotificationHandler>().Object,
                new OptionsMonitor<SubscriptionOptions>(new SubscriptionOptions()));
            Assert.That(unrelated.MonitoredItems.TryAdd("unrelated",
                new OptionsMonitor<MonitoredItemOptions>(new MonitoredItemOptions
                {
                    StartNodeId = m_fixture.Sensor(BuiltInType.Double).State.AnalogSignal!.NodeId,
                    AttributeId = Attributes.Value
                }), out IMonitoredItem? item), Is.True);
            using var ready = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (!item!.Created)
            {
                await Task.Delay(20, ready.Token).ConfigureAwait(false);
            }
            int subscriptions = m_fixture.Subscriptions.Count;
            JsonElement observed = Success(await CallAsync("machinetool_observe_values", new JsonObject
            {
                ["nodeIds"] = new JsonArray(valueNodeId),
                ["durationSeconds"] = 15,
                ["maximumNotifications"] = 1
            }).ConfigureAwait(false));
            Assert.That(observed.GetProperty("completionReason").GetString(), Is.EqualTo("LimitReached"));
            Assert.That(observed.GetProperty("droppedNotifications").GetInt64(), Is.Zero);
            JsonElement change = observed.GetProperty("changes").EnumerateArray().Single();
            Assert.That(change.GetProperty("nodeId").GetString(), Is.EqualTo(valueNodeId));
            DataValue reading = Decode(change.GetProperty("reading"));
            Assert.That(reading.WrappedValue.TryGetValue(out ulong value), Is.True);
            Assert.That(value, Is.EqualTo(9007199254740993ul));
            Assert.That(reading.WrappedValue, Is.EqualTo(original.WrappedValue));
            Assert.That(reading.StatusCode, Is.EqualTo(original.StatusCode));
            Assert.That(reading.SourceTimestamp, Is.EqualTo(original.SourceTimestamp));
            Assert.That(reading.ServerTimestamp.IsNull, Is.False);
            Assert.That(m_fixture.Subscriptions.Count, Is.EqualTo(subscriptions));
            Assert.That(m_fixture.Subscriptions.Items, Does.Contain(unrelated));
            Assert.That(item.Created, Is.True);
            Assert.That(m_fixture.Session.Connected, Is.True);
            AssertNoDeviceActions();
        }

        /// <summary>
        /// All five report selections copy exact whole structures in each independent store and never consume them.
        /// </summary>
        [Test]
        public async Task ReportsCopyExactWholeSelectionsWithoutConsumingEitherStore()
        {
            foreach (string dataset in new[] { "work", "service" })
            {
                foreach ((JsonObject query, uint[] indices) in new (JsonObject, uint[])[]
                {
                    (Selection("all"), [1, 2, 3]),
                    (Selection("first"), [1]),
                    (Selection("last"), [3]),
                    (IndexQuery(1, 2), [1, 2]),
                    (TimeQuery("2026-09-01T00:00:02Z", "2026-09-01T00:00:03Z"), [2, 3])
                })
                {
                    JsonElement result = Success(await CallAsync("wmtp_report_records",
                        Records(dataset, query)).ConfigureAwait(false));
                    Assert.That(result.GetProperty("count").GetInt32(), Is.EqualTo(indices.Length));
                    Assert.That(result.GetProperty("dataset").GetString(), Is.EqualTo(dataset));
                    Assert.That(result.GetProperty("completeSelection").GetBoolean(), Is.True);
                    Assert.That(result.GetProperty("consumed").GetBoolean(), Is.False);
                    Assert.That(result.GetProperty("completeHardwareHistory").GetBoolean(), Is.False);
                    ArrayOf<WMTPOutputDataType> report = DecodeRecords(result);
                    Assert.That(report.Count, Is.EqualTo(indices.Length));
                    for (int position = 0; position < indices.Length; position++)
                    {
                        WMTPOutputDataType record = report[position];
                        WMTPOutputDataType expected = WmtpMcpFixture.Record(indices[position],
                            dataset == "work" ? 0 : 100);
                        Assert.That(record.Index, Is.EqualTo(indices[position]));
                        Assert.That(record.ActualValue, Is.EqualTo(expected.ActualValue));
                        Assert.That(record.Timestamp, Is.EqualTo(expected.Timestamp));
                        Assert.That(record.EngineeringUnits.UnitId, Is.EqualTo(4408652));
                        Assert.That(record.ValuePrecision, Is.EqualTo(0.125));
                        Assert.That(record.IsEqual(expected), Is.True, $"{dataset} record {record.Index}");
                    }
                    report[0].ActualValue = -99;
                    ArrayOf<WMTPOutputDataType> repeated = DecodeRecords(Success(
                        await CallAsync("wmtp_report_records", Records(dataset, query)).ConfigureAwait(false)));
                    Assert.That(repeated[0].ActualValue, Is.EqualTo(indices[0] + 0.5 +
                        (dataset == "work" ? 0 : 100)));
                }
                Assert.That(await CountAsync(dataset).ConfigureAwait(false), Is.EqualTo(3));
            }
            Assert.That(await m_fixture.OtherWork.CountAsync(new WmtpRecordQuery()).ConfigureAwait(false),
                Is.EqualTo(1));
            m_fixture.WorkProvider.Verify(provider => provider.ReadAsync(
                It.Is<WmtpRecordQuery>(query => query.Selection == WmtpRecordSelection.First),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
            m_fixture.WorkProvider.Verify(provider => provider.ReadAsync(
                It.Is<WmtpRecordQuery>(query => query.Selection == WmtpRecordSelection.Last),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
            AssertNoDeviceActions();
        }

        /// <summary>
        /// Time count is a real inclusive time query, with empty ranges and independent peripheral identities.
        /// </summary>
        [Test]
        public async Task CountsTimeRangesEmptySelectionsAndPeripheralStoresRemainDistinct()
        {
            foreach (string dataset in new[] { "work", "service" })
            {
                JsonElement count = Success(await CallAsync("wmtp_count_records", Records(dataset,
                    TimeQuery("2026-09-01T00:00:02Z", "2026-09-01T00:00:02Z"))).ConfigureAwait(false));
                Assert.That(count.GetProperty("count").GetUInt32(), Is.EqualTo(1));
                JsonElement empty = Success(await CallAsync("wmtp_report_records",
                    Records(dataset, IndexQuery(40, 50))).ConfigureAwait(false));
                Assert.That(empty.GetProperty("count").GetInt32(), Is.Zero);
                Assert.That(DecodeRecords(empty).Count, Is.Zero);
            }
            m_fixture.WorkProvider.Verify(provider => provider.CountAsync(
                It.Is<WmtpRecordQuery>(query => query.Selection == WmtpRecordSelection.Time &&
                    query.FromTime == WmtpMcpFixture.SourceTime.Add(TimeSpan.FromSeconds(2)) &&
                    query.ToTime == WmtpMcpFixture.SourceTime.Add(TimeSpan.FromSeconds(2))),
                It.IsAny<CancellationToken>()), Times.Once);
            JsonObject other = Records("work", Selection("all"));
            other["peripheralNodeId"] = m_fixture.Other.NodeId.ToString();
            ArrayOf<WMTPOutputDataType> report = DecodeRecords(Success(
                await CallAsync("wmtp_report_records", other).ConfigureAwait(false)));
            Assert.That(report.Count, Is.EqualTo(1));
            Assert.That(report[0].Index, Is.EqualTo(20));
            Assert.That(report[0].ActualValue, Is.EqualTo(220.5));
            JsonObject missing = Records("work", Selection("all"));
            missing["peripheralNodeId"] = m_fixture.Minimal.NodeId.ToString();
            Error(await CallAsync("wmtp_count_records", missing).ConfigureAwait(false), "BadNotSupported");
            Assert.That(await CountAsync("work").ConfigureAwait(false), Is.EqualTo(3));
            Assert.That(await CountAsync("service").ConfigureAwait(false), Is.EqualTo(3));
        }

        /// <summary>
        /// Record, sensor and encoded-byte bounds fail explicitly, without successful truncated data or deletion.
        /// </summary>
        [Test]
        public async Task CompleteReportsAndMeasurementsFailAtBoundsInsteadOfTruncating()
        {
            JsonObject report = Records("work", Selection("all"));
            report["maximumRecords"] = 2;
            Error(await CallAsync("wmtp_report_records", report).ConfigureAwait(false), "BadEncodingLimitsExceeded");
            report["maximumRecords"] = 3;
            Assert.That(Success(await CallAsync("wmtp_report_records", report).ConfigureAwait(false))
                .GetProperty("count").GetInt32(), Is.EqualTo(3));
            report["maximumResponseBytes"] = 1024;
            Error(await CallAsync("wmtp_report_records", report).ConfigureAwait(false), "BadEncodingLimitsExceeded");
            JsonObject read = Target();
            read["maximumMeasurements"] = 9;
            Error(await CallAsync("wmtp_read_peripheral", read).ConfigureAwait(false), "BadEncodingLimitsExceeded");
            read["maximumMeasurements"] = 10;
            read["maximumResponseBytes"] = 1024;
            Error(await CallAsync("wmtp_read_peripheral", read).ConfigureAwait(false), "BadEncodingLimitsExceeded");
            Assert.That(await CountAsync("work").ConfigureAwait(false), Is.EqualTo(3));
            Assert.That(await CountAsync("service").ConfigureAwait(false), Is.EqualTo(3));
            AssertNoDeviceActions();
        }

        /// <summary>
        /// The MCP binder and UA encoding preserve each explicitly selected native Number width through the provider.
        /// </summary>
        [Test]
        public async Task AllNumericDeltaTypesReachTheProviderWithoutBoxingOrCoercion()
        {
            ArrayOf<BuiltInType> types = WmtpMcpFixture.NumericTypes();
            for (int index = 0; index < types.Count; index++)
            {
                BuiltInType type = types[index];
                JsonElement accepted = Success(await ConfigureMeasurementAsync(type, "delta",
                    new JsonObject { ["type"] = type.ToString(), ["value"] = 8 }).ConfigureAwait(false));
                Assert.That(accepted.GetProperty("accepted").GetBoolean(), Is.True);
                Assert.That(accepted.GetProperty("physicalEffectVerified").GetBoolean(), Is.False);
                Variant expected = WmtpMcpFixture.Scalar(type, 8);
                m_fixture.Measurement.Verify(provider => provider.ConfigureAsync(type.ToString(), 1000,
                    It.Is<Variant>(delta => delta.Equals(expected)), It.IsAny<CancellationToken>()), Times.Once);
                Assert.That(m_fixture.Sensor(type).Delta, Is.EqualTo(expected));
                Assert.That(m_fixture.Sensor(type).State.DeltaCondition!.Value, Is.EqualTo(8));
            }
        }

        /// <summary>
        /// Large integers remain exact on the wire and server validation, not MCP rounding, enforces Double storage.
        /// </summary>
        [Test]
        public async Task UInt64DeltaPrecisionAndTriggerDeltaInvariantAreEnforcedByTheServer()
        {
            Success(await ConfigureMeasurementAsync(BuiltInType.UInt64, "delta",
                new JsonObject { ["type"] = "UInt64", ["value"] = "9007199254740992" }).ConfigureAwait(false));
            m_fixture.Measurement.Verify(provider => provider.ConfigureAsync("UInt64", 1000,
                It.Is<Variant>(delta => delta.Equals(Variant.From(9007199254740992ul))),
                It.IsAny<CancellationToken>()), Times.Once);
            Error(await ConfigureMeasurementAsync(BuiltInType.UInt64, "delta",
                new JsonObject { ["type"] = "UInt64", ["value"] = "9007199254740993" }).ConfigureAwait(false),
                "BadOutOfRange");
            Assert.That(m_fixture.Sensor(BuiltInType.UInt64).Delta, Is.EqualTo(Variant.From(9007199254740992ul)));
            m_fixture.Measurement.Verify(provider => provider.ConfigureAsync("UInt64", It.IsAny<uint>(),
                It.IsAny<Variant>(), It.IsAny<CancellationToken>()), Times.Once);
            Error(await ConfigureMeasurementAsync(BuiltInType.Double, "trigger", JsonValue.Create(0))
                .ConfigureAwait(false), "BadOutOfRange");
            Success(await ConfigureMeasurementAsync(BuiltInType.Double, "delta",
                new JsonObject { ["type"] = "Double", ["value"] = 0.125 }).ConfigureAwait(false));
            Success(await ConfigureMeasurementAsync(BuiltInType.Double, "trigger", JsonValue.Create(0))
                .ConfigureAwait(false));
            Error(await ConfigureMeasurementAsync(BuiltInType.Double, "delta",
                new JsonObject { ["type"] = "Double", ["value"] = 0 }).ConfigureAwait(false), "BadOutOfRange");
            Assert.That(m_fixture.Sensor(BuiltInType.Double).TriggerMilliseconds, Is.Zero);
            Assert.That(m_fixture.Sensor(BuiltInType.Double).Delta, Is.EqualTo(Variant.From(0.125)));
            Success(await ConfigureMeasurementAsync(BuiltInType.Double, "trigger", JsonValue.Create(uint.MaxValue))
                .ConfigureAwait(false));
            Assert.That(m_fixture.Sensor(BuiltInType.Double).TriggerMilliseconds, Is.EqualTo(uint.MaxValue));
        }

        /// <summary>
        /// A measurement cannot be changed through another peripheral and wrong Number types remain UA errors.
        /// </summary>
        [Test]
        public async Task CrossPeripheralAndWrongTypedSettingsDoNotReachEquipment()
        {
            JsonObject request = Target();
            request["measurementNodeId"] = m_fixture.Other.Measurements[0].State.NodeId.ToString();
            request["setting"] = "trigger";
            request["value"] = 10;
            request["confirm"] = true;
            Error(await CallAsync("wmtp_configure_measurement", request).ConfigureAwait(false), "BadNodeIdUnknown");
            Error(await ConfigureMeasurementAsync(BuiltInType.UInt64, "delta",
                new JsonObject { ["type"] = "Double", ["value"] = 2 }).ConfigureAwait(false), "BadTypeMismatch");
            Assert.That(m_fixture.Other.Measurements[0].TriggerMilliseconds, Is.EqualTo(1000));
            Assert.That(m_fixture.Sensor(BuiltInType.UInt64).Delta, Is.EqualTo(Variant.From(0ul)));
            m_fixture.Measurement.Verify(provider => provider.ConfigureAsync(
                It.IsAny<string>(), It.IsAny<uint>(), It.IsAny<Variant>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Equipment refusal is distinct from malformed input and leaves both published settings unchanged.
        /// </summary>
        [Test]
        public async Task MeasurementProviderRefusalDoesNotPublishProposedSettings()
        {
            m_fixture.Measurement.Setup(provider => provider.ConfigureAsync(
                    It.IsAny<string>(), It.IsAny<uint>(), It.IsAny<Variant>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<StatusCode>(StatusCodes.BadOutOfService));
            Error(await ConfigureMeasurementAsync(BuiltInType.Double, "delta",
                new JsonObject { ["type"] = "Double", ["value"] = 2 }).ConfigureAwait(false), "BadOutOfService");
            WmtpMeasurement sensor = m_fixture.Sensor(BuiltInType.Double);
            Assert.That(sensor.TriggerMilliseconds, Is.EqualTo(1000));
            Assert.That(sensor.Delta, Is.EqualTo(Variant.From(0d)));
            Assert.That(sensor.State.DeltaCondition!.Value, Is.Zero);
            m_fixture.Measurement.Verify(provider => provider.ConfigureAsync("Double", 1000,
                It.Is<Variant>(value => value.Equals(Variant.From(2d))), It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Boolean calibration requests reach the provider and return acceptance without asserting physical success.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task CalibrationUsesTheRealProviderAndExplicitBoolean(bool enabled)
        {
            JsonObject request = Target();
            request["setting"] = "calibration";
            request["value"] = enabled;
            request["confirm"] = true;
            JsonElement response = Success(await CallAsync("wmtp_configure_peripheral", request).ConfigureAwait(false));
            Assert.That(response.GetProperty("accepted").GetBoolean(), Is.True);
            Assert.That(response.GetProperty("physicalEffectVerified").GetBoolean(), Is.False);
            Assert.That(m_fixture.Peripheral.Nodes.CalibrationMode!.Value, Is.EqualTo(enabled));
            m_fixture.Calibration.Verify(provider => provider.SetCalibrationAsync(enabled,
                It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// UTC, offset boundaries and daylight-saving semantics survive the actual TimeZoneDataType wire structure.
        /// </summary>
        [TestCase(-840, true)]
        [TestCase(0, false)]
        [TestCase(840, false)]
        public async Task DeviceTimeUsesTheActualTimeZoneStructure(int offset, bool daylightSaving)
        {
            JsonObject request = Target();
            request["setting"] = "time";
            request["value"] = new JsonObject
            {
                ["utcTime"] = "2026-09-01T00:00:02Z",
                ["timeZone"] = new JsonObject { ["offset"] = offset, ["daylightSavingInOffset"] = daylightSaving }
            };
            request["confirm"] = true;
            Success(await CallAsync("wmtp_configure_peripheral", request).ConfigureAwait(false));
            m_fixture.DeviceTime.Verify(provider => provider.SetTimeAsync(
                WmtpMcpFixture.SourceTime.Add(TimeSpan.FromSeconds(2)),
                It.Is<TimeZoneDataType>(zone => zone.Offset == offset &&
                    zone.DaylightSavingInOffset == daylightSaving), It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Provider refusal and server policy denial retain their own UA status without implying accepted changes.
        /// </summary>
        [Test]
        public async Task CalibrationAndTimeRefusalDoNotInventDeviceState()
        {
            m_fixture.Calibration.Setup(provider => provider.SetCalibrationAsync(
                    It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<StatusCode>(StatusCodes.BadOutOfService));
            JsonObject calibration = Target();
            calibration["setting"] = "calibration";
            calibration["value"] = true;
            calibration["confirm"] = true;
            Error(await CallAsync("wmtp_configure_peripheral", calibration).ConfigureAwait(false), "BadOutOfService");
            Assert.That(m_fixture.Peripheral.Nodes.CalibrationMode!.Value, Is.False);
            m_fixture.DeviceTime.Setup(provider => provider.SetTimeAsync(
                    It.IsAny<DateTimeUtc>(), It.IsAny<TimeZoneDataType>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<StatusCode>(StatusCodes.BadNotSupported));
            JsonObject time = Target();
            time["setting"] = "time";
            time["value"] = new JsonObject
            {
                ["utcTime"] = "2026-09-01T00:00:02Z",
                ["timeZone"] = new JsonObject { ["offset"] = 0, ["daylightSavingInOffset"] = false }
            };
            time["confirm"] = true;
            Error(await CallAsync("wmtp_configure_peripheral", time).ConfigureAwait(false), "BadNotSupported");
            m_fixture.DeviceTime.Verify(provider => provider.SetTimeAsync(It.IsAny<DateTimeUtc>(),
                It.IsAny<TimeZoneDataType>(), It.IsAny<CancellationToken>()), Times.Once);
            calibration["peripheralNodeId"] = m_fixture.Minimal.NodeId.ToString();
            Error(await CallAsync("wmtp_configure_peripheral", calibration).ConfigureAwait(false), "BadNotSupported");
            m_fixture.Calibration.Verify(provider => provider.SetCalibrationAsync(
                It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// No device or destructive action reaches authorization/providers without explicit MCP confirmation.
        /// </summary>
        [Test]
        public async Task UnconfirmedActionsHaveNoProviderOrRecordEffects()
        {
            m_fixture.Authorization.Invocations.Clear();
            JsonObject calibration = Target();
            calibration["setting"] = "calibration";
            calibration["value"] = true;
            Error(await CallAsync("wmtp_configure_peripheral", calibration).ConfigureAwait(false),
                "BadUserAccessDenied");
            Error(await ConfigureMeasurementAsync(BuiltInType.Double, "trigger", JsonValue.Create(10), confirm: false)
                .ConfigureAwait(false), "BadUserAccessDenied");
            Error(await CallAsync("wmtp_delete_records", Records("work", Selection("all"))).ConfigureAwait(false),
                "BadUserAccessDenied");
            JsonObject abort = Target();
            abort["dataset"] = "service";
            Error(await CallAsync("wmtp_abort_operation", abort).ConfigureAwait(false), "BadUserAccessDenied");
            JsonObject transfer = Target();
            transfer["kind"] = "firmware";
            transfer["payloadBase64"] = "AQID";
            Error(await CallAsync("wmtp_transfer", transfer).ConfigureAwait(false), "BadUserAccessDenied");
            m_fixture.Authorization.Verify(provider => provider.AuthorizeAsync(
                It.IsAny<WmtpRequest>(), It.IsAny<WmtpOperation>(), It.IsAny<CancellationToken>()), Times.Never);
            Assert.That(m_fixture.Peripheral.Nodes.CalibrationMode!.Value, Is.False);
            Assert.That(m_fixture.Sensor(BuiltInType.Double).TriggerMilliseconds, Is.EqualTo(1000));
            Assert.That(await m_fixture.Work.CountAsync(new WmtpRecordQuery()).ConfigureAwait(false), Is.EqualTo(3));
            AssertNoDeviceActions();
        }

        /// <summary>
        /// Real MCP native unions reject malformed shapes and bounds before any authorized equipment operation.
        /// </summary>
        [Test]
        public async Task McpBindingRejectsInvalidNativeShapesAndBoundsWithoutDeviceEffects()
        {
            m_fixture.Authorization.Invocations.Clear();
            JsonObject count = Records("work", Selection("all"));
            count["query"] = """{"selection":"all"}""";
            Error(await CallAsync("wmtp_count_records", count).ConfigureAwait(false), "BadInvalidArgument");
            JsonObject delete = Records("work", IndexQuery(1, 2));
            delete["confirm"] = true;
            delete["query"]!["extra"] = "never ignored";
            Error(await CallAsync("wmtp_delete_records", delete).ConfigureAwait(false), "BadInvalidArgument");
            Error(await ConfigureMeasurementAsync(BuiltInType.Double, "trigger", JsonValue.Create(0.5))
                .ConfigureAwait(false), "BadOutOfRange");
            Error(await ConfigureMeasurementAsync(BuiltInType.Double, "delta",
                new JsonObject { ["type"] = "Decimal", ["value"] = "0.1234567890123456789" }).ConfigureAwait(false),
                "BadOutOfRange");
            JsonObject calibration = Target();
            calibration["setting"] = "calibration";
            calibration["value"] = "true";
            calibration["confirm"] = true;
            Error(await CallAsync("wmtp_configure_peripheral", calibration).ConfigureAwait(false),
                "BadInvalidArgument");
            JsonObject transfer = Target();
            transfer["kind"] = "license";
            transfer["payloadBase64"] = "AQID";
            transfer["maximumBytes"] = 2;
            transfer["confirm"] = true;
            Error(await CallAsync("wmtp_transfer", transfer).ConfigureAwait(false), "BadOutOfRange");
            m_fixture.Authorization.Verify(provider => provider.AuthorizeAsync(
                It.IsAny<WmtpRequest>(), It.IsAny<WmtpOperation>(), It.IsAny<CancellationToken>()), Times.Never);
            Assert.That(await m_fixture.Work.CountAsync(new WmtpRecordQuery()).ConfigureAwait(false), Is.EqualTo(3));
            Assert.That(m_fixture.Sensor(BuiltInType.Double).TriggerMilliseconds, Is.EqualTo(1000));
            AssertNoDeviceActions();
        }

        /// <summary>
        /// Exact decoded bytes reach the provider and acceptance never claims installation or activation.
        /// </summary>
        [TestCase("firmware", WmtpTransferKind.Firmware)]
        [TestCase("license", WmtpTransferKind.License)]
        public async Task TransfersReportProviderAcceptanceNotInstallation(string kind, WmtpTransferKind expectedKind)
        {
            byte[]? received = null;
            ByteString retainedProviderBuffer = default;
            m_fixture.Transfers.Setup(provider => provider.TransferAsync(
                    It.IsAny<WmtpTransferKind>(), It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                .Callback((WmtpTransferKind _, ByteString payload, CancellationToken _) =>
                {
                    received = payload.ToArray();
                    retainedProviderBuffer = payload;
                })
                .Returns(new ValueTask<StatusCode>(StatusCodes.Good));
            JsonObject request = Target();
            request["kind"] = kind;
            request["payloadBase64"] = "AQIDBA==";
            request["maximumBytes"] = 4;
            request["confirm"] = true;
            JsonElement response = Success(await CallAsync("wmtp_transfer", request).ConfigureAwait(false));
            Assert.That(received, Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
            Assert.That(retainedProviderBuffer.ToArray(), Is.All.Zero);
            Assert.That(response.GetProperty("acceptedByProvider").GetBoolean(), Is.True);
            Assert.That(response.GetProperty("transferredBytes").GetInt32(), Is.EqualTo(4));
            Assert.That(response.GetProperty("installationVerified").GetBoolean(), Is.False);
            Assert.That(response.GetProperty("activationVerified").GetBoolean(), Is.False);
            Assert.That(response.GetRawText(), Does.Not.Contain("AQIDBA=="));
            Assert.That(m_fixture.Peripheral.Nodes.LicenseInformation!.Value,
                Is.EqualTo("Test entitlement description"));
            m_fixture.Transfers.Verify(provider => provider.TransferAsync(expectedKind,
                It.IsAny<ByteString>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Initial provider refusal and unexpected private diagnostics remain sanitized failures, never installation.
        /// </summary>
        [Test]
        public async Task TransferRefusalAndPrivateProviderFailuresRemainErrors()
        {
            JsonObject request = Target();
            request["kind"] = "firmware";
            request["payloadBase64"] = "AQID";
            request["confirm"] = true;
            Error(await CallAsync("wmtp_transfer", request).ConfigureAwait(false), "BadNotSupported");
            m_fixture.Transfers.Setup(provider => provider.TransferAsync(
                    It.IsAny<WmtpTransferKind>(), It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                .Throws(new InvalidOperationException("private-equipment-secret-AQID"));
            CallToolResult failed = await CallAsync("wmtp_transfer", request).ConfigureAwait(false);
            Error(failed, "BadUnexpectedError");
            Assert.That(WmtpMcpHarness.Json(failed).GetRawText(), Does.Not.Contain("private-equipment-secret"));
            Assert.That(WmtpMcpHarness.Json(failed).GetRawText(), Does.Not.Contain("AQID"));
            Assert.That(m_fixture.Peripheral.Nodes.LicenseInformation!.Value,
                Is.EqualTo("Test entitlement description"));
            m_fixture.Transfers.Verify(provider => provider.TransferAsync(WmtpTransferKind.Firmware,
                It.IsAny<ByteString>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        /// <summary>
        /// Real MCP session selection rejects ambiguity, honors named callers and leaves borrowed sessions connected.
        /// </summary>
        [Test]
        public async Task NamedSessionSelectionAndServerAuthorizationRemainIndependentFromConsent()
        {
            Error(await m_mcp.CallAsync("wmtp_read_peripheral", Target()).ConfigureAwait(false), "BadInvalidState");
            JsonObject unknown = Target();
            unknown["sessionName"] = "not-connected";
            Error(await m_mcp.CallAsync("wmtp_read_peripheral", unknown).ConfigureAwait(false), "BadInvalidState");
            m_fixture.Authorization.Setup(provider => provider.AuthorizeAsync(
                    It.IsAny<WmtpRequest>(), WmtpOperation.Calibrate, It.IsAny<CancellationToken>()))
                .Returns((WmtpRequest request, WmtpOperation _, CancellationToken _) =>
                    new ValueTask<StatusCode>(request.SessionId == m_fixture.Session.SessionId
                        ? StatusCodes.Good : StatusCodes.BadUserAccessDenied));
            JsonObject calibration = Target();
            calibration["setting"] = "calibration";
            calibration["value"] = true;
            calibration["confirm"] = true;
            calibration["sessionName"] = "second";
            Error(await CallAsync("wmtp_configure_peripheral", calibration).ConfigureAwait(false),
                "BadUserAccessDenied");
            Assert.That(m_fixture.Peripheral.Nodes.CalibrationMode!.Value, Is.False);
            m_fixture.Calibration.Verify(provider => provider.SetCalibrationAsync(
                It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
            calibration["sessionName"] = "wireless";
            Success(await CallAsync("wmtp_configure_peripheral", calibration).ConfigureAwait(false));
            Assert.That(m_fixture.Peripheral.Nodes.CalibrationMode.Value, Is.True);
            await using (WmtpMcpHarness single = await WmtpMcpHarness.CreateAsync(m_fixture.SecondSession)
                .ConfigureAwait(false))
            {
                JsonElement count = Success(await single.CallAsync("wmtp_count_records",
                    Records("service", Selection("all"))).ConfigureAwait(false));
                Assert.That(count.GetProperty("count").GetInt32(), Is.EqualTo(3));
            }
            Assert.That(m_fixture.Session.Connected, Is.True);
            Assert.That(m_fixture.SecondSession.Connected, Is.True);
        }

        /// <summary>
        /// Explicit deletion affects only the inclusive selection in one store and one peripheral.
        /// </summary>
        [TestCase("work", "index", 1)]
        [TestCase("service", "time", 2)]
        [TestCase("work", "all", 0)]
        [TestCase("service", "all", 0)]
        public async Task ExplicitDeletionKeepsOtherStoresAndInstancesIntact(
            string dataset, string selection, int expectedCount)
        {
            JsonObject query = selection switch
            {
                "index" => IndexQuery(1, 2),
                "time" => TimeQuery("2026-09-01T00:00:02Z", "2026-09-01T00:00:02Z"),
                _ => Selection("all")
            };
            JsonObject request = Records(dataset, query);
            request["confirm"] = true;
            Success(await CallAsync("wmtp_delete_records", request).ConfigureAwait(false));
            Assert.That(await CountAsync(dataset).ConfigureAwait(false), Is.EqualTo(expectedCount));
            Assert.That(await CountAsync(dataset == "work" ? "service" : "work").ConfigureAwait(false), Is.EqualTo(3));
            Assert.That(await m_fixture.OtherWork.CountAsync(new WmtpRecordQuery()).ConfigureAwait(false),
                Is.EqualTo(1));
            ArrayOf<WMTPOutputDataType> remaining = DecodeRecords(Success(
                await CallAsync("wmtp_report_records", Records(dataset, Selection("all"))).ConfigureAwait(false)));
            Assert.That(remaining.Count, Is.EqualTo(expectedCount));
            if (selection == "index")
            {
                Assert.That(remaining[0].Index, Is.EqualTo(3));
            }
            else if (selection == "time")
            {
                Assert.That(remaining[0].Index, Is.EqualTo(1));
                Assert.That(remaining[1].Index, Is.EqualTo(3));
            }
        }

        /// <summary>
        /// A released Boolean false deletion result is not converted to successful provider acceptance.
        /// </summary>
        [Test]
        public async Task DeleteAllBooleanRefusalIsAnErrorAndDoesNotConsumeRecords()
        {
            DeleteAllStoredRecordsMethodState method = m_fixture.Peripheral.State.WMTPWorkCycleData!
                .DeleteAllStoredRecords!;
            method.OnCallAsync = (_, _, _, _) => new ValueTask<DeleteAllStoredRecordsMethodStateResult>(
                new DeleteAllStoredRecordsMethodStateResult
                {
                    ServiceResult = ServiceResult.Good, DeleteAllStoredRecordsSuccessfull = false
                });
            JsonObject request = Records("work", Selection("all"));
            request["confirm"] = true;
            Error(await CallAsync("wmtp_delete_records", request).ConfigureAwait(false), "BadRequestNotAllowed");
            Assert.That(await CountAsync("work").ConfigureAwait(false), Is.EqualTo(3));
            m_fixture.WorkProvider.Verify(provider => provider.DeleteAsync(It.IsAny<WmtpRecordQuery>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Delivered MCP cancellation and local deadlines drain their handler without a wire abort or collateral work.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task LocalCancellationAndTimeoutNeverAbortUnrelatedOperations(bool timeout)
        {
            var foreignEntered = NewSignal();
            var ownEntered = NewSignal();
            var release = NewSignal();
            var foreignFinished = NewSignal();
            var ownFinished = NewSignal();
            CancellationToken foreignToken = default;
            m_fixture.WorkProvider.Setup(provider => provider.ReadAsync(
                    It.IsAny<WmtpRecordQuery>(), It.IsAny<CancellationToken>()))
                .Returns(async (WmtpRecordQuery query, CancellationToken token) =>
                {
                    foreignToken = token;
                    foreignEntered.TrySetResult(true);
                    try
                    {
                        await release.Task.WaitAsync(token).ConfigureAwait(false);
                        return await m_fixture.Work.ReadAsync(query, token).ConfigureAwait(false);
                    }
                    finally
                    {
                        foreignFinished.TrySetResult(true);
                    }
                });
            m_fixture.OtherWorkProvider.Setup(provider => provider.ReadAsync(
                    It.IsAny<WmtpRecordQuery>(), It.IsAny<CancellationToken>()))
                .Returns(async (WmtpRecordQuery query, CancellationToken token) =>
                {
                    ownEntered.TrySetResult(true);
                    try
                    {
                        await release.Task.WaitAsync(token).ConfigureAwait(false);
                        return await m_fixture.OtherWork.ReadAsync(query, token).ConfigureAwait(false);
                    }
                    finally
                    {
                        ownFinished.TrySetResult(true);
                    }
                });
            JsonObject foreignRequest = Records("work", Selection("all"));
            foreignRequest["timeoutSeconds"] = 60;
            Task<CallToolResult> foreign = CallAsync("wmtp_report_records", foreignRequest);
            Task<CallToolResult>? own = null;
            try
            {
                await foreignEntered.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                JsonObject ownRequest = Records("work", Selection("all"));
                ownRequest["peripheralNodeId"] = m_fixture.Other.NodeId.ToString();
                ownRequest["sessionName"] = "second";
                ownRequest["timeoutSeconds"] = timeout ? 1 : 60;
                own = CallAsync("wmtp_report_records", ownRequest);
                await ownEntered.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                RequestId id = m_mcp.LastRequestId;
                CancellationToken delivered = m_mcp.LastRequestToken;
                Task cleanup = m_mcp.LastRequestCompleted;
                if (timeout)
                {
                    Error(await own.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false), "BadTimeout");
                }
                else
                {
                    await m_mcp.CancelAsync(id).ConfigureAwait(false);
                    await Assert.CatchAsync<OperationCanceledException>(async () =>
                        await own.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false));
                    Assert.That(delivered.IsCancellationRequested, Is.True);
                }
                await cleanup.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                Assert.That(foreign.IsCompleted, Is.False);
                Assert.That(foreignToken.IsCancellationRequested, Is.False);
                Assert.That(m_fixture.Session.Connected, Is.True);
                Assert.That(m_fixture.SecondSession.Connected, Is.True);
                m_fixture.Authorization.Verify(provider => provider.AuthorizeAsync(
                    It.IsAny<WmtpRequest>(), WmtpOperation.Abort, It.IsAny<CancellationToken>()), Times.Never);
                release.TrySetResult(true);
                JsonElement completed = Success(
                    await foreign.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false));
                Assert.That(completed.GetProperty("count").GetInt32(), Is.EqualTo(3));
                Assert.That(await CountAsync("work").ConfigureAwait(false), Is.EqualTo(3));
            }
            finally
            {
                release.TrySetResult(true);
                await DrainAsync(foreign).ConfigureAwait(false);
                if (own != null)
                {
                    await DrainAsync(own).ConfigureAwait(false);
                    await ownFinished.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                }
                await foreignFinished.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Only the explicit abort tool sends the peripheral-wide method and cross-session permission remains enforced.
        /// </summary>
        [Test]
        public async Task ExplicitWireAbortHonorsOwnerPolicyAndCancelsOnlyTheSelectedPeripheral()
        {
            var entered = NewSignal();
            var release = NewSignal();
            CancellationToken activeToken = default;
            m_fixture.WorkProvider.Setup(provider => provider.ReadAsync(
                    It.IsAny<WmtpRecordQuery>(), It.IsAny<CancellationToken>()))
                .Returns(async (WmtpRecordQuery query, CancellationToken token) =>
                {
                    activeToken = token;
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(token).ConfigureAwait(false);
                    return await m_fixture.Work.ReadAsync(query, token).ConfigureAwait(false);
                });
            m_fixture.Authorization.Setup(provider => provider.AuthorizeAsync(
                    It.IsAny<WmtpRequest>(), WmtpOperation.Abort, It.IsAny<CancellationToken>()))
                .Returns((WmtpRequest request, WmtpOperation _, CancellationToken _) =>
                    new ValueTask<StatusCode>(request.SessionId == request.ActiveSessionId
                        ? StatusCodes.Good : StatusCodes.BadUserAccessDenied));
            JsonObject report = Records("work", Selection("all"));
            report["timeoutSeconds"] = 60;
            Task<CallToolResult> pending = CallAsync("wmtp_report_records", report);
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                JsonObject request = Target();
                request["dataset"] = "service";
                request["confirm"] = true;
                request["sessionName"] = "second";
                Error(await CallAsync("wmtp_abort_operation", request).ConfigureAwait(false), "BadUserAccessDenied");
                Assert.That(pending.IsCompleted, Is.False);
                Assert.That(activeToken.IsCancellationRequested, Is.False);
                request["sessionName"] = "wireless";
                JsonElement accepted = Success(await CallAsync("wmtp_abort_operation", request).ConfigureAwait(false));
                Assert.That(accepted.GetProperty("operation").GetString(), Is.EqualTo("peripheral-wide abort"));
                Assert.That(activeToken.IsCancellationRequested, Is.True);
                Error(await pending.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false),
                    "BadRequestCancelledByClient");
                Assert.That(await m_fixture.OtherWork.CountAsync(new WmtpRecordQuery()).ConfigureAwait(false),
                    Is.EqualTo(1));
                Assert.That(await CountAsync("service").ConfigureAwait(false), Is.EqualTo(3));
                Assert.That(m_fixture.Session.Connected, Is.True);
                Assert.That(m_fixture.SecondSession.Connected, Is.True);
                m_fixture.Authorization.Verify(provider => provider.AuthorizeAsync(
                    It.Is<WmtpRequest>(request => request.SessionId == m_fixture.SecondSession.SessionId),
                    WmtpOperation.Abort, It.IsAny<CancellationToken>()), Times.Once);
                m_fixture.Authorization.Verify(provider => provider.AuthorizeAsync(
                    It.Is<WmtpRequest>(request => request.SessionId == m_fixture.Session.SessionId),
                    WmtpOperation.Abort, It.IsAny<CancellationToken>()), Times.Once);
            }
            finally
            {
                release.TrySetResult(true);
                await DrainAsync(pending).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Decodes a stack-encoded DataValue using the real session's namespace table and factory.
        /// </summary>
        private DataValue Decode(JsonElement reading)
        {
            using var decoder = new JsonDecoder(reading.GetRawText(), m_fixture.Session.MessageContext);
            return decoder.ReadDataValue("DataValue");
        }

        /// <summary>
        /// Decodes the exact released output structures rather than asserting JSON field presence alone.
        /// </summary>
        private ArrayOf<WMTPOutputDataType> DecodeRecords(JsonElement response)
        {
            using var decoder = new JsonDecoder(response.GetRawText(), m_fixture.Session.MessageContext);
            return decoder.ReadEncodeableArray<WMTPOutputDataType>("records");
        }

        /// <summary>
        /// Reads a whole-dataset count through the real MCP tool.
        /// </summary>
        private async Task<uint> CountAsync(string dataset)
        {
            return Success(await CallAsync("wmtp_count_records", Records(dataset, Selection("all")))
                .ConfigureAwait(false)).GetProperty("count").GetUInt32();
        }

        /// <summary>
        /// Sends one generated measurement operation through actual MCP.
        /// </summary>
        private Task<CallToolResult> ConfigureMeasurementAsync(
            BuiltInType type, string setting, JsonNode? value, bool confirm = true)
        {
            JsonObject request = Target();
            request["measurementNodeId"] = m_fixture.Sensor(type).State.NodeId.ToString();
            request["setting"] = setting;
            request["value"] = value;
            request["confirm"] = confirm;
            return CallAsync("wmtp_configure_measurement", request);
        }

        /// <summary>
        /// Adds the explicit connected identity except when a test deliberately supplies a different one.
        /// </summary>
        private Task<CallToolResult> CallAsync(
            string name, JsonObject? arguments = null, CancellationToken cancellationToken = default)
        {
            arguments ??= new JsonObject();
            if (!arguments.ContainsKey("sessionName"))
            {
                arguments["sessionName"] = "wireless";
            }
            return m_mcp.CallAsync(name, arguments, cancellationToken);
        }

        /// <summary>
        /// Creates a target argument without embedding implicit namespace indices.
        /// </summary>
        private JsonObject Target(NodeId node = default)
        {
            return new JsonObject
            {
                ["peripheralNodeId"] = (node.IsNull ? m_fixture.Peripheral.NodeId : node).ToString()
            };
        }

        /// <summary>
        /// Creates an independent, reusable record-query argument object.
        /// </summary>
        private JsonObject Records(string dataset, JsonObject query)
        {
            JsonObject result = Target();
            result["dataset"] = dataset;
            result["query"] = query.DeepClone();
            return result;
        }

        /// <summary>
        /// Creates one exact no-range selection.
        /// </summary>
        private static JsonObject Selection(string selection)
        {
            return new JsonObject { ["selection"] = selection };
        }

        /// <summary>
        /// Creates an inclusive exact integral range.
        /// </summary>
        private static JsonObject IndexQuery(uint from, uint to)
        {
            return new JsonObject { ["selection"] = "index", ["fromIndex"] = from, ["toIndex"] = to };
        }

        /// <summary>
        /// Creates an inclusive acquisition-time range.
        /// </summary>
        private static JsonObject TimeQuery(string from, string to)
        {
            return new JsonObject { ["selection"] = "time", ["fromTime"] = from, ["toTime"] = to };
        }

        /// <summary>
        /// Requires actual MCP success before inspecting the structured result.
        /// </summary>
        private static JsonElement Success(CallToolResult result)
        {
            Assert.That(result.IsError, Is.Not.True,
                string.Join(string.Empty, result.Content.OfType<TextContentBlock>().Select(block => block.Text)));
            return WmtpMcpHarness.Json(result);
        }

        /// <summary>
        /// Requires exact UA error identity through the MCP boundary.
        /// </summary>
        private static void Error(CallToolResult result, string status)
        {
            WmtpMcpParameterTests.AssertError(result, status);
        }

        /// <summary>
        /// Creates a deterministic asynchronous handshake without arbitrary sleeps.
        /// </summary>
        private static TaskCompletionSource<bool> NewSignal()
        {
            return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        /// <summary>
        /// Joins cleanup even after a cancellation assertion fails, leaving no provider or MCP request running.
        /// </summary>
        private static async Task DrainAsync(Task<CallToolResult> request)
        {
            try
            {
                await request.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Cancellation is asserted by the caller; cleanup must still join the request.
            }
        }

        /// <summary>
        /// Proves passive and refused requests have no device provider or destructive-store effects.
        /// </summary>
        private void AssertNoDeviceActions()
        {
            m_fixture.Measurement.Verify(provider => provider.ConfigureAsync(It.IsAny<string>(), It.IsAny<uint>(),
                It.IsAny<Variant>(), It.IsAny<CancellationToken>()), Times.Never);
            m_fixture.Calibration.Verify(provider => provider.SetCalibrationAsync(
                It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
            m_fixture.DeviceTime.Verify(provider => provider.SetTimeAsync(It.IsAny<DateTimeUtc>(),
                It.IsAny<TimeZoneDataType>(), It.IsAny<CancellationToken>()), Times.Never);
            m_fixture.Transfers.Verify(provider => provider.TransferAsync(It.IsAny<WmtpTransferKind>(),
                It.IsAny<ByteString>(), It.IsAny<CancellationToken>()), Times.Never);
            m_fixture.WorkProvider.Verify(provider => provider.DeleteAsync(
                It.IsAny<WmtpRecordQuery>(), It.IsAny<CancellationToken>()), Times.Never);
            m_fixture.Authorization.Verify(provider => provider.AuthorizeAsync(
                It.IsAny<WmtpRequest>(), WmtpOperation.Abort, It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>The provider-backed real TCP equipment.</summary>
        private WmtpMcpFixture m_fixture = null!;

        /// <summary>The owned MCP client/server stream harness.</summary>
        private WmtpMcpHarness m_mcp = null!;
    }
}
#endif
