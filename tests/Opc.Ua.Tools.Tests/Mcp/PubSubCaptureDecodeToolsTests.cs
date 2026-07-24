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
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using NUnit.Framework;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.Pcap.Capture;
using Opc.Ua.PubSub.Pcap;
using Opc.Ua.PubSub.Pcap.KeyLog;
using Opc.Ua.PubSub.Security;

namespace Opc.Ua.Tools.Tests.Mcp
{
    [TestFixture]
    [NonParallelizable]
    public sealed class PubSubCaptureDecodeToolsTests
    {
        private const string kTransportProfile =
            "http://opcfoundation.org/UA-Profile/Transport/pubsub-udp-uadp";

        private readonly List<string> m_files = [];

        [TearDown]
        public void TearDown()
        {
            foreach (string file in m_files)
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
            m_files.Clear();
        }

        [Test]
        public async Task CaptureWriteAndDecodeRoundTripsAsync()
        {
            var registry = new PubSubCaptureRegistry();
            await using var manager = new PubSubCaptureSessionManager(registry);
            PubSubCaptureSessionInfo started =
                await PubSubCaptureTools.StartCaptureAsync(
                    manager,
                    CancellationToken.None).ConfigureAwait(false);
            byte[] payload = await PubSubMcpTestHelpers.EncodeMinimalUadpAsync(
                1,
                2,
                3,
                new Variant(42)).ConfigureAwait(false);
            EmitFrame(registry, payload);

            PubSubCaptureSessionInfo active =
                await PubSubCaptureTools.CaptureStatusAsync(
                    manager,
                    CancellationToken.None).ConfigureAwait(false);
            Assert.That(
                () => PubSubCaptureTools.GetLastStoppedSourceAsync(
                    manager,
                    CancellationToken.None).AsTask(),
                Throws.TypeOf<PcapDiagnosticsException>());

            PubSubCaptureSessionInfo stopped =
                await PubSubCaptureTools.StopCaptureAsync(
                    manager,
                    CancellationToken.None).ConfigureAwait(false);
            PubSubCaptureSessionInfo stoppedStatus =
                await PubSubCaptureTools.CaptureStatusAsync(
                    manager,
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(started.IsActive, Is.True);
            Assert.That(active.FrameCount, Is.EqualTo(1));
            Assert.That(stopped.State, Is.EqualTo("stopped"));
            Assert.That(stoppedStatus.FrameCount, Is.EqualTo(1));

            IList<ContentBlock> text =
                await PubSubDecodeTools.DissectCaptureAsync(
                    manager,
                    "text").ConfigureAwait(false);
            IList<ContentBlock> json =
                await PubSubDecodeTools.DissectCaptureAsync(
                    manager,
                    "json").ConfigureAwait(false);
            Assert.That(text, Has.Count.EqualTo(1));
            Assert.That(json, Has.Count.EqualTo(1));

            string relativePcap = Path.Combine(
                "pubsub-mcp-tests",
                Guid.NewGuid().ToString("N") + ".pcap");
            string relativePcapNg = Path.ChangeExtension(relativePcap, ".pcapng");
            PubSubPcapWriteInfo pcap = await PubSubCaptureTools.WritePcapAsync(
                McpTestEnvironment.Services,
                manager,
                relativePcap,
                CancellationToken.None).ConfigureAwait(false);
            PubSubPcapWriteInfo pcapNg = await PubSubCaptureTools.WritePcapAsync(
                McpTestEnvironment.Services,
                manager,
                relativePcapNg,
                CancellationToken.None).ConfigureAwait(false);
            m_files.Add(pcap.FilePath);
            m_files.Add(pcapNg.FilePath);

            Assert.That(pcap.Format, Is.EqualTo("pcap"));
            Assert.That(pcap.FramesWritten, Is.EqualTo(1));
            Assert.That(pcapNg.Format, Is.EqualTo("pcapng"));
            Assert.That(File.Exists(pcap.FilePath), Is.True);

            IList<ContentBlock> decodedText =
                await PubSubDecodeTools.DecodePcapAsync(
                    McpTestEnvironment.Services,
                    pcap.FilePath,
                    "text").ConfigureAwait(false);
            IList<ContentBlock> decodedJson =
                await PubSubDecodeTools.DecodePcapAsync(
                    McpTestEnvironment.Services,
                    pcap.FilePath,
                    "json",
                    maxFrames: 1).ConfigureAwait(false);
            Assert.That(decodedText, Has.Count.EqualTo(1));
            Assert.That(decodedJson, Has.Count.EqualTo(1));
            Assert.That(
                () => PubSubDecodeTools.DecodePcapAsync(
                    McpTestEnvironment.Services,
                    pcap.FilePath,
                    "invalid"),
                Throws.TypeOf<PcapDiagnosticsException>());
        }

        [Test]
        public async Task LoadKeyLogAsyncLoadsAndCachesKeysAsync()
        {
            string directory = Path.Combine(
                McpTestEnvironment.PcapBaseFolder,
                "pubsub-mcp-tests");
            Directory.CreateDirectory(directory);
            string filePath = Path.Combine(
                directory,
                Guid.NewGuid().ToString("N") + ".uakeys.json");
            m_files.Add(filePath);
            using var material = new PubSubKeyMaterial(
                "group",
                7,
                PubSubSecurityPolicyUri.PubSubAes256Ctr,
                [1, 2, 3, 4],
                [5, 6, 7, 8],
                [9, 10]);
            await using (var writer = new PubSubKeyLogWriter(filePath))
            {
                await writer.AppendAsync(material).ConfigureAwait(false);
            }

            PubSubKeyLogInfo info = await PubSubDecodeTools.LoadKeyLogAsync(
                McpTestEnvironment.Services,
                filePath,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(info.FilePath, Is.EqualTo(filePath));
            Assert.That(info.KeyCount, Is.EqualTo(1));
        }

        [Test]
        public async Task CaptureAndDecodeToolsValidateArgumentsAndIdleStateAsync()
        {
            var registry = new PubSubCaptureRegistry();
            await using var manager = new PubSubCaptureSessionManager(registry);
            IPubSubCaptureSource source = await manager.StartAsync().ConfigureAwait(false);
            _ = await manager.StopAsync().ConfigureAwait(false);
            await source.DisposeAsync().ConfigureAwait(false);

            PubSubCaptureSessionInfo idle =
                await PubSubCaptureTools.StopCaptureAsync(
                    manager,
                    CancellationToken.None).ConfigureAwait(false);
            Assert.That(idle.State, Is.EqualTo("idle"));

            Assert.That(
                () => PubSubCaptureTools.StartCaptureAsync(
                    null!,
                    CancellationToken.None),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                () => PubSubCaptureTools.WritePcapAsync(
                    null!,
                    manager,
                    "file.pcap",
                    CancellationToken.None),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                () => PubSubDecodeTools.DecodePcapAsync(
                    null!,
                    "file.pcap"),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                () => PubSubDecodeTools.LoadKeyLogAsync(
                    McpTestEnvironment.Services,
                    string.Empty,
                    CancellationToken.None),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public async Task WritePcapAsyncWhileActiveStopsCaptureAndReplacesLastSourceAsync()
        {
            var registry = new PubSubCaptureRegistry();
            await using var manager = new PubSubCaptureSessionManager(registry);

            await PubSubCaptureTools.StartCaptureAsync(manager, CancellationToken.None)
                .ConfigureAwait(false);
            byte[] payload = await PubSubMcpTestHelpers.EncodeMinimalUadpAsync(
                1,
                2,
                3,
                new Variant(7)).ConfigureAwait(false);
            EmitFrame(registry, payload);

            // Writing a pcap while the capture is still active must stop it
            // first (PubSubCaptureTools.GetStoppedSourceAsync's "active"
            // branch) rather than throwing or requiring an explicit stop.
            string relativePcap = Path.Combine(
                "pubsub-mcp-tests",
                Guid.NewGuid().ToString("N") + ".pcap");
            PubSubPcapWriteInfo pcap = await PubSubCaptureTools.WritePcapAsync(
                McpTestEnvironment.Services,
                manager,
                relativePcap,
                CancellationToken.None).ConfigureAwait(false);
            m_files.Add(pcap.FilePath);

            Assert.Multiple(() =>
            {
                Assert.That(pcap.FramesWritten, Is.EqualTo(1));
                Assert.That(manager.ActiveSource, Is.Null);
            });

            // Starting a new capture while a previous stopped snapshot is
            // still cached must dispose that cached snapshot
            // (PubSubCaptureTools.ClearLastSourceAsync's dispose branch).
            PubSubCaptureSessionInfo restarted = await PubSubCaptureTools.StartCaptureAsync(
                manager,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(restarted.IsActive, Is.True);

            await PubSubCaptureTools.StopCaptureAsync(manager, CancellationToken.None)
                .ConfigureAwait(false);
        }

        private static void EmitFrame(
            PubSubCaptureRegistry registry,
            byte[] payload)
        {
            var context = new PubSubCaptureContext(
                PubSubCaptureDirection.Inbound,
                kTransportProfile,
                new DateTimeUtc(DateTime.UtcNow),
                "127.0.0.1:4840");
            registry.CurrentObserver!.OnFrameCaptured(in context, payload);
        }
    }
}
#endif
