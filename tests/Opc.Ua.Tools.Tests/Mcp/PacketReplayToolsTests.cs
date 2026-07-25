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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.Pcap.Capture;
using Opc.Ua.Pcap.DependencyInjection;
using Opc.Ua.Pcap.Replay;

namespace Opc.Ua.Tools.Tests.Mcp
{
    [TestFixture]
    [NonParallelizable]
    public sealed class PacketReplayToolsTests
    {
        private readonly List<string> m_replaySessionIds = [];
        private readonly List<string> m_tempFolders = [];

        private static ReplaySessionManager ReplayManager =>
            McpTestEnvironment.Services.GetRequiredService<ReplaySessionManager>();

        [TearDown]
        public async Task TearDownAsync()
        {
            foreach (string id in m_replaySessionIds)
            {
                try
                {
                    await ReplayManager.StopAsync(id, CancellationToken.None).ConfigureAwait(false);
                }
                catch (PcapDiagnosticsException)
                {
                    // Already stopped/unknown; nothing else to clean up.
                }
            }

            m_replaySessionIds.Clear();

            foreach (string folder in m_tempFolders)
            {
                PcapMcpTestHelpers.DeleteDirectory(folder);
            }

            m_tempFolders.Clear();
        }

        [Test]
        public void ReplayPcapAsyncRejectsNullServices()
        {
            Assert.That(
                () => PacketReplayTools.ReplayPcapAsync(
                    null!,
                    "capture.pcap",
                    null),
                Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("services"));
        }

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(0d)]
        [TestCase(-1d)]
        public void ReplayPcapAsyncRejectsNonPositiveFiniteSpeed(double speed)
        {
            using ServiceProvider services = new ServiceCollection().BuildServiceProvider();

            Assert.That(
                () => PacketReplayTools.ReplayPcapAsync(
                    services,
                    "capture.pcap",
                    null,
                    speed: speed),
                Throws.TypeOf<ArgumentException>().With.Property("ParamName").EqualTo("speed"));
        }

        [Test]
        public void ReplayPcapAsyncThrowsForUnsupportedMode()
        {
            using ServiceProvider services = new ServiceCollection().BuildServiceProvider();

            Assert.That(
                () => PacketReplayTools.ReplayPcapAsync(
                    services,
                    "capture.pcap",
                    null,
                    mode: "bogus-mode"),
                Throws.TypeOf<PcapDiagnosticsException>().With.Message.Contains("Unsupported replay mode"));
        }

        [Test]
        public void ReplayPcapAsyncThrowsWhenMockClientReplayIsDisabled()
        {
            using ServiceProvider services = new ServiceCollection().BuildServiceProvider();

            Assert.That(
                () => PacketReplayTools.ReplayPcapAsync(
                    services,
                    "capture.pcap",
                    null,
                    mode: "mock-client",
                    targetEndpointUrl: "opc.tcp://localhost:4840"),
                Throws.TypeOf<PcapDiagnosticsException>().With.Message.Contains("Mock-client replay is disabled"));
        }

        [Test]
        public void ReplayPcapAsyncThrowsWhenReplaySessionManagerIsNotRegistered()
        {
            using ServiceProvider services = new ServiceCollection().BuildServiceProvider();

            Assert.That(
                () => PacketReplayTools.ReplayPcapAsync(
                    services,
                    "capture.pcap",
                    null),
                Throws.TypeOf<NotSupportedException>());
        }

        [Test]
        public void StopReplayAsyncRejectsNullServices()
        {
            Assert.That(
                () => PacketReplayTools.StopReplayAsync(
                    null!,
                    "session",
                    CancellationToken.None),
                Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("services"));
        }

        [Test]
        public void ListReplaysRejectsNullServices()
        {
            Assert.That(
                () => PacketReplayTools.ListReplays(null!),
                Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("services"));
        }

        [Test]
        public void ListReplaysThrowsWhenReplaySessionManagerIsNotRegistered()
        {
            using ServiceProvider services = new ServiceCollection().BuildServiceProvider();

            Assert.That(
                () => PacketReplayTools.ListReplays(services),
                Throws.TypeOf<NotSupportedException>());
        }

        [Test]
        public async Task ReplayStartListStopMockServerRoundTripsAsync()
        {
            string folder = PcapMcpTestHelpers.CreateTempFolder("replay-round-trip");
            m_tempFolders.Add(folder);
            (string pcapPath, string keyLogPath) = await PcapMcpTestHelpers.CreateFakeCaptureAsync(
                folder,
                CancellationToken.None).ConfigureAwait(false);

            ReplaySessionInfo started = await PacketReplayTools.ReplayPcapAsync(
                McpTestEnvironment.Services,
                pcapPath,
                keyLogPath,
                mode: "mock-server").ConfigureAwait(false);
            m_replaySessionIds.Add(started.SessionId);

            Assert.That(started.Mode, Is.EqualTo("MockServer"));
            Assert.That(started.IsRunning, Is.True);
            Assert.That(started.ListenUri, Is.Not.Null.And.Not.Empty);

            IReadOnlyList<ReplaySessionInfo> listed = PacketReplayTools.ListReplays(McpTestEnvironment.Services);
            Assert.That(listed.Any(s => s.SessionId == started.SessionId), Is.True);

            ReplaySessionInfo stopped = await PacketReplayTools.StopReplayAsync(
                McpTestEnvironment.Services,
                started.SessionId,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(stopped.IsRunning, Is.False);

            IReadOnlyList<ReplaySessionInfo> listedAfterStop =
                PacketReplayTools.ListReplays(McpTestEnvironment.Services);
            Assert.That(
                listedAfterStop.First(s => s.SessionId == started.SessionId).IsRunning,
                Is.False);
        }

        [Test]
        public async Task ReplayPcapAsyncAcceptsAlternateModeSpellingAndExplicitPortAsync()
        {
            string folder = PcapMcpTestHelpers.CreateTempFolder("replay-alt-mode");
            m_tempFolders.Add(folder);
            (string pcapPath, string keyLogPath) = await PcapMcpTestHelpers.CreateFakeCaptureAsync(
                folder,
                CancellationToken.None).ConfigureAwait(false);

            ReplaySessionInfo started = await PacketReplayTools.ReplayPcapAsync(
                McpTestEnvironment.Services,
                pcapPath,
                keyLogPath,
                mode: "mockserver").ConfigureAwait(false);
            m_replaySessionIds.Add(started.SessionId);

            Assert.That(started.Mode, Is.EqualTo("MockServer"));
            Assert.That(started.TargetEndpointUrl, Is.Null);
        }

        [Test]
        public void StopReplayAsyncThrowsForUnknownSessionId()
        {
            Assert.That(
                () => PacketReplayTools.StopReplayAsync(
                    McpTestEnvironment.Services,
                    "missing-replay-session",
                    CancellationToken.None),
                Throws.TypeOf<PcapDiagnosticsException>().With.Message.Contains("missing-replay-session"));
        }
    }
}
#endif
