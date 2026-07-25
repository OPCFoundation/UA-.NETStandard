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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.Pcap.Capture;
using Opc.Ua.Pcap.Formats;
using Opc.Ua.Pcap.Models;

namespace Opc.Ua.Tools.Tests.Mcp
{
    [TestFixture]
    [NonParallelizable]
    public sealed class PacketDecodeToolsTests
    {
        private readonly List<string> m_sessionIds = [];
        private readonly List<string> m_tempFolders = [];

        private static CaptureSessionManager Manager =>
            McpTestEnvironment.Services.GetRequiredService<CaptureSessionManager>();

        private static TraceFormatterRegistry Formatters =>
            McpTestEnvironment.Services.GetRequiredService<TraceFormatterRegistry>();

        [TearDown]
        public async Task TearDownAsync()
        {
            foreach (string id in m_sessionIds)
            {
                try
                {
                    await Manager.RemoveAsync(id, CancellationToken.None).ConfigureAwait(false);
                }
                catch (PcapDiagnosticsException)
                {
                    // Already removed by the test.
                }
            }

            m_sessionIds.Clear();

            foreach (string folder in m_tempFolders)
            {
                PcapMcpTestHelpers.DeleteDirectory(folder);
            }

            m_tempFolders.Clear();
        }

        private async Task<CaptureSessionInfo> StartReplaySessionAsync(
            string folder,
            bool includeKeyLog = true)
        {
            (string pcapPath, string keyLogPath) = await PcapMcpTestHelpers.CreateFakeCaptureAsync(
                folder,
                CancellationToken.None).ConfigureAwait(false);

            CaptureSessionInfo info = await PacketCaptureTools.StartCaptureAsync(
                Manager,
                McpTestEnvironment.Services.GetRequiredService<OpcUaSessionManager>(),
                new StartCaptureRequest
                {
                    Source = CaptureSourceKind.Replay,
                    PcapFilePath = pcapPath,
                    KeyLogFilePath = includeKeyLog ? keyLogPath : null
                },
                CancellationToken.None).ConfigureAwait(false);
            m_sessionIds.Add(info.SessionId);
            return info;
        }

        [Test]
        public void ListActiveChannelsRejectsNullSessions()
        {
            Assert.That(
                () => PacketDecodeTools.ListActiveChannels(null!),
                Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("sessions"));
        }

        [Test]
        public void ListActiveChannelsReturnsTheSharedConnectedSession()
        {
            IReadOnlyList<ActiveChannelInfo> channels = PacketDecodeTools.ListActiveChannels(
                McpTestEnvironment.SessionManager);

            Assert.That(channels.Any(c => c.SessionName == McpTestEnvironment.SessionName), Is.True);
            ActiveChannelInfo channel = channels.First(c => c.SessionName == McpTestEnvironment.SessionName);
            Assert.That(channel.EndpointUrl, Is.Not.Empty);
            Assert.That(channel.SecurityPolicyUri, Is.Not.Null);
            Assert.That(channel.SecurityMode, Is.Not.Empty);
        }

        [Test]
        public void DumpKeysAsyncRejectsNullArguments()
        {
            Assert.That(
                () => PacketDecodeTools.DumpKeysAsync(
                    null!,
                    Manager,
                    "session"),
                Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("services"));
            Assert.That(
                () => PacketDecodeTools.DumpKeysAsync(
                    McpTestEnvironment.Services,
                    null!,
                    "session"),
                Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("manager"));
        }

        [Test]
        public async Task DumpKeysAsyncReturnsJsonKeyLogContentsAsync()
        {
            string folder = PcapMcpTestHelpers.CreateTempFolder("dump-keys-json");
            m_tempFolders.Add(folder);
            CaptureSessionInfo session = await StartReplaySessionAsync(folder).ConfigureAwait(false);

            IList<ContentBlock> result = await PacketDecodeTools.DumpKeysAsync(
                McpTestEnvironment.Services,
                Manager,
                session.SessionId,
                "json").ConfigureAwait(false);

            Assert.That(result, Has.Count.EqualTo(1));
            string text = ((TextContentBlock)result[0]).Text;
            Assert.That(text, Does.Contain("channelId"));
        }

