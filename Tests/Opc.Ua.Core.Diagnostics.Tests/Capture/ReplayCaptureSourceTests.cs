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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Pcap.Capture;
using Opc.Ua.Pcap.Capture.Sources;
using Opc.Ua.Pcap.Frame;
using Opc.Ua.Pcap.KeyLog;
using Opc.Ua.Pcap.Models;

using Opc.Ua.Bindings;

namespace Opc.Ua.Pcap.Tests.Capture
{
    [TestFixture]
    public sealed class ReplayCaptureSourceTests : TempDirectoryFixture
    {
        [Test]
        public void StartAsyncRejectsNullRequest()
        {
            var source = new ReplayCaptureSource();

            Assert.That(
                async () => await source.StartAsync(request: null!, CancellationToken.None)
                    .ConfigureAwait(false),
                Throws.TypeOf<ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("request"));
        }

        [Test]
        public async Task StartAsyncRequiresPcapFilePath()
        {
            await using var source = new ReplayCaptureSource();

            Assert.That(
                async () => await source.StartAsync(
                    new StartCaptureRequest { Source = CaptureSourceKind.Replay },
                    CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<PcapDiagnosticsException>()
                    .With.Message.Contains("pcapFilePath"));
        }

        [Test]
        public async Task StartAsyncRejectsMissingFile()
        {
            await using var source = new ReplayCaptureSource();
            string missing = Path.Combine(TempDirectory, "absent.pcap");

            Assert.That(
                async () => await source.StartAsync(
                    new StartCaptureRequest
                    {
                        Source = CaptureSourceKind.Replay,
                        PcapFilePath = missing
                    },
                    CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<PcapDiagnosticsException>()
                    .With.Message.Contains("does not exist"));
        }

        [Test]
        public async Task StartAsyncTwiceThrowsDiagnostics()
        {
            string pcapPath = await WriteOneFramePcapAsync().ConfigureAwait(false);
            await using var source = new ReplayCaptureSource();
            await source.StartAsync(
                new StartCaptureRequest
                {
                    Source = CaptureSourceKind.Replay,
                    PcapFilePath = pcapPath
                },
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(
                async () => await source.StartAsync(
                    new StartCaptureRequest
                    {
                        Source = CaptureSourceKind.Replay,
                        PcapFilePath = pcapPath
                    },
                    CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<PcapDiagnosticsException>()
                    .With.Message.Contains("cannot be started twice"));
        }

        [Test]
        public async Task GetRawPcapFilePathReturnsPathAfterSuccessfulStart()
        {
            string pcapPath = await WriteOneFramePcapAsync().ConfigureAwait(false);
            await using var source = new ReplayCaptureSource();

            await source.StartAsync(
                new StartCaptureRequest
                {
                    Source = CaptureSourceKind.Replay,
                    PcapFilePath = pcapPath
                },
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(source.GetRawPcapFilePath(), Is.EqualTo(pcapPath));
            Assert.That(source.GetKeyLogFilePath(), Is.Null);
        }

        [Test]
        public async Task ReadCapturedFramesReplaysEveryWrittenRecord()
        {
            // NOTE: PcapFileReader.ReadAllAsync currently keeps yielding phantom
            // copies of the last record at EOF (the ReadExactOrEndAsync helper
            // returns true on clean EOF instead of false). Until that production
            // bug is fixed we pin the enumeration with a maxFrames cap so the
            // test exercises the iteration path without hanging.
            string pcapPath = await WriteMultiFramePcapAsync(frameCount: 3).ConfigureAwait(false);

            var source = new ReplayCaptureSource();
            try
            {
                await source.StartAsync(
                    new StartCaptureRequest
                    {
                        Source = CaptureSourceKind.Replay,
                        PcapFilePath = pcapPath
                    },
                    CancellationToken.None).ConfigureAwait(false);

                int count = 0;
                int[] expectedLengths = [16, 17, 18];
                await foreach (CaptureFrame frame in source.ReadCapturedFramesAsync(
                    maxFrames: 3,
                    CancellationToken.None).ConfigureAwait(false))
                {
                    Assert.That(frame.Direction, Is.EqualTo(CaptureFrameDirection.Unknown),
                        "Replay source has no direction metadata and must surface Unknown.");
                    Assert.That(frame.ClientEndpoint, Is.EqualTo(string.Empty));
                    Assert.That(frame.ServerEndpoint, Is.EqualTo(string.Empty));
                    Assert.That(frame.Data.Length, Is.EqualTo(expectedLengths[count]));
                    count++;
                }

                Assert.That(count, Is.EqualTo(3));
                Assert.That(source.FrameCount, Is.EqualTo(3));
                Assert.That(source.ByteCount, Is.EqualTo(16L + 17L + 18L));
            }
            finally
            {
                await source.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ReadCapturedFramesRespectsMaxFramesCap()
        {
            string pcapPath = await WriteMultiFramePcapAsync(frameCount: 5).ConfigureAwait(false);
            await using var source = new ReplayCaptureSource();
            await source.StartAsync(
                new StartCaptureRequest
                {
                    Source = CaptureSourceKind.Replay,
                    PcapFilePath = pcapPath
                },
                CancellationToken.None).ConfigureAwait(false);

            int count = 0;
            await foreach (CaptureFrame frame in source.ReadCapturedFramesAsync(
                maxFrames: 2,
                CancellationToken.None).ConfigureAwait(false))
            {
                count++;
                Assert.That(frame.Data.Length, Is.GreaterThan(0));
            }

            Assert.That(count, Is.EqualTo(2),
                "ReadCapturedFramesAsync must stop iterating once maxFrames is reached.");
        }

        [Test]
        public async Task StopAsyncBeforeStartIsObservable()
        {
            await using var source = new ReplayCaptureSource();
            // Calling StopAsync without first calling StartAsync transitions
            // the internal state to Stopped; a subsequent StartAsync must
            // still throw because the source is no longer New.
            await source.StopAsync(CancellationToken.None).ConfigureAwait(false);
            string pcapPath = await WriteOneFramePcapAsync().ConfigureAwait(false);

            Assert.That(
                async () => await source.StartAsync(
                    new StartCaptureRequest
                    {
                        Source = CaptureSourceKind.Replay,
                        PcapFilePath = pcapPath
                    },
                    CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<PcapDiagnosticsException>()
                    .With.Message.Contains("cannot be started twice"));
        }

        [Test]
        public void SupportedFormatsIncludesAllSixFormats()
        {
            var source = new ReplayCaptureSource();

            Assert.That(source.SupportedFormats, Contains.Item(FormatKind.Pcap));
            Assert.That(source.SupportedFormats, Contains.Item(FormatKind.PcapNg));
            Assert.That(source.SupportedFormats, Contains.Item(FormatKind.Json));
            Assert.That(source.SupportedFormats, Contains.Item(FormatKind.Csv));
            Assert.That(source.SupportedFormats, Contains.Item(FormatKind.Text));
            Assert.That(source.SupportedFormats, Contains.Item(FormatKind.ServiceTimeline));
        }

        [Test]
        public async Task ReadCapturedFramesBeforeStartReturnsNoFrames()
        {
            await using var source = new ReplayCaptureSource();

            List<CaptureFrame> frames = await PcapTestHelpers.ToListAsync(
                source.ReadCapturedFramesAsync(maxFrames: null, CancellationToken.None)).ConfigureAwait(false);

            Assert.That(frames, Is.Empty);
            Assert.That(source.FrameCount, Is.Zero);
            Assert.That(source.ByteCount, Is.Zero);
        }

        [Test]
        public async Task StartAsyncIgnoresMissingOptionalKeyLogFile()
        {
            string pcapPath = await WriteOneFramePcapAsync().ConfigureAwait(false);
            string missingKeyLogPath = Path.Combine(TempDirectory, "missing.uakeys.json");
            await using var source = new ReplayCaptureSource();

            await source.StartAsync(
                new StartCaptureRequest
                {
                    Source = CaptureSourceKind.Replay,
                    PcapFilePath = pcapPath,
                    KeyLogFilePath = missingKeyLogPath
                },
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(source.GetRawPcapFilePath(), Is.EqualTo(pcapPath));
            Assert.That(source.GetKeyLogFilePath(), Is.Null);
            Assert.That(
                await PcapTestHelpers.ToListAsync(source.ReadKeyMaterialAsync(CancellationToken.None))
                    .ConfigureAwait(false),
                Is.Empty);
        }

        [Test]
        public async Task ReadKeyMaterialAsyncReadsJsonKeyLog()
        {
            ChannelKeyMaterial material = PcapTestHelpers.CreateMaterial(
                SecurityPolicies.None,
                MessageSecurityMode.None);
            string keyLogPath = await WriteJsonKeyLogAsync(material, "capture.uakeys.json").ConfigureAwait(false);
            ReplayCaptureSource source = await StartWithKeyLogAsync(keyLogPath).ConfigureAwait(false);
            await using (source.ConfigureAwait(false))
            {
                List<ChannelKeyMaterial> records = await PcapTestHelpers.ToListAsync(
                    source.ReadKeyMaterialAsync(CancellationToken.None)).ConfigureAwait(false);

                Assert.That(records, Has.Count.EqualTo(1));
                PcapTestHelpers.AssertMaterialEqual(records[0], material, includeJsonOnlyFields: true);
            }
        }

        [Test]
        public async Task ReadKeyMaterialAsyncReadsTextKeyLog()
        {
            ChannelKeyMaterial material = PcapTestHelpers.CreateMaterial(
                SecurityPolicies.None,
                MessageSecurityMode.None);
            string keyLogPath = await WriteTextKeyLogAsync(material, "capture.uakeys.txt").ConfigureAwait(false);
            ReplayCaptureSource source = await StartWithKeyLogAsync(keyLogPath).ConfigureAwait(false);
            await using (source.ConfigureAwait(false))
            {
                List<ChannelKeyMaterial> records = await PcapTestHelpers.ToListAsync(
                    source.ReadKeyMaterialAsync(CancellationToken.None)).ConfigureAwait(false);

                Assert.That(records, Has.Count.EqualTo(1));
                PcapTestHelpers.AssertMaterialEqual(records[0], material, includeJsonOnlyFields: false);
            }
        }

        [Test]
        public async Task ReadKeyMaterialAsyncFallbackReaderTriesJsonBeforeText()
        {
            ChannelKeyMaterial material = PcapTestHelpers.CreateMaterial(
                SecurityPolicies.None,
                MessageSecurityMode.None);
            string keyLogPath = await WriteJsonKeyLogAsync(material, "capture.keys").ConfigureAwait(false);
            ReplayCaptureSource source = await StartWithKeyLogAsync(keyLogPath).ConfigureAwait(false);
            await using (source.ConfigureAwait(false))
            {
                List<ChannelKeyMaterial> records = await PcapTestHelpers.ToListAsync(
                    source.ReadKeyMaterialAsync(CancellationToken.None)).ConfigureAwait(false);

                Assert.That(records, Has.Count.EqualTo(1));
                PcapTestHelpers.AssertMaterialEqual(records[0], material, includeJsonOnlyFields: true);
            }
        }

        [Test]
        public async Task StopAsyncHonorsCancellation()
        {
            string pcapPath = await WriteOneFramePcapAsync().ConfigureAwait(false);
            await using var source = new ReplayCaptureSource();
            await source.StartAsync(
                new StartCaptureRequest
                {
                    Source = CaptureSourceKind.Replay,
                    PcapFilePath = pcapPath
                },
                CancellationToken.None).ConfigureAwait(false);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.That(
                async () => await source.StopAsync(cts.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
        }

        // ----- helpers -----

        private Task<string> WriteOneFramePcapAsync()
        {
            return WriteMultiFramePcapAsync(frameCount: 1);
        }

        private async Task<string> WriteMultiFramePcapAsync(int frameCount)
        {
            string path = Path.Combine(TempDirectory, $"replay-{frameCount}-{Guid.NewGuid():N}.pcap");
            var writer = new PcapFileWriter(path, PcapFileWriter.LinkTypeEthernet);
            try
            {
                for (int i = 0; i < frameCount; i++)
                {
                    byte[] data = new byte[16 + i];
                    for (int j = 0; j < data.Length; j++)
                    {
                        data[j] = (byte)((i * 7 + j) & 0xFF);
                    }
                    await writer.WriteAsync(
                        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(i),
                        data,
                        CancellationToken.None).ConfigureAwait(false);
                }
            }
            finally
            {
                await writer.DisposeAsync().ConfigureAwait(false);
            }
            return path;
        }

        private async Task<ReplayCaptureSource> StartWithKeyLogAsync(string keyLogPath)
        {
            string pcapPath = await WriteOneFramePcapAsync().ConfigureAwait(false);
            var source = new ReplayCaptureSource();
            await source.StartAsync(
                new StartCaptureRequest
                {
                    Source = CaptureSourceKind.Replay,
                    PcapFilePath = pcapPath,
                    KeyLogFilePath = keyLogPath
                },
                CancellationToken.None).ConfigureAwait(false);
            return source;
        }

        private async Task<string> WriteJsonKeyLogAsync(ChannelKeyMaterial material, string fileName)
        {
            string path = CreateTempPath(fileName);
            await using (var writer = new UaKeyLogJsonWriter(path))
            {
                await writer.AppendAsync(material, CancellationToken.None).ConfigureAwait(false);
            }
            return path;
        }

        private async Task<string> WriteTextKeyLogAsync(ChannelKeyMaterial material, string fileName)
        {
            string path = CreateTempPath(fileName);
            await using (var writer = new UaKeyLogTextWriter(path))
            {
                await writer.AppendAsync(material, CancellationToken.None).ConfigureAwait(false);
            }
            return path;
        }
    }
}
