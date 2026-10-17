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
using Opc.Ua.AutoID.Server;

namespace Opc.Ua.AutoID.Tests
{
    /// <summary>
    /// The Scan, ScanStart and ScanStop semantics of OPC 30010 6.1.3.4 to 6.1.3.6, exercised through
    /// the method handlers with a scriptable provider.
    /// </summary>
    [TestFixture]
    [Category("AutoID")]
    public sealed class AutoIdScanSemanticsTests
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

        public static IEnumerable<TestCaseData> InvalidSettings()
        {
            yield return new TestCaseData(AutoIdTestData.Settings()).SetName("every termination condition infinite");
            yield return new TestCaseData(AutoIdTestData.Settings(duration: -1, cycles: 1)).SetName("negative duration");
            yield return new TestCaseData(AutoIdTestData.Settings(duration: double.NaN, cycles: 1)).SetName("NaN duration");
            yield return new TestCaseData(AutoIdTestData.Settings(duration: double.PositiveInfinity, cycles: 1))
                .SetName("infinite duration value");
            yield return new TestCaseData(AutoIdTestData.Settings(cycles: -1, dataAvailable: true))
                .SetName("negative cycles");
            yield return new TestCaseData(AutoIdTestData.Settings(duration: 31_000)).SetName("duration above the maximum");
            yield return new TestCaseData(new ScanSettings
            {
                Duration = 100,
                EncodingMask = (uint)ScanSettingsFields.LocationType,
                LocationType = (LocationTypeEnumeration)99
            }).SetName("undefined location type");
        }

