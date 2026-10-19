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
    /// The configurable limits of the server: scan duration, result count, read length and the
    /// access events.
    /// </summary>
    [TestFixture]
    [Category("AutoID")]
    public sealed class AutoIdLimitsTests
    {
        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_fixture = new AutoIdManagerFixture();
            await m_fixture.StartAsync(new AutoIdServerOptions
            {
                MaximumScanDuration = TimeSpan.FromMilliseconds(300),
                MaximumScanResults = 3,
                MaximumTagReadLength = 8,
                PublishAccessEvents = false
            }).ConfigureAwait(false);
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            await m_fixture!.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task AProviderThatIgnoresTheDurationIsCancelledWithBadTimeoutAsync()
        {
            var provider = new FakeProvider
            {
                OnScan = async (settings, ct) =>
                {
                    await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
                    return default;
                }
            };
            RfidReaderDeviceState device = await CreateAsync(provider).ConfigureAwait(false);

            ServiceResultException timeout = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await device.Scan!.OnCallAsync!(
                    m_fixture!.Context, device.Scan, device.NodeId, AutoIdTestData.Settings(duration: 250),
                    CancellationToken.None).ConfigureAwait(false)))!;

            Assert.Multiple(() =>
            {
                Assert.That(timeout.StatusCode, Is.EqualTo((uint)StatusCodes.BadTimeout));
                Assert.That(device.DeviceStatus!.Value, Is.EqualTo(DeviceStatusEnumeration.Idle));
            });
        }

        [Test]
        public async Task ADurationAboveTheConfiguredMaximumIsInvalidAsync()
        {
            RfidReaderDeviceState device = await CreateAsync(new FakeProvider()).ConfigureAwait(false);

            ScanRfidReaderDeviceTypeMethodStateResult result = await device.Scan!.OnCallAsync!(
                m_fixture!.Context, device.Scan, device.NodeId, AutoIdTestData.Settings(duration: 301),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public async Task MoreResultsThanTheLimitFailAsync()
        {
            var provider = new FakeProvider
            {
                OnScan = (settings, ct) => new ValueTask<AutoIdScanOutcome>(new AutoIdScanOutcome(
                    Enumerable.Range(0, 4)
                        .Select(index => (ScanResult)AutoIdTestData.RfidResult(AutoIdTestData.Bytes(1, (byte)index)))
                        .ToArray().ToArrayOf(),
                    AutoIdOperationStatusEnumeration.SUCCESS))
            };
            RfidReaderDeviceState device = await CreateAsync(provider).ConfigureAwait(false);

            ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await device.Scan!.OnCallAsync!(
                    m_fixture!.Context, device.Scan, device.NodeId, AutoIdTestData.Settings(cycles: 1),
                    CancellationToken.None).ConfigureAwait(false)))!;

            Assert.That(failure.StatusCode, Is.EqualTo((uint)StatusCodes.BadResponseTooLarge));
        }

        [Test]
        public async Task AReadLongerThanTheLimitIsInvalidAndAccessEventsCanBeSwitchedOffAsync()
        {
            var provider = new FakeProvider();
            AutoIdReader reader = await m_fixture!.Runtime.CreateReaderAsync(
                m_fixture.Name(Unique()), provider).ConfigureAwait(false);
            var device = (RfidReaderDeviceState)reader.Device;
            var events = new EventCapture(device);
            ScanData id = AutoIdTestData.Epc(AutoIdTestData.Bytes(1, 2));

            ReadTagMethodStateResult tooLong = await device.ReadTag!.OnCallAsync!(
                m_fixture.Context, device.ReadTag, device.NodeId, id, "EPC", 1, 0, 9, default, CancellationToken.None)
                .ConfigureAwait(false);
            ReadTagMethodStateResult accepted = await device.ReadTag.OnCallAsync!(
                m_fixture.Context, device.ReadTag, device.NodeId, id, "EPC", 1, 0, 8, default, CancellationToken.None)
                .ConfigureAwait(false);
            await provider.Sink!.ReportScanAsync(
                new ScanResult[] { AutoIdTestData.RfidResult(AutoIdTestData.Bytes(3, 3)) }.ToArrayOf())
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(tooLong.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
                Assert.That(accepted.Status, Is.EqualTo(AutoIdOperationStatusEnumeration.SUCCESS));
                Assert.That(events.Of<RfidAccessEventState>(), Is.Empty);
                Assert.That(events.Of<RfidScanEventState>().Count(), Is.EqualTo(1), "scan events stay");
            });
        }

        private async Task<RfidReaderDeviceState> CreateAsync(FakeProvider provider)
        {
            AutoIdReader reader = await m_fixture!.Runtime.CreateReaderAsync(
                m_fixture.Name(Unique()), provider).ConfigureAwait(false);
            return (RfidReaderDeviceState)reader.Device;
        }

        private static string Unique()
        {
            return TestContext.CurrentContext.Test.Name;
        }

        private AutoIdManagerFixture? m_fixture;
    }
}
