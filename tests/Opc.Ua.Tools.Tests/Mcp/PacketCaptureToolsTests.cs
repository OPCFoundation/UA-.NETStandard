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
    public sealed class PacketCaptureToolsTests
    {
        private readonly List<string> m_sessionIds = [];
        private readonly List<string> m_tempFolders = [];

        private static CaptureSessionManager Manager =>
            McpTestEnvironment.Services.GetRequiredService<CaptureSessionManager>();

        private static TraceFormatterRegistry Formatters =>
            McpTestEnvironment.Services.GetRequiredService<TraceFormatterRegistry>();

        private static OpcUaSessionManager Sessions =>
            McpTestEnvironment.Services.GetRequiredService<OpcUaSessionManager>();

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

        [Test]
        public void ListInterfacesReturnsInterfaceList()
        {
            try
            {
                IReadOnlyList<NetworkInterfaceInfo> interfaces =
                    PacketCaptureTools.ListInterfaces();
                Assert.That(interfaces, Is.Not.Null);
            }
            catch (PcapDiagnosticsException exception)
            {
                Assert.That(
                    exception.Message,
                    Does.Contain("Unable to enumerate devices"));
            }
        }

        [Test]
        public void StartCaptureAsyncRejectsNullArguments()
        {
            var request = new StartCaptureRequest { Source = CaptureSourceKind.Replay };

            Assert.That(
                () => PacketCaptureTools.StartCaptureAsync(
                    null!,
                    Sessions,
                    request,
                    CancellationToken.None),
                Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("manager"));
            Assert.That(
                () => PacketCaptureTools.StartCaptureAsync(
                    Manager,
                    null!,
                    request,
                    CancellationToken.None),
                Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("sessions"));
            Assert.That(
                () => PacketCaptureTools.StartCaptureAsync(
                    Manager,
                    Sessions,
                    null!,
                    CancellationToken.None),
                Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("request"));
        }

        [Test]
        public void StopCaptureAsyncRejectsNullManager()
        {
            Assert.That(
                () => PacketCaptureTools.StopCaptureAsync(
                    null!,
                    "session",
                    CancellationToken.None),
                Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("manager"));
        }

        [Test]
        public void ListCapturesRejectsNullManagerAndReturnsEmptyForNoSessions()
        {
            Assert.That(
                () => PacketCaptureTools.ListCaptures(null!),
                Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("manager"));
            Assert.That(
                PacketCaptureTools.ListCaptures(Manager, "bogus"),
                Is.Empty);
        }

        [Test]
        public void GetCaptureAsyncRejectsNullArguments()
        {
            Assert.That(
                () => PacketCaptureTools.GetCaptureAsync(
                    null!,
                    Formatters,
                    "session"),
                Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("manager"));
            Assert.That(
                () => PacketCaptureTools.GetCaptureAsync(
                    Manager,
                    null!,
                    "session"),
                Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("formatters"));
        }

        [Test]
        public void CaptureNowAsyncRejectsNullArguments()
        {
            var request = new CaptureNowRequest
            {
                Start = new StartCaptureRequest { Source = CaptureSourceKind.Replay },
                DurationSeconds = 0
            };

            Assert.That(
                () => PacketCaptureTools.CaptureNowAsync(
                    null!,
                    Sessions,
                    Formatters,
                    request,
                    CancellationToken.None),
                Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("manager"));
            Assert.That(
                () => PacketCaptureTools.CaptureNowAsync(
                    Manager,
                    null!,
                    Formatters,
                    request,
                    CancellationToken.None),
                Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("sessions"));
            Assert.That(
                () => PacketCaptureTools.CaptureNowAsync(
                    Manager,
                    Sessions,
                    null!,
                    request,
                    CancellationToken.None),
                Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("formatters"));
            Assert.That(
                () => PacketCaptureTools.CaptureNowAsync(
                    Manager,
                    Sessions,
                    Formatters,
                    null!,
                    CancellationToken.None),
                Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("request"));
        }

        [Test]
        public async Task StartGetListStopAndGetCompletedReplaySessionRoundTripsAsync()
        {
            string folder = PcapMcpTestHelpers.CreateTempFolder("start-get-list-stop");
            m_tempFolders.Add(folder);
            (string pcapPath, string keyLogPath) = await PcapMcpTestHelpers.CreateFakeCaptureAsync(
                folder,
                CancellationToken.None).ConfigureAwait(false);

            CaptureSessionInfo started = await PacketCaptureTools.StartCaptureAsync(
                Manager,
                Sessions,
                new StartCaptureRequest
                {
                    Source = CaptureSourceKind.Replay,
                    PcapFilePath = pcapPath,
                    KeyLogFilePath = keyLogPath
                },
                CancellationToken.None).ConfigureAwait(false);
            m_sessionIds.Add(started.SessionId);

            Assert.That(started.State, Is.EqualTo(CaptureSessionState.Running));

            IReadOnlyList<CaptureSessionInfo> active = PacketCaptureTools.ListCaptures(Manager, "active");
            Assert.That(active.Any(s => s.SessionId == started.SessionId), Is.True);

            IReadOnlyList<CaptureSessionInfo> allSessions = PacketCaptureTools.ListCaptures(
                Manager,
                string.Empty);
            Assert.That(allSessions.Any(s => s.SessionId == started.SessionId), Is.True);
            Assert.That(
                () => PacketCaptureTools.ListCaptures(Manager, "bogus"),
                Throws.TypeOf<PcapDiagnosticsException>()
                    .With.Message.Contains("Unsupported state filter"));

            // While running, reading without allowPartial throws.
            Assert.That(
                () => PacketCaptureTools.GetCaptureAsync(
                    Manager,
                    Formatters,
                    started.SessionId,
                    "json"),
                Throws.TypeOf<PcapDiagnosticsException>().With.Message.Contains("allowPartial"));

            // allowPartial=true permits reading a running session.
            IList<ContentBlock> partial = await PacketCaptureTools.GetCaptureAsync(
                Manager,
                Formatters,
                started.SessionId,
                "json",
                allowPartial: true).ConfigureAwait(false);
            Assert.That(partial, Has.Count.EqualTo(1));
            Assert.That(((TextContentBlock)partial[0]).Text, Does.Contain("Direction"));

            CaptureSessionInfo stopped = await PacketCaptureTools.StopCaptureAsync(
                Manager,
                started.SessionId,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(stopped.State, Is.EqualTo(CaptureSessionState.Completed));

            IReadOnlyList<CaptureSessionInfo> completed = PacketCaptureTools.ListCaptures(Manager, "completed");
            Assert.That(completed.Any(s => s.SessionId == started.SessionId), Is.True);

            foreach (string format in new[] { "pcap", "pcapng", "json", "csv", "text", "service-timeline" })
            {
                IList<ContentBlock> result = await PacketCaptureTools.GetCaptureAsync(
                    Manager,
                    Formatters,
                    started.SessionId,
                    format).ConfigureAwait(false);
                Assert.That(result, Has.Count.EqualTo(1), $"format {format} should return one content block");
            }

            Assert.That(
                () => PacketCaptureTools.GetCaptureAsync(
                    Manager,
                    Formatters,
                    started.SessionId,
                    "bogus-format"),
                Throws.TypeOf<PcapDiagnosticsException>().With.Message.Contains("Unsupported format"));
        }

        [Test]
        public async Task CaptureNowAsyncStartsCapturesAndStopsWithZeroDurationAsync()
        {
            string folder = PcapMcpTestHelpers.CreateTempFolder("capture-now");
            m_tempFolders.Add(folder);
            (string pcapPath, string keyLogPath) = await PcapMcpTestHelpers.CreateFakeCaptureAsync(
                folder,
                CancellationToken.None).ConfigureAwait(false);

            var request = new CaptureNowRequest
            {
                Start = new StartCaptureRequest
                {
                    Source = CaptureSourceKind.Replay,
                    PcapFilePath = pcapPath,
                    KeyLogFilePath = keyLogPath
                },
                DurationSeconds = 0,
                Format = FormatKind.Json
            };

            IList<ContentBlock> result = await PacketCaptureTools.CaptureNowAsync(
                Manager,
                Sessions,
                Formatters,
                request,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(result, Has.Count.EqualTo(1));

            // The session started by capture_now was already stopped and
            // completed internally; find it so the fixture can remove it.
            CaptureSession? finished = Manager.List()
                .FirstOrDefault(s => s.Request.PcapFilePath == pcapPath);
            Assert.That(finished, Is.Not.Null);
            Assert.That(finished!.State, Is.EqualTo(CaptureSessionState.Completed));
            m_sessionIds.Add(finished.Id);
        }

        [Test]
        public async Task CaptureNowAsyncStopsSessionInFinallyBlockWhenCanceledAsync()
        {
            string folder = PcapMcpTestHelpers.CreateTempFolder("capture-now-cancel");
            m_tempFolders.Add(folder);
            (string pcapPath, string keyLogPath) = await PcapMcpTestHelpers.CreateFakeCaptureAsync(
                folder,
                CancellationToken.None).ConfigureAwait(false);

            var request = new CaptureNowRequest
            {
                Start = new StartCaptureRequest
                {
                    Source = CaptureSourceKind.Replay,
                    PcapFilePath = pcapPath,
                    KeyLogFilePath = keyLogPath
                },
                DurationSeconds = 5
            };

            // Start the wait uncancelled so manager.StartAsync completes
            // normally and the session reaches Running, then cancel while
            // the multi-second Task.Delay is still pending. This is the
            // same bounded-race pattern already used for cancellation
            // tests elsewhere in this repo (e.g. ReplaySessionManagerTests):
            // the 50 ms cancel fires long before the 5 s delay could
            // complete, so the outcome is stable across slow CI machines.
            using var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromMilliseconds(50));

            Exception? exception = Assert.CatchAsync(() =>
                PacketCaptureTools.CaptureNowAsync(
                    Manager,
                    Sessions,
                    Formatters,
                    request,
                    cts.Token));

            Assert.That(exception, Is.InstanceOf<OperationCanceledException>());

            CaptureSession? finished = Manager.List()
                .FirstOrDefault(s => s.Request.PcapFilePath == pcapPath);
            Assert.That(finished, Is.Not.Null);

            // The finally block's extra StopAsync call (triggered because
            // the session was still Running/Starting when the exception
            // propagated) drives the session to Completed even though the
            // happy path never got there.
            Assert.That(finished!.State, Is.EqualTo(CaptureSessionState.Completed));
            m_sessionIds.Add(finished.Id);
        }

        [Test]
        public void GetCaptureAsyncThrowsForUnknownSession()
        {
            Assert.That(
                () => PacketCaptureTools.GetCaptureAsync(
                    Manager,
                    Formatters,
                    "missing-session-id"),
                Throws.TypeOf<PcapDiagnosticsException>().With.Message.Contains("missing-session-id"));
        }
    }
}
#endif
