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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Core.Diagnostics.Capture;
using Opc.Ua.Core.Diagnostics.Models;
using Opc.Ua.Core.Diagnostics.Replay;

using Opc.Ua.Bindings;

namespace Opc.Ua.Core.Diagnostics.Tests.Replay
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
    }
}
