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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Moq;
using NUnit.Framework;
using Opc.Ua.AutoID.Server;

namespace Opc.Ua.AutoID.Tests
{
    /// <summary>
    /// Exercises the optional Diagnostics projection of the existing reader reports.
    /// </summary>
    [TestFixture]
    [Category("AutoID")]
    public sealed class AutoIdDiagnosticsTests
    {
        /// <summary>
        /// Starts a runtime with access events disabled independently of diagnostics.
        /// </summary>
        [OneTimeSetUp]
        public async Task SetUpAsync()
        {
            m_fixture = new AutoIdManagerFixture();
            await m_fixture.StartAsync(new AutoIdServerOptions { PublishAccessEvents = false }).ConfigureAwait(false);
        }

        /// <summary>
        /// Disposes the runtime and its readers.
        /// </summary>
        [OneTimeTearDown]
        public async Task TearDownAsync()
        {
            await m_fixture.DisposeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Preserves optional omission and publishes the declared types without initial measurements.
        /// </summary>
        [TestCase(AutoIdReaderKind.Rfid)]
        [TestCase(AutoIdReaderKind.Ocr)]
        [TestCase(AutoIdReaderKind.Optical)]
        [TestCase(AutoIdReaderKind.OpticalVerifier)]
        [TestCase(AutoIdReaderKind.Rtls)]
        public async Task DiagnosticsAreOptInReadOnlyAndUseStandardQualifiedMetadataAsync(AutoIdReaderKind kind)
        {
            await using AutoIdReader omitted = await CreateAsync(kind, false).ConfigureAwait(false);
            Assert.That(omitted.Device.Diagnostics, Is.Null);
            await using AutoIdReader reader = await CreateAsync(kind).ConfigureAwait(false);
            BaseObjectState diagnostics = reader.Device.Diagnostics!;
            var groups = new List<BaseInstanceState>();
            diagnostics.GetChildren(m_fixture.Context, groups);
            Assert.That(groups.Select(child => child.BrowseName.Name),
                Is.EquivalentTo(new[] { "Presence", "Logbook", "LastAccess" }));
            AssertVariable(Variable(reader, "Presence"), Opc.Ua.DataTypeIds.UInt16,
                StatusCodes.UncertainInitialValue);
            AssertVariable(Variable(reader, "Logbook", "LogColumns"), Opc.Ua.DataTypeIds.String, StatusCodes.Good);
            AssertVariable(Variable(reader, "Logbook", "LastLogEntry"), Opc.Ua.DataTypeIds.String,
                StatusCodes.UncertainInitialValue);
            AssertVariable(Variable(reader, "LastAccess", "Client"), Opc.Ua.DataTypeIds.String,
                StatusCodes.UncertainInitialValue);
            AssertVariable(Variable(reader, "LastAccess", "Command"), Opc.Ua.DataTypeIds.String,
                StatusCodes.UncertainInitialValue);
            AssertVariable(Variable(reader, "LastAccess", "Identifier"), Opc.Ua.DataTypeIds.BaseDataType,
                StatusCodes.UncertainInitialValue);
            AssertVariable(Variable(reader, "LastAccess", "Timestamp"), Opc.Ua.DataTypeIds.UtcTime,
                StatusCodes.UncertainInitialValue);
            Assert.That(Variable(reader, "Logbook", "LogColumns").Value, Is.EqualTo(Variant.From("Message")));
            var fields = new List<BaseInstanceState>();
            Group(reader, "LastAccess").GetChildren(m_fixture.Context, fields);
            Assert.That(fields.Count, Is.EqualTo(kind == AutoIdReaderKind.Rfid ? 10 : 4));
            if (kind == AutoIdReaderKind.Rfid)
            {
                AssertVariable(Variable(reader, "LastAccess", "RWData"), Opc.Ua.DataTypeIds.BaseDataType,
                    StatusCodes.UncertainInitialValue);
                foreach (string field in new[] { "Antenna", "CurrentPowerLevel", "Strength" })
                {
                    AssertVariable(Variable(reader, "LastAccess", field), Opc.Ua.DataTypeIds.Int32,
                        StatusCodes.UncertainInitialValue);
                }
                AssertVariable(Variable(reader, "LastAccess", "PC"), Opc.Ua.DataTypeIds.UInt16,
                    StatusCodes.UncertainInitialValue);
                AssertVariable(Variable(reader, "LastAccess", "Polarization"), Opc.Ua.DataTypeIds.String,
                    StatusCodes.UncertainInitialValue);
            }
        }

        /// <summary>
        /// Uses the same report to update the value and emit one event, without a second event source.
        /// </summary>
        [Test]
        public async Task PresenceAndLogReportsUpdateValuesAndRetainSingleEventsAsync()
        {
            await using AutoIdReader reader = await CreateAsync().ConfigureAwait(false);
            var events = new EventCapture(reader.Device);
            await reader.ReportPresenceAsync(3).ConfigureAwait(false);
            await reader.ReportLogEntryAsync("Reader ready").ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(Variable(reader, "Presence").Value, Is.EqualTo(Variant.From((ushort)3)));
                Assert.That(Variable(reader, "Presence").StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(Variable(reader, "Presence").Timestamp.IsNull, Is.False);
                Assert.That(Variable(reader, "Logbook", "LastLogEntry").Value,
                    Is.EqualTo(Variant.From("Reader ready")));
                Assert.That(events.Of<AutoIdPresenceEventState>().Single().Presence!.Value, Is.EqualTo(3));
                Assert.That(events.Of<AutoIdLogEntryEventState>().Single().Message!.Value.Text,
                    Is.EqualTo("Reader ready"));
                Assert.That(events.Events.Count, Is.EqualTo(2));
            });
            await reader.ReportPresenceAsync(0).ConfigureAwait(false);
            Assert.That(Variable(reader, "Presence").Value, Is.EqualTo(Variant.From((ushort)0)));
        }

