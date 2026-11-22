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

using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Configuration;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Verifies that bounded fixture teardown does not abandon independent servers.
    /// </summary>
    [TestFixture]
    [Category("Server")]
    [NonParallelizable]
    public sealed class ServerFixtureShutdownTests
    {
        /// <summary>
        /// A watchdog expiry belongs to its fixture, not every server in the test process.
        /// </summary>
        [Test]
        [CancelAfter(30_000)]
        public async Task TimedOutFixtureDoesNotSkipAnotherFixturesShutdownAsync()
        {
            var delayed = new ServerFixture<StandardServer>(
                telemetry => new ShutdownProbeServer(telemetry, delayCompletion: true));
            var next = new ServerFixture<StandardServer>(
                telemetry => new ShutdownProbeServer(telemetry, delayCompletion: false));
            ShutdownProbeServer? delayedServer = null;
            ShutdownProbeServer? nextServer = null;
            IApplicationInstance? nextApplication = null;
            try
            {
                delayedServer = (ShutdownProbeServer)await delayed.StartAsync().ConfigureAwait(false);
                await delayed.StopAsync().ConfigureAwait(false);
                Assert.That(delayedServer.DelayedStopEntered, Is.True);

                nextServer = (ShutdownProbeServer)await next.StartAsync().ConfigureAwait(false);
                nextApplication = next.Application;
                var manager = (ConfigurationNodeManager)nextServer.CurrentInstance.ConfigurationNodeManager;
                Assert.That(manager.AlarmScheduler.IsActive, Is.True);
                await next.StopAsync().ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(nextServer.StopCount, Is.GreaterThan(0),
                        "A different fixture must still stop its server after a watchdog expiry.");
                    Assert.That(manager.AlarmScheduler.IsActive, Is.False,
                        "The independent server's alarm monitoring must be stopped.");
                });
            }
            finally
            {
                delayedServer?.ReleaseStop();
                await delayed.StopAsync().ConfigureAwait(false);
                if (nextServer is not null)
                {
                    await nextServer.StopAsync().ConfigureAwait(false);
                    nextServer.Dispose();
                }
                if (nextApplication is not null)
                {
                    await nextApplication.DisposeAsync().ConfigureAwait(false);
                }
                await next.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Delays only completion reporting after the real server has released its resources.
        /// </summary>
        private sealed class ShutdownProbeServer : StandardServer
        {
            /// <summary>
            /// Creates a server with optional delayed reporting of its first completed stop.
            /// </summary>
            public ShutdownProbeServer(ITelemetryContext telemetry, bool delayCompletion)
                : base(telemetry)
            {
                m_delayCompletion = delayCompletion ? 1 : 0;
            }

            /// <summary>
            /// Gets whether the delayed completion boundary was reached.
            /// </summary>
            public bool DelayedStopEntered => Volatile.Read(ref m_delayedStopEntered) != 0;

            /// <summary>
            /// Gets the number of actual stop invocations.
            /// </summary>
            public int StopCount => Volatile.Read(ref m_stopCount);

            /// <inheritdoc/>
            public override async ValueTask StopAsync(CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref m_stopCount);
                await base.StopAsync(cancellationToken).ConfigureAwait(false);
                if (Interlocked.Exchange(ref m_delayCompletion, 0) != 0)
                {
                    Volatile.Write(ref m_delayedStopEntered, 1);
                    await m_release.Task.ConfigureAwait(false);
                }
            }

            /// <summary>
            /// Releases the delayed completion without leaving background teardown behind.
            /// </summary>
            public void ReleaseStop()
            {
                m_release.TrySetResult(true);
            }

            /// <inheritdoc/>
            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    ReleaseStop();
                }
                base.Dispose(disposing);
            }

            /// <summary>The delayed completion release signal.</summary>
            private readonly TaskCompletionSource<bool> m_release =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>Whether the first completed stop must wait for disposal.</summary>
            private int m_delayCompletion;

            /// <summary>Whether the delayed completion boundary was reached.</summary>
            private int m_delayedStopEntered;

            /// <summary>The number of stop invocations.</summary>
            private int m_stopCount;
        }
    }
}
