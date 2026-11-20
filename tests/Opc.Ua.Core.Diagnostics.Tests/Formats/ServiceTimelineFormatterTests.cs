/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Pcap.Capture;
using Opc.Ua.Pcap.Formats;
using Opc.Ua.Pcap.Frame;
using Opc.Ua.Pcap.KeyLog;
using Opc.Ua.Pcap.Models;

namespace Opc.Ua.Pcap.Tests.Formats
{
    [TestFixture]
    public sealed class ServiceTimelineFormatterTests
    {
        [Test]
        public void MetadataDescribesTextualServiceTimelineFormat()
        {
            var formatter = new ServiceTimelineFormatter();

            Assert.That(formatter.Kind, Is.EqualTo(FormatKind.ServiceTimeline));
            Assert.That(formatter.MimeType, Is.EqualTo("text/plain"));
            Assert.That(formatter.IsBinary, Is.False);
        }

        [Test]
        public void FormatAsyncThrowsWhenSourceHasNoKeyMaterial()
        {
            var formatter = new ServiceTimelineFormatter();
            // Frames present but no key material → must fail per contract.
            CaptureFrame[] frames =
            [
                new CaptureFrame(
                    DateTimeOffset.UtcNow,
                    CaptureFrameDirection.ClientToServer,
                    string.Empty,
                    string.Empty,
                    "HELF"u8.ToArray())
            ];
            using var source = new InMemoryCaptureSource(frames);

            Assert.That(
                async () => await formatter.FormatAsync(source, maxFrames: null, CancellationToken.None)
                    .ConfigureAwait(false),
                Throws.TypeOf<PcapDiagnosticsException>()
                    .With.Message.Contains("Service timeline requires both captured frames and key material"));
        }

        [Test]
        public void FormatAsyncThrowsWhenSourceHasKeyMaterialButNoFrames()
        {
            var formatter = new ServiceTimelineFormatter();
            ChannelKeyMaterial material = PcapTestHelpers.CreateMaterial(
                SecurityPolicies.None,
                MessageSecurityMode.None);
            using var source = new InMemoryCaptureSource(
                materials: [material]);

            Assert.That(
                async () => await formatter.FormatAsync(source, maxFrames: null, CancellationToken.None)
                    .ConfigureAwait(false),
                Throws.TypeOf<PcapDiagnosticsException>()
                    .With.Message.Contains("Service timeline requires both captured frames and key material"));
        }

        [Test]
        public async Task FormatAsyncEmitsTimelineHeaderAndOpenSecureChannelRow()
        {
            var formatter = new ServiceTimelineFormatter();
            ChannelKeyMaterial material = PcapTestHelpers.CreateMaterial(
                SecurityPolicies.None,
                MessageSecurityMode.None);

            // 'OPNF' (Open + Final) chunk – the reassembler's
            // AddOpenSecureChannelMessage path doesn't require symmetric
            // key material to produce a completed call. 16-byte chunk =
            // 4 byte type marker + 4 byte length + 4 byte channelId
            // + 4 byte tokenId.
            byte[] open =
            [
                0x4F, 0x50, 0x4E, 0x46, // "OPNF" (TcpMessageType.Open + Final)
                0x10, 0x00, 0x00, 0x00, // length = 16
                0xAA, 0xBB, 0xCC, 0xDD, // channelId
                0x11, 0x22, 0x33, 0x44  // tokenId
            ];

            CaptureFrame[] frames =
            [
                new CaptureFrame(
                    new DateTimeOffset(2026, 5, 6, 7, 8, 9, TimeSpan.Zero),
                    CaptureFrameDirection.ClientToServer,
                    string.Empty,
                    string.Empty,
                    open)
            ];
            await using var source = new InMemoryCaptureSource(
                frames,
                materials: [material]);

            FormatResult result = await formatter.FormatAsync(source, maxFrames: null, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(result.FramesFormatted, Is.EqualTo(1));
            Assert.That(result.MimeType, Is.EqualTo("text/plain"));
            string body = Encoding.UTF8.GetString(result.Bytes);
            // Header line documents the columns of the timeline.
            Assert.That(body, Does.Contain("Timestamp"));
            Assert.That(body, Does.Contain("Channel"));
            Assert.That(body, Does.Contain("Service"));
            // An OPNF chunk produces an OpenSecureChannelRequest entry.
            Assert.That(body, Does.Contain("OpenSecureChannelRequest"));
        }

        [Test]
        public void FormatAsyncRejectsNullSource()
        {
            var formatter = new ServiceTimelineFormatter();

            Assert.That(
                async () => await formatter.FormatAsync(source: null!, maxFrames: null, CancellationToken.None)
                    .ConfigureAwait(false),
                Throws.TypeOf<ArgumentNullException>());
        }
    }
}
