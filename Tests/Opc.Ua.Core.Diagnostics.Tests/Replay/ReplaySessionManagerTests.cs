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
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Pcap.Audit;
using Opc.Ua.Pcap.Capture;
using Opc.Ua.Pcap.Models;
using Opc.Ua.Pcap.Replay;

using Opc.Ua.Bindings;

namespace Opc.Ua.Pcap.Tests.Replay
{
    [TestFixture]
    public sealed class ReplaySessionManagerTests : TempDirectoryFixture
    {
        [Test]
        public async Task ListReturnsEmptyWhenNoSessionsStarted()
        {
            var manager = new ReplaySessionManager();
            await using (manager.ConfigureAwait(false))
            {
                Assert.That(manager.List(), Is.Empty);
            }
        }

        [Test]
        public async Task DisposeAsyncClearsEmptyManager()
        {
            var manager = new ReplaySessionManager();

            await manager.DisposeAsync().ConfigureAwait(false);

            Assert.That(manager.List(), Is.Empty);
        }

        [Test]
        public async Task StartAsyncCanBeCanceledWhileLoadingReplayFrames()
        {
            var manager = new ReplaySessionManager();
            await using (manager.ConfigureAwait(false))
            {
                StartReplayRequest request = await CreateServerRequestAsync().ConfigureAwait(false);
                // Pre-cancel the token so the test is deterministic on
                // fast machines. A 20 ms time-based cancel races with a
                // single-frame replay that loads in well under 20 ms;
                // the cancel never fires before the operation completes.
                using var cts = new CancellationTokenSource();
                cts.Cancel();

                Exception? exception = Assert.CatchAsync(async () =>
                    await manager.StartAsync(request, cts.Token).ConfigureAwait(false));

                Assert.That(exception, Is.InstanceOf<OperationCanceledException>());
                Assert.That(manager.List(), Is.Empty);
            }
        }

        [Test]
        public async Task GetThrowsForUnknownId()
        {
            var manager = new ReplaySessionManager();
            await using (manager.ConfigureAwait(false))
            {
                PcapDiagnosticsException? exception = Assert.Throws<PcapDiagnosticsException>(() => manager.Get("missing"));

                Assert.That(exception, Is.Not.Null);
                Assert.That(exception!.Message, Does.Contain("missing"));
            }
        }

        [Test]
        public async Task StopAsyncThrowsForUnknownId()
        {
            var manager = new ReplaySessionManager();
            await using (manager.ConfigureAwait(false))
            {
                PcapDiagnosticsException? exception = Assert.ThrowsAsync<PcapDiagnosticsException>(async () =>
                    await manager.StopAsync("missing", CancellationToken.None).ConfigureAwait(false));

                Assert.That(exception, Is.Not.Null);
                Assert.That(exception!.Message, Does.Contain("missing"));
            }
        }

        [Test]
        public async Task StartAsyncRejectsNullMissingPcapAndInvalidSpeed()
        {
            var manager = new ReplaySessionManager();
            await using (manager.ConfigureAwait(false))
            {
                Assert.That(
                    async () => await manager.StartAsync(null!, CancellationToken.None).ConfigureAwait(false),
                    Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("request"));
                Assert.That(
                    async () => await manager.StartAsync(new StartReplayRequest(), CancellationToken.None)
                        .ConfigureAwait(false),
                    Throws.TypeOf<ArgumentException>().With.Property("ParamName").EqualTo("request.PcapFilePath"));
                Assert.That(
                    async () => await manager.StartAsync(
                        new StartReplayRequest
                        {
                            PcapFilePath = "capture.pcap",
                            Speed = double.NaN
                        },
                        CancellationToken.None).ConfigureAwait(false),
                    Throws.TypeOf<ArgumentException>().With.Property("ParamName").EqualTo("speed"));
            }
        }

        [Test]
        public void PrivateReplayPropertyHelpersFormatOptionalValues()
        {
            var request = new StartReplayRequest
            {
                Mode = ReplayMode.MockClient,
                PcapFilePath = "capture.pcap",
                KeyLogFilePath = "keys.uakeys.json",
                ListenPort = 4840,
                Speed = 2.5
            };

            Dictionary<string, string> properties = InvokeStatic<Dictionary<string, string>>(
                "CreateStartReplayProperties",
                request);

            Assert.That(properties["Mode"], Is.EqualTo("MockClient"));
            Assert.That(properties["Speed"], Is.EqualTo("2.5"));
            Assert.That(properties["KeyLogFilePath"], Is.EqualTo("keys.uakeys.json"));
            Assert.That(properties["ListenPort"], Is.EqualTo("4840"));
        }

        [Test]
        public async Task PrivateAuditAsyncNoOpsWithoutSinkAndForwardsWithSink()
        {
            var sink = new RecordingAuditSink();
            var managerWithoutSink = new ReplaySessionManager();
            var managerWithSink = new ReplaySessionManager(auditSink: sink);
            await using (managerWithoutSink.ConfigureAwait(false))
            await using (managerWithSink.ConfigureAwait(false))
            {
                await InvokeAuditAsync(managerWithoutSink, PcapAuditEventKind.StartReplay).ConfigureAwait(false);
                await InvokeAuditAsync(managerWithSink, PcapAuditEventKind.StopReplay).ConfigureAwait(false);
            }

            Assert.That(sink.Events, Has.Count.EqualTo(1));
            Assert.That(sink.Events[0].Kind, Is.EqualTo(PcapAuditEventKind.StopReplay));
            Assert.That(sink.Events[0].SessionId, Is.EqualTo("session"));
        }

