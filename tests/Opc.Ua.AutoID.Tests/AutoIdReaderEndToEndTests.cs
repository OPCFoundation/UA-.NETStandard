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
using NUnit.Framework;
using Opc.Ua.AutoID.Client;
using Opc.Ua.AutoID.Server;
using Opc.Ua.Client;
using Opc.Ua.Tests;

namespace Opc.Ua.AutoID.Tests
{
    /// <summary>
    /// Drives the hosted OPC 30010 server with the AutoID client over a real session.
    /// </summary>
    [TestFixture]
    [Category("AutoID")]
    [NonParallelizable]
    public sealed class AutoIdReaderEndToEndTests
    {
        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_host = await AutoIdHostFixture.StartAsync().ConfigureAwait(false);
            m_session = await m_host.ConnectAsync("AutoIdE2e").ConfigureAwait(false);
            m_client = new AutoIdClient(m_session, NUnitTelemetryContext.Create());
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            if (m_session != null)
            {
                await m_session.DisposeAsync().ConfigureAwait(false);
            }
            if (m_host != null)
            {
                await m_host.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public void ConfiguratorsRunInRegistrationOrderAfterTheAddressSpaceIsReady()
        {
            Assert.That(m_host!.SynchronousConfiguratorSawReaders, Is.EqualTo(5));
        }

        [Test]
        public void ConfigureAutoIdForTheStandaloneManagerReceivesItsRuntime()
        {
            Assert.That(m_host!.BuildContextSawTheStandaloneRuntime, Is.True);
        }

        [Test]
        public async Task EnumerationFindsOneReaderOfEachKindAsync()
        {
            var found = new List<AutoIdReaderEntry>();
            await foreach (AutoIdReaderEntry entry in m_client!.EnumerateReadersAsync())
            {
                found.Add(entry);
            }

            Assert.That(m_client.IsSupported, Is.True);
            Assert.That(
                found.ToDictionary(entry => entry.BrowseName.Name!, entry => entry.Kind),
                Does.Contain(new KeyValuePair<string, AutoIdReaderKind>(AutoIdHostFixture.RfidName, AutoIdReaderKind.Rfid))
                    .And.Contain(new KeyValuePair<string, AutoIdReaderKind>(AutoIdHostFixture.OcrName, AutoIdReaderKind.Ocr))
                    .And.Contain(new KeyValuePair<string, AutoIdReaderKind>(AutoIdHostFixture.OpticalName, AutoIdReaderKind.Optical))
                    .And.Contain(new KeyValuePair<string, AutoIdReaderKind>(
                        AutoIdHostFixture.VerifierName, AutoIdReaderKind.OpticalVerifier))
                    .And.Contain(new KeyValuePair<string, AutoIdReaderKind>(AutoIdHostFixture.RtlsName, AutoIdReaderKind.Rtls)));
        }

        [Test]
        public async Task TheDeviceSnapshotMatchesWhatTheServerPublishesAsync()
        {
            AutoIdReaderEntry reader = await ReaderAsync(AutoIdHostFixture.RfidName).ConfigureAwait(false);

            AutoIdDeviceSnapshot snapshot = await m_client!.ReadDeviceAsync(reader).ConfigureAwait(false);
            AutoIdDeviceSnapshot statusOnly = await m_client.ReadDeviceAsync(reader, AutoIdReadOptions.StatusOnly)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(snapshot.NodeId, Is.EqualTo(reader.NodeId));
                Assert.That(snapshot.DeviceName, Is.EqualTo(AutoIdHostFixture.RfidName));
                Assert.That(snapshot.DeviceStatus, Is.EqualTo(DeviceStatusEnumeration.Idle));
                Assert.That(snapshot.ModelVersion, Is.EqualTo("1.00"));
                Assert.That(snapshot.DeviceInfo, Is.EqualTo("Dock door reader"));
                Assert.That(snapshot.DeviceLocationName, Is.EqualTo("Gate 1"));
                Assert.That(snapshot.DeviceLocation!.Name, Is.EqualTo("Dock 1"));
                Assert.That(snapshot.AntennaNames[0].AntennaName, Is.EqualTo("North"));
                Assert.That(snapshot.Manufacturer.Text, Is.EqualTo("Acme"));
                Assert.That(snapshot.Model.Text, Is.EqualTo("R-100"));
                Assert.That(snapshot.SerialNumber, Is.EqualTo("SN-1"));
                Assert.That(statusOnly.DeviceName, Is.EqualTo(AutoIdHostFixture.RfidName));
                Assert.That(statusOnly.DeviceInfo, Is.Empty);
                Assert.That(statusOnly.Manufacturer.Text, Is.Null.Or.Empty);
                Assert.That(statusOnly.LastScanData.IsNull, Is.True);
                Assert.That(statusOnly.LastScanStatus, Is.EqualTo(StatusCodes.BadNotFound));
            });
        }