        [Test]
        public async Task DumpKeysAsyncWithNoAuditSinkStillSucceedsAsync()
        {
            string folder = PcapMcpTestHelpers.CreateTempFolder("dump-keys-no-audit");
            m_tempFolders.Add(folder);
            CaptureSessionInfo session = await StartReplaySessionAsync(folder).ConfigureAwait(false);

            using ServiceProvider noAuditServices = new ServiceCollection().BuildServiceProvider();

            IList<ContentBlock> result = await PacketDecodeTools.DumpKeysAsync(
                noAuditServices,
                Manager,
                session.SessionId,
                "json").ConfigureAwait(false);

            Assert.That(result, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task DumpKeysAsyncThrowsWhenJsonKeyLogIsMissingAsync()
        {
            string folder = PcapMcpTestHelpers.CreateTempFolder("dump-keys-json-missing");
            m_tempFolders.Add(folder);
            CaptureSessionInfo session = await StartReplaySessionAsync(folder, includeKeyLog: false)
                .ConfigureAwait(false);

            Assert.That(
                () => PacketDecodeTools.DumpKeysAsync(
                    McpTestEnvironment.Services,
                    Manager,
                    session.SessionId,
                    "json"),
                Throws.TypeOf<PcapDiagnosticsException>().With.Message.Contains("does not have a JSON keylog file"));
        }

        [Test]
        public async Task DumpKeysAsyncThrowsWhenTextKeyLogIsMissingAsync()
        {
            string folder = PcapMcpTestHelpers.CreateTempFolder("dump-keys-text-missing");
            m_tempFolders.Add(folder);
            CaptureSessionInfo session = await StartReplaySessionAsync(folder).ConfigureAwait(false);

            Assert.That(
                () => PacketDecodeTools.DumpKeysAsync(
                    McpTestEnvironment.Services,
                    Manager,
                    session.SessionId,
                    "text"),
                Throws.TypeOf<PcapDiagnosticsException>().With.Message.Contains("does not have a text keylog file"));
        }

        [Test]
        public async Task DumpKeysAsyncThrowsForUnsupportedFormatAsync()
        {
            string folder = PcapMcpTestHelpers.CreateTempFolder("dump-keys-bad-format");
            m_tempFolders.Add(folder);
            CaptureSessionInfo session = await StartReplaySessionAsync(folder).ConfigureAwait(false);

            Assert.That(
                () => PacketDecodeTools.DumpKeysAsync(
                    McpTestEnvironment.Services,
                    Manager,
                    session.SessionId,
                    "xml"),
                Throws.TypeOf<PcapDiagnosticsException>().With.Message.Contains("Unsupported key format"));
        }

        [Test]
        public void ResolveAndValidateDecodePathAcceptsContainedPathAndRejectsEscapes()
        {
            string root = PcapMcpTestHelpers.CreateTempFolder("resolve-path");
            m_tempFolders.Add(root);
            string insidePath = Path.Combine(root, "sub", "capture.pcap");

            string resolved = PacketDecodeTools.ResolveAndValidateDecodePath(insidePath, root);
            Assert.That(resolved, Is.EqualTo(Path.GetFullPath(insidePath)));

            string outsidePath = Path.Combine(Path.GetTempPath(), "mcp-decode-outside.pcap");
            Assert.That(
                () => PacketDecodeTools.ResolveAndValidateDecodePath(outsidePath, root),
                Throws.TypeOf<ArgumentException>().With.Property("ParamName").EqualTo("filePath"));

            string traversal = Path.Combine(root, "..", "mcp-decode-escape.pcap");
            Assert.That(
                () => PacketDecodeTools.ResolveAndValidateDecodePath(traversal, root),
                Throws.TypeOf<ArgumentException>().With.Property("ParamName").EqualTo("filePath"));

            Assert.That(
                () => PacketDecodeTools.ResolveAndValidateDecodePath(" ", root),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => PacketDecodeTools.ResolveAndValidateDecodePath(insidePath, " "),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void DecodePcapWithKeysAsyncRejectsNullOrWhitespaceArguments()
        {
            Assert.That(
                () => PacketDecodeTools.DecodePcapWithKeysAsync(
                    null!,
                    Formatters,
                    "pcap",
                    "keys"),
                Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("services"));
            Assert.That(
                () => PacketDecodeTools.DecodePcapWithKeysAsync(
                    McpTestEnvironment.Services,
                    null!,
                    "pcap",
                    "keys"),
                Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("formatters"));
            Assert.That(
                () => PacketDecodeTools.DecodePcapWithKeysAsync(
                    McpTestEnvironment.Services,
                    Formatters,
                    " ",
                    "keys"),
                Throws.TypeOf<ArgumentException>()
                    .With.Property("ParamName").EqualTo("pcapPath"));
            Assert.That(
                () => PacketDecodeTools.DecodePcapWithKeysAsync(
                    McpTestEnvironment.Services,
                    Formatters,
                    "pcap",
                    " "),
                Throws.TypeOf<ArgumentException>().With.Property("ParamName").EqualTo("keyLogPath"));
        }

        [Test]
        public void DecodePcapWithKeysAsyncFallsBackToDefaultAllowedRootAndRejectsOutsidePath()
        {
            using ServiceProvider emptyServices = new ServiceCollection().BuildServiceProvider();
            string outsidePath = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "mcp-decode-tests-outside.pcap");

            Assert.That(
                () => PacketDecodeTools.DecodePcapWithKeysAsync(
                    emptyServices,
                    Formatters,
                    outsidePath,
                    outsidePath),
                Throws.TypeOf<ArgumentException>().With.Message.Contains("outside the allowed root"));
        }

        [Test]
        public async Task DecodePcapWithKeysAsyncUsesConfiguredMcpPcapBaseFolderAsync()
        {
            string root = PcapMcpTestHelpers.CreateTempFolder("decode-mcp-root");
            m_tempFolders.Add(root);
            (string pcapPath, string keyLogPath) = await PcapMcpTestHelpers.CreateFakeCaptureAsync(
                root,
                CancellationToken.None).ConfigureAwait(false);

            var services = new ServiceCollection();
            services.AddSingleton(new McpServerOptions { PcapBaseFolder = root });
            using ServiceProvider provider = services.BuildServiceProvider();

            IList<ContentBlock> result = await PacketDecodeTools.DecodePcapWithKeysAsync(
                provider,
                Formatters,
                pcapPath,
                keyLogPath,
                "json").ConfigureAwait(false);

            Assert.That(result, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task DecodePcapWithKeysAsyncThrowsForNonexistentPcapFileAsync()
        {
            string root = PcapMcpTestHelpers.CreateTempFolder("decode-missing");
            m_tempFolders.Add(root);
            Directory.CreateDirectory(root);
            string missingPcap = Path.Combine(root, "missing.pcap");
            string missingKeys = Path.Combine(root, "missing.uakeys.json");

            var services = new ServiceCollection();
            services.AddSingleton(new McpServerOptions { PcapBaseFolder = root });
            using ServiceProvider provider = services.BuildServiceProvider();

            Assert.That(
                () => PacketDecodeTools.DecodePcapWithKeysAsync(
                    provider,
                    Formatters,
                    missingPcap,
                    missingKeys),
                Throws.TypeOf<PcapDiagnosticsException>().With.Message.Contains("does not exist"));
        }

        [Test]
        public async Task DecodePcapWithKeysAsyncRejectsUnsupportedAndDisallowedFormatsAsync()
        {
            string root = PcapMcpTestHelpers.CreateTempFolder("decode-bad-format");
            m_tempFolders.Add(root);
            (string pcapPath, string keyLogPath) = await PcapMcpTestHelpers.CreateFakeCaptureAsync(
                root,
                CancellationToken.None).ConfigureAwait(false);

            var services = new ServiceCollection();
            services.AddSingleton(new McpServerOptions { PcapBaseFolder = root });
            using ServiceProvider provider = services.BuildServiceProvider();

            Assert.That(
                () => PacketDecodeTools.DecodePcapWithKeysAsync(
                    provider,
                    Formatters,
                    pcapPath,
                    keyLogPath,
                    "bogus"),
                Throws.TypeOf<PcapDiagnosticsException>().With.Message.Contains("Unsupported decode format"));

            // "csv" parses to a valid FormatKind but is not one of the
            // three formats decode_pcap_with_keys accepts.
            Assert.That(
                () => PacketDecodeTools.DecodePcapWithKeysAsync(
                    provider,
                    Formatters,
                    pcapPath,
                    keyLogPath,
                    "csv"),
                Throws.TypeOf<PcapDiagnosticsException>().With.Message.Contains("Unsupported decode format"));
        }

        [Test]
        public async Task DecodePcapWithKeysAsyncDecodesEachSupportedFormatAsync()
        {
            string root = PcapMcpTestHelpers.CreateTempFolder("decode-formats");
            m_tempFolders.Add(root);
            (string pcapPath, string keyLogPath) = await PcapMcpTestHelpers.CreateFakeCaptureAsync(
                root,
                CancellationToken.None).ConfigureAwait(false);

            var services = new ServiceCollection();
            services.AddSingleton(new McpServerOptions { PcapBaseFolder = root });
            using ServiceProvider provider = services.BuildServiceProvider();

            foreach (string format in new[] { "service-timeline", "json", "text" })
            {
                IList<ContentBlock> result = await PacketDecodeTools.DecodePcapWithKeysAsync(
                    provider,
                    Formatters,
                    pcapPath,
                    keyLogPath,
                    format).ConfigureAwait(false);
                Assert.That(result, Has.Count.EqualTo(1), $"format {format} should return one content block");
            }
        }

        [Test]
        public void SummarizeServiceCallsAsyncRejectsNullArguments()
        {
            Assert.That(
                () => PacketDecodeTools.SummarizeServiceCallsAsync(
                    null!,
                    Manager,
                    "session"),
                Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("services"));
            Assert.That(
                () => PacketDecodeTools.SummarizeServiceCallsAsync(
                    McpTestEnvironment.Services,
                    null!,
                    "session"),
                Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("manager"));
        }

        [Test]
        public void SummarizeServiceCallsAsyncRequiresPcapAndKeyLogPathsWhenSessionIdIsEmpty()
        {
            Assert.That(
                () => PacketDecodeTools.SummarizeServiceCallsAsync(
                    McpTestEnvironment.Services,
                    Manager,
                    string.Empty,
                    pcapPath: null,
                    keyLogPath: null),
                Throws.TypeOf<ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("pcapPath"));
        }

        [Test]
        public async Task SummarizeServiceCallsAsyncFromPcapPathReturnsEmptySummaryAsync()
        {
            string root = PcapMcpTestHelpers.CreateTempFolder("summarize-pcap-path");
            m_tempFolders.Add(root);
            (string pcapPath, string keyLogPath) = await PcapMcpTestHelpers.CreateFakeCaptureAsync(
                root,
                CancellationToken.None).ConfigureAwait(false);

            var services = new ServiceCollection();
            services.AddSingleton(new McpServerOptions { PcapBaseFolder = root });
            using ServiceProvider provider = services.BuildServiceProvider();

            ServiceCallSummary summary = await PacketDecodeTools.SummarizeServiceCallsAsync(
                provider,
                Manager,
                string.Empty,
                pcapPath,
                keyLogPath).ConfigureAwait(false);

            Assert.That(summary.TotalCalls, Is.Zero);
            Assert.That(summary.Errors, Is.Zero);
            Assert.That(summary.PerService, Is.Empty);
        }

        [Test]
        public async Task SummarizeServiceCallsAsyncFromSessionIdReturnsEmptySummaryAsync()
        {
            string folder = PcapMcpTestHelpers.CreateTempFolder("summarize-session-id");
            m_tempFolders.Add(folder);
            CaptureSessionInfo session = await StartReplaySessionAsync(folder).ConfigureAwait(false);

            ServiceCallSummary summary = await PacketDecodeTools.SummarizeServiceCallsAsync(
                McpTestEnvironment.Services,
                Manager,
                session.SessionId).ConfigureAwait(false);

            Assert.That(summary.TotalCalls, Is.Zero);
            Assert.That(summary.AverageLatencyMs, Is.Zero);
        }
    }
}
#endif
