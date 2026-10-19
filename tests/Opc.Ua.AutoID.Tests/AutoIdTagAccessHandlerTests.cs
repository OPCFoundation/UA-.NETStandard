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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.AutoID.Server;

namespace Opc.Ua.AutoID.Tests
{
    /// <summary>
    /// The tag access and location methods of OPC 30010 6.5.3 and 6.6.3 through the method handlers.
    /// </summary>
    [TestFixture]
    [Category("AutoID")]
    public sealed class AutoIdTagAccessHandlerTests
    {
        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_fixture = new AutoIdManagerFixture();
            await m_fixture.StartAsync().ConfigureAwait(false);
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            await m_fixture!.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task ReadTagPassesTheProviderStatusAndDataThroughAsync()
        {
            (RfidReaderDeviceState device, FakeProvider provider, EventCapture events) = await CreateAsync().ConfigureAwait(false);

            ReadTagMethodStateResult result = await ReadAsync(device).ConfigureAwait(false);
            provider.OnRead = ct => new ValueTask<AutoIdTagReadOutcome>(
                new AutoIdTagReadOutcome(default, AutoIdOperationStatusEnumeration.PASSWORD_ERROR));
            ReadTagMethodStateResult refused = await ReadAsync(device).ConfigureAwait(false);

            RfidAccessEventState access = events.Of<RfidAccessEventState>().First();
            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
                Assert.That(result.ResultData, Is.EqualTo(AutoIdTestData.Bytes(1, 2)));
                Assert.That(result.Status, Is.EqualTo(AutoIdOperationStatusEnumeration.SUCCESS));
                Assert.That(refused.Status, Is.EqualTo(AutoIdOperationStatusEnumeration.PASSWORD_ERROR));
                Assert.That(access.Command!.Value, Is.EqualTo("ReadTag"));
                Assert.That(access.AccessResult!.Value[0].RWData.ByteString, Is.EqualTo(AutoIdTestData.Bytes(1, 2)));
                Assert.That(access.AccessResult.Value[0].CodeTypeRWData, Is.EqualTo("RAW:BYTES"));
                Assert.That(
                    events.Of<RfidAccessEventState>().Count(),
                    Is.EqualTo(2),
                    "a wrong password is still an access to the tag");
            });
        }

        [TestCase(AutoIdOperationStatusEnumeration.SUCCESS, true)]
        [TestCase(AutoIdOperationStatusEnumeration.PASSWORD_ERROR, true)]
        [TestCase(AutoIdOperationStatusEnumeration.READ_ERROR, true)]
        [TestCase(AutoIdOperationStatusEnumeration.NO_IDENTIFIER, false)]
        [TestCase(AutoIdOperationStatusEnumeration.MULTIPLE_IDENTIFIERS, false)]
        [TestCase(AutoIdOperationStatusEnumeration.NOT_SUPPORTED_BY_DEVICE, false)]
        [TestCase(AutoIdOperationStatusEnumeration.DEVICE_NOT_READY, false)]
        [TestCase(AutoIdOperationStatusEnumeration.INVALID_CONFIGURATION, false)]
        [TestCase(AutoIdOperationStatusEnumeration.DEVICE_FAULT, false)]
        public void OnlyCommandsThatReachedATagCountAsAnAccess(AutoIdOperationStatusEnumeration status, bool accessed)
        {
            Assert.That(AutoIdReader.TagWasAccessed(status), Is.EqualTo(accessed));
        }

        [Test]
        public async Task ACommandThatFoundNoTagRaisesNoAccessEventAsync()
        {
            (RfidReaderDeviceState device, FakeProvider provider, EventCapture events) = await CreateAsync().ConfigureAwait(false);
            provider.OnRead = ct => new ValueTask<AutoIdTagReadOutcome>(
                new AutoIdTagReadOutcome(default, AutoIdOperationStatusEnumeration.NO_IDENTIFIER));

            ReadTagMethodStateResult result = await ReadAsync(device).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Status, Is.EqualTo(AutoIdOperationStatusEnumeration.NO_IDENTIFIER));
                Assert.That(events.Events, Is.Empty);
            });
        }

        [Test]
        public async Task EveryTagMethodRaisesItsAccessEventAndReturnsTheStatusAsync()
        {
            (RfidReaderDeviceState device, _, EventCapture events) = await CreateAsync().ConfigureAwait(false);
            ScanData id = AutoIdTestData.Epc(AutoIdTestData.Bytes(1, 2));
            ISystemContext context = m_fixture!.Context;

            WriteTagMethodStateResult write = await device.WriteTag!.OnCallAsync!(
                context, device.WriteTag, device.NodeId, id, "EPC", 3, 0, AutoIdTestData.Bytes(7), default,
                CancellationToken.None).ConfigureAwait(false);
            WriteTagIDMethodStateResult writeId = await device.WriteTagID!.OnCallAsync!(
                context, device.WriteTagID, device.NodeId, id, "EPC", AutoIdTestData.Bytes(1, 2, 3, 4), 0, false,
                default, CancellationToken.None).ConfigureAwait(false);
            KillTagMethodStateResult kill = await device.KillTag!.OnCallAsync!(
                context, device.KillTag, device.NodeId, id, "EPC", AutoIdTestData.Bytes(1, 1, 1, 1),
                CancellationToken.None).ConfigureAwait(false);
            LockTagMethodStateResult locked = await device.LockTag!.OnCallAsync!(
                context, device.LockTag, device.NodeId, id, "EPC", default, RfidLockRegionEnumeration.User,
                RfidLockOperationEnumeration.Lock, 0, 0, CancellationToken.None).ConfigureAwait(false);
            SetTagPasswordMethodStateResult password = await device.SetTagPassword!.OnCallAsync!(
                context, device.SetTagPassword, device.NodeId, id, "EPC", RfidPasswordTypeEnumeration.Access,
                default, AutoIdTestData.Bytes(1, 1, 1, 1), CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    new[] { write.Status, writeId.Status, kill.Status, locked.Status, password.Status },
                    Has.All.EqualTo(AutoIdOperationStatusEnumeration.SUCCESS));
                Assert.That(
                    events.Of<RfidAccessEventState>().Select(e => e.Command!.Value),
                    Is.EqualTo(s_commands));
                Assert.That(
                    events.Of<RfidAccessEventState>().First().AccessResult!.Value[0].Identifier.Epc.UId,
                    Is.EqualTo(AutoIdTestData.Bytes(1, 2)));
            });
        }

        [Test]
        public async Task MalformedArgumentsFailWithBadInvalidArgumentAndNeverReachTheProviderAsync()
        {
            (RfidReaderDeviceState device, FakeProvider provider, EventCapture events) = await CreateAsync().ConfigureAwait(false);
            int calls = 0;
            provider.OnRead = ct =>
            {
                calls++;
                return default;
            };
            ISystemContext context = m_fixture!.Context;
            ScanData empty = new() { SwitchField = ScanDataFields.ByteString };
            ScanData good = AutoIdTestData.Epc(AutoIdTestData.Bytes(1, 2));

            ReadTagMethodStateResult noData = await device.ReadTag!.OnCallAsync!(
                context, device.ReadTag, device.NodeId, empty, "EPC", 1, 0, 4, default, CancellationToken.None)
                .ConfigureAwait(false);
            ReadTagMethodStateResult noType = await device.ReadTag.OnCallAsync!(
                context, device.ReadTag, device.NodeId, good, string.Empty, 1, 0, 4, default, CancellationToken.None)
                .ConfigureAwait(false);
            ReadTagMethodStateResult tooLong = await device.ReadTag.OnCallAsync!(
                context, device.ReadTag, device.NodeId, good, "EPC", 1, 0, 65537, default, CancellationToken.None)
                .ConfigureAwait(false);
            LockTagMethodStateResult badRegion = await device.LockTag!.OnCallAsync!(
                context, device.LockTag, device.NodeId, good, "EPC", default, (RfidLockRegionEnumeration)9,
                RfidLockOperationEnumeration.Lock, 0, 0, CancellationToken.None).ConfigureAwait(false);
            LockTagMethodStateResult badOperation = await device.LockTag.OnCallAsync!(
                context, device.LockTag, device.NodeId, good, "EPC", default, RfidLockRegionEnumeration.User,
                (RfidLockOperationEnumeration)9, 0, 0, CancellationToken.None).ConfigureAwait(false);
            SetTagPasswordMethodStateResult badPassword = await device.SetTagPassword!.OnCallAsync!(
                context, device.SetTagPassword, device.NodeId, good, "EPC", (RfidPasswordTypeEnumeration)9,
                default, default, CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    new StatusCode[]
                    {
                        noData.ServiceResult.StatusCode, noType.ServiceResult.StatusCode,
                        tooLong.ServiceResult.StatusCode, badRegion.ServiceResult.StatusCode,
                        badOperation.ServiceResult.StatusCode, badPassword.ServiceResult.StatusCode
                    },
                    Has.All.EqualTo(StatusCodes.BadInvalidArgument));
                Assert.That(calls, Is.Zero);
                Assert.That(events.Events, Is.Empty);
            });
        }

        [Test]
        public async Task AnIdentifierArmWithoutDataIsRejectedForEveryArmAsync()
        {
            (RfidReaderDeviceState device, _, _) = await CreateAsync().ConfigureAwait(false);
            ISystemContext context = m_fixture!.Context;
            ScanData[] identifiers =
            [
                new ScanData { SwitchField = ScanDataFields.None },
                new ScanData { SwitchField = ScanDataFields.String, String = string.Empty },
                new ScanData { SwitchField = ScanDataFields.Epc, Epc = new ScanDataEpc() },
                new ScanData { SwitchField = ScanDataFields.Custom }
            ];
            var statuses = new System.Collections.Generic.List<StatusCode>();
            foreach (ScanData identifier in identifiers)
            {
                ReadTagMethodStateResult result = await device.ReadTag!.OnCallAsync!(
                    context, device.ReadTag, device.NodeId, identifier, "EPC", 1, 0, 4, default,
                    CancellationToken.None).ConfigureAwait(false);
                statuses.Add(result.ServiceResult.StatusCode);
            }
            ReadTagMethodStateResult custom = await device.ReadTag!.OnCallAsync!(
                context, device.ReadTag, device.NodeId,
                new ScanData { SwitchField = ScanDataFields.Custom, Custom = Variant.From(5) },
                "CUSTOM:X", 1, 0, 4, default, CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(statuses, Has.All.EqualTo(StatusCodes.BadInvalidArgument));
                Assert.That(ServiceResult.IsGood(custom.ServiceResult), Is.True, "a custom arm with a value is data");
            });
        }

        [Test]
        public async Task ATagCallDuringShutdownFailsWithBadServerHaltedAsync()
        {
            (RfidReaderDeviceState device, FakeProvider provider, _) = await CreateAsync().ConfigureAwait(false);
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            provider.OnRead = async ct =>
            {
                started.TrySetResult(true);
                await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
                return default;
            };
            Task<ReadTagMethodStateResult> call = ReadAsync(device).AsTask();
            await started.Task.ConfigureAwait(false);

            await ReaderOf(device).DisposeAsync().ConfigureAwait(false);

            ServiceResultException halted = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await call.ConfigureAwait(false)))!;
            Assert.That(halted.StatusCode, Is.EqualTo((uint)StatusCodes.BadServerHalted));
            ServiceResultException after = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await ReadAsync(device).ConfigureAwait(false)))!;
            Assert.That(after.StatusCode, Is.EqualTo((uint)StatusCodes.BadInvalidState));
        }

        [Test]
        public async Task DeviceLocationIsReturnedInItsOwnTypeAndUpdatableAsync()
        {
            var provider = new FakeProvider();
            AutoIdReader reader = await m_fixture!.Runtime.Reader(m_fixture.Name(Unique("Located")), provider)
                .WithDeviceLocation(new Location { SwitchField = LocationFields.NMEA, NMEA = "4807.038,N" })
                .BuildAsync().ConfigureAwait(false);
            var device = (RfidReaderDeviceState)reader.Device;
            ISystemContext context = m_fixture.Context;

            GetDeviceLocationMethodStateResult matching = await device.GetDeviceLocation!.OnCallAsync!(
                context, device.GetDeviceLocation, device.NodeId, LocationTypeEnumeration.NMEA, CancellationToken.None)
                .ConfigureAwait(false);
            GetDeviceLocationMethodStateResult other = await device.GetDeviceLocation.OnCallAsync!(
                context, device.GetDeviceLocation, device.NodeId, LocationTypeEnumeration.WGS84, CancellationToken.None)
                .ConfigureAwait(false);
            GetDeviceLocationMethodStateResult undefined = await device.GetDeviceLocation.OnCallAsync!(
                context, device.GetDeviceLocation, device.NodeId, (LocationTypeEnumeration)99, CancellationToken.None)
                .ConfigureAwait(false);
            reader.SetDeviceLocation(new Location { SwitchField = LocationFields.Name, Name = "Truck 7" });
            GetDeviceLocationMethodStateResult moved = await device.GetDeviceLocation.OnCallAsync!(
                context, device.GetDeviceLocation, device.NodeId, LocationTypeEnumeration.NAME, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(matching.Location.NMEA, Is.EqualTo("4807.038,N"));
                Assert.That(other.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(other.Location, Is.Null);
                Assert.That(undefined.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
                Assert.That(moved.Location.Name, Is.EqualTo("Truck 7"));
                Assert.Throws<ArgumentNullException>(() => reader.SetDeviceLocation(null!));
            });
        }

        [Test]
        public async Task SettingTheLocationOfAReaderWithoutOneFailsAsync()
        {
            AutoIdReader reader = await m_fixture!.Runtime.CreateReaderAsync(
                m_fixture.Name(Unique("Plain")), new FakeProvider()).ConfigureAwait(false);

            Assert.Throws<InvalidOperationException>(
                () => reader.SetDeviceLocation(new Location { SwitchField = LocationFields.Name, Name = "x" }));
        }

        [Test]
        public async Task LocationMethodsOfAnRtlsDeviceValidateAndPassThroughAsync()
        {
            var provider = new InMemoryRtlsProvider(
                new[] { LocationTypeEnumeration.WGS84, LocationTypeEnumeration.NAME }.ToArrayOf());
            ByteString tag = AutoIdTestData.Bytes(1, 2, 3);
            provider.Locate(tag, new RtlsLocationResult
            {
                CodeType = "UID",
                ScanData = new ScanData { SwitchField = ScanDataFields.ByteString, ByteString = tag },
                Timestamp = DateTimeUtc.Now,
                EncodingMask = (uint)ScanResultFields.Location,
                Location = new Location { SwitchField = LocationFields.Name, Name = "Bay 3" }
            });
            AutoIdReader reader = await m_fixture!.Runtime.CreateReaderAsync(m_fixture.Name(Unique("Rtls")), provider)
                .ConfigureAwait(false);
            var device = (RtlsDeviceState)reader.Device;
            ISystemContext context = m_fixture.Context;
            ScanData id = new() { SwitchField = ScanDataFields.ByteString, ByteString = tag };

            GetSupportedLocationTypesMethodStateResult types = await device.GetSupportedLocationTypes!.OnCallAsync!(
                context, device.GetSupportedLocationTypes, device.NodeId, CancellationToken.None).ConfigureAwait(false);
            GetLocationMethodStateResult found = await device.GetLocation!.OnCallAsync!(
                context, device.GetLocation, device.NodeId, id, LocationTypeEnumeration.NAME, "UID",
                CancellationToken.None).ConfigureAwait(false);
            GetLocationMethodStateResult wrongType = await device.GetLocation.OnCallAsync!(
                context, device.GetLocation, device.NodeId, id, LocationTypeEnumeration.WGS84, "UID",
                CancellationToken.None).ConfigureAwait(false);
            GetLocationMethodStateResult unsupported = await device.GetLocation.OnCallAsync!(
                context, device.GetLocation, device.NodeId, id, LocationTypeEnumeration.LOCAL, "UID",
                CancellationToken.None).ConfigureAwait(false);
            GetLocationMethodStateResult noId = await device.GetLocation.OnCallAsync!(
                context, device.GetLocation, device.NodeId, new ScanData(), LocationTypeEnumeration.NAME, "UID",
                CancellationToken.None).ConfigureAwait(false);
            GetLocationMethodStateResult unknown = await device.GetLocation.OnCallAsync!(
                context, device.GetLocation, device.NodeId,
                new ScanData { SwitchField = ScanDataFields.ByteString, ByteString = AutoIdTestData.Bytes(9) },
                LocationTypeEnumeration.NAME, "UID", CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    types.SupportedLocationTypes.ToArray(),
                    Is.EqualTo([LocationTypeEnumeration.WGS84, LocationTypeEnumeration.NAME]));
                Assert.That(found.Result.Location.Name, Is.EqualTo("Bay 3"));
                Assert.That(wrongType.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(unknown.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadNotFound));
                Assert.That(unsupported.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
                Assert.That(noId.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
                Assert.That(device.LengthUnit!.Value.DisplayName.Text, Is.EqualTo("m"));
                Assert.That(device.SpeedUnit!.Value.UnitId, Is.EqualTo(5067859));
            });
        }

        [Test]
        public async Task GetLocationOfASupportedTypeTheObjectHasNotIsNotAvailableAsync()
        {
            // The default system supports WGS84 and LOCAL; the object was located in local coordinates.
            var provider = new InMemoryRtlsProvider();
            ByteString tag = AutoIdTestData.Bytes(7, 7, 1);
            provider.Locate(tag, LocalResult(tag, 2.0, 3.0));
            (RtlsDeviceState device, ISystemContext context) = await CreateRtlsAsync("NoReference", provider)
                .ConfigureAwait(false);

            GetLocationMethodStateResult local = await GetLocationAsync(
                device, context, tag, LocationTypeEnumeration.LOCAL).ConfigureAwait(false);
            GetLocationMethodStateResult wgs84 = await GetLocationAsync(
                device, context, tag, LocationTypeEnumeration.WGS84).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(local.ServiceResult), Is.True);
                Assert.That(local.Result.Location.Local.X, Is.EqualTo(2.0));
                Assert.That(wgs84.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            });
        }

        [Test]
        public async Task GetLocationTranslatesALocalLocationWithTheConfiguredConverterAsync()
        {
            var provider = new InMemoryRtlsProvider(locationConverter: LocalToWgs84);
            ByteString tag = AutoIdTestData.Bytes(7, 7, 2);
            provider.Locate(tag, LocalResult(tag, 10.0, 20.0));
            (RtlsDeviceState device, ISystemContext context) = await CreateRtlsAsync("Reference", provider)
                .ConfigureAwait(false);

            GetLocationMethodStateResult wgs84 = await GetLocationAsync(
                device, context, tag, LocationTypeEnumeration.WGS84).ConfigureAwait(false);
            GetLocationMethodStateResult local = await GetLocationAsync(
                device, context, tag, LocationTypeEnumeration.LOCAL).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(wgs84.ServiceResult), Is.True);
                Assert.That(wgs84.Result.Location.SwitchField, Is.EqualTo(LocationFields.WGS84));
                Assert.That(wgs84.Result.Location.WGS84.Latitude, Is.EqualTo(48.0 + (20.0 / 111_320.0)));
                Assert.That(wgs84.Result.ScanData.ByteString, Is.EqualTo(tag));
                Assert.That(local.Result.Location.SwitchField, Is.EqualTo(LocationFields.Local));
            });
        }

        [Test]
        public async Task RtlsUnitsCanBeConfiguredAsync()
        {
            static EUInformation Unit(int id, string name) => new() { UnitId = id, DisplayName = new LocalizedText(name) };
            AutoIdReader reader = await m_fixture!.Runtime.Reader(m_fixture.Name(Unique("Units")), new InMemoryRtlsProvider())
                .WithRtlsUnits(Unit(1, "ft"), Unit(2, "rad"), Unit(3, "gon"), Unit(4, "kn"))
                .BuildAsync().ConfigureAwait(false);

            var device = (RtlsDeviceState)reader.Device;
            Assert.Multiple(() =>
            {
                Assert.That(device.LengthUnit!.Value.DisplayName.Text, Is.EqualTo("ft"));
                Assert.That(device.RotationalUnit!.Value.UnitId, Is.EqualTo(2));
                Assert.That(device.GeographicalUnit!.Value.UnitId, Is.EqualTo(3));
                Assert.That(device.SpeedUnit!.Value.DisplayName.Text, Is.EqualTo("kn"));
            });
        }

        private async Task<(RtlsDeviceState Device, ISystemContext Context)> CreateRtlsAsync(
            string name,
            InMemoryRtlsProvider provider)
        {
            AutoIdReader reader = await m_fixture!.Runtime.CreateReaderAsync(m_fixture.Name(Unique(name)), provider)
                .ConfigureAwait(false);
            return ((RtlsDeviceState)reader.Device, m_fixture.Context);
        }

        private static ValueTask<GetLocationMethodStateResult> GetLocationAsync(
            RtlsDeviceState device,
            ISystemContext context,
            ByteString tag,
            LocationTypeEnumeration type)
        {
            return device.GetLocation!.OnCallAsync!(
                context,
                device.GetLocation,
                device.NodeId,
                new ScanData { SwitchField = ScanDataFields.ByteString, ByteString = tag },
                type,
                "UID",
                CancellationToken.None);
        }

        private static RtlsLocationResult LocalResult(ByteString tag, double x, double y)
        {
            return new RtlsLocationResult
            {
                CodeType = "UID",
                ScanData = new ScanData { SwitchField = ScanDataFields.ByteString, ByteString = tag },
                Timestamp = DateTimeUtc.Now,
                EncodingMask = (uint)ScanResultFields.Location,
                Location = new Location
                {
                    SwitchField = LocationFields.Local,
                    Local = new LocalCoordinate { X = x, Y = y }
                }
            };
        }

        /// <summary>
        /// A local coordinate system in metres whose origin is at 48 N 11 E and whose Y axis points north.
        /// </summary>
        private static Location? LocalToWgs84(Location location, LocationTypeEnumeration type)
        {
            if (type != LocationTypeEnumeration.WGS84 || location.SwitchField != LocationFields.Local)
            {
                return null;
            }
            return new Location
            {
                SwitchField = LocationFields.WGS84,
                WGS84 = new WGS84Coordinate
                {
                    N_S_Hemisphere = "N",
                    Latitude = 48.0 + (location.Local.Y / 111_320.0),
                    E_W_Hemisphere = "E",
                    Longitude = 11.0 + (location.Local.X / 74_600.0)
                }
            };
        }

        private ValueTask<ReadTagMethodStateResult> ReadAsync(RfidReaderDeviceState device)
        {
            return device.ReadTag!.OnCallAsync!(
                m_fixture!.Context,
                device.ReadTag,
                device.NodeId,
                AutoIdTestData.Epc(AutoIdTestData.Bytes(1, 2)),
                "EPC",
                1,
                0,
                4,
                default,
                CancellationToken.None);
        }

        private AutoIdReader ReaderOf(RfidReaderDeviceState device)
        {
            return m_fixture!.Runtime.Readers.ToArray()!.Single(reader => reader.Device == device);
        }

        private async Task<(RfidReaderDeviceState Device, FakeProvider Provider, EventCapture Events)> CreateAsync()
        {
            var provider = new FakeProvider();
            AutoIdReader reader = await m_fixture!.Runtime.CreateReaderAsync(
                m_fixture.Name(Unique("Tag") + (++s_counter)), provider).ConfigureAwait(false);
            return ((RfidReaderDeviceState)reader.Device, provider, new EventCapture(reader.Device));
        }

        private static string Unique(string name)
        {
            return TestContext.CurrentContext.Test.Name.Replace(' ', '_') + "_" + name;
        }

        private static readonly string[] s_commands = ["WriteTag", "WriteTagID", "KillTag", "LockTag", "SetTagPassword"];
        private static int s_counter;
        private AutoIdManagerFixture? m_fixture;
    }
}