        [Test]
        public async Task StartGetListStopAndDisposeAuditMockServerSession()
        {
            var sink = new RecordingAuditSink();
            var manager = new ReplaySessionManager(auditSink: sink);
            await using (manager.ConfigureAwait(false))
            {
                StartReplayRequest request = await CreateServerRequestAsync().ConfigureAwait(false);

                ReplaySession session = await manager.StartAsync(request, CancellationToken.None).ConfigureAwait(false);

                Assert.That(session.IsRunning, Is.True);
                Assert.That(session.Mode, Is.EqualTo(ReplayMode.MockServer));
                Assert.That(session.ListenUri, Is.Not.Null);
                Assert.That(manager.Get(session.Id), Is.SameAs(session));
                Assert.That(manager.List(), Has.Count.EqualTo(1));
                Assert.That(sink.Events, Has.Count.EqualTo(1));
                Assert.That(sink.Events[0].Kind, Is.EqualTo(PcapAuditEventKind.StartReplay));

                await manager.StopAsync(session.Id, CancellationToken.None).ConfigureAwait(false);

                Assert.That(session.IsRunning, Is.False);
                Assert.That(sink.Events, Has.Count.EqualTo(2));
                Assert.That(sink.Events[1].Kind, Is.EqualTo(PcapAuditEventKind.StopReplay));
            }

            Assert.That(sink.Events, Has.Count.EqualTo(2));
        }

        [Test]
        public async Task StartAsyncRejectsUnsupportedModeAndMockClientWithoutTarget()
        {
            var manager = new ReplaySessionManager();
            await using (manager.ConfigureAwait(false))
            {
                StartReplayRequest baseRequest = await CreateServerRequestAsync().ConfigureAwait(false);
                var unsupported = new StartReplayRequest
                {
                    Mode = (ReplayMode)999,
                    PcapFilePath = baseRequest.PcapFilePath,
                    KeyLogFilePath = baseRequest.KeyLogFilePath
                };
                var mockClient = new StartReplayRequest
                {
                    Mode = ReplayMode.MockClient,
                    PcapFilePath = baseRequest.PcapFilePath,
                    KeyLogFilePath = baseRequest.KeyLogFilePath
                };

                Assert.That(
                    async () => await manager.StartAsync(unsupported, CancellationToken.None).ConfigureAwait(false),
                    Throws.TypeOf<PcapDiagnosticsException>().With.Message.Contains("Unsupported replay mode"));
                Assert.That(
                    async () => await manager.StartAsync(mockClient, CancellationToken.None).ConfigureAwait(false),
                    Throws.TypeOf<PcapDiagnosticsException>().With.Message.Contains("targetEndpointUrl"));
                Assert.That(manager.List(), Is.Empty);
            }
        }

        [Test]
        public void GetRejectsNullOrWhiteSpaceId()
        {
            var manager = new ReplaySessionManager();

            Assert.That(
                () => manager.Get(" "),
                Throws.TypeOf<ArgumentException>().With.Property("ParamName").EqualTo("id"));
        }

        private async ValueTask<StartReplayRequest> CreateServerRequestAsync()
        {
            FakeCaptureFolder capture = await ReplayTestHelpers.CreateFakeCaptureFolderAsync(
                TempDirectory,
                new[] { ReplayTestHelpers.CreateFrame(DateTimeOffset.UtcNow, fromClient: false, 0xAA) },
                CancellationToken.None).ConfigureAwait(false);
            return new StartReplayRequest
            {
                Mode = ReplayMode.MockServer,
                PcapFilePath = capture.PcapFilePath,
                KeyLogFilePath = capture.KeyLogFilePath,
                ListenScheme = "opc.tcp",
                Speed = 1.0
            };
        }

        private static T InvokeStatic<T>(string methodName, params object?[] parameters)
        {
            MethodInfo method = typeof(ReplaySessionManager).GetMethod(
                methodName,
                BindingFlags.NonPublic | BindingFlags.Static)!;

            return (T)method.Invoke(null, parameters)!;
        }

        private static async ValueTask InvokeAuditAsync(ReplaySessionManager manager, PcapAuditEventKind kind)
        {
            MethodInfo method = typeof(ReplaySessionManager).GetMethod(
                "AuditAsync",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            var properties = new Dictionary<string, string> { ["Name"] = "Value" };
            var task = (ValueTask)method.Invoke(
                manager,
                new object?[]
                {
                    kind,
                    "session",
                    "capture.pcap",
                    "opc.tcp://localhost",
                    properties,
                    CancellationToken.None
                })!;
            await task.ConfigureAwait(false);
        }

        private sealed class RecordingAuditSink : IPcapAuditSink
        {
            public List<PcapAuditEvent> Events { get; } = [];

            public ValueTask OnEventAsync(PcapAuditEvent auditEvent, CancellationToken cancellationToken)
            {
                Events.Add(auditEvent);
                return new ValueTask();
            }
        }
    }
}
