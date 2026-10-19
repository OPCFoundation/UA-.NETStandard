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
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Pcap.Bindings;
using Opc.Ua.Pcap.Capture;
using Opc.Ua.Pcap.Capture.Sources;
using Opc.Ua.Pcap.Formats;
using Opc.Ua.Pcap.Frame;
using Opc.Ua.Pcap.Models;

namespace Opc.Ua.Pcap.Tests.Capture
{
    /// <summary>
    /// Exercises replay admission and raw export using only fixture-owned capture artifacts.
    /// </summary>
    [TestFixture]
    public sealed class SecurityF11ReplayCaptureTests
    {
        [SetUp]
        public void SetUp()
        {
            m_directory = Path.Combine(
                TestContext.CurrentContext.WorkDirectory, $"security-f11-{Guid.NewGuid():N}");
            Directory.CreateDirectory(m_directory);
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(m_directory, recursive: true);
        }

        [TestCase("keys.uakeys.json")]
        [TestCase("misnamed.pcap")]
        [TestCase("configured-artifact")]
        [Category("SecurityRegression")]
        public async Task SecurityF11ReplayCannotExportKeySidecarAsPcap(string name)
        {
            string path = Path.Combine(m_directory, name);
            await File.WriteAllTextAsync(path, kNotAKeyLog).ConfigureAwait(false);
            await using CaptureSessionManager manager = CreateManager();

            Assert.That(
                async () => await manager.StartAsync(new StartCaptureRequest
                {
                    Source = CaptureSourceKind.Replay,
                    PcapFilePath = path,
                    RequireCompleteReplay = false
                }, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<PcapDiagnosticsException>());
            Assert.That(manager.List(), Is.Empty, "A rejected artifact must not become an exportable session.");
        }

        [TestCase("empty")]
        [TestCase("pcap-magic-only")]
        [TestCase("pcap-version")]
        [TestCase("pcap-snapshot")]
        [TestCase("pcap-record-header")]
        [TestCase("pcap-record-body")]
        [TestCase("pcap-original-length")]
        [TestCase("pcap-captured-length")]
        [TestCase("pcap-timestamp")]
        [TestCase("pcap-keylog-tail")]
        [TestCase("pcapng-section")]
        [TestCase("pcapng-byte-order")]
        [TestCase("pcapng-block-body")]
        [TestCase("pcapng-block-alignment")]
        [TestCase("pcapng-block-trailer")]
        [TestCase("pcapng-interface")]
        [TestCase("pcapng-packet-length")]
        [TestCase("pcapng-tail")]
        [TestCase("pcapng-decryption-secrets")]
        [Category("SecurityRegression")]
        public async Task SecurityF11ReplayRejectsIncompleteOrMalformedContainers(string corruption)
        {
            string path = Path.Combine(m_directory, "import.pcap");
            byte[] bytes = await CreateMalformedCaptureAsync(corruption).ConfigureAwait(false);
            await File.WriteAllBytesAsync(path, bytes).ConfigureAwait(false);
            await using var source = new ReplayCaptureSource();

            await Assert.ThatAsync(
                async () => await source.StartAsync(new StartCaptureRequest
                {
                    Source = CaptureSourceKind.Replay,
                    PcapFilePath = path
                }, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<PcapDiagnosticsException>()).ConfigureAwait(false);
            Assert.That(source.GetRawPcapFilePath(), Is.Null);
            Assert.That(source.GetKeyLogFilePath(), Is.Null);

            byte[] valid = CreatePcap(littleEndian: true, nanoseconds: false);
            await File.WriteAllBytesAsync(path, valid).ConfigureAwait(false);
            await source.StartAsync(new StartCaptureRequest
            {
                Source = CaptureSourceKind.Replay,
                PcapFilePath = path
            }, CancellationToken.None).ConfigureAwait(false);
            await source.StopAsync(CancellationToken.None).ConfigureAwait(false);
            FormatResult exported = await new PcapFormatter().FormatAsync(source, null, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(exported.Bytes, Is.EqualTo(valid).AsCollection);
            Assert.That(exported.FramesFormatted, Is.EqualTo(1));
        }

        [TestCase(true, false)]
        [TestCase(false, false)]
        [TestCase(true, true)]
        [TestCase(false, true)]
        [Category("SecurityRegression")]
        public async Task SecurityF11CompletePcapReplaysAndExportsInEitherByteOrder(
            bool littleEndian,
            bool nanoseconds)
        {
            byte[] bytes = CreatePcap(littleEndian, nanoseconds);
            string path = Path.Combine(m_directory, "capture-without-extension");
            await File.WriteAllBytesAsync(path, bytes).ConfigureAwait(false);
            await using CaptureSessionManager manager = CreateManager();
            CaptureSession session = await manager.StartAsync(new StartCaptureRequest
            {
                Source = CaptureSourceKind.Replay,
                PcapFilePath = path
            }, CancellationToken.None).ConfigureAwait(false);
            await manager.StopAsync(session.Id, CancellationToken.None).ConfigureAwait(false);

            List<CaptureFrame> frames = await PcapTestHelpers.ToListAsync(
                session.Source.ReadCapturedFramesAsync(null, CancellationToken.None)).ConfigureAwait(false);
            FormatResult exported = await new PcapFormatter().FormatAsync(
                session.Source, null, CancellationToken.None).ConfigureAwait(false);
            FormatResult pcapNg = await new PcapNgFormatter().FormatAsync(
                session.Source, null, CancellationToken.None).ConfigureAwait(false);

            Assert.That(session.State, Is.EqualTo(CaptureSessionState.Completed));
            Assert.That(frames, Has.Count.EqualTo(1));
            Assert.That(frames[0].Data.ToArray(), Is.EqualTo(s_packet).AsCollection);
            Assert.That(frames[0].Timestamp, Is.EqualTo(
                DateTimeOffset.UnixEpoch.AddSeconds(1).AddTicks(nanoseconds ? 1_234_567 : 1_234_560)));
            Assert.That(exported.Kind, Is.EqualTo(FormatKind.Pcap));
            Assert.That(exported.Bytes, Is.EqualTo(bytes).AsCollection);
            Assert.That(exported.FramesFormatted, Is.EqualTo(1));
            Assert.That(pcapNg.Kind, Is.EqualTo(FormatKind.PcapNg));
            Assert.That(pcapNg.FramesFormatted, Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        [Category("SecurityRegression")]
        public async Task SecurityF11CompletePcapNgRemainsExportable(bool bigEndian)
        {
            byte[] bytes = await CreatePcapNgAsync(withFrame: true).ConfigureAwait(false);
            if (bigEndian)
            {
                foreach (int offset in new[] { 4, 8, 24, 28, 32, 40, 44, 48, 52, 56, 60, 64, 68, 72, 80 })
                {
                    bytes.AsSpan(offset, 4).Reverse();
                }

                bytes.AsSpan(12, 2).Reverse();
                bytes.AsSpan(36, 2).Reverse();
            }

            string path = Path.Combine(m_directory, "capture.data");
            await File.WriteAllBytesAsync(path, bytes).ConfigureAwait(false);
            await using CaptureSessionManager manager = CreateManager();
            CaptureSession session = await manager.StartAsync(new StartCaptureRequest
            {
                Source = CaptureSourceKind.Replay,
                PcapFilePath = path
            }, CancellationToken.None).ConfigureAwait(false);
            await manager.StopAsync(session.Id, CancellationToken.None).ConfigureAwait(false);

            foreach (ITraceFormatter formatter in new ITraceFormatter[] { new PcapFormatter(), new PcapNgFormatter() })
            {
                FormatResult exported = await formatter.FormatAsync(session.Source, null, CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(exported.Bytes, Is.EqualTo(bytes).AsCollection);
                Assert.That(exported.Kind, Is.EqualTo(FormatKind.PcapNg));
                Assert.That(exported.MimeType, Is.EqualTo("application/x-pcapng"));
                Assert.That(exported.FramesFormatted, Is.EqualTo(1));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        [Category("SecurityRegression")]
        public async Task SecurityF11CompleteEmptyCaptureRemainsExportable(bool pcapNg)
        {
            byte[] bytes = pcapNg
                ? await CreatePcapNgAsync(withFrame: false).ConfigureAwait(false)
                : CreatePcap(littleEndian: true, nanoseconds: false)[..24];
            string path = Path.Combine(m_directory, "empty-capture");
            await File.WriteAllBytesAsync(path, bytes).ConfigureAwait(false);
            await using var source = new ReplayCaptureSource();
            await source.StartAsync(new StartCaptureRequest
            {
                Source = CaptureSourceKind.Replay,
                PcapFilePath = path
            }, CancellationToken.None).ConfigureAwait(false);
            await source.StopAsync(CancellationToken.None).ConfigureAwait(false);

            FormatResult result = await new PcapFormatter().FormatAsync(source, null, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(result.Bytes, Is.EqualTo(bytes).AsCollection);
            Assert.That(result.FramesFormatted, Is.Zero);
        }

        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF11ReplayCannotReclassifyRegisteredKeyArtifact()
        {
            string capturePath = Path.Combine(m_directory, "capture");
            string keyPath = Path.Combine(m_directory, "configured-keys.pcap");
            byte[] bytes = CreatePcap(littleEndian: true, nanoseconds: false);
            await File.WriteAllBytesAsync(capturePath, bytes).ConfigureAwait(false);
            await File.WriteAllBytesAsync(keyPath, bytes).ConfigureAwait(false);
            await using CaptureSessionManager manager = CreateManager();
            CaptureSession owner = await manager.StartAsync(new StartCaptureRequest
            {
                Source = CaptureSourceKind.Replay,
                PcapFilePath = capturePath,
                KeyLogFilePath = keyPath
            }, CancellationToken.None).ConfigureAwait(false);
            await manager.StopAsync(owner.Id, CancellationToken.None).ConfigureAwait(false);

            Assert.That(
                async () => await manager.StartAsync(new StartCaptureRequest
                {
                    Source = CaptureSourceKind.Replay,
                    PcapFilePath = keyPath
                }, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<PcapDiagnosticsException>().With.Message.Contains("Key-log"));
            Assert.That(manager.List(), Has.Count.EqualTo(1));
            FormatResult permitted = await new PcapFormatter().FormatAsync(
                owner.Source, null, CancellationToken.None).ConfigureAwait(false);
            Assert.That(permitted.Bytes, Is.EqualTo(bytes).AsCollection);
        }

        [TestCase(FormatKind.Pcap, false)]
        [TestCase(FormatKind.PcapNg, false)]
        [TestCase(FormatKind.Pcap, true)]
        [TestCase(FormatKind.PcapNg, true)]
        [Category("SecurityRegression")]
        public async Task SecurityF11RawFormatterRejectsNonCaptureAndKeyArtifacts(FormatKind format, bool markedAsKey)
        {
            string path = Path.Combine(m_directory, "formatter-input.pcap");
            byte[] bytes = markedAsKey
                ? CreatePcap(littleEndian: true, nanoseconds: false)
                : Encoding.UTF8.GetBytes(kNotAKeyLog);
            await File.WriteAllBytesAsync(path, bytes).ConfigureAwait(false);
            await using var source = new InMemoryCaptureSource(
                pcapFilePath: path, keyLogFilePath: markedAsKey ? path : null);
            ITraceFormatter formatter = TraceFormatterRegistry.CreateDefault().Get(format);

            Assert.That(
                async () => await formatter.FormatAsync(source, null, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<PcapDiagnosticsException>());
        }

        [TestCase(FormatKind.Pcap)]
        [TestCase(FormatKind.PcapNg)]
        [Category("SecurityRegression")]
        public async Task SecurityF11RawExportRevalidatesFileAfterReplayStarts(FormatKind format)
        {
            string path = Path.Combine(m_directory, "capture.pcap");
            await File.WriteAllBytesAsync(path, CreatePcap(littleEndian: true, nanoseconds: false))
                .ConfigureAwait(false);
            await using var source = new ReplayCaptureSource();
            await source.StartAsync(new StartCaptureRequest
            {
                Source = CaptureSourceKind.Replay,
                PcapFilePath = path
            }, CancellationToken.None).ConfigureAwait(false);
            await source.StopAsync(CancellationToken.None).ConfigureAwait(false);
            await File.WriteAllTextAsync(path, kNotAKeyLog).ConfigureAwait(false);

            ITraceFormatter formatter = TraceFormatterRegistry.CreateDefault().Get(format);
            Assert.That(
                async () => await formatter.FormatAsync(source, null, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<PcapDiagnosticsException>());
        }

        private CaptureSessionManager CreateManager()
        {
            return new CaptureSessionManager(
                new DefaultCaptureSourceFactory(new ChannelCaptureRegistry()), m_directory);
        }

        private static byte[] CreatePcap(bool littleEndian, bool nanoseconds)
        {
            byte[] bytes = new byte[40 + s_packet.Length];
            WriteUInt32(bytes, nanoseconds ? 0xA1B23C4DU : 0xA1B2C3D4U, littleEndian);
            WriteUInt16(bytes.AsSpan(4), 2, littleEndian);
            WriteUInt16(bytes.AsSpan(6), 4, littleEndian);
            WriteUInt32(bytes.AsSpan(16), 65535, littleEndian);
            WriteUInt32(bytes.AsSpan(20), PcapFileWriter.LinkTypeRaw, littleEndian);
            WriteUInt32(bytes.AsSpan(24), 1, littleEndian);
            WriteUInt32(bytes.AsSpan(28), nanoseconds ? 123_456_789U : 123_456U, littleEndian);
            WriteUInt32(bytes.AsSpan(32), (uint)s_packet.Length, littleEndian);
            WriteUInt32(bytes.AsSpan(36), (uint)s_packet.Length, littleEndian);
            s_packet.CopyTo(bytes, 40);
            return bytes;
        }

        private static async Task<byte[]> CreatePcapNgAsync(bool withFrame)
        {
            using MemoryStream stream = new();
            await using (var writer = new PcapNgFileWriter(stream, PcapFileWriter.LinkTypeRaw))
            {
                if (withFrame)
                {
                    await writer.WriteAsync(DateTimeOffset.UnixEpoch.AddSeconds(1), s_packet, CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }

            return stream.ToArray();
        }

        private static async Task<byte[]> CreateMalformedCaptureAsync(string corruption)
        {
            byte[] bytes = corruption.StartsWith("pcapng", StringComparison.Ordinal)
                ? await CreatePcapNgAsync(withFrame: true).ConfigureAwait(false)
                : CreatePcap(littleEndian: true, nanoseconds: false);
            switch (corruption)
            {
                case "empty":
                    return [];
                case "pcap-magic-only":
                    return bytes[..4];
                case "pcap-version":
                    bytes[4] = 3;
                    break;
                case "pcap-snapshot":
                    bytes.AsSpan(16, 4).Clear();
                    break;
                case "pcap-record-header":
                    return bytes[..25];
                case "pcap-record-body":
                case "pcapng-block-body":
                    return bytes[..^1];
                case "pcap-original-length":
                    bytes.AsSpan(36, 4).Clear();
                    break;
                case "pcap-captured-length":
                    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(32), uint.MaxValue);
                    break;
                case "pcap-timestamp":
                    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(28), 1_000_000);
                    break;
                case "pcap-keylog-tail":
                    return [.. bytes, .. Encoding.UTF8.GetBytes(kNotAKeyLog)];
                case "pcapng-section":
                    return bytes[..20];
                case "pcapng-byte-order":
                    bytes.AsSpan(8, 4).Clear();
                    break;
                case "pcapng-block-alignment":
                    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(52), 35);
                    break;
                case "pcapng-block-trailer":
                    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(80), 32);
                    break;
                case "pcapng-interface":
                    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(56), 1);
                    break;
                case "pcapng-packet-length":
                    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(68), uint.MaxValue);
                    break;
                case "pcapng-tail":
                    return [.. bytes, 1];
                case "pcapng-decryption-secrets":
                    byte[] block = new byte[20];
                    BinaryPrimitives.WriteUInt32LittleEndian(block, 10);
                    BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(4), 20);
                    BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(16), 20);
                    return [.. bytes, .. block];
                default:
                    throw new ArgumentException("Unknown capture corruption.", nameof(corruption));
            }

            return bytes;
        }

        private static void WriteUInt16(Span<byte> bytes, ushort value, bool littleEndian)
        {
            if (littleEndian)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
            }
            else
            {
                BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
            }
        }

        private static void WriteUInt32(Span<byte> bytes, uint value, bool littleEndian)
        {
            if (littleEndian)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
            }
            else
            {
                BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
            }
        }

        private const string kNotAKeyLog = /*lang=json,strict*/ "{\"clientEncryptingKey\":\"security-regression-marker-not-a-key\"}";
        private static readonly byte[] s_packet = [0x45, 0, 42];
        private string m_directory = string.Empty;
    }
}