        /// <summary>
        /// Preserves RFID values, uses one acquisition timestamp and clears every omitted field.
        /// </summary>
        [Test]
        public async Task AccessProjectionIsIndependentOfEventsAndClearsAbsentRfidFieldsAsync()
        {
            await using AutoIdReader reader = await CreateAsync().ConfigureAwait(false);
            var events = new EventCapture(reader.Device);
            RfidAccessResult result = Access();
            result.EncodingMask |= (uint)(RfidAccessResultFields.RWData | RfidAccessResultFields.Antenna |
                RfidAccessResultFields.CurrentPowerLevel | RfidAccessResultFields.PC |
                RfidAccessResultFields.Polarization | RfidAccessResultFields.Strength);
            result.RWData = new ScanData { SwitchField = ScanDataFields.String, String = "Product" };
            result.Antenna = 4;
            result.CurrentPowerLevel = 19;
            result.PC = 23;
            result.Polarization = "Linear";
            result.Strength = -40;
            await reader.ReportAccessAsync("ReadTag", "Controller", [result]).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(Variable(reader, "LastAccess", "Client").Value, Is.EqualTo(Variant.From("Controller")));
                Assert.That(Variable(reader, "LastAccess", "Command").Value, Is.EqualTo(Variant.From("ReadTag")));
                Assert.That(Variable(reader, "LastAccess", "Identifier").Value, Is.EqualTo(Variant.From("Tag")));
                Assert.That(Variable(reader, "LastAccess", "Timestamp").Value,
                    Is.EqualTo(Variant.From(result.Timestamp)));
                Assert.That(Variable(reader, "LastAccess", "RWData").Value, Is.EqualTo(Variant.From("Product")));
                Assert.That(Variable(reader, "LastAccess", "Antenna").Value, Is.EqualTo(Variant.From(4)));
                Assert.That(Variable(reader, "LastAccess", "CurrentPowerLevel").Value, Is.EqualTo(Variant.From(19)));
                Assert.That(Variable(reader, "LastAccess", "PC").Value, Is.EqualTo(Variant.From((ushort)23)));
                Assert.That(Variable(reader, "LastAccess", "Polarization").Value, Is.EqualTo(Variant.From("Linear")));
                Assert.That(Variable(reader, "LastAccess", "Strength").Value, Is.EqualTo(Variant.From(-40)));
                Assert.That(events.Events, Is.Empty);
            });
            AssertAcquisition(reader, result.Timestamp);
            RfidAccessResult sparse = Access();
            sparse.EncodingMask = 0;
            await reader.ReportAccessAsync("Scan", "Device", [sparse]).ConfigureAwait(false);
            foreach (string name in new[]
            {
                "Identifier", "Timestamp", "RWData", "Antenna", "CurrentPowerLevel", "PC", "Polarization", "Strength"
            })
            {
                BaseVariableState variable = Variable(reader, "LastAccess", name);
                Assert.Multiple(() =>
                {
                    Assert.That(variable.StatusCode, Is.EqualTo(StatusCodes.BadNoData), name);
                    Assert.That(variable.Value.IsNull, Is.True, name);
                });
            }
            AssertAcquisition(reader, Variable(reader, "LastAccess", "Command").Timestamp);
        }

        /// <summary>
        /// Normalizes present null and empty polarization strings without treating an absent field as available.
        /// </summary>
        [TestCase(null)]
        [TestCase("")]
        public async Task PolarizationPresencePreservesQualityForNullAndEmptyStringsAsync(string? polarization)
        {
            await using AutoIdReader reader = await CreateAsync().ConfigureAwait(false);
            RfidAccessResult report = Access();
            report.EncodingMask |= (uint)RfidAccessResultFields.Polarization;
            report.Polarization = polarization;
            await reader.ReportAccessAsync("Scan", "Controller", [report]).ConfigureAwait(false);
            BaseVariableState variable = Variable(reader, "LastAccess", "Polarization");
            Assert.Multiple(() =>
            {
                Assert.That(variable.Value, Is.EqualTo(Variant.From(string.Empty)));
                Assert.That(variable.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(variable.Timestamp, Is.EqualTo(report.Timestamp));
            });
            report.EncodingMask &= ~(uint)RfidAccessResultFields.Polarization;
            await reader.ReportAccessAsync("Scan", "Controller", [report]).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(variable.Value.IsNull, Is.True);
                Assert.That(variable.StatusCode, Is.EqualTo(StatusCodes.BadNoData));
                Assert.That(variable.Timestamp, Is.EqualTo(report.Timestamp));
            });
        }

        /// <summary>
        /// Retains independent identifier and RWData storage after the provider reuses its buffers.
        /// </summary>
        [TestCase("Identifier", ScanDataFields.ByteString)]
        [TestCase("RWData", ScanDataFields.ByteString)]
        [TestCase("Identifier", ScanDataFields.Epc)]
        [TestCase("RWData", ScanDataFields.Epc)]
        [TestCase("Identifier", ScanDataFields.Custom)]
        [TestCase("RWData", ScanDataFields.Custom)]
        public async Task AccessDiagnosticsSnapshotProviderBuffersAsync(string field, ScanDataFields arm)
        {
            await using AutoIdReader reader = await CreateAsync().ConfigureAwait(false);
            byte[] bytes = [0x12, 0x34, 0x56];
            var epc = new ScanDataEpc { PC = 7, UId = ByteString.From(bytes), XPC_W1 = 8, XPC_W2 = 9 };
            var data = new ScanData { SwitchField = arm };
            switch (arm)
            {
                case ScanDataFields.ByteString:
                    data.ByteString = ByteString.From(bytes);
                    break;
                case ScanDataFields.Epc:
                    data.Epc = epc;
                    break;
                case ScanDataFields.Custom:
                    data.Custom = Variant.From(ByteString.From(bytes));
                    break;
            }
            RfidAccessResult report = Access();
            report.EncodingMask |= (uint)RfidAccessResultFields.RWData;
            if (field == "Identifier")
            {
                report.Identifier = data;
            }
            else
            {
                report.RWData = data;
            }
            await reader.ReportAccessAsync("ReadTag", "Controller", [report]).ConfigureAwait(false);
            BaseVariableState variable = Variable(reader, "LastAccess", field);
            DateTimeUtc timestamp = variable.Timestamp;
            bytes[0] = 0xFF;
            epc.PC = 77;
            epc.XPC_W1 = 88;
            epc.XPC_W2 = 99;
            Variant expected = arm == ScanDataFields.Epc
                ? Variant.FromStructure(new ScanDataEpc
                {
                    PC = 7, UId = ByteString.From(0x12, 0x34, 0x56), XPC_W1 = 8, XPC_W2 = 9
                })
                : Variant.From(ByteString.From(0x12, 0x34, 0x56));
            Assert.Multiple(() =>
            {
                Assert.That(variable.Value, Is.EqualTo(expected));
                Assert.That(variable.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(variable.Timestamp, Is.EqualTo(timestamp));
                Assert.That(variable.Timestamp, Is.EqualTo(report.Timestamp));
            });
            AttributeReadResult read = await variable.OnReadValueAsync!(
                m_fixture.Context, variable, default, default, default).ConfigureAwait(false);
            Assert.That(read.Value, Is.EqualTo(expected));
            Assert.That(read.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(read.SourceTimestamp, Is.EqualTo(timestamp));
            AssertAcquisition(reader, report.Timestamp);
        }

        /// <summary>
        /// Copies nested custom Variant arrays instead of retaining their containers or ByteString values.
        /// </summary>
        [TestCase("Identifier")]
        [TestCase("RWData")]
        public async Task AccessDiagnosticsSnapshotNestedCustomValuesAsync(string field)
        {
            await using AutoIdReader reader = await CreateAsync().ConfigureAwait(false);
            byte[] bytes = [1, 2, 3];
            Variant[] values = [Variant.From(ByteString.From(bytes)), Variant.From(42)];
            var data = new ScanData
            {
                SwitchField = ScanDataFields.Custom,
                Custom = Variant.From(values.ToArrayOf())
            };
            RfidAccessResult report = Access();
            report.EncodingMask |= (uint)RfidAccessResultFields.RWData;
            if (field == "Identifier")
            {
                report.Identifier = data;
            }
            else
            {
                report.RWData = data;
            }
            await reader.ReportAccessAsync("ReadTag", "Controller", [report]).ConfigureAwait(false);
            BaseVariableState variable = Variable(reader, "LastAccess", field);
            DateTimeUtc timestamp = variable.Timestamp;
            bytes[0] = 9;
            values[1] = Variant.From(99);
            Assert.Multiple(() =>
            {
                Assert.That(variable.Value.TryGetValue(out ArrayOf<Variant> stored), Is.True);
                Assert.That(stored.Count, Is.EqualTo(2));
                Assert.That(stored[0], Is.EqualTo(Variant.From(ByteString.From(1, 2, 3))));
                Assert.That(stored[1], Is.EqualTo(Variant.From(42)));
                Assert.That(variable.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(variable.Timestamp, Is.EqualTo(timestamp));
                Assert.That(variable.Timestamp, Is.EqualTo(report.Timestamp));
            });
        }

        /// <summary>
        /// Preserves null versus empty byte strings and their existing diagnostic availability policy.
        /// </summary>
        [TestCase("Identifier", false)]
        [TestCase("Identifier", true)]
        [TestCase("RWData", false)]
        [TestCase("RWData", true)]
        public async Task AccessDiagnosticsPreserveNullAndEmptyByteStringsAsync(string field, bool empty)
        {
            await using AutoIdReader reader = await CreateAsync().ConfigureAwait(false);
            var data = new ScanData
            {
                SwitchField = ScanDataFields.ByteString,
                ByteString = empty ? ByteString.Empty : default
            };
            RfidAccessResult report = Access();
            report.EncodingMask |= (uint)RfidAccessResultFields.RWData;
            if (field == "Identifier")
            {
                report.Identifier = data;
            }
            else
            {
                report.RWData = data;
            }
            await reader.ReportAccessAsync("ReadTag", "Controller", [report]).ConfigureAwait(false);
            BaseVariableState variable = Variable(reader, "LastAccess", field);
            Assert.That(variable.Value, Is.EqualTo(Variant.From(empty ? ByteString.Empty : default)));
            Assert.That(variable.Value.TryGetValue(out ByteString stored), Is.True);
            Assert.That(stored.IsNull, Is.EqualTo(!empty));
            Assert.That(variable.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(variable.Timestamp, Is.EqualTo(report.Timestamp));
        }

        /// <summary>
        /// Preserves the existing typed-null EPC representation without dereferencing a missing structure.
        /// </summary>
        [TestCase("Identifier")]
        [TestCase("RWData")]
        public async Task AccessDiagnosticsPreserveNullEpcValuesAsync(string field)
        {
            await using AutoIdReader reader = await CreateAsync().ConfigureAwait(false);
            var data = new ScanData { SwitchField = ScanDataFields.Epc, Epc = null! };
            RfidAccessResult report = Access();
            report.EncodingMask |= (uint)RfidAccessResultFields.RWData;
            if (field == "Identifier")
            {
                report.Identifier = data;
            }
            else
            {
                report.RWData = data;
            }
            await reader.ReportAccessAsync("ReadTag", "Controller", [report]).ConfigureAwait(false);
            BaseVariableState variable = Variable(reader, "LastAccess", field);
            Assert.That(variable.Value, Is.EqualTo(Variant.From(ExtensionObject.Null)));
            Assert.That(variable.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(variable.Timestamp, Is.EqualTo(report.Timestamp));
        }

        /// <summary>
        /// Rejects a custom value exceeding codec limits before replacing any LastAccess field.
        /// </summary>
        [TestCase("Identifier")]
        [TestCase("RWData")]
        public async Task CustomSnapshotFailurePreservesPreviousDiagnosticsAsync(string field)
        {
            await using AutoIdReader reader = await CreateAsync().ConfigureAwait(false);
            await reader.ReportAccessAsync("Previous", "Controller", [Access()]).ConfigureAwait(false);
            var children = new List<BaseInstanceState>();
            Group(reader, "LastAccess").GetChildren(m_fixture.Context, children);
            var previous = children.Cast<BaseVariableState>()
                .Select(variable => (Variable: variable, variable.Value, variable.StatusCode, variable.Timestamp))
                .ToArray();
            Variant nested = Variant.From(42);
            int limit = m_fixture.Context.AsMessageContext().MaxEncodingNestingLevels;
            for (int index = 0; index < limit + 2; index++)
            {
                nested = Variant.From(new[] { nested }.ToArrayOf());
            }
            var data = new ScanData { SwitchField = ScanDataFields.Custom, Custom = nested };
            RfidAccessResult report = Access();
            report.EncodingMask |= (uint)RfidAccessResultFields.RWData;
            if (field == "Identifier")
            {
                report.Identifier = data;
            }
            else
            {
                report.RWData = data;
            }
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await reader.ReportAccessAsync("Replacement", "Other controller", [report]).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            foreach (var snapshot in previous)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(snapshot.Variable.Value, Is.EqualTo(snapshot.Value));
                    Assert.That(snapshot.Variable.StatusCode, Is.EqualTo(snapshot.StatusCode));
                    Assert.That(snapshot.Variable.Timestamp, Is.EqualTo(snapshot.Timestamp));
                });
            }
        }

        /// <summary>
        /// Projects the first report result consistently with LastScanData, without fabricating presence.
        /// </summary>
        [Test]
        public async Task ScanReportsShareAccessProjectionAndDoNotInventPresenceAsync()
        {
            await using AutoIdReader reader = await CreateAsync().ConfigureAwait(false);
            RfidScanResult first = AutoIdTestData.RfidResult(AutoIdTestData.Bytes(1));
            first.Sighting = [new RfidSighting { Antenna = 2, Strength = -45 }];
            RfidScanResult second = AutoIdTestData.RfidResult(AutoIdTestData.Bytes(2));
            await reader.ReportScanAsync([first, second]).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(Variable(reader, "LastAccess", "Identifier").Value,
                    Is.EqualTo(reader.Device.LastScanData!.Value));
                Assert.That(Variable(reader, "LastAccess", "Timestamp").Value,
                    Is.EqualTo(Variant.From(first.Timestamp)));
                Assert.That(Variable(reader, "LastAccess", "Antenna").Value, Is.EqualTo(Variant.From(2)));
                Assert.That(Variable(reader, "LastAccess", "Strength").Value, Is.EqualTo(Variant.From(-45)));
                Assert.That(Variable(reader, "Presence").StatusCode, Is.EqualTo(StatusCodes.UncertainInitialValue));
            });
            AssertAcquisition(reader, first.Timestamp);
        }

        /// <summary>
        /// Projects base access results on non-RFID devices without adding RFID fields.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task NonRfidAccessReportsKeepTheirBaseFieldsAsync(bool rfidResult)
        {
            await using AutoIdReader reader = await CreateAsync(AutoIdReaderKind.Optical).ConfigureAwait(false);
            AccessResult result = rfidResult ? Access() : new AccessResult
            {
                EncodingMask = (uint)(AccessResultFields.Identifier | AccessResultFields.Timestamp),
                Identifier = new ScanData { SwitchField = ScanDataFields.String, String = "Tag" },
                Timestamp = DateTimeUtc.Now
            };
            await reader.ReportAccessAsync("Scan", "Optical client", [result]).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(Variable(reader, "LastAccess", "Client").Value,
                    Is.EqualTo(Variant.From("Optical client")));
                Assert.That(Variable(reader, "LastAccess", "Identifier").Value, Is.EqualTo(Variant.From("Tag")));
                Assert.That(Variable(reader, "LastAccess", "Identifier").StatusCode, Is.EqualTo(StatusCodes.Good));
            });
            AssertAcquisition(reader, result.Timestamp);
        }

        /// <summary>
        /// Rejects invalid and cancelled reports before mutating diagnostics, and rejects reports after disposal.
        /// </summary>
        [Test]
        public async Task InvalidCancelledAndDisposedReportsDoNotReplacePreviousDiagnosticsAsync()
        {
            await using AutoIdReader reader = await CreateAsync().ConfigureAwait(false);
            await reader.ReportPresenceAsync(2).ConfigureAwait(false);
            await reader.ReportLogEntryAsync("Previous").ConfigureAwait(false);
            await reader.ReportAccessAsync("ReadTag", "Previous", [Access()]).ConfigureAwait(false);
            DateTimeUtc timestamp = Variable(reader, "LastAccess", "Client").Timestamp;
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            BaseVariableState presence = Variable(reader, "Presence");
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await presence.OnReadValueAsync!(m_fixture.Context, presence, default, default, cancelled.Token)
                    .ConfigureAwait(false));
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await reader.ReportPresenceAsync(4, cancelled.Token).ConfigureAwait(false));
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await reader.ReportLogEntryAsync("Cancelled", cancelled.Token).ConfigureAwait(false));
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await reader.ReportAccessAsync("WriteTag", "Cancelled", [Access()], cancelled.Token)
                    .ConfigureAwait(false));
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await reader.ReportLogEntryAsync(string.Empty).ConfigureAwait(false));
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await reader.ReportAccessAsync("Scan", "Invalid", []).ConfigureAwait(false));
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await reader.ReportAccessAsync(string.Empty, "Invalid", [Access()]).ConfigureAwait(false));
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await reader.ReportAccessAsync("Scan", "Invalid", [new AccessResult()]).ConfigureAwait(false));
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await reader.ReportAccessAsync("Scan", "Invalid", [Access(), new AccessResult()])
                    .ConfigureAwait(false));
            Assert.Multiple(() =>
            {
                Assert.That(Variable(reader, "Presence").Value, Is.EqualTo(Variant.From((ushort)2)));
                Assert.That(Variable(reader, "Logbook", "LastLogEntry").Value, Is.EqualTo(Variant.From("Previous")));
                Assert.That(Variable(reader, "LastAccess", "Client").Value, Is.EqualTo(Variant.From("Previous")));
                Assert.That(Variable(reader, "LastAccess", "Client").Timestamp, Is.EqualTo(timestamp));
            });
            await reader.DisposeAsync().ConfigureAwait(false);
            await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await reader.ReportPresenceAsync(9).ConfigureAwait(false));
            await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await reader.ReportLogEntryAsync("Disposed").ConfigureAwait(false));
            await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await reader.ReportAccessAsync("Scan", "Disposed", [Access()]).ConfigureAwait(false));
            Assert.That(Variable(reader, "LastAccess", "Client").Timestamp, Is.EqualTo(timestamp));
        }

        /// <summary>
        /// Rejects unregistered decoded values at any nesting depth without replacing the previous acquisition.
        /// </summary>
        [Test]
        public async Task CustomDecodedStructuresRequireSemanticRoundTripAsync(
            [Values("Identifier", "RWData")] string field,
            [Values("Scalar", "VariantArray", "Structure")] string nesting,
            [Values] bool registered)
        {
            await using AutoIdReader reader = await CreateAsync().ConfigureAwait(false);
            await reader.ReportAccessAsync("Previous", "Controller", [Access()]).ConfigureAwait(false);
            var children = new List<BaseInstanceState>();
            Group(reader, "LastAccess").GetChildren(m_fixture.Context, children);
            var previous = children.Cast<BaseVariableState>()
                .Select(variable => (Variable: variable, variable.Value, variable.StatusCode, variable.Timestamp))
                .ToArray();
            string uri = "urn:opcfoundation:autoid:diagnostic:" + Interlocked.Increment(ref m_sequence);
            m_fixture.Context.NamespaceUris.GetIndexOrAppend(uri);
            byte[] bytes = [1, 2, 3];
            var payload = new DiagnosticPayload(uri) { PC = 7, UId = ByteString.From(bytes) };
            if (registered)
            {
                var activator = new Mock<IEncodeableType>();
                activator.SetupGet(value => value.Type).Returns(typeof(DiagnosticPayload));
                activator.SetupGet(value => value.XmlName).Returns(new XmlQualifiedName("Payload", uri));
                activator.Setup(value => value.CreateInstance()).Returns(() => new DiagnosticPayload(uri));
                m_fixture.Context.EncodeableFactory.Builder
                    .AddEncodeableType(payload.TypeId, activator.Object)
                    .AddEncodeableType(payload.BinaryEncodingId, activator.Object).Commit();
            }
            Assert.That(m_fixture.Context.EncodeableFactory.TryGetEncodeableType(
                payload.BinaryEncodingId, out _), Is.EqualTo(registered));
            Variant custom = NestDiagnosticValue(Variant.FromStructure(payload), nesting);
            RfidAccessResult report = Access();
            report.EncodingMask |= (uint)RfidAccessResultFields.RWData;
            var data = new ScanData { SwitchField = ScanDataFields.Custom, Custom = custom };
            if (field == "Identifier")
            {
                report.Identifier = data;
            }
            else
            {
                report.RWData = data;
            }
            if (!registered)
            {
                ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await reader.ReportAccessAsync("Replacement", "Other", [report]).ConfigureAwait(false));
                Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingError));
                foreach (var snapshot in previous)
                {
                    Assert.Multiple(() =>
                    {
                        Assert.That(snapshot.Variable.Value, Is.EqualTo(snapshot.Value));
                        Assert.That(snapshot.Variable.StatusCode, Is.EqualTo(snapshot.StatusCode));
                        Assert.That(snapshot.Variable.Timestamp, Is.EqualTo(snapshot.Timestamp));
                    });
                }
                return;
            }
            await reader.ReportAccessAsync("Replacement", "Other", [report]).ConfigureAwait(false);
            bytes[0] = 9;
            payload.PC = 99;
            Variant expected = NestDiagnosticValue(Variant.FromStructure(
                new DiagnosticPayload(uri) { PC = 7, UId = ByteString.From(1, 2, 3) }), nesting);
            BaseVariableState stored = Variable(reader, "LastAccess", field);
            Assert.That(DiagnosticLeaf(stored.Value, nesting).TryGetValue(out ExtensionObject encoded), Is.True);
            Assert.That(encoded.TryGetValue(out DiagnosticPayload? retained), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(retained, Is.Not.SameAs(payload));
                Assert.That(retained!.BinaryEncodingId, Is.EqualTo(payload.BinaryEncodingId));
                Assert.That(stored.Value, Is.EqualTo(expected));
                Assert.That(stored.Value.Equals(custom), Is.False);
                Assert.That(stored.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(stored.Timestamp, Is.EqualTo(report.Timestamp));
            });
        }

        /// <summary>
        /// Copies caller-supplied opaque binary values without requiring an activator for their encoding id.
        /// </summary>
        [Test]
        public async Task CustomOpaqueStructuresRemainValidAndIndependentAsync(
            [Values("Identifier", "RWData")] string field,
            [Values("Scalar", "VariantArray", "Structure")] string nesting)
        {
            await using AutoIdReader reader = await CreateAsync().ConfigureAwait(false);
            string uri = "urn:opcfoundation:autoid:opaque:" + Interlocked.Increment(ref m_sequence);
            m_fixture.Context.NamespaceUris.GetIndexOrAppend(uri);
            var encodingId = new ExpandedNodeId(2u, uri);
            byte[] bytes = [1, 2, 3];
            Variant custom = NestDiagnosticValue(Variant.From(
                new ExtensionObject(encodingId, ByteString.From(bytes))), nesting);
            Assert.That(m_fixture.Context.EncodeableFactory.TryGetEncodeableType(encodingId, out _), Is.False);
            var data = new ScanData { SwitchField = ScanDataFields.Custom, Custom = custom };
            RfidAccessResult report = Access();
            report.EncodingMask |= (uint)RfidAccessResultFields.RWData;
            if (field == "Identifier")
            {
                report.Identifier = data;
            }
            else
            {
                report.RWData = data;
            }
            await reader.ReportAccessAsync("Replacement", "Controller", [report]).ConfigureAwait(false);
            bytes[0] = 9;
            BaseVariableState stored = Variable(reader, "LastAccess", field);
            Variant expected = NestDiagnosticValue(Variant.From(
                new ExtensionObject(encodingId, ByteString.From(1, 2, 3))), nesting);
            Assert.That(DiagnosticLeaf(stored.Value, nesting).TryGetValue(out ExtensionObject encoded), Is.True);
            Assert.That(encoded.TryGetAsBinary(out ByteString retained), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(encoded.TypeId, Is.EqualTo(encodingId));
                Assert.That(retained, Is.EqualTo(ByteString.From(1, 2, 3)));
                Assert.That(stored.Value, Is.EqualTo(expected));
                Assert.That(stored.Value.Equals(custom), Is.False);
                Assert.That(stored.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(stored.Timestamp, Is.EqualTo(report.Timestamp));
            });
        }

        /// <summary>
        /// Diagnostic copy rejection does not suppress scan publication or change a completed physical command.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ScanDiagnosticCopyRejectionPreservesEventsAndOutcomeAsync(bool command)
        {
            await using var fixture = new AutoIdManagerFixture();
            await fixture.StartAsync(new AutoIdServerOptions { PublishAccessEvents = true }).ConfigureAwait(false);
            var provider = new FakeProvider();
            await using AutoIdReader reader = await fixture.Runtime.Reader(fixture.Name("CopyRejection"), provider)
                .WithDiagnostics().BuildAsync().ConfigureAwait(false);
            await reader.ReportAccessAsync("Previous", "Controller", [Access()]).ConfigureAwait(false);
            var children = new List<BaseInstanceState>();
            reader.Device.Diagnostics!.FindChildWithQualifiedName(fixture.Context,
                new QualifiedName("LastAccess",
                    fixture.Context.NamespaceUris.GetIndexOrAppend(AutoIdModel.NamespaceUri)))!
                .GetChildren(fixture.Context, children);
            var previous = children.Cast<BaseVariableState>()
                .Select(variable => (Variable: variable, variable.Value, variable.StatusCode, variable.Timestamp))
                .ToArray();
            const string uri = "urn:opcfoundation:autoid:scan-copy-rejection";
            fixture.Context.NamespaceUris.GetIndexOrAppend(uri);
            var payload = new DiagnosticPayload(uri) { PC = 7, UId = ByteString.From(1, 2, 3) };
            Assert.That(fixture.Context.EncodeableFactory.TryGetEncodeableType(
                payload.BinaryEncodingId, out _), Is.False);
            RfidScanResult scan = AutoIdTestData.RfidResult(AutoIdTestData.Bytes(1));
            scan.ScanData = new ScanData
            {
                SwitchField = ScanDataFields.Custom, Custom = Variant.FromStructure(payload)
            };
            var events = new EventCapture(reader.Device);
            if (command)
            {
                provider.OnScan = (settings, ct) => new ValueTask<AutoIdScanOutcome>(
                    new AutoIdScanOutcome(new ScanResult[] { scan }.ToArrayOf(),
                        AutoIdOperationStatusEnumeration.SUCCESS));
                var device = (RfidReaderDeviceState)reader.Device;
                ScanRfidReaderDeviceTypeMethodStateResult result = await device.Scan!.OnCallAsync!(
                    fixture.Context, device.Scan, device.NodeId,
                    AutoIdTestData.Settings(duration: 100), CancellationToken.None).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
                    Assert.That(result.Status, Is.EqualTo(AutoIdOperationStatusEnumeration.SUCCESS));
                    Assert.That(result.ResultsOut.Count, Is.EqualTo(1));
                    Assert.That(result.ResultsOut[0], Is.SameAs(scan));
                    Assert.That(device.DeviceStatus!.Value, Is.EqualTo(DeviceStatusEnumeration.Idle));
                });
            }
            else
            {
                await reader.ReportScanAsync([scan]).ConfigureAwait(false);
            }
            Assert.Multiple(() =>
            {
                Assert.That(reader.Device.LastScanData!.Value, Is.EqualTo(scan.ScanData.Custom));
                Assert.That(reader.Device.LastScanData.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(reader.Device.LastScanTimestamp!.Value, Is.EqualTo(scan.Timestamp));
                Assert.That(events.Of<RfidScanEventState>().Count(), Is.EqualTo(command ? 0 : 1));
                Assert.That(events.Of<RfidAccessEventState>().Count(), Is.EqualTo(1));
                Assert.That(events.Events.Count, Is.EqualTo(command ? 1 : 2));
            });
            RfidAccessResult access = events.Of<RfidAccessEventState>().Single().AccessResult!.Value[0];
            Assert.That(access.Identifier.Custom, Is.EqualTo(scan.ScanData.Custom));
            foreach (var snapshot in previous)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(snapshot.Variable.Value, Is.EqualTo(snapshot.Value));
                    Assert.That(snapshot.Variable.StatusCode, Is.EqualTo(snapshot.StatusCode));
                    Assert.That(snapshot.Variable.Timestamp, Is.EqualTo(snapshot.Timestamp));
                });
            }
            payload.PC = 99;
            foreach (var snapshot in previous)
            {
                Assert.That(snapshot.Variable.Value, Is.EqualTo(snapshot.Value));
            }
        }

        /// <summary>
        /// Notification failures after publication are not treated as diagnostic snapshot rejection.
        /// </summary>
        [TestCase(StatusCodes.BadEncodingError)]
        [TestCase(StatusCodes.BadDecodingError)]
        [TestCase(StatusCodes.BadEncodingLimitsExceeded)]
        public async Task ScanNotificationFailureIsNotSuppressedAsCopyRejectionAsync(uint status)
        {
            await using var fixture = new AutoIdManagerFixture();
            await fixture.StartAsync(new AutoIdServerOptions { PublishAccessEvents = true }).ConfigureAwait(false);
            await using AutoIdReader reader = await fixture.Runtime.Reader(fixture.Name("NotificationFailure"),
                new FakeProvider()).WithDiagnostics().BuildAsync().ConfigureAwait(false);
            ushort ns = fixture.Context.NamespaceUris.GetIndexOrAppend(AutoIdModel.NamespaceUri);
            NodeState access = reader.Device.Diagnostics!.FindChildWithQualifiedName(
                fixture.Context, new QualifiedName("LastAccess", ns))!;
            var identifier = (BaseVariableState)access.FindChildWithQualifiedName(
                fixture.Context, new QualifiedName("Identifier", ns))!;
            var failure = new ServiceResultException(status, "Notification failed after the cache update.");
            int notifications = 0;
            identifier.OnStateChanged += (_, _, _) =>
            {
                notifications++;
                throw failure;
            };
            RfidScanResult scan = AutoIdTestData.RfidResult(AutoIdTestData.Bytes(1, 2));
            var events = new EventCapture(reader.Device);
            try
            {
                ServiceResultException? actual = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await reader.ReportScanAsync([scan]).ConfigureAwait(false));
                Assert.Multiple(() =>
                {
                    Assert.That(actual, Is.SameAs(failure));
                    Assert.That(notifications, Is.EqualTo(1));
                    Assert.That(identifier.Value, Is.EqualTo(Variant.FromStructure(scan.ScanData.Epc)));
                    Assert.That(identifier.StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(identifier.Timestamp, Is.EqualTo(scan.Timestamp));
                    Assert.That(events.Of<RfidScanEventState>().Count(), Is.EqualTo(1));
                    Assert.That(events.Of<RfidAccessEventState>().Count(), Is.Zero);
                });
            }
            finally
            {
                identifier.OnStateChanged = null;
            }
        }

        /// <summary>
        /// Places the value directly, in a Variant array, or inside a registered generated structure.
        /// </summary>
        private static Variant NestDiagnosticValue(Variant value, string nesting)
        {
            return nesting switch
            {
                "Scalar" => value,
                "VariantArray" => Variant.From(new[] { value }.ToArrayOf()),
                "Structure" => Variant.FromStructure(
                    new ScanData { SwitchField = ScanDataFields.Custom, Custom = value }),
                _ => throw new ArgumentOutOfRangeException(nameof(nesting))
            };
        }

        /// <summary>
        /// Selects the leaf without asking the factory to decode an opaque ExtensionObject.
        /// </summary>
        private static Variant DiagnosticLeaf(Variant value, string nesting)
        {
            if (nesting == "VariantArray")
            {
                Assert.That(value.TryGetValue(out ArrayOf<Variant> items), Is.True);
                return items[0];
            }
            if (nesting == "Structure")
            {
                Assert.That(value.TryGetValue(out ExtensionObject encoded), Is.True);
                Assert.That(encoded.TryGetValue(out ScanData? data), Is.True);
                return data!.Custom;
            }
            return value;
        }

        /// <summary>
        /// Creates a reader without transferring ownership of the shared test runtime.
        /// </summary>
        private async Task<AutoIdReader> CreateAsync(
            AutoIdReaderKind kind = AutoIdReaderKind.Rfid, bool diagnostics = true)
        {
            var builder = m_fixture.Runtime.Reader(
                m_fixture.Name("Diagnostics" + Interlocked.Increment(ref m_sequence)),
                new FakeProvider { Kind = kind });
            if (diagnostics)
            {
                builder.WithDiagnostics();
            }
            return await builder.BuildAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Gets a qualified standard variable without relying on child enumeration order.
        /// </summary>
        private BaseVariableState Variable(AutoIdReader reader, params string[] path)
        {
            NodeState node = reader.Device.Diagnostics!;
            foreach (string name in path)
            {
                ushort ns = m_fixture.Context.NamespaceUris.GetIndexOrAppend(AutoIdModel.NamespaceUri);
                node = node.FindChildWithQualifiedName(m_fixture.Context,
                    new QualifiedName(name, ns))!;
            }
            return (BaseVariableState)node;
        }

        /// <summary>
        /// Gets the selected diagnostics group.
        /// </summary>
        private BaseObjectState Group(AutoIdReader reader, string name)
        {
            return (BaseObjectState)reader.Device.Diagnostics!.FindChildWithQualifiedName(m_fixture.Context,
                new QualifiedName(name, m_fixture.Context.NamespaceUris.GetIndexOrAppend(AutoIdModel.NamespaceUri)))!;
        }

        /// <summary>
        /// Verifies the complete LastAccess group uses one source timestamp.
        /// </summary>
        private void AssertAcquisition(AutoIdReader reader, DateTimeUtc timestamp)
        {
            var children = new List<BaseInstanceState>();
            Group(reader, "LastAccess").GetChildren(m_fixture.Context, children);
            foreach (BaseVariableState variable in children.Cast<BaseVariableState>())
            {
                Assert.That(variable.Timestamp, Is.EqualTo(timestamp), variable.BrowseName.Name);
            }
        }

        /// <summary>
        /// Verifies the standard data type, reference, scalar rank and read-only access.
        /// </summary>
        private void AssertVariable(BaseVariableState variable, NodeId dataType, StatusCode status)
        {
            Assert.Multiple(() =>
            {
                Assert.That(variable.BrowseName.NamespaceIndex,
                    Is.EqualTo(m_fixture.Context.NamespaceUris.GetIndexOrAppend(AutoIdModel.NamespaceUri)));
                Assert.That(variable.ReferenceTypeId, Is.EqualTo(Opc.Ua.Types.ReferenceTypeIds.HasComponent));
                Assert.That(variable.DataType, Is.EqualTo(dataType));
                Assert.That(variable.ValueRank, Is.EqualTo(ValueRanks.Scalar));
                Assert.That(variable.AccessLevel, Is.EqualTo(AccessLevels.CurrentRead));
                Assert.That(variable.UserAccessLevel, Is.EqualTo(AccessLevels.CurrentRead));
                Assert.That(variable.StatusCode, Is.EqualTo(status));
                Assert.That(variable.OnReadValueAsync, Is.Not.Null);
            });
        }

        /// <summary>
        /// Creates a complete base access report using the inherited RFID field mask.
        /// </summary>
        private static RfidAccessResult Access()
        {
            return new RfidAccessResult
            {
                EncodingMask = (uint)(RfidAccessResultFields.Identifier | RfidAccessResultFields.Timestamp),
                Identifier = new ScanData { SwitchField = ScanDataFields.String, String = "Tag" },
                Timestamp = DateTimeUtc.Now
            };
        }

        /// <summary>The runtime fixture.</summary>
        private AutoIdManagerFixture m_fixture = null!;

        /// <summary>The unique reader-name suffix.</summary>
        private int m_sequence;

        /// <summary>
        /// Carries an otherwise ordinary EPC value under a test-specific, initially unregistered encoding id.
        /// </summary>
        private sealed class DiagnosticPayload(string namespaceUri) : ScanDataEpc
        {
            /// <inheritdoc/>
            public override ExpandedNodeId TypeId => new(1u, namespaceUri);

            /// <inheritdoc/>
            public override ExpandedNodeId BinaryEncodingId => new(2u, namespaceUri);

            /// <inheritdoc/>
            public override ExpandedNodeId XmlEncodingId => new(3u, namespaceUri);
        }
    }
}