        [TestCaseSource(nameof(InvalidSettings))]
        public async Task InvalidSettingsFailTheScanWithBadInvalidArgumentAsync(ScanSettings settings)
        {
            (RfidReaderDeviceState device, FakeProvider provider) = await CreateAsync().ConfigureAwait(false);
            int scans = 0;
            provider.OnScan = (s, ct) =>
            {
                scans++;
                return default;
            };

            ScanRfidReaderDeviceTypeMethodStateResult result = await ScanAsync(device, settings).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
                Assert.That(scans, Is.Zero, "the provider is not asked to scan");
            });
        }

        [Test]
        public async Task MissingSettingsFailTheScanWithBadInvalidArgumentAsync()
        {
            (RfidReaderDeviceState device, _) = await CreateAsync().ConfigureAwait(false);

            ScanRfidReaderDeviceTypeMethodStateResult result = await ScanAsync(device, null!).ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public async Task AFiniteScanReturnsTypedResultsAndPublishesTheLastScanAsync()
        {
            (RfidReaderDeviceState device, FakeProvider provider) = await CreateAsync().ConfigureAwait(false);
            DeviceStatusEnumeration? duringScan = null;
            DateTimeUtc first = DateTimeUtc.Now;
            RfidScanResult one = AutoIdTestData.RfidResult(AutoIdTestData.Bytes(1, 2));
            one.Timestamp = first;
            one.Sighting = [new RfidSighting { Antenna = 3, Strength = -61 }];
            RfidScanResult two = AutoIdTestData.RfidResult(AutoIdTestData.Bytes(3, 4));
            provider.OnScan = (settings, ct) =>
            {
                duringScan = device.DeviceStatus!.Value;
                return new ValueTask<AutoIdScanOutcome>(new AutoIdScanOutcome(
                    new ScanResult[] { one, two }.ToArrayOf(), AutoIdOperationStatusEnumeration.SUCCESS));
            };

            ScanRfidReaderDeviceTypeMethodStateResult result = await ScanAsync(
                device, AutoIdTestData.Settings(duration: 100)).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
                Assert.That(result.Status, Is.EqualTo(AutoIdOperationStatusEnumeration.SUCCESS));
                Assert.That(result.ResultsOut.Count, Is.EqualTo(2));
                Assert.That(result.ResultsOut[0], Is.SameAs(one));
                Assert.That(duringScan, Is.EqualTo(DeviceStatusEnumeration.Busy));
                Assert.That(device.DeviceStatus!.Value, Is.EqualTo(DeviceStatusEnumeration.Idle));
                Assert.That(device.LastScanTimestamp!.Value, Is.EqualTo(first));
                Assert.That(device.LastScanData!.Timestamp, Is.EqualTo(first), "equal to the timestamp variable");
                Assert.That(device.LastScanData.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(EpcOf(device.LastScanData.WrappedValue).UId, Is.EqualTo(AutoIdTestData.Bytes(1, 2)));
                Assert.That(device.LastScanAntenna!.Value, Is.EqualTo(3));
                Assert.That(device.LastScanRSSI!.Value, Is.EqualTo(-61));
            });
        }

        [Test]
        public async Task AScanWithoutResultsReturnsTheProviderStatusAsync()
        {
            (RfidReaderDeviceState device, _) = await CreateAsync().ConfigureAwait(false);

            ScanRfidReaderDeviceTypeMethodStateResult result = await ScanAsync(
                device, AutoIdTestData.Settings(cycles: 1)).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
                Assert.That(result.Status, Is.EqualTo(AutoIdOperationStatusEnumeration.NO_IDENTIFIER));
                Assert.That(result.ResultsOut.Count, Is.Zero);
                Assert.That(device.LastScanData!.StatusCode, Is.EqualTo(StatusCodes.UncertainInitialValue));
            });
        }

        [Test]
        public async Task OnlyOneScanRunsAtATimeAsync()
        {
            (RfidReaderDeviceState device, FakeProvider provider) = await CreateAsync().ConfigureAwait(false);
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            provider.OnScan = async (settings, ct) =>
            {
                started.TrySetResult(true);
                await release.Task.ConfigureAwait(false);
                return default;
            };
            Task<ScanRfidReaderDeviceTypeMethodStateResult> first = ScanAsync(
                device, AutoIdTestData.Settings(duration: 1000)).AsTask();
            await started.Task.ConfigureAwait(false);

            ScanRfidReaderDeviceTypeMethodStateResult second = await ScanAsync(
                device, AutoIdTestData.Settings(duration: 100)).ConfigureAwait(false);
            ScanStartMethodStateResult start = await StartAsync(device, AutoIdTestData.Settings()).ConfigureAwait(false);
            DeviceStatusEnumeration whileBusy = device.DeviceStatus!.Value;
            release.SetResult(true);
            await first.ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(second.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(start.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(whileBusy, Is.EqualTo(DeviceStatusEnumeration.Busy));
                Assert.That(provider.StartCalls, Is.Zero);
                Assert.That(device.DeviceStatus!.Value, Is.EqualTo(DeviceStatusEnumeration.Idle));
            });
        }

        [Test]
        public async Task AFailingProviderLeavesTheReaderIdleAsync()
        {
            (RfidReaderDeviceState device, FakeProvider provider) = await CreateAsync().ConfigureAwait(false);
            provider.OnScan = (settings, ct) => throw new InvalidOperationException("antenna cable unplugged");

            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await ScanAsync(device, AutoIdTestData.Settings(cycles: 1)).ConfigureAwait(false));

            provider.OnScan = (settings, ct) => new ValueTask<AutoIdScanOutcome>(
                new AutoIdScanOutcome(default, AutoIdOperationStatusEnumeration.NO_IDENTIFIER));
            ScanRfidReaderDeviceTypeMethodStateResult retry = await ScanAsync(
                device, AutoIdTestData.Settings(cycles: 1)).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(device.DeviceStatus!.Value, Is.EqualTo(DeviceStatusEnumeration.Idle));
                Assert.That(ServiceResult.IsGood(retry.ServiceResult), Is.True);
            });
        }

        [Test]
        public async Task ACallerCancellationPropagatesAndFreesTheReaderAsync()
        {
            (RfidReaderDeviceState device, FakeProvider provider) = await CreateAsync().ConfigureAwait(false);
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            provider.OnScan = async (settings, ct) =>
            {
                started.TrySetResult(true);
                await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
                return default;
            };
            using var cancel = new CancellationTokenSource();
            Task<ScanRfidReaderDeviceTypeMethodStateResult> call = ScanAsync(
                device, AutoIdTestData.Settings(duration: 1000), null, cancel.Token).AsTask();
            await started.Task.ConfigureAwait(false);

            await cancel.CancelAsync().ConfigureAwait(false);

            Assert.CatchAsync<OperationCanceledException>(async () => await call.ConfigureAwait(false));
            Assert.That(device.DeviceStatus!.Value, Is.EqualTo(DeviceStatusEnumeration.Idle));
        }

        [Test]
        public async Task ACancellationTheProviderRaisesOnItsOwnIsNotMistakenForShutdownAsync()
        {
            (RfidReaderDeviceState device, FakeProvider provider) = await CreateAsync().ConfigureAwait(false);
            provider.OnScan = (settings, ct) => throw new OperationCanceledException("the antenna gave up");
            provider.OnRead = ct => throw new OperationCanceledException("the antenna gave up");

            OperationCanceledException scan = Assert.CatchAsync<OperationCanceledException>(
                async () => await ScanAsync(device, AutoIdTestData.Settings(cycles: 1)).ConfigureAwait(false))!;
            OperationCanceledException read = Assert.CatchAsync<OperationCanceledException>(
                async () => await device.ReadTag!.OnCallAsync!(
                    m_fixture!.Context, device.ReadTag, device.NodeId,
                    AutoIdTestData.Epc(AutoIdTestData.Bytes(1, 2)), "EPC", 1, 0, 4, default, CancellationToken.None)
                    .ConfigureAwait(false))!;

            Assert.Multiple(() =>
            {
                Assert.That(scan.Message, Is.EqualTo("the antenna gave up"));
                Assert.That(read.Message, Is.EqualTo("the antenna gave up"));
                Assert.That(device.DeviceStatus!.Value, Is.EqualTo(DeviceStatusEnumeration.Idle));
            });
        }

        [Test]
        public async Task AProviderResultOfTheWrongTypeFailsTheScanAsync()
        {
            (RfidReaderDeviceState device, FakeProvider provider) = await CreateAsync().ConfigureAwait(false);
            provider.OnScan = (settings, ct) => new ValueTask<AutoIdScanOutcome>(new AutoIdScanOutcome(
                new ScanResult[] { new OcrScanResult { CodeType = "RAW:STRING" } }.ToArrayOf(),
                AutoIdOperationStatusEnumeration.SUCCESS));

            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(
                async () => await ScanAsync(device, AutoIdTestData.Settings(cycles: 1)).ConfigureAwait(false))!;

            Assert.Multiple(() =>
            {
                Assert.That(failure.StatusCode, Is.EqualTo((uint)StatusCodes.BadInternalError));
                Assert.That(device.DeviceStatus!.Value, Is.EqualTo(DeviceStatusEnumeration.Idle));
            });
        }

        [Test]
        public async Task TooManyResultsFailTheScanInsteadOfTruncatingAsync()
        {
            (RfidReaderDeviceState device, FakeProvider provider) = await CreateAsync().ConfigureAwait(false);
            ScanResult[] many = [.. Enumerable.Range(0, 1001).Select(index => (ScanResult)AutoIdTestData.RfidResult(AutoIdTestData.Bytes(1, (byte)index)))];
            provider.OnScan = (settings, ct) => new ValueTask<AutoIdScanOutcome>(
                new AutoIdScanOutcome(many.ToArrayOf(), AutoIdOperationStatusEnumeration.SUCCESS));

            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(
                async () => await ScanAsync(device, AutoIdTestData.Settings(cycles: 1)).ConfigureAwait(false))!;

            Assert.That(failure.StatusCode, Is.EqualTo((uint)StatusCodes.BadResponseTooLarge));
        }

        [Test]
        public async Task AFaultyDeviceReportsErrorAndRefusesToScanAsync()
        {
            (RfidReaderDeviceState device, FakeProvider provider) = await CreateAsync().ConfigureAwait(false);
            AutoIdReader reader = ReaderOf(device);
            await reader.SetFaultedAsync(true).ConfigureAwait(false);

            ScanRfidReaderDeviceTypeMethodStateResult scan = await ScanAsync(
                device, AutoIdTestData.Settings(cycles: 1)).ConfigureAwait(false);
            ScanStartMethodStateResult start = await StartAsync(device, AutoIdTestData.Settings()).ConfigureAwait(false);
            DeviceStatusEnumeration faulty = reader.Status;
            await reader.SetFaultedAsync(true).ConfigureAwait(false);
            await reader.SetFaultedAsync(false).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(faulty, Is.EqualTo(DeviceStatusEnumeration.Error));
                Assert.That(ServiceResult.IsGood(scan.ServiceResult), Is.True);
                Assert.That(scan.Status, Is.EqualTo(AutoIdOperationStatusEnumeration.DEVICE_NOT_READY));
                Assert.That(start.Status, Is.EqualTo(AutoIdOperationStatusEnumeration.DEVICE_NOT_READY));
                Assert.That(provider.StartCalls, Is.Zero);
                Assert.That(reader.Status, Is.EqualTo(DeviceStatusEnumeration.Idle));
            });
        }

        [Test]
        public async Task AFaultStopsTheAsynchronousScanAsync()
        {
            (RfidReaderDeviceState device, FakeProvider provider) = await CreateAsync().ConfigureAwait(false);
            AutoIdReader reader = ReaderOf(device);
            await StartAsync(device, AutoIdTestData.Settings()).ConfigureAwait(false);

            await reader.SetFaultedAsync(true).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(provider.StopCalls, Is.EqualTo(1));
                Assert.That(reader.IsAsyncScanActive, Is.False);
                Assert.That(reader.Status, Is.EqualTo(DeviceStatusEnumeration.Error));
            });
        }

        [Test]
        public async Task ScanStartMarksTheReaderScanningUntilStoppedAsync()
        {
            (RfidReaderDeviceState device, FakeProvider provider) = await CreateAsync().ConfigureAwait(false);
            AutoIdReader reader = ReaderOf(device);

            ScanStartMethodStateResult start = await StartAsync(device, AutoIdTestData.Settings()).ConfigureAwait(false);
            ScanStartMethodStateResult again = await StartAsync(device, AutoIdTestData.Settings()).ConfigureAwait(false);
            DeviceStatusEnumeration scanning = reader.Status;
            ServiceResult stop = await StopAsync(device).ConfigureAwait(false);
            ServiceResult stopAgain = await StopAsync(device).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(start.ServiceResult), Is.True);
                Assert.That(start.Status, Is.EqualTo(AutoIdOperationStatusEnumeration.SUCCESS));
                Assert.That(again.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(scanning, Is.EqualTo(DeviceStatusEnumeration.Scanning));
                Assert.That(ServiceResult.IsGood(stop), Is.True);
                Assert.That(stopAgain.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(provider.StartCalls, Is.EqualTo(1));
                Assert.That(provider.StopCalls, Is.EqualTo(1));
                Assert.That(reader.Status, Is.EqualTo(DeviceStatusEnumeration.Idle));
            });
        }

        [Test]
        public async Task ScanStartWithInvalidSettingsFailsAndInfiniteSettingsAreAllowedAsync()
        {
            (RfidReaderDeviceState device, FakeProvider provider) = await CreateAsync().ConfigureAwait(false);

            ScanStartMethodStateResult invalid = await StartAsync(
                device, AutoIdTestData.Settings(duration: -5)).ConfigureAwait(false);
            ScanStartMethodStateResult infinite = await StartAsync(
                device, AutoIdTestData.Settings()).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(invalid.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
                Assert.That(ServiceResult.IsGood(infinite.ServiceResult), Is.True);
                Assert.That(provider.StartCalls, Is.EqualTo(1));
            });
        }

        [Test]
        public async Task AScanThatDoesNotStartLeavesTheReaderIdleAsync()
        {
            (RfidReaderDeviceState device, FakeProvider provider) = await CreateAsync().ConfigureAwait(false);
            AutoIdReader reader = ReaderOf(device);
            provider.OnStart = (settings, ct) => new ValueTask<AutoIdOperationStatusEnumeration>(
                AutoIdOperationStatusEnumeration.DEVICE_FAULT);

            ScanStartMethodStateResult refused = await StartAsync(device, AutoIdTestData.Settings()).ConfigureAwait(false);
            provider.OnStart = (settings, ct) => throw new InvalidOperationException("no radio");
            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await StartAsync(device, AutoIdTestData.Settings()).ConfigureAwait(false));

            Assert.Multiple(() =>
            {
                Assert.That(refused.Status, Is.EqualTo(AutoIdOperationStatusEnumeration.DEVICE_FAULT));
                Assert.That(reader.IsAsyncScanActive, Is.False);
                Assert.That(reader.Status, Is.EqualTo(DeviceStatusEnumeration.Idle));
            });
        }

        [Test]
        public async Task AScanThatEndsOnItsOwnReturnsTheReaderToIdleAsync()
        {
            (RfidReaderDeviceState device, FakeProvider provider) = await CreateAsync().ConfigureAwait(false);
            AutoIdReader reader = ReaderOf(device);
            await StartAsync(device, AutoIdTestData.Settings(duration: 50)).ConfigureAwait(false);

            await provider.Sink!.CompleteScanAsync().ConfigureAwait(false);
            await provider.Sink.CompleteScanAsync().ConfigureAwait(false);
            ScanStartMethodStateResult next = await StartAsync(device, AutoIdTestData.Settings()).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(next.ServiceResult), Is.True, "a completed scan frees the reader");
                Assert.That(provider.StopCalls, Is.Zero, "the provider ended it, no stop is sent");
                Assert.That(reader.IsAsyncScanActive, Is.True);
            });
        }

        [Test]
        public async Task ACompletionDuringStartDoesNotLeaveTheReaderStuckAsync()
        {
            (RfidReaderDeviceState device, FakeProvider provider) = await CreateAsync().ConfigureAwait(false);
            AutoIdReader reader = ReaderOf(device);
            provider.OnStart = async (settings, ct) =>
            {
                await provider.Sink!.CompleteScanAsync(ct).ConfigureAwait(false);
                return AutoIdOperationStatusEnumeration.NO_IDENTIFIER;
            };

            ScanStartMethodStateResult start = await StartAsync(
                device, AutoIdTestData.Settings(dataAvailable: true)).ConfigureAwait(false);
            provider.OnStart = (settings, ct) => new ValueTask<AutoIdOperationStatusEnumeration>(
                AutoIdOperationStatusEnumeration.SUCCESS);
            ScanStartMethodStateResult next = await StartAsync(device, AutoIdTestData.Settings()).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(start.Status, Is.EqualTo(AutoIdOperationStatusEnumeration.NO_IDENTIFIER));
                Assert.That(ServiceResult.IsGood(next.ServiceResult), Is.True);
                Assert.That(reader.IsAsyncScanActive, Is.True);
            });
        }

        [Test]
        public async Task ClosingTheStartingSessionStopsTheScanButOtherSessionsDoNotAsync()
        {
            (RfidReaderDeviceState device, FakeProvider provider) = await CreateAsync().ConfigureAwait(false);
            AutoIdReader reader = ReaderOf(device);
            var owner = new NodeId(Guid.NewGuid());
            var other = new NodeId(Guid.NewGuid());
            await StartAsync(device, AutoIdTestData.Settings(), new TestSessionContext(m_fixture!.Context, owner))
                .ConfigureAwait(false);

            await reader.OnSessionClosingAsync(other, CancellationToken.None).ConfigureAwait(false);
            await reader.OnSessionClosingAsync(NodeId.Null, CancellationToken.None).ConfigureAwait(false);
            bool stillActive = reader.IsAsyncScanActive;
            await reader.OnSessionClosingAsync(owner, CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(stillActive, Is.True);
                Assert.That(reader.IsAsyncScanActive, Is.False);
                Assert.That(provider.StopCalls, Is.EqualTo(1));
                Assert.That(reader.Status, Is.EqualTo(DeviceStatusEnumeration.Idle));
            });
        }

        [Test]
        public async Task AnyoneMayStopAScanAnotherSessionStartedAsync()
        {
            (RfidReaderDeviceState device, FakeProvider provider) = await CreateAsync().ConfigureAwait(false);
            await StartAsync(
                device,
                AutoIdTestData.Settings(),
                new TestSessionContext(m_fixture!.Context, new NodeId(Guid.NewGuid()))).ConfigureAwait(false);

            ServiceResult stop = await StopAsync(device).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(stop), Is.True);
                Assert.That(provider.StopCalls, Is.EqualTo(1));
            });
        }

        [Test]
        public async Task ConcurrentStartsAdmitExactlyOneAsync()
        {
            (RfidReaderDeviceState device, FakeProvider provider) = await CreateAsync().ConfigureAwait(false);

            ScanStartMethodStateResult[] results = await Task.WhenAll(
                Enumerable.Range(0, 8).Select(_ => Task.Run(
                    async () => await StartAsync(device, AutoIdTestData.Settings()).ConfigureAwait(false))))
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(results.Count(result => ServiceResult.IsGood(result.ServiceResult)), Is.EqualTo(1));
                Assert.That(
                    results.Count(result => result.ServiceResult.StatusCode == StatusCodes.BadInvalidState),
                    Is.EqualTo(7));
                Assert.That(provider.StartCalls, Is.EqualTo(1));
            });
        }

        [Test]
        public async Task ReportedScansUpdateTheLastScanAndRaiseTheScanEventAsync()
        {
            (RfidReaderDeviceState device, FakeProvider provider) = await CreateAsync().ConfigureAwait(false);
            var events = new EventCapture(device);
            RfidScanResult result = AutoIdTestData.RfidResult(AutoIdTestData.Bytes("\t\t"u8.ToArray()));

            await provider.Sink!.ReportScanAsync(new ScanResult[] { result }.ToArrayOf()).ConfigureAwait(false);

            RfidScanEventState scan = events.Of<RfidScanEventState>().Single();
            Assert.Multiple(() =>
            {
                Assert.That(scan.DeviceName!.Value, Is.EqualTo(device.DeviceName!.Value));
                Assert.That(scan.ScanResult!.Value.Count, Is.EqualTo(1));
                Assert.That(scan.ScanResult.Value[0], Is.EqualTo(result));
                Assert.That(device.LastScanTimestamp!.Value, Is.EqualTo(result.Timestamp));
                Assert.That(events.Of<RfidAccessEventState>().Single().Command!.Value, Is.EqualTo("Scan"));
            });
        }

        [Test]
        public async Task EveryReaderKindRaisesItsOwnScanEventTypeAsync()
        {
            AutoIdRuntime runtime = m_fixture!.Runtime;
            var ocr = new MinimalProvider(AutoIdReaderKind.Ocr);
            var optical = new MinimalProvider(AutoIdReaderKind.Optical);
            var verifier = new MinimalProvider(AutoIdReaderKind.OpticalVerifier);
            var rtls = new FakeRtlsProvider(AutoIdReaderKind.Rtls, [LocationTypeEnumeration.NAME]);
            var captures = new Dictionary<string, EventCapture>();
            foreach ((string name, IAutoIdReaderProvider provider) in new (string, IAutoIdReaderProvider)[]
            {
                ("KindOcr", ocr), ("KindOptical", optical), ("KindVerifier", verifier), ("KindRtls", rtls)
            })
            {
                AutoIdReader reader = await runtime.CreateReaderAsync(m_fixture.Name(name), provider).ConfigureAwait(false);
                captures[name] = new EventCapture(reader.Device);
                ScanResult result = provider.Kind switch
                {
                    AutoIdReaderKind.Ocr => new OcrScanResult { CodeType = "RAW:STRING" },
                    AutoIdReaderKind.Optical => new OpticalScanResult { CodeType = "RAW:STRING" },
                    AutoIdReaderKind.OpticalVerifier => new OpticalVerifierScanResult { CodeType = "RAW:STRING" },
                    _ => new RtlsLocationResult { CodeType = "UID" }
                };
                result.ScanData = new ScanData { SwitchField = ScanDataFields.String, String = "A" };
                result.Timestamp = DateTimeUtc.Now;
                await reader.ReportScanAsync(new[] { result }.ToArrayOf()).ConfigureAwait(false);
            }

            Assert.Multiple(() =>
            {
                Assert.That(captures["KindOcr"].Of<OcrScanEventState>().Count(), Is.EqualTo(1));
                Assert.That(captures["KindOptical"].Of<OpticalScanEventState>().Count(), Is.EqualTo(1));
                Assert.That(captures["KindVerifier"].Of<OpticalVerifierScanEventState>().Count(), Is.EqualTo(1));
                Assert.That(captures["KindRtls"].Of<RtlsLocationEventState>().Count(), Is.EqualTo(1));
                Assert.That(captures["KindOcr"].Of<AutoIdAccessEventState>().Single().Command!.Value, Is.EqualTo("Scan"));
            });
        }

        [Test]
        public async Task InvalidReportsAreRejectedAsync()
        {
            (RfidReaderDeviceState device, FakeProvider provider) = await CreateAsync().ConfigureAwait(false);
            var events = new EventCapture(device);
            IAutoIdReaderSink sink = provider.Sink!;
            ScanResult[] many = [.. Enumerable.Range(0, 1001).Select(index => (ScanResult)AutoIdTestData.RfidResult(AutoIdTestData.Bytes(1, (byte)index)))];
            using var cancelled = new CancellationTokenSource();
            await cancelled.CancelAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.ThrowsAsync<ArgumentException>(async () => await sink.ReportScanAsync(default).ConfigureAwait(false));
                Assert.That(
                    Assert.ThrowsAsync<ServiceResultException>(async () => await sink.ReportScanAsync(
                        new ScanResult[] { new OcrScanResult() }.ToArrayOf()).ConfigureAwait(false))!.StatusCode,
                    Is.EqualTo((uint)StatusCodes.BadInternalError));
                Assert.That(
                    Assert.ThrowsAsync<ServiceResultException>(async () => await sink.ReportScanAsync(
                        many.ToArrayOf()).ConfigureAwait(false))!.StatusCode,
                    Is.EqualTo((uint)StatusCodes.BadResponseTooLarge));
                Assert.CatchAsync<OperationCanceledException>(async () => await sink.ReportScanAsync(
                    new ScanResult[] { AutoIdTestData.RfidResult(AutoIdTestData.Bytes(1, 1)) }.ToArrayOf(),
                    cancelled.Token).ConfigureAwait(false));
                Assert.That(events.Events, Is.Empty, "a rejected report raises no event");
            });
        }

        [Test]
        public async Task PresenceAndLogEntriesRaiseTheirEventsAsync()
        {
            (RfidReaderDeviceState device, FakeProvider provider) = await CreateAsync().ConfigureAwait(false);
            var events = new EventCapture(device);

            await provider.Sink!.ReportPresenceAsync(3).ConfigureAwait(false);
            await provider.Sink.ReportLogEntryAsync("antenna 2 reconnected").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(events.Of<AutoIdPresenceEventState>().Single().Presence!.Value, Is.EqualTo(3));
                Assert.That(
                    events.Of<AutoIdLogEntryEventState>().Single().Message!.Value.Text,
                    Is.EqualTo("antenna 2 reconnected"));
                Assert.ThrowsAsync<ArgumentException>(
                    async () => await provider.Sink.ReportLogEntryAsync(string.Empty).ConfigureAwait(false));
            });
        }

        [Test]
        public async Task ReportedAccessRaisesTheAccessEventAsync()
        {
            (RfidReaderDeviceState device, FakeProvider provider) = await CreateAsync().ConfigureAwait(false);
            var events = new EventCapture(device);
            var access = new RfidAccessResult
            {
                EncodingMask = (uint)RfidAccessResultFields.CodeType,
                CodeType = "EPC"
            };

            await provider.Sink!.ReportAccessAsync("ReadTag", "pedal", new AccessResult[] { access }.ToArrayOf())
                .ConfigureAwait(false);

            RfidAccessEventState e = events.Of<RfidAccessEventState>().Single();
            Assert.Multiple(() =>
            {
                Assert.That(e.Command!.Value, Is.EqualTo("ReadTag"));
                Assert.That(e.Client!.Value, Is.EqualTo("pedal"));
                Assert.That(e.AccessResult!.Value[0], Is.EqualTo(access));
                Assert.ThrowsAsync<ArgumentException>(async () => await provider.Sink.ReportAccessAsync(
                    string.Empty, "x", new AccessResult[] { access }.ToArrayOf()).ConfigureAwait(false));
                Assert.ThrowsAsync<ArgumentException>(async () => await provider.Sink.ReportAccessAsync(
                    "ReadTag", "x", default).ConfigureAwait(false));
                Assert.ThrowsAsync<ArgumentException>(async () => await provider.Sink.ReportAccessAsync(
                    "ReadTag", "x", new AccessResult[] { new() }.ToArrayOf()).ConfigureAwait(false));
            });
        }

        [Test]
        public async Task ReportsAfterDisposalFailAsync()
        {
            (RfidReaderDeviceState device, FakeProvider provider) = await CreateAsync().ConfigureAwait(false);
            await ReaderOf(device).DisposeAsync().ConfigureAwait(false);
            IAutoIdReaderSink sink = provider.Sink!;

            Assert.Multiple(() =>
            {
                Assert.ThrowsAsync<ObjectDisposedException>(async () => await sink.ReportPresenceAsync(1).ConfigureAwait(false));
                Assert.ThrowsAsync<ObjectDisposedException>(async () => await sink.ReportLogEntryAsync("x").ConfigureAwait(false));
                Assert.ThrowsAsync<ObjectDisposedException>(async () => await sink.CompleteScanAsync().ConfigureAwait(false));
                Assert.ThrowsAsync<ObjectDisposedException>(async () => await sink.ReportScanAsync(
                    new ScanResult[] { AutoIdTestData.RfidResult(AutoIdTestData.Bytes(1, 1)) }.ToArrayOf())
                    .ConfigureAwait(false));
            });
        }

        private async Task<(RfidReaderDeviceState Device, FakeProvider Provider)> CreateAsync()
        {
            var provider = new FakeProvider();
            AutoIdReader reader = await m_fixture!.Runtime.CreateReaderAsync(
                m_fixture.Name(TestContext.CurrentContext.Test.Name.Replace(' ', '_') + "_" + (++s_counter)),
                provider).ConfigureAwait(false);
            return ((RfidReaderDeviceState)reader.Device, provider);
        }

        private AutoIdReader ReaderOf(RfidReaderDeviceState device)
        {
            return m_fixture!.Runtime.Readers.ToArray()!.Single(reader => reader.Device == device);
        }

        private ValueTask<ScanRfidReaderDeviceTypeMethodStateResult> ScanAsync(
            RfidReaderDeviceState device,
            ScanSettings settings,
            ISystemContext? context = null,
            CancellationToken cancellationToken = default)
        {
            return device.Scan!.OnCallAsync!(
                context ?? m_fixture!.Context, device.Scan, device.NodeId, settings, cancellationToken);
        }

        private ValueTask<ScanStartMethodStateResult> StartAsync(
            RfidReaderDeviceState device,
            ScanSettings settings,
            ISystemContext? context = null)
        {
            return device.ScanStart!.OnCallAsync!(
                context ?? m_fixture!.Context, device.ScanStart, device.NodeId, settings, CancellationToken.None);
        }

        private ValueTask<ServiceResult> StopAsync(RfidReaderDeviceState device)
        {
            return device.ScanStop!.OnCallMethod2Async!(
                m_fixture!.Context, device.ScanStop, device.NodeId, default, [], CancellationToken.None);
        }

        private static ScanDataEpc EpcOf(Variant value)
        {
#pragma warning disable CS8600 // The generic structure accessor declares its out value as possibly null.
            Assert.That(value.TryGetValue(out ExtensionObject extension), Is.True);
            Assert.That(extension.TryGetValue(out ScanDataEpc epc, null), Is.True);
#pragma warning restore CS8600
            return epc!;
        }

        private static int s_counter;
        private AutoIdManagerFixture? m_fixture;
    }
}