        [Test]
        public async Task TheDeviceSnapshotNamesTheVariablesToMonitorAndThePublishedMethodsAsync()
        {
            AutoIdReaderEntry reader = await ReaderAsync(AutoIdHostFixture.RfidName).ConfigureAwait(false);
            AutoIdReaderEntry rtls = await ReaderAsync(AutoIdHostFixture.RtlsName).ConfigureAwait(false);
            AutoIdDeviceState device = m_host!.RfidReader.Device;

            AutoIdDeviceSnapshot snapshot = await m_client!.ReadDeviceAsync(reader).ConfigureAwait(false);
            AutoIdDeviceSnapshot statusOnly = await m_client.ReadDeviceAsync(reader, AutoIdReadOptions.StatusOnly)
                .ConfigureAwait(false);
            AutoIdDeviceSnapshot locating = await m_client.ReadDeviceAsync(rtls, AutoIdReadOptions.StatusOnly)
                .ConfigureAwait(false);
            DataValue status = await m_session!.ReadValueAsync(snapshot.DeviceStatusId).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(snapshot.DeviceStatusId, Is.EqualTo(device.DeviceStatus!.NodeId));
                Assert.That(snapshot.LastScanDataId, Is.EqualTo(device.LastScanData!.NodeId));
                Assert.That(snapshot.LastScanTimestampId, Is.EqualTo(device.LastScanTimestamp!.NodeId));
                Assert.That(snapshot.DeviceLocationId, Is.EqualTo(device.DeviceLocation!.NodeId));
                Assert.That(
                    statusOnly.DeviceStatusId,
                    Is.EqualTo(snapshot.DeviceStatusId),
                    "the ids come from the browse");
                Assert.That(statusOnly.LastScanDataId, Is.EqualTo(snapshot.LastScanDataId));
                Assert.That(status.WrappedValue.TryGetValue(out DeviceStatusEnumeration value), Is.True);
                Assert.That(value, Is.EqualTo(snapshot.DeviceStatus));
                Assert.That(
                    snapshot.Methods.ToArray()!.Select(method => method.Name),
                    Is.SupersetOf(new[]
                    {
                        BrowseNames.Scan, BrowseNames.ScanStart, BrowseNames.ScanStop, BrowseNames.ReadTag,
                        BrowseNames.WriteTag, BrowseNames.WriteTagID, BrowseNames.KillTag, BrowseNames.LockTag,
                        BrowseNames.SetTagPassword
                    }));
                Assert.That(snapshot.HasMethod(BrowseNames.ReadTag), Is.True);
                Assert.That(snapshot.HasMethod(BrowseNames.GetLocation), Is.False);
                Assert.That(statusOnly.Methods, Is.EqualTo(snapshot.Methods));
                Assert.That(locating.HasMethod(BrowseNames.GetLocation), Is.True);
                Assert.That(locating.HasMethod(BrowseNames.GetSupportedLocationTypes), Is.True);
                Assert.That(locating.HasMethod(BrowseNames.ReadTag), Is.False);
                Assert.That(locating.DeviceLocationId.IsNull, Is.True, "the locating system has no device location");
            });
        }

        [Test]
        public async Task AReaderWithoutAScanHasAnUncertainLastScanAndNoOptionalPropertiesAsync()
        {
            AutoIdReader fresh = await m_host!.Runtime.CreateReaderAsync(
                new QualifiedName("FreshOcr", m_host.Manager.InstanceNamespaceIndex),
                new InMemoryScanReaderProvider(AutoIdReaderKind.Ocr)).ConfigureAwait(false);
            AutoIdReaderEntry entry = await m_client!.GetReaderAsync(fresh.NodeId).ConfigureAwait(false);

            AutoIdDeviceSnapshot snapshot = await m_client.ReadDeviceAsync(entry).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(entry.Kind, Is.EqualTo(AutoIdReaderKind.Ocr));
                Assert.That(entry.BrowseName.Name, Is.EqualTo("FreshOcr"));
                Assert.That(snapshot.LastScanStatus, Is.EqualTo(StatusCodes.UncertainInitialValue));
                Assert.That(snapshot.DeviceLocation, Is.Null);
                Assert.That(snapshot.AntennaNames.Count, Is.Zero);
                Assert.That(snapshot.DeviceInfo, Is.Empty);
            });
        }

        [Test]
        public async Task ASynchronousScanReturnsTheTagsInRangeAndUpdatesTheLastScanAsync()
        {
            AutoIdReaderEntry reader = await ReaderAsync(AutoIdHostFixture.RfidName).ConfigureAwait(false);
            ByteString epc = AutoIdTestData.Bytes(0xE2, 0x01, 0x01, 0x01);
            await m_host!.Rfid.PlaceTagAsync(new InMemoryRfidTag(epc) { Antenna = 1, Strength = -50 }).ConfigureAwait(false);
            try
            {
                AutoIdScanResponse response = await m_client!.ScanAsync(
                    reader, AutoIdTestData.Settings(duration: 500, dataAvailable: true)).ConfigureAwait(false);
                AutoIdDeviceSnapshot snapshot = await m_client.ReadDeviceAsync(reader).ConfigureAwait(false);

                var result = (RfidScanResult)response.Results.ToArray()!.Single(
                    r => r.ScanData.Epc.UId == epc);
                Assert.Multiple(() =>
                {
                    Assert.That(response.Status, Is.EqualTo(AutoIdOperationStatusEnumeration.SUCCESS));
                    Assert.That(result.CodeType, Is.EqualTo("EPC"));
                    Assert.That(result.Sighting[0].Strength, Is.EqualTo(-50));
                    Assert.That(snapshot.LastScanStatus, Is.EqualTo(StatusCodes.Good));
                    Assert.That(snapshot.LastScanData.IsNull, Is.False);
                    Assert.That(snapshot.LastScanTimestamp.IsNull, Is.False);
                });
            }
            finally
            {
                await m_host.Rfid.RemoveTagAsync(epc).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task AScanWithoutTagsReportsNoIdentifierAsync()
        {
            AutoIdReaderEntry reader = await ReaderAsync(AutoIdHostFixture.OcrName).ConfigureAwait(false);

            AutoIdScanResponse response = await m_client!.ScanAsync(
                reader, AutoIdTestData.Settings(cycles: 1)).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(response.Status, Is.EqualTo(AutoIdOperationStatusEnumeration.NO_IDENTIFIER));
                Assert.That(response.Results.Count, Is.Zero);
            });
        }

        [Test]
        public async Task InvalidSettingsAreRejectedOnTheWireAsync()
        {
            AutoIdReaderEntry reader = await ReaderAsync(AutoIdHostFixture.RfidName).ConfigureAwait(false);

            ServiceResultException infinite = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await m_client!.ScanAsync(reader, AutoIdTestData.Settings()).ConfigureAwait(false)))!;
            ServiceResultException negative = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await m_client!.ScanAsync(reader, AutoIdTestData.Settings(duration: -1))
                    .ConfigureAwait(false)))!;
            ServiceResultException tooLong = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await m_client!.ScanAsync(reader, AutoIdTestData.Settings(duration: 6000))
                    .ConfigureAwait(false)))!;

            Assert.Multiple(() =>
            {
                Assert.That(infinite.StatusCode, Is.EqualTo((uint)StatusCodes.BadInvalidArgument));
                Assert.That(negative.StatusCode, Is.EqualTo((uint)StatusCodes.BadInvalidArgument));
                Assert.That(tooLong.StatusCode, Is.EqualTo((uint)StatusCodes.BadInvalidArgument));
            });
        }

        [Test]
        public async Task ASecondScanWhileOneIsActiveFailsWithBadInvalidStateAsync()
        {
            AutoIdReaderEntry reader = await ReaderAsync(AutoIdHostFixture.OcrName).ConfigureAwait(false);
            Task<AutoIdScanResponse> first = m_client!.ScanAsync(
                reader, AutoIdTestData.Settings(duration: 1500)).AsTask();
            await WaitForStatusAsync(AutoIdHostFixture.OcrName, DeviceStatusEnumeration.Busy).ConfigureAwait(false);

            ServiceResultException second = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await m_client.ScanAsync(reader, AutoIdTestData.Settings(duration: 100))
                    .ConfigureAwait(false)))!;
            ServiceResultException start = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await m_client.ScanStartAsync(reader, AutoIdTestData.Settings()).ConfigureAwait(false)))!;
            await first.ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(second.StatusCode, Is.EqualTo((uint)StatusCodes.BadInvalidState));
                Assert.That(start.StatusCode, Is.EqualTo((uint)StatusCodes.BadInvalidState));
            });
            await WaitForStatusAsync(AutoIdHostFixture.OcrName, DeviceStatusEnumeration.Idle).ConfigureAwait(false);
        }

        [Test]
        public async Task ScanStartDeliversScanEventsUntilScanStopAsync()
        {
            AutoIdReaderEntry reader = await ReaderAsync(AutoIdHostFixture.RfidName).ConfigureAwait(false);
            ByteString epc = AutoIdTestData.Bytes(0xE2, 0x02, 0x02, 0x02);
            await m_host!.Rfid.PlaceTagAsync(new InMemoryRfidTag(epc)).ConfigureAwait(false);
            try
            {
                AutoIdOperationStatusEnumeration started = await m_client!.ScanStartAsync(
                    reader, AutoIdTestData.Settings()).ConfigureAwait(false);
                await WaitForStatusAsync(AutoIdHostFixture.RfidName, DeviceStatusEnumeration.Scanning).ConfigureAwait(false);

                AutoIdScanEventTypeRecord record = await FirstEventAsync(
                    ct => m_client.ObserveScanEventsAsync(m_session!.DefaultStreaming, reader, ct),
                    () => Task.CompletedTask,
                    r => r.ScanResult?.OfType<RfidScanResult>().Any(x => x.ScanData.Epc.UId == epc) == true)
                    .ConfigureAwait(false);
                await m_client.ScanStopAsync(reader).ConfigureAwait(false);
                ServiceResultException stopAgain = (await Assert.ThrowsAsync<ServiceResultException>(
                    async () => await m_client.ScanStopAsync(reader).ConfigureAwait(false)))!;

                Assert.Multiple(() =>
                {
                    Assert.That(started, Is.EqualTo(AutoIdOperationStatusEnumeration.SUCCESS));
                    Assert.That(record.DeviceName, Is.EqualTo(AutoIdHostFixture.RfidName));
                    Assert.That(record.SourceNode, Is.EqualTo(reader.NodeId));
                    Assert.That(record.ScanResult![0], Is.TypeOf<RfidScanResult>());
                    Assert.That(stopAgain.StatusCode, Is.EqualTo((uint)StatusCodes.BadInvalidState));
                });
                await WaitForStatusAsync(AutoIdHostFixture.RfidName, DeviceStatusEnumeration.Idle).ConfigureAwait(false);
            }
            finally
            {
                await m_host.Rfid.RemoveTagAsync(epc).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task AScanThatEndsOnItsOwnLeavesTheReaderIdleAsync()
        {
            AutoIdReaderEntry reader = await ReaderAsync(AutoIdHostFixture.OpticalName).ConfigureAwait(false);
            m_host!.Optical.Present(new OpticalScanResult
            {
                CodeType = "RAW:STRING",
                ScanData = new ScanData { SwitchField = ScanDataFields.String, String = "4012345678901" },
                Timestamp = DateTimeUtc.Now
            });
            try
            {
                AutoIdOperationStatusEnumeration started = await m_client!.ScanStartAsync(
                    reader, AutoIdTestData.Settings(dataAvailable: true)).ConfigureAwait(false);
                await WaitForStatusAsync(AutoIdHostFixture.OpticalName, DeviceStatusEnumeration.Idle).ConfigureAwait(false);

                AutoIdOperationStatusEnumeration again = await m_client.ScanStartAsync(
                    reader, AutoIdTestData.Settings(cycles: 1)).ConfigureAwait(false);
                await WaitForStatusAsync(AutoIdHostFixture.OpticalName, DeviceStatusEnumeration.Idle).ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(started, Is.EqualTo(AutoIdOperationStatusEnumeration.SUCCESS));
                    Assert.That(again, Is.EqualTo(AutoIdOperationStatusEnumeration.SUCCESS));
                });
            }
            finally
            {
                m_host.Optical.Clear();
            }
        }

        [Test]
        public async Task ClosingTheSessionThatStartedAScanStopsItAsync()
        {
            AutoIdReaderEntry reader = await ReaderAsync(AutoIdHostFixture.OcrName).ConfigureAwait(false);
            await using (ManagedSession other = await m_host!.ConnectAsync("AutoIdOwner").ConfigureAwait(false))
            {
                var ownerClient = new AutoIdClient(other, NUnitTelemetryContext.Create());
                AutoIdOperationStatusEnumeration started = await ownerClient.ScanStartAsync(
                    reader, AutoIdTestData.Settings()).ConfigureAwait(false);
                await WaitForStatusAsync(AutoIdHostFixture.OcrName, DeviceStatusEnumeration.Scanning).ConfigureAwait(false);
                Assert.That(started, Is.EqualTo(AutoIdOperationStatusEnumeration.SUCCESS));
            }

            await WaitForStatusAsync(AutoIdHostFixture.OcrName, DeviceStatusEnumeration.Idle).ConfigureAwait(false);
        }

        [Test]
        public async Task TagCommandsReturnTheStatusesOfTheTagAsync()
        {
            AutoIdReaderEntry reader = await ReaderAsync(AutoIdHostFixture.RfidName).ConfigureAwait(false);
            ByteString epc = AutoIdTestData.Bytes(0xE2, 0x03, 0x03, 0x03);
            ByteString access = AutoIdTestData.Bytes(1, 2, 3, 4);
            ByteString kill = AutoIdTestData.Bytes("\t\t\t\t"u8.ToArray());
            await m_host!.Rfid.PlaceTagAsync(new InMemoryRfidTag(epc)
            {
                AccessPassword = access,
                KillPassword = kill,
                UserMemorySize = 16
            }).ConfigureAwait(false);
            ScanData id = AutoIdTestData.Epc(epc);

            AutoIdTagReadResponse readEpc = await m_client!.ReadTagAsync(reader, id, "EPC", 1, 4, 4).ConfigureAwait(false);
            AutoIdOperationStatusEnumeration written = await m_client.WriteTagAsync(
                reader, id, "EPC", 3, 0, AutoIdTestData.Bytes(0xCA, 0xFE)).ConfigureAwait(false);
            AutoIdTagReadResponse readUser = await m_client.ReadTagAsync(reader, id, "EPC", 3, 0, 2).ConfigureAwait(false);
            AutoIdOperationStatusEnumeration lockWrong = await m_client.LockTagAsync(
                reader, id, "EPC", AutoIdTestData.Bytes(0, 0, 0, 1), RfidLockRegionEnumeration.User,
                RfidLockOperationEnumeration.Lock).ConfigureAwait(false);
            AutoIdOperationStatusEnumeration locked = await m_client.LockTagAsync(
                reader, id, "EPC", access, RfidLockRegionEnumeration.User, RfidLockOperationEnumeration.Lock)
                .ConfigureAwait(false);
            AutoIdOperationStatusEnumeration blocked = await m_client.WriteTagAsync(
                reader, id, "EPC", 3, 0, AutoIdTestData.Bytes(1)).ConfigureAwait(false);
            AutoIdOperationStatusEnumeration passwordChanged = await m_client.SetTagPasswordAsync(
                reader, id, "EPC", RfidPasswordTypeEnumeration.Access, access, AutoIdTestData.Bytes(5, 6, 7, 8))
                .ConfigureAwait(false);
            AutoIdOperationStatusEnumeration oldPassword = await m_client.WriteTagAsync(
                reader, id, "EPC", 3, 0, AutoIdTestData.Bytes(1), access).ConfigureAwait(false);
            ByteString renamed = AutoIdTestData.Bytes(0xE2, 0x04, 0x04, 0x04);
            AutoIdOperationStatusEnumeration idWritten = await m_client.WriteTagIdAsync(
                reader, id, "EPC", renamed).ConfigureAwait(false);
            AutoIdOperationStatusEnumeration oddId = await m_client.WriteTagIdAsync(
                reader, AutoIdTestData.Epc(renamed), "EPC", AutoIdTestData.Bytes(1, 2, 3)).ConfigureAwait(false);
            AutoIdOperationStatusEnumeration missing = await m_client.KillTagAsync(
                reader, id, "EPC", kill).ConfigureAwait(false);
            AutoIdOperationStatusEnumeration wrongKill = await m_client.KillTagAsync(
                reader, AutoIdTestData.Epc(renamed), "EPC", access).ConfigureAwait(false);
            AutoIdOperationStatusEnumeration killed = await m_client.KillTagAsync(
                reader, AutoIdTestData.Epc(renamed), "EPC", kill).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(readEpc.Status, Is.EqualTo(AutoIdOperationStatusEnumeration.SUCCESS));
                Assert.That(readEpc.Data, Is.EqualTo(epc));
                Assert.That(written, Is.EqualTo(AutoIdOperationStatusEnumeration.SUCCESS));
                Assert.That(readUser.Data, Is.EqualTo(AutoIdTestData.Bytes(0xCA, 0xFE)));
                Assert.That(lockWrong, Is.EqualTo(AutoIdOperationStatusEnumeration.PASSWORD_ERROR));
                Assert.That(locked, Is.EqualTo(AutoIdOperationStatusEnumeration.SUCCESS));
                Assert.That(blocked, Is.EqualTo(AutoIdOperationStatusEnumeration.PERMISSON_ERROR));
                Assert.That(passwordChanged, Is.EqualTo(AutoIdOperationStatusEnumeration.SUCCESS));
                Assert.That(oldPassword, Is.EqualTo(AutoIdOperationStatusEnumeration.PASSWORD_ERROR));
                Assert.That(idWritten, Is.EqualTo(AutoIdOperationStatusEnumeration.SUCCESS));
                Assert.That(oddId, Is.EqualTo(AutoIdOperationStatusEnumeration.OP_NOT_POSSIBLE_ERROR));
                Assert.That(missing, Is.EqualTo(AutoIdOperationStatusEnumeration.NO_IDENTIFIER), "the old EPC is gone");
                Assert.That(wrongKill, Is.EqualTo(AutoIdOperationStatusEnumeration.PASSWORD_ERROR));
                Assert.That(killed, Is.EqualTo(AutoIdOperationStatusEnumeration.SUCCESS));
                Assert.That(m_host.Rfid.TagCount, Is.Zero);
            });
        }

        [Test]
        public async Task AccessEventsReportTheCommandsOfTheClientsAsync()
        {
            AutoIdReaderEntry reader = await ReaderAsync(AutoIdHostFixture.RfidName).ConfigureAwait(false);
            ByteString epc = AutoIdTestData.Bytes(0xE2, 0x05, 0x05, 0x05);
            await m_host!.Rfid.PlaceTagAsync(new InMemoryRfidTag(epc)).ConfigureAwait(false);
            try
            {
                AutoIdAccessEventTypeRecord record = await FirstEventAsync(
                    ct => m_client!.ObserveAccessEventsAsync(m_session!.DefaultStreaming, reader, ct),
                    async () => await m_client!.ReadTagAsync(
                        reader, AutoIdTestData.Epc(epc), "EPC", 1, 0, 4).ConfigureAwait(false),
                    r => r.Command == "ReadTag").ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(record.DeviceName, Is.EqualTo(AutoIdHostFixture.RfidName));
                    // The application name the client created its session with (OPC 30010 7.10).
                    Assert.That(record.Client, Is.EqualTo("AutoIdE2e"));
                    Assert.That(record.AccessResult![0], Is.TypeOf<RfidAccessResult>());
                    Assert.That(record.AccessResult[0].CodeType, Is.EqualTo("EPC"));
                });
            }
            finally
            {
                await m_host.Rfid.RemoveTagAsync(epc).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task PresenceChangesAreReportedAsEventsAsync()
        {
            AutoIdReaderEntry reader = await ReaderAsync(AutoIdHostFixture.RfidName).ConfigureAwait(false);
            ByteString epc = AutoIdTestData.Bytes(0xE2, 0x06, 0x06, 0x06);
            bool placed = false;

            AutoIdPresenceEventTypeRecord record = await FirstEventAsync(
                ct => m_client!.ObservePresenceEventsAsync(m_session!.DefaultStreaming, reader, ct),
                async () =>
                {
                    if (placed)
                    {
                        await m_host!.Rfid.RemoveTagAsync(epc).ConfigureAwait(false);
                    }
                    else
                    {
                        await m_host!.Rfid.PlaceTagAsync(new InMemoryRfidTag(epc)).ConfigureAwait(false);
                    }
                    placed = !placed;
                },
                r => r.Presence == 1).ConfigureAwait(false);
            if (placed)
            {
                await m_host!.Rfid.RemoveTagAsync(epc).ConfigureAwait(false);
            }

            Assert.That(record.DeviceName, Is.EqualTo(AutoIdHostFixture.RfidName));
        }

        [Test]
        public async Task LogbookEntriesAreReportedAsEventsAsync()
        {
            AutoIdReaderEntry reader = await ReaderAsync(AutoIdHostFixture.RfidName).ConfigureAwait(false);

            AutoIdLogEntryEventTypeRecord record = await FirstEventAsync(
                ct => m_client!.ObserveLogEntriesAsync(m_session!.DefaultStreaming, reader, ct),
                async () => await m_host!.RfidReader.ReportLogEntryAsync("antenna 1 reconnected").ConfigureAwait(false),
                r => r.Message.Text == "antenna 1 reconnected").ConfigureAwait(false);

            Assert.That(record.DeviceName, Is.EqualTo(AutoIdHostFixture.RfidName));
        }

        [Test]
        public async Task OcrAndOpticalReadersScanTheirCodesAsync()
        {
            AutoIdReaderEntry ocr = await ReaderAsync(AutoIdHostFixture.OcrName).ConfigureAwait(false);
            AutoIdReaderEntry optical = await ReaderAsync(AutoIdHostFixture.OpticalName).ConfigureAwait(false);
            m_host!.Ocr.Present(new OcrScanResult
            {
                CodeType = "RAW:STRING",
                ScanData = new ScanData { SwitchField = ScanDataFields.String, String = "LOT 4711" },
                Timestamp = DateTimeUtc.Now,
                Quality = 90
            });
            m_host.Optical.Present(new OpticalScanResult
            {
                EncodingMask = (uint)OpticalScanResultFields.Grade,
                CodeType = "GS1",
                ScanData = new ScanData { SwitchField = ScanDataFields.ByteString, ByteString = AutoIdTestData.Bytes(1, 2) },
                Timestamp = DateTimeUtc.Now,
                Grade = 3.5f
            });
            try
            {
                AutoIdScanResponse ocrResponse = await m_client!.ScanAsync(
                    ocr, AutoIdTestData.Settings(cycles: 1)).ConfigureAwait(false);
                AutoIdScanResponse opticalResponse = await m_client.ScanAsync(
                    optical, AutoIdTestData.Settings(cycles: 1)).ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(ocrResponse.Results[0].ScanData.String, Is.EqualTo("LOT 4711"));
                    Assert.That(((OcrScanResult)ocrResponse.Results[0]).Quality, Is.EqualTo(90));
                    Assert.That(opticalResponse.Results[0], Is.InstanceOf<OpticalScanResult>());
                    Assert.That(((OpticalScanResult)opticalResponse.Results[0]).Grade, Is.EqualTo(3.5f));
                });
            }
            finally
            {
                m_host.Ocr.Clear();
                m_host.Optical.Clear();
            }
        }

        [Test]
        public async Task AVerifierReportsThroughEventsAndItsSynchronousScanIsNotOfferedAsync()
        {
            AutoIdReaderEntry verifier = await ReaderAsync(AutoIdHostFixture.VerifierName).ConfigureAwait(false);
            m_host!.Verifier.Present(new OpticalVerifierScanResult
            {
                CodeType = "RAW:STRING",
                ScanData = new ScanData { SwitchField = ScanDataFields.String, String = "V-1" },
                Timestamp = DateTimeUtc.Now,
                IsoGrade = "B"
            });
            try
            {
                NotSupportedException unsupported = (await Assert.ThrowsAsync<NotSupportedException>(
                    async () => await m_client!.ScanAsync(verifier, AutoIdTestData.Settings(cycles: 1))
                        .ConfigureAwait(false)))!;
                await m_client!.ScanStartAsync(verifier, AutoIdTestData.Settings()).ConfigureAwait(false);
                AutoIdScanEventTypeRecord record = await FirstEventAsync(
                    ct => m_client.ObserveScanEventsAsync(m_session!.DefaultStreaming, verifier, ct),
                    () => Task.CompletedTask,
                    r => r.ScanResult?.OfType<OpticalVerifierScanResult>().Any(x => x.IsoGrade == "B") == true)
                    .ConfigureAwait(false);
                await m_client.ScanStopAsync(verifier).ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(unsupported.Message, Does.Contain("WGS84Coordinate"));
                    Assert.That(record.DeviceName, Is.EqualTo(AutoIdHostFixture.VerifierName));
                });
            }
            finally
            {
                m_host.Verifier.Clear();
            }
        }

        [Test]
        public async Task ALocatingSystemAnswersLocationRequestsAsync()
        {
            AutoIdReaderEntry rtls = await ReaderAsync(AutoIdHostFixture.RtlsName).ConfigureAwait(false);
            ByteString id = AutoIdTestData.Bytes(0xAB, 0xCD);
            m_host!.Rtls.Locate(id, new RtlsLocationResult
            {
                CodeType = "UID",
                ScanData = new ScanData { SwitchField = ScanDataFields.ByteString, ByteString = id },
                Timestamp = DateTimeUtc.Now,
                EncodingMask = (uint)ScanResultFields.Location,
                Location = new Location
                {
                    SwitchField = LocationFields.WGS84,
                    WGS84 = new WGS84Coordinate { Latitude = 48.13, Longitude = 11.57 }
                },
                Speed = 1.5
            });
            try
            {
                ArrayOf<LocationTypeEnumeration> types = await m_client!.GetSupportedLocationTypesAsync(rtls)
                    .ConfigureAwait(false);
                RtlsLocationResult found = await m_client.GetLocationAsync(
                    rtls,
                    new ScanData { SwitchField = ScanDataFields.ByteString, ByteString = id },
                    "UID",
                    LocationTypeEnumeration.WGS84).ConfigureAwait(false);
                ServiceResultException unknown = (await Assert.ThrowsAsync<ServiceResultException>(
                    async () => await m_client.GetLocationAsync(
                        rtls,
                        new ScanData { SwitchField = ScanDataFields.ByteString, ByteString = AutoIdTestData.Bytes("\t"u8.ToArray()) },
                        "UID",
                        LocationTypeEnumeration.WGS84).ConfigureAwait(false)))!;
                ServiceResultException notAvailable = (await Assert.ThrowsAsync<ServiceResultException>(
                    async () => await m_client.GetLocationAsync(
                        rtls,
                        new ScanData { SwitchField = ScanDataFields.ByteString, ByteString = id },
                        "UID",
                        LocationTypeEnumeration.LOCAL).ConfigureAwait(false)))!;
                ServiceResultException unsupportedType = (await Assert.ThrowsAsync<ServiceResultException>(
                    async () => await m_client.GetLocationAsync(
                        rtls,
                        new ScanData { SwitchField = ScanDataFields.ByteString, ByteString = id },
                        "UID",
                        LocationTypeEnumeration.NMEA).ConfigureAwait(false)))!;
                AutoIdScanResponse scan = await m_client.ScanAsync(rtls, AutoIdTestData.Settings(cycles: 1))
                    .ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(types.ToArray(), Is.EqualTo([LocationTypeEnumeration.WGS84, LocationTypeEnumeration.LOCAL]));
                    Assert.That(found.Location.WGS84.Latitude, Is.EqualTo(48.13));
                    Assert.That(found.Speed, Is.EqualTo(1.5));
                    Assert.That(unknown.StatusCode, Is.EqualTo((uint)StatusCodes.BadNotFound));
                    Assert.That(notAvailable.StatusCode, Is.EqualTo((uint)StatusCodes.BadInvalidState));
                    Assert.That(unsupportedType.StatusCode, Is.EqualTo((uint)StatusCodes.BadInvalidArgument));
                    Assert.That(scan.Results[0], Is.TypeOf<RtlsLocationResult>());
                });
            }
            finally
            {
                m_host.Rtls.Forget(id);
            }
        }

        [Test]
        public async Task TheDeviceLocationIsReturnedInItsTypeAsync()
        {
            AutoIdReaderEntry reader = await ReaderAsync(AutoIdHostFixture.RfidName).ConfigureAwait(false);
            AutoIdReaderEntry ocr = await ReaderAsync(AutoIdHostFixture.OcrName).ConfigureAwait(false);

            Location location = await m_client!.GetDeviceLocationAsync(reader, LocationTypeEnumeration.NAME)
                .ConfigureAwait(false);
            ServiceResultException other = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await m_client.GetDeviceLocationAsync(reader, LocationTypeEnumeration.WGS84)
                    .ConfigureAwait(false)))!;
            ServiceResultException undefined = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await m_client.GetDeviceLocationAsync(reader, (LocationTypeEnumeration)99)
                    .ConfigureAwait(false)))!;
            ServiceResultException none = (await Assert.CatchAsync<ServiceResultException>(
                async () => await m_client.GetDeviceLocationAsync(ocr, LocationTypeEnumeration.NAME)
                    .ConfigureAwait(false)))!;

            Assert.Multiple(() =>
            {
                Assert.That(location.Name, Is.EqualTo("Dock 1"));
                Assert.That(other.StatusCode, Is.EqualTo((uint)StatusCodes.BadInvalidState),
                    "a defined type the location is not available in, like GetLocation (6.6.3.2)");
                Assert.That(undefined.StatusCode, Is.EqualTo((uint)StatusCodes.BadInvalidArgument));
                Assert.That(none.StatusCode, Is.AnyOf(
                        (uint)StatusCodes.BadMethodInvalid,
                        (uint)StatusCodes.BadNodeIdUnknown,
                        (uint)StatusCodes.BadNotImplemented),
                    "an unpublished optional method cannot be called");
            });
        }

        [Test]
        public async Task AFaultyDeviceIsVisibleOnTheWireAsync()
        {
            AutoIdReaderEntry reader = await ReaderAsync(AutoIdHostFixture.OpticalName).ConfigureAwait(false);
            AutoIdReader hosted = m_host!.Runtime.Readers.ToArray()!.Single(r => r.Name.Name == AutoIdHostFixture.OpticalName);
            await hosted.SetFaultedAsync(true).ConfigureAwait(false);
            try
            {
                AutoIdDeviceSnapshot faulty = await m_client!.ReadDeviceAsync(reader).ConfigureAwait(false);
                AutoIdScanResponse scan = await m_client.ScanAsync(reader, AutoIdTestData.Settings(cycles: 1))
                    .ConfigureAwait(false);
                AutoIdOperationStatusEnumeration start = await m_client.ScanStartAsync(reader, AutoIdTestData.Settings())
                    .ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(faulty.DeviceStatus, Is.EqualTo(DeviceStatusEnumeration.Error));
                    Assert.That(scan.Status, Is.EqualTo(AutoIdOperationStatusEnumeration.DEVICE_NOT_READY));
                    Assert.That(start, Is.EqualTo(AutoIdOperationStatusEnumeration.DEVICE_NOT_READY));
                });
            }
            finally
            {
                await hosted.SetFaultedAsync(false).ConfigureAwait(false);
            }
            await WaitForStatusAsync(AutoIdHostFixture.OpticalName, DeviceStatusEnumeration.Idle).ConfigureAwait(false);
        }

        [Test]
        public async Task ARefreshedSnapshotCarriesTheCurrentStatusAsync()
        {
            AutoIdReaderEntry reader = await ReaderAsync(AutoIdHostFixture.OpticalName).ConfigureAwait(false);
            AutoIdReader hosted = m_host!.Runtime.Readers.ToArray()!
                .Single(r => r.Name.Name == AutoIdHostFixture.OpticalName);
            AutoIdDeviceSnapshot idle = await m_client!.ReadDeviceAsync(reader, AutoIdReadOptions.StatusOnly)
                .ConfigureAwait(false);
            await hosted.SetFaultedAsync(true).ConfigureAwait(false);
            try
            {
                AutoIdDeviceSnapshot faulty = await m_client.RefreshDeviceAsync(idle).ConfigureAwait(false);
                // Other tests of this fixture scan the same reader, so compare against what the hosted
                // variable holds instead of assuming the reader has never scanned.
                StatusCode hostedScanStatus = hosted.Device.LastScanData!.StatusCode;

                Assert.Multiple(() =>
                {
                    Assert.That(idle.DeviceStatus, Is.EqualTo(DeviceStatusEnumeration.Idle));
                    Assert.That(faulty.DeviceStatus, Is.EqualTo(DeviceStatusEnumeration.Error));
                    Assert.That(faulty.LastScanStatus, Is.EqualTo(hostedScanStatus));
                    Assert.That(faulty.Methods, Is.EqualTo(idle.Methods));
                });
            }
            finally
            {
                await hosted.SetFaultedAsync(false).ConfigureAwait(false);
            }
            await WaitForStatusAsync(AutoIdHostFixture.OpticalName, DeviceStatusEnumeration.Idle).ConfigureAwait(false);
        }

        [Test]
        public async Task ARefreshOfAStaleSnapshotSurfacesTheStatusOfTheReadAsync()
        {
            AutoIdReaderEntry reader = await ReaderAsync(AutoIdHostFixture.RfidName).ConfigureAwait(false);
            AutoIdDeviceSnapshot current = await m_client!.ReadDeviceAsync(reader, AutoIdReadOptions.StatusOnly)
                .ConfigureAwait(false);
            Assert.That(current.LastScanDataId.IsNull, Is.False);
            // NodeIds the server does not know, as after the variables were deleted.
            var unknown = new NodeId("NoSuchVariable", current.DeviceStatusId.NamespaceIndex);

            AutoIdDeviceSnapshot staleScan = await m_client
                .RefreshDeviceAsync(current with { LastScanDataId = unknown })
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(staleScan.DeviceStatus, Is.EqualTo(current.DeviceStatus));
                Assert.That(staleScan.LastScanStatus, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                Assert.That(staleScan.LastScanData.IsNull, Is.True);
            });
            await Assert.ThatAsync(
                async () => await m_client.RefreshDeviceAsync(current with { DeviceStatusId = unknown })
                    .ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadNodeIdUnknown)).ConfigureAwait(false);
        }

        [Test]
        public async Task TheServerPublishesTheFacetsOfTheReadersItHostsAsync()
        {
            DataValue profileRead = await m_session!.ReadValueAsync(
                Ua.VariableIds.Server_ServerCapabilities_ServerProfileArray).ConfigureAwait(false);
            DataValue unitRead = await m_session!.ReadValueAsync(
                Ua.VariableIds.Server_ServerCapabilities_ConformanceUnits).ConfigureAwait(false);

            Assert.That(profileRead.WrappedValue.TryGetValue(out ArrayOf<string> profiles), Is.True);
            Assert.That(unitRead.WrappedValue.TryGetValue(out ArrayOf<QualifiedName> units), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(
                    profiles.ToArray(),
                    Is.SupersetOf(
                    [
                        AutoIdProfiles.SimpleSyncServer, AutoIdProfiles.BaseSyncServer, AutoIdProfiles.BaseAsyncServer
                    ]));
                Assert.That(profiles.ToArray(), Does.Not.Contain(AutoIdProfiles.FullServer));
                Assert.That(
                    units.ToArray()!.Select(unit => unit.Name),
                    Is.SupersetOf(
                    [
                        AutoIdProfiles.DeviceTypeUnit, AutoIdProfiles.SyncAccessUnit, AutoIdProfiles.AsyncAccessUnit,
                        AutoIdProfiles.AsyncAccessControlUnit, AutoIdProfiles.SimpleSyncAccessUnit
                    ]));
            });
        }

        [Test]
        public async Task ClientCallsForTheWrongReaderKindAreRejectedBeforeTheyReachTheServerAsync()
        {
            AutoIdReaderEntry ocr = await ReaderAsync(AutoIdHostFixture.OcrName).ConfigureAwait(false);
            AutoIdReaderEntry rfid = await ReaderAsync(AutoIdHostFixture.RfidName).ConfigureAwait(false);
            ScanData id = AutoIdTestData.Epc(AutoIdTestData.Bytes(1, 2));

            await Assert.MultipleAsync(async () =>
            {
                await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await m_client!.ReadTagAsync(ocr, id, "EPC", 1, 0, 1).ConfigureAwait(false));
                await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await m_client!.GetSupportedLocationTypesAsync(rfid).ConfigureAwait(false));
                await Assert.ThrowsAsync<ArgumentNullException>(
                    async () => await m_client!.ReadTagAsync(rfid, null!, "EPC", 1, 0, 1).ConfigureAwait(false));
                await Assert.ThrowsAsync<ArgumentException>(
                    async () => await m_client!.ReadTagAsync(rfid, id, string.Empty, 1, 0, 1).ConfigureAwait(false));
                await Assert.ThrowsAsync<ArgumentNullException>(
                    async () => await m_client!.ScanAsync(null!, AutoIdTestData.Settings(cycles: 1)).ConfigureAwait(false));
                await Assert.ThrowsAsync<ArgumentNullException>(
                    async () => await m_client!.ScanAsync(rfid, null!).ConfigureAwait(false));
                await Assert.ThrowsAsync<ArgumentNullException>(
                    async () => await m_client!.ScanStartAsync(rfid, null!).ConfigureAwait(false));
                await Assert.ThrowsAsync<ArgumentNullException>(
                    async () => await m_client!.GetLocationAsync(
                        await ReaderAsync(AutoIdHostFixture.RtlsName).ConfigureAwait(false),
                        null!, "UID", LocationTypeEnumeration.NAME).ConfigureAwait(false));
            });
        }

        [Test]
        public async Task ABrowseTheServerRejectsIsReportedNotHiddenAsync()
        {
            ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                await foreach (AutoIdReaderEntry entry in m_client!.EnumerateReadersUnderAsync(
                    new NodeId("DoesNotExist", 7)))
                {
                    Assert.Fail("Nothing can be found below an unknown node: " + entry.BrowseName);
                }
            }))!;
            ServiceResultException unknownReader = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await m_client!.GetReaderAsync(new NodeId("DoesNotExist", 7)).ConfigureAwait(false)))!;
            ServiceResultException notAReader = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await m_client!.GetReaderAsync(
                    new NodeId(Di.Objects.DeviceSet, (ushort)m_session!.NamespaceUris.GetIndex(Di.Namespaces.OpcUaDi)))
                    .ConfigureAwait(false)))!;

            await Assert.MultipleAsync(async () =>
            {
                Assert.That(StatusCode.IsBad(failure.StatusCode), Is.True);
                Assert.That(StatusCode.IsBad(unknownReader.StatusCode), Is.True);
                Assert.That(notAReader.StatusCode, Is.EqualTo((uint)StatusCodes.BadTypeMismatch));
                await Assert.ThrowsAsync<ArgumentException>(
                    async () => await m_client!.GetReaderAsync(NodeId.Null).ConfigureAwait(false));
            });
        }

        [Test]
        public async Task EnumerationAndEventStreamsHonourCancellationAsync()
        {
            AutoIdReaderEntry reader = await ReaderAsync(AutoIdHostFixture.RfidName).ConfigureAwait(false);
            using var cancelled = new CancellationTokenSource();
            await cancelled.CancelAsync().ConfigureAwait(false);

            await Assert.CatchAsync<OperationCanceledException>(async () =>
            {
                await foreach (AutoIdReaderEntry entry in m_client!.EnumerateReadersAsync(cancelled.Token))
                {
                    Assert.Fail("A cancelled enumeration yields nothing: " + entry.BrowseName);
                }
            });
            await Assert.CatchAsync<OperationCanceledException>(
                async () => await m_client!.ScanAsync(reader, AutoIdTestData.Settings(cycles: 1), cancelled.Token)
                    .ConfigureAwait(false));
            using var shortLived = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            await Assert.CatchAsync<OperationCanceledException>(async () =>
            {
                await foreach (AutoIdPresenceEventTypeRecord record in m_client!.ObservePresenceEventsAsync(
                    m_session!.DefaultStreaming, reader, shortLived.Token))
                {
                    Assert.Fail("No presence changed: " + record.Presence);
                }
            });
            Assert.Throws<ArgumentNullException>(() => m_client!.ObserveScanEventsAsync(null!, reader));
            Assert.Throws<ArgumentNullException>(() => m_client!.ObserveScanEventsAsync(m_session!.DefaultStreaming, null!));
        }

        [Test]
        public void TheClientValidatesItsConstructionAndExtensions()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            Assert.Multiple(() =>
            {
                Assert.Throws<ArgumentNullException>(() => new AutoIdClient(null!, telemetry));
                Assert.Throws<ArgumentNullException>(() => new AutoIdClient(m_session!, null!));
                Assert.Throws<ArgumentNullException>(() => SessionAutoIdExtensions.AutoId(null!, telemetry));
                Assert.Throws<ArgumentNullException>(() => m_session!.AutoId(null!));
                Assert.That(m_session!.AutoId(telemetry).IsSupported, Is.True);
                Assert.That(m_client!.DeviceSetId.IsNull, Is.False);
                Assert.That(m_client.Session, Is.SameAs(m_session));
                Assert.That(m_client.Device(NodeId.Null).ObjectId.IsNull, Is.True);
                Assert.Throws<ArgumentNullException>(() => new AutoIdClientFactory(null!, telemetry));
                Assert.Throws<ArgumentNullException>(() => new AutoIdClientFactory(_ => null!, null!));
            });
        }

        private async Task<AutoIdReaderEntry> ReaderAsync(string name)
        {
            await foreach (AutoIdReaderEntry entry in m_client!.EnumerateReadersAsync())
            {
                if (entry.BrowseName.Name == name)
                {
                    return entry;
                }
            }
            throw new AssertionException("No reader named '" + name + "'.");
        }

        private async Task WaitForStatusAsync(string name, DeviceStatusEnumeration status)
        {
            AutoIdReaderEntry reader = await ReaderAsync(name).ConfigureAwait(false);
            DeviceStatusEnumeration last = default;
            for (int ii = 0; ii < 200; ii++)
            {
                last = (await m_client!.ReadDeviceAsync(reader, AutoIdReadOptions.StatusOnly).ConfigureAwait(false))
                    .DeviceStatus;
                if (last == status)
                {
                    return;
                }
                await Task.Delay(50).ConfigureAwait(false);
            }
            Assert.Fail($"Reader '{name}' did not reach {status}; last status was {last}.");
        }

        /// <summary>
        /// Observes events until one matches, triggering the source repeatedly so the test does not
        /// depend on when the server-side monitored item becomes active.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        private static async Task<T> FirstEventAsync<T>(
            Func<CancellationToken, IAsyncEnumerable<T>> observe,
            Func<Task> trigger,
            Func<T, bool> match)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            Task<T> observer = Task.Run(async () =>
            {
                await foreach (T item in observe(timeout.Token).ConfigureAwait(false))
                {
                    if (match(item))
                    {
                        return item;
                    }
                }
                throw new InvalidOperationException("The event stream ended without a matching event.");
            });
            while (!observer.IsCompleted && !timeout.IsCancellationRequested)
            {
                await trigger().ConfigureAwait(false);
                await Task.WhenAny(observer, Task.Delay(200)).ConfigureAwait(false);
            }
            return await observer.ConfigureAwait(false);
        }

        private AutoIdHostFixture? m_host;
        private ManagedSession? m_session;
        private AutoIdClient? m_client;
    }
}
