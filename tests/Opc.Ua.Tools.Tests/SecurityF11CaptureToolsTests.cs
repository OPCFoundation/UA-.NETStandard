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
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.Pcap.Capture;
using Opc.Ua.Pcap.Formats;
using Opc.Ua.Pcap.Frame;
using Opc.Ua.Pcap.KeyLog;
using Opc.Ua.Pcap.Models;

namespace Opc.Ua.Tools.Tests.CaptureAuthorization
{
    /// <summary>
    /// Exercises ordinary capture tools with diagnostics key-disclosure tools disabled and no live OPC UA sessions.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class SecurityF11CaptureToolsTests
    {
        [SetUp]
        public void SetUp()
        {
            m_directory = Path.Combine(
                TestContext.CurrentContext.WorkDirectory, $"security-f11-tools-{Guid.NewGuid():N}");
            Directory.CreateDirectory(m_directory);
            var services = new ServiceCollection();
            services.AddOpcUaMcpCore(new OpcUaMcpOptions { PcapBaseFolder = m_directory });
            services.AddOpcUaMcpDiagnostics(options =>
            {
                options.BaseFolder = m_directory;
                options.EnableDiagnosticsTools = false;
            });
            services.AddMcpServer().WithOpcUaDiagnosticsTools(
                McpToolProfile.Full, diagnosticsToolsEnabled: false);
            m_services = services.BuildServiceProvider();
            m_formatters = m_services.GetRequiredService<TraceFormatterRegistry>();
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            if (m_services is not null)
            {
                await m_services.DisposeAsync().ConfigureAwait(false);
            }

            if (Directory.Exists(m_directory))
            {
                Directory.Delete(m_directory, recursive: true);
            }
        }

        [TestCase("keys.uakeys.json")]
        [TestCase("misnamed.pcap")]
        [TestCase("custom-artifact")]
        [Category("SecurityRegression")]
        public async Task SecurityF11CaptureToolsCannotReplayDisclosedKeyLogPath(string name)
        {
            CaptureSessionManager manager = m_services.GetRequiredService<CaptureSessionManager>();
            OpcUaSessionManager sessions = m_services.GetRequiredService<OpcUaSessionManager>();
            (string pcap, _, _) = await CreateCaptureAsync().ConfigureAwait(false);
            string keyLog = Path.Combine(m_directory, name);
            await File.WriteAllTextAsync(keyLog, kNotAKeyLog).ConfigureAwait(false);
            CaptureSessionInfo started = await PacketCaptureTools.StartCaptureAsync(
                manager,
                sessions,
                new StartCaptureRequest
                {
                    Source = CaptureSourceKind.Replay,
                    PcapFilePath = pcap,
                    KeyLogFilePath = keyLog
                },
                CancellationToken.None).ConfigureAwait(false);
            await PacketCaptureTools.StopCaptureAsync(manager, started.SessionId, CancellationToken.None)
                .ConfigureAwait(false);
            IReadOnlyList<CaptureSessionInfo> listed = PacketCaptureTools.ListCaptures(manager);
            Assert.That(listed, Has.Count.EqualTo(1));
            Assert.That(listed[0].KeyLogFilePath, Is.EqualTo(keyLog));
            Assert.That(m_services.GetServices<McpServerTool>().Select(tool => tool.ProtocolTool.Name),
                Does.Contain("start_capture").And.Contain("get_capture").And.Not.Contain("dump_keys"));

            Assert.That(
                () => ReplayAndExportRawCaptureAsync(listed[0].KeyLogFilePath!),
                Throws.TypeOf<PcapDiagnosticsException>());
            Assert.That(PacketCaptureTools.ListCaptures(manager), Has.Count.EqualTo(1));
            BlobResourceContents exported = GetBlob(await PacketCaptureTools.GetCaptureAsync(
                manager, m_formatters, started.SessionId, "pcap").ConfigureAwait(false));
            Assert.That(exported.Blob.ToArray(),
                Is.EqualTo(await File.ReadAllBytesAsync(pcap).ConfigureAwait(false)).AsCollection);
            Assert.That(Encoding.UTF8.GetString(exported.Blob.ToArray()), Does.Not.Contain(kMarker));
        }

        [TestCase("pcap")]
        [TestCase("pcapng")]
        [Category("SecurityRegression")]
        public async Task SecurityF11CaptureToolsRevalidateStoppedReplayBeforeExport(string format)
        {
            CaptureSessionManager manager = m_services.GetRequiredService<CaptureSessionManager>();
            OpcUaSessionManager sessions = m_services.GetRequiredService<OpcUaSessionManager>();
            (string pcap, _, _) = await CreateCaptureAsync().ConfigureAwait(false);
            CaptureSessionInfo started = await PacketCaptureTools.StartCaptureAsync(
                manager,
                sessions,
                new StartCaptureRequest { Source = CaptureSourceKind.Replay, PcapFilePath = pcap },
                CancellationToken.None).ConfigureAwait(false);
            await PacketCaptureTools.StopCaptureAsync(manager, started.SessionId, CancellationToken.None)
                .ConfigureAwait(false);
            await File.WriteAllTextAsync(pcap, kNotAKeyLog).ConfigureAwait(false);

            Assert.That(
                () => PacketCaptureTools.GetCaptureAsync(manager, m_formatters, started.SessionId, format),
                Throws.TypeOf<PcapDiagnosticsException>());
        }

        [TestCase("pcap")]
        [TestCase("pcapng")]
        [TestCase("json")]
        [TestCase("csv")]
        [TestCase("text")]
        [TestCase("service-timeline")]
        [Category("SecurityRegression")]
        public async Task SecurityF11CaptureToolsPreserveSupportedCaptureFormats(string format)
        {
            CaptureSessionManager manager = m_services.GetRequiredService<CaptureSessionManager>();
            OpcUaSessionManager sessions = m_services.GetRequiredService<OpcUaSessionManager>();
            (string pcap, string keys, byte[] packet) = await CreateCaptureAsync().ConfigureAwait(false);
            CaptureSessionInfo started = await PacketCaptureTools.StartCaptureAsync(
                manager,
                sessions,
                new StartCaptureRequest
                {
                    Source = CaptureSourceKind.Replay,
                    PcapFilePath = pcap,
                    KeyLogFilePath = keys
                },
                CancellationToken.None).ConfigureAwait(false);
            CaptureSessionInfo stopped = await PacketCaptureTools.StopCaptureAsync(
                manager, started.SessionId, CancellationToken.None).ConfigureAwait(false);
            Assert.That(stopped.State, Is.EqualTo(CaptureSessionState.Completed));

            IList<ContentBlock> content = await PacketCaptureTools.GetCaptureAsync(
                manager, m_formatters, started.SessionId, format).ConfigureAwait(false);

            Assert.That(content, Has.Count.EqualTo(1));
            if (format is "pcap" or "pcapng")
            {
                BlobResourceContents exported = GetBlob(content);
                byte[] bytes = exported.Blob.ToArray();
                if (format == "pcap")
                {
                    Assert.That(exported.MimeType, Is.EqualTo("application/vnd.tcpdump.pcap"));
                    Assert.That(bytes,
                        Is.EqualTo(await File.ReadAllBytesAsync(pcap).ConfigureAwait(false)).AsCollection);
                }
                else
                {
                    Assert.That(exported.MimeType, Is.EqualTo("application/x-pcapng"));
                    Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(bytes), Is.EqualTo(0x0A0D0D0A));
                    Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(68)),
                        Is.EqualTo(packet.Length));
                    Assert.That(bytes.AsSpan(76, packet.Length).ToArray(), Is.EqualTo(packet).AsCollection);
                }
            }
            else
            {
                Assert.That(content[0], Is.TypeOf<TextContentBlock>());
                string text = ((TextContentBlock)content[0]).Text;
                if (format == "json")
                {
                    using var document = JsonDocument.Parse(text);
                    Assert.That(document.RootElement.GetArrayLength(), Is.EqualTo(1));
                    Assert.That(document.RootElement[0].GetProperty("Length").GetInt32(), Is.EqualTo(packet.Length));
                }
                else if (format == "csv")
                {
                    Assert.That(text, Does.Contain($",{packet.Length.ToString(CultureInfo.InvariantCulture)},"));
                }
                else if (format == "text")
                {
                    Assert.That(text, Does.Contain($"{packet.Length.ToString(CultureInfo.InvariantCulture)}B "));
                }
                else
                {
                    Assert.That(text, Does.StartWith("Timestamp"));
                    Assert.That(manager.Get(started.SessionId).Source.FrameCount, Is.EqualTo(1));
                    Assert.That(manager.Get(started.SessionId).Source.ByteCount, Is.EqualTo(packet.Length));
                }
            }
        }

        [TestCase("pcap")]
        [TestCase("pcapng")]
        [Category("SecurityRegression")]
        public async Task SecurityF11CaptureToolsExportCompletePcapNg(string format)
        {
            CaptureSessionManager manager = m_services.GetRequiredService<CaptureSessionManager>();
            OpcUaSessionManager sessions = m_services.GetRequiredService<OpcUaSessionManager>();
            string path = Path.Combine(m_directory, "capture-without-extension");
            byte[] packet = LoopbackFrameBuilder.Build(true, 1, [1, 2, 3, 4]);
            using MemoryStream stream = new();
            await using (var writer = new PcapNgFileWriter(stream, PcapFileWriter.LinkTypeNull))
            {
                await writer.WriteAsync(DateTimeOffset.UnixEpoch.AddSeconds(1), packet, CancellationToken.None)
                    .ConfigureAwait(false);
            }

            byte[] expected = stream.ToArray();
            await File.WriteAllBytesAsync(path, expected).ConfigureAwait(false);
            CaptureSessionInfo started = await PacketCaptureTools.StartCaptureAsync(
                manager,
                sessions,
                new StartCaptureRequest { Source = CaptureSourceKind.Replay, PcapFilePath = path },
                CancellationToken.None).ConfigureAwait(false);
            await PacketCaptureTools.StopCaptureAsync(manager, started.SessionId, CancellationToken.None)
                .ConfigureAwait(false);

            BlobResourceContents exported = GetBlob(await PacketCaptureTools.GetCaptureAsync(
                manager, m_formatters, started.SessionId, format).ConfigureAwait(false));

            Assert.That(exported.MimeType, Is.EqualTo("application/x-pcapng"));
            Assert.That(exported.Blob.ToArray(), Is.EqualTo(expected).AsCollection);
        }

        private async Task<(string PcapPath, string KeyLogPath, byte[] Packet)> CreateCaptureAsync()
        {
            string pcap = Path.Combine(m_directory, "capture.pcap");
            string keys = Path.Combine(m_directory, "none.uakeys.json");
            byte[] packet = LoopbackFrameBuilder.Build(true, 1, [1, 2, 3, 4]);
            await using (var writer = new PcapFileWriter(pcap, PcapFileWriter.LinkTypeNull))
            {
                await writer.WriteAsync(DateTimeOffset.UnixEpoch.AddSeconds(1), packet, CancellationToken.None)
                    .ConfigureAwait(false);
            }

            using var material = new ChannelKeyMaterial(
                1, 1, SecurityPolicies.None, MessageSecurityMode.None,
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), 60000,
                clientNonce: null, serverNonce: null,
                clientSigningKey: null, clientEncryptingKey: null, clientInitializationVector: null,
                serverSigningKey: null, serverEncryptingKey: null, serverInitializationVector: null);
            await using (var writer = new UaKeyLogJsonWriter(keys))
            {
                await writer.AppendAsync(material, CancellationToken.None).ConfigureAwait(false);
            }

            return (pcap, keys, packet);
        }

        private async Task<byte[]> ReplayAndExportRawCaptureAsync(string path)
        {
            CaptureSessionManager manager = m_services.GetRequiredService<CaptureSessionManager>();
            OpcUaSessionManager sessions = m_services.GetRequiredService<OpcUaSessionManager>();
            CaptureSessionInfo replay = await PacketCaptureTools.StartCaptureAsync(
                manager,
                sessions,
                new StartCaptureRequest
                {
                    Source = CaptureSourceKind.Replay,
                    PcapFilePath = path,
                    RequireCompleteReplay = false
                },
                CancellationToken.None).ConfigureAwait(false);
            await PacketCaptureTools.StopCaptureAsync(manager, replay.SessionId, CancellationToken.None)
                .ConfigureAwait(false);
            return GetBlob(await PacketCaptureTools.GetCaptureAsync(
                manager, m_formatters, replay.SessionId, "pcap").ConfigureAwait(false)).Blob.ToArray();
        }

        private static BlobResourceContents GetBlob(IList<ContentBlock> content)
        {
            Assert.That(content, Has.Count.EqualTo(1));
            Assert.That(content[0], Is.TypeOf<EmbeddedResourceBlock>());
            ResourceContents resource = ((EmbeddedResourceBlock)content[0]).Resource;
            Assert.That(resource, Is.TypeOf<BlobResourceContents>());
            return (BlobResourceContents)resource;
        }

        private const string kMarker = "security-regression-marker-not-a-key";
        private const string kNotAKeyLog = "{\"clientEncryptingKey\":\"" + kMarker + "\"}";
        private string m_directory = string.Empty;
        private ServiceProvider m_services = null!;
        private TraceFormatterRegistry m_formatters = null!;
    }
}
#endif
