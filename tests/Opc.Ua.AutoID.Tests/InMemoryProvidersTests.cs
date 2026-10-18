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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.AutoID.Server;

namespace Opc.Ua.AutoID.Tests
{
    /// <summary>
    /// The simulated OCR, optical, verifier and RTLS providers.
    /// </summary>
    [TestFixture]
    [Category("AutoID")]
    public sealed class InMemoryProvidersTests
    {
        [Test]
        public void TheScanReaderServesOnlyOcrAndOpticalReaders()
        {
            Assert.Multiple(() =>
            {
                Assert.DoesNotThrow(() => new InMemoryScanReaderProvider(AutoIdReaderKind.Ocr).Dispose());
                Assert.DoesNotThrow(() => new InMemoryScanReaderProvider(AutoIdReaderKind.Optical).Dispose());
                Assert.Throws<ArgumentOutOfRangeException>(() => new InMemoryScanReaderProvider(AutoIdReaderKind.Rfid));
                Assert.Throws<ArgumentOutOfRangeException>(() => new InMemoryScanReaderProvider(AutoIdReaderKind.Rtls));
            });
        }

        [Test]
        public async Task ACodeMustMatchTheReaderKindAsync()
        {
            using var ocr = new InMemoryScanReaderProvider(AutoIdReaderKind.Ocr, TimeSpan.FromMilliseconds(2));
            using var optical = new InMemoryScanReaderProvider(AutoIdReaderKind.Optical, TimeSpan.FromMilliseconds(2));
            ocr.Present(new OcrScanResult { CodeType = "RAW:STRING" });
            optical.Present(new OpticalVerifierScanResult { CodeType = "RAW:STRING" });

            AutoIdScanOutcome ocrOutcome = await ocr.ScanAsync(AutoIdTestData.Settings(cycles: 1)).ConfigureAwait(false);
            AutoIdScanOutcome opticalOutcome = await optical.ScanAsync(AutoIdTestData.Settings(cycles: 1)).ConfigureAwait(false);
            ocr.Clear();
            AutoIdScanOutcome cleared = await ocr.ScanAsync(AutoIdTestData.Settings(cycles: 1)).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.Throws<ArgumentException>(() => ocr.Present(new OpticalScanResult()));
                Assert.Throws<ArgumentException>(() => optical.Present(new OcrScanResult()));
                Assert.Throws<ArgumentNullException>(() => ocr.Present(null!));
                Assert.That(ocrOutcome.Results.Count, Is.EqualTo(1));
                Assert.That(opticalOutcome.Results[0], Is.TypeOf<OpticalVerifierScanResult>(), "a subtype is an optical result");
                Assert.That(cleared.Status, Is.EqualTo(AutoIdOperationStatusEnumeration.NO_IDENTIFIER));
            });
        }

        [Test]
        public async Task TheScanReaderReportsAsynchronouslyAndStopsAsync()
        {
            using var reader = new InMemoryScanReaderProvider(AutoIdReaderKind.Optical, TimeSpan.FromMilliseconds(2));
            reader.Present(new OpticalScanResult { CodeType = "RAW:STRING" });
            var sink = new RecordingSink();
            await reader.AttachAsync(sink).ConfigureAwait(false);

            AutoIdOperationStatusEnumeration started = await reader.StartScanAsync(AutoIdTestData.Settings(cycles: 3))
                .ConfigureAwait(false);
            await sink.WaitForCompletionAsync().ConfigureAwait(false);
            await reader.StartScanAsync(AutoIdTestData.Settings()).ConfigureAwait(false);
            await sink.WaitForScansAsync(5).ConfigureAwait(false);
            await reader.StopScanAsync().ConfigureAwait(false);
            await reader.StopScanAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(started, Is.EqualTo(AutoIdOperationStatusEnumeration.SUCCESS));
                Assert.That(sink.Completions, Is.EqualTo(1));
                Assert.ThrowsAsync<ArgumentNullException>(async () => await reader.AttachAsync(null!).ConfigureAwait(false));
            });
        }

        [Test]
        public async Task TheVerifierReportsVerificationResultsAsync()
        {
            using var verifier = new InMemoryOpticalVerifierProvider(TimeSpan.FromMilliseconds(2));
            var result = new OpticalVerifierScanResult { CodeType = "RAW:STRING", IsoGrade = "A" };
            verifier.Present(result);
            var sink = new RecordingSink();
            await verifier.AttachAsync(sink).ConfigureAwait(false);

            AutoIdOperationStatusEnumeration started = await verifier.StartScanAsync(
                AutoIdTestData.Settings(dataAvailable: true)).ConfigureAwait(false);
            await sink.WaitForCompletionAsync().ConfigureAwait(false);
            verifier.Clear();
            await verifier.DetachAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(verifier.Kind, Is.EqualTo(AutoIdReaderKind.OpticalVerifier));
                Assert.That(started, Is.EqualTo(AutoIdOperationStatusEnumeration.SUCCESS));
                Assert.That(((OpticalVerifierScanResult)sink.Scans[0][0]).IsoGrade, Is.EqualTo("A"));
                Assert.Throws<ArgumentNullException>(() => verifier.Present(null!));
                Assert.ThrowsAsync<ArgumentNullException>(async () => await verifier.AttachAsync(null!).ConfigureAwait(false));
            });
        }

        [Test]
        public async Task TheLocatingSystemKnowsRegisteredObjectsAsync()
        {
            using var rtls = new InMemoryRtlsProvider(cycleInterval: TimeSpan.FromMilliseconds(2));
            ByteString id = AutoIdTestData.Bytes(4, 5, 6);
            var located = new RtlsLocationResult
            {
                CodeType = "UID",
                ScanData = new ScanData { SwitchField = ScanDataFields.ByteString, ByteString = id },
                Timestamp = DateTimeUtc.Now,
                EncodingMask = (uint)ScanResultFields.Location,
                Location = new Location
                {
                    SwitchField = LocationFields.WGS84,
                    WGS84 = new WGS84Coordinate { Latitude = 48.1, Longitude = 11.5 }
                }
            };
            rtls.Locate(id, located);

            AutoIdScanOutcome scan = await rtls.ScanAsync(AutoIdTestData.Settings(cycles: 1)).ConfigureAwait(false);
            RtlsLocationResult? byByteString = await rtls.GetLocationAsync(
                new ScanData { SwitchField = ScanDataFields.ByteString, ByteString = id },
                "UID", LocationTypeEnumeration.WGS84).ConfigureAwait(false);
            RtlsLocationResult? byEpc = await rtls.GetLocationAsync(
                AutoIdTestData.Epc(id), "UID", LocationTypeEnumeration.WGS84).ConfigureAwait(false);
            RtlsLocationResult? otherType = await rtls.GetLocationAsync(
                AutoIdTestData.Epc(id), "UID", LocationTypeEnumeration.NAME).ConfigureAwait(false);
            RtlsLocationResult? byString = await rtls.GetLocationAsync(
                new ScanData { SwitchField = ScanDataFields.String, String = "x" },
                "UID", LocationTypeEnumeration.WGS84).ConfigureAwait(false);
            bool forgotten = rtls.Forget(id);
            bool forgottenAgain = rtls.Forget(id);

            Assert.Multiple(() =>
            {
                Assert.That(rtls.Kind, Is.EqualTo(AutoIdReaderKind.Rtls));
                Assert.That(
                    rtls.SupportedLocationTypes.ToArray(),
                    Is.EqualTo([LocationTypeEnumeration.WGS84, LocationTypeEnumeration.LOCAL]));
                Assert.That(scan.Results.Count, Is.EqualTo(1));
                Assert.That(byByteString, Is.SameAs(located));
                Assert.That(byEpc, Is.SameAs(located));
                Assert.That(otherType, Is.SameAs(located), "a known object keeps its stored location");
                Assert.That(byString, Is.Null);
                Assert.That(forgotten, Is.True);
                Assert.That(forgottenAgain, Is.False);
                Assert.Throws<ArgumentNullException>(() => rtls.Locate(id, null!));
                Assert.Throws<ArgumentException>(() => rtls.Locate(default, located));
            });
        }

        [Test]
        public async Task TheLocationConverterTranslatesIntoTheRequestedTypeAsync()
        {
            int calls = 0;
            using var rtls = new InMemoryRtlsProvider(
                new[] { LocationTypeEnumeration.LOCAL, LocationTypeEnumeration.WGS84, LocationTypeEnumeration.NAME }
                    .ToArrayOf(),
                TimeSpan.FromMilliseconds(2),
                (location, type) =>
                {
                    calls++;
                    return type switch
                    {
                        LocationTypeEnumeration.WGS84 => new Location
                        {
                            SwitchField = LocationFields.WGS84,
                            WGS84 = new WGS84Coordinate { Latitude = 48.0 + location.Local.Y, Longitude = 11.0 }
                        },
                        // A converter that answers with another type than requested is ignored.
                        LocationTypeEnumeration.NAME => new Location { SwitchField = LocationFields.NMEA, NMEA = "x" },
                        _ => null
                    };
                });
            ByteString id = AutoIdTestData.Bytes(4, 5, 7);
            var located = new RtlsLocationResult
            {
                CodeType = "UID",
                ScanData = new ScanData { SwitchField = ScanDataFields.ByteString, ByteString = id },
                Timestamp = DateTimeUtc.Now,
                EncodingMask = (uint)ScanResultFields.Location,
                Location = new Location { SwitchField = LocationFields.Local, Local = new LocalCoordinate { Y = 1.0 } }
            };
            rtls.Locate(id, located);
            rtls.Locate(AutoIdTestData.Bytes(1), new RtlsLocationResult { CodeType = "UID" });
            var byteString = new ScanData { SwitchField = ScanDataFields.ByteString, ByteString = id };

            RtlsLocationResult? local = await rtls.GetLocationAsync(byteString, "UID", LocationTypeEnumeration.LOCAL)
                .ConfigureAwait(false);
            RtlsLocationResult? wgs84 = await rtls.GetLocationAsync(byteString, "UID", LocationTypeEnumeration.WGS84)
                .ConfigureAwait(false);
            RtlsLocationResult? wrongAnswer = await rtls.GetLocationAsync(
                byteString, "UID", LocationTypeEnumeration.NAME).ConfigureAwait(false);
            RtlsLocationResult? noAnswer = await rtls.GetLocationAsync(byteString, "UID", LocationTypeEnumeration.NMEA)
                .ConfigureAwait(false);
            RtlsLocationResult? noLocation = await rtls.GetLocationAsync(
                new ScanData { SwitchField = ScanDataFields.ByteString, ByteString = AutoIdTestData.Bytes(1) },
                "UID", LocationTypeEnumeration.WGS84).ConfigureAwait(false);
            RtlsLocationResult? unknown = await rtls.GetLocationAsync(
                new ScanData { SwitchField = ScanDataFields.ByteString, ByteString = AutoIdTestData.Bytes(2) },
                "UID", LocationTypeEnumeration.WGS84).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(local, Is.SameAs(located));
                Assert.That(wgs84, Is.Not.SameAs(located));
                Assert.That(wgs84!.Location.WGS84.Latitude, Is.EqualTo(49.0));
                Assert.That(wgs84.ScanData.ByteString, Is.EqualTo(id));
                Assert.That(wgs84.Timestamp, Is.EqualTo(located.Timestamp));
                Assert.That(
                    located.Location.SwitchField,
                    Is.EqualTo(LocationFields.Local),
                    "the stored result is unchanged");
                Assert.That(wrongAnswer, Is.SameAs(located));
                Assert.That(noAnswer, Is.SameAs(located));
                Assert.That(noLocation!.Location?.SwitchField ?? LocationFields.None, Is.EqualTo(LocationFields.None));
                Assert.That(unknown, Is.Null);
                Assert.That(calls, Is.EqualTo(3));
            });
        }

        [Test]
        public async Task TheLocatingSystemReportsAndStopsAsynchronouslyAsync()
        {
            using var rtls = new InMemoryRtlsProvider(
                new[] { LocationTypeEnumeration.NAME }.ToArrayOf(), TimeSpan.FromMilliseconds(2));
            rtls.Locate(AutoIdTestData.Bytes(1), new RtlsLocationResult { CodeType = "UID", Timestamp = DateTimeUtc.Now });
            var sink = new RecordingSink();
            await rtls.AttachAsync(sink).ConfigureAwait(false);

            await rtls.StartScanAsync(AutoIdTestData.Settings()).ConfigureAwait(false);
            await sink.WaitForScansAsync(2).ConfigureAwait(false);
            await rtls.StopScanAsync().ConfigureAwait(false);
            await rtls.DetachAsync().ConfigureAwait(false);

            Assert.That(rtls.SupportedLocationTypes.ToArray(), Is.EqualTo([LocationTypeEnumeration.NAME]));
        }

        [Test]
        public async Task DisposingAProviderStopsItsLoopAsync()
        {
            var reader = new InMemoryScanReaderProvider(AutoIdReaderKind.Ocr, TimeSpan.FromMilliseconds(2));
            reader.Present(new OcrScanResult { CodeType = "RAW:STRING" });
            var sink = new RecordingSink();
            await reader.AttachAsync(sink).ConfigureAwait(false);
            await reader.StartScanAsync(AutoIdTestData.Settings()).ConfigureAwait(false);
            await sink.WaitForScansAsync(1).ConfigureAwait(false);

            reader.Dispose();
            int count = sink.Scans.Count;
            await Task.Delay(40).ConfigureAwait(false);

            Assert.That(sink.Scans, Has.Count.LessThanOrEqualTo(count + 1));
        }
    }
}
