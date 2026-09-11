/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Unit tests for the connection hold time of the reverse connect manager.
    /// </summary>
    /// <remarks>
    /// A single reverse connect listener is intended to serve multiple servers.
    /// Servers which connect before the application registered a waiting
    /// connection for them are held for the configured hold time. Registering a
    /// waiting connection wakes up all held connections, but a connection which
    /// does not match the new registration must keep waiting for the remainder
    /// of its own hold time instead of being rejected.
    /// </remarks>
    [TestFixture]
    [Category("Client")]
    [Parallelizable]
    public class ReverseConnectManagerUnitTests
    {
        private const int kHoldTime = 30000;
        private const int kSettleTimeout = 1000;
        private const int kCompletionTimeout = 20000;
        private const string kServerUriA = "urn:server-a:UA:Server";
        private const string kServerUriB = "urn:server-b:UA:Server";

        private static readonly Uri s_endpointUrlA = new("opc.tcp://server-a:62541/Server");
        private static readonly Uri s_endpointUrlB = new("opc.tcp://server-b:62541/Server");

        /// <summary>
        /// A waiting connection event which can be created without a listener.
        /// </summary>
        private sealed class TestConnectionWaitingEventArgs : ConnectionWaitingEventArgs
        {
            public TestConnectionWaitingEventArgs(string serverUri, Uri endpointUrl)
                : base(serverUri, endpointUrl)
            {
            }
        }

        /// <summary>
        /// A reverse connection which is held must not be rejected when a
        /// waiting connection for another server is registered.
        /// </summary>
        [Test]
        public async Task HeldConnectionSurvivesRegistrationForAnotherServerAsync()
        {
            using ReverseConnectManager manager = CreateManager(kHoldTime);

            var serverA = new TestConnectionWaitingEventArgs(kServerUriA, s_endpointUrlA);
            var serverB = new TestConnectionWaitingEventArgs(kServerUriB, s_endpointUrlB);

            // both servers connect before the application waits for them
            Task holdA = manager.InvokeConnectionWaitingForTest(serverA);
            Task holdB = manager.InvokeConnectionWaitingForTest(serverB);

            Assert.That(holdA.IsCompleted, Is.False);
            Assert.That(holdB.IsCompleted, Is.False);

            // wait for the first server, this wakes up both held connections
            ITransportWaitingConnection connectionA = await manager
                .WaitForConnectionAsync(s_endpointUrlA, kServerUriA)
                .ConfigureAwait(false);

            Assert.That(connectionA, Is.SameAs(serverA));
            await WaitForCompletionAsync(holdA).ConfigureAwait(false);
            Assert.That(serverA.Accepted, Is.True);

            // the connection of the second server must still be held
            Task settled = await Task.WhenAny(holdB, Task.Delay(kSettleTimeout))
                .ConfigureAwait(false);
            Assert.That(
                settled,
                Is.Not.SameAs(holdB),
                "The reverse connection of the second server was released before its hold time expired.");
            Assert.That(serverB.Accepted, Is.False);

            // the held connection is still available for the second server
            ITransportWaitingConnection connectionB = await manager
                .WaitForConnectionAsync(s_endpointUrlB, kServerUriB)
                .ConfigureAwait(false);

            Assert.That(connectionB, Is.SameAs(serverB));
            await WaitForCompletionAsync(holdB).ConfigureAwait(false);
            Assert.That(serverB.Accepted, Is.True);
        }

        /// <summary>
        /// A reverse connection which is never registered for is rejected once
        /// the hold time expired.
        /// </summary>
        [Test]
        public async Task HeldConnectionIsRejectedWhenHoldTimeExpiresAsync()
        {
            using ReverseConnectManager manager = CreateManager(1000);

            var server = new TestConnectionWaitingEventArgs(kServerUriA, s_endpointUrlA);

            Task hold = manager.InvokeConnectionWaitingForTest(server);
            Assert.That(hold.IsCompleted, Is.False);

            await WaitForCompletionAsync(hold).ConfigureAwait(false);
            Assert.That(server.Accepted, Is.False);
        }

        /// <summary>
        /// Disposing the manager releases the connections which are held,
        /// they are not kept until the hold time expires.
        /// </summary>
        [Test]
        public async Task DisposeReleasesHeldConnectionAsync()
        {
            ReverseConnectManager manager = CreateManager(kHoldTime);

            var server = new TestConnectionWaitingEventArgs(kServerUriA, s_endpointUrlA);

            Task hold = manager.InvokeConnectionWaitingForTest(server);
            Assert.That(hold.IsCompleted, Is.False);

            manager.Dispose();

            await WaitForCompletionAsync(hold).ConfigureAwait(false);
            Assert.That(server.Accepted, Is.False);
        }

        /// <summary>
        /// Create a started manager without a listener, only the hold time and
        /// the wait timeout of the configuration are used by the tests.
        /// </summary>
        private static ReverseConnectManager CreateManager(int holdTime)
        {
            var manager = new ReverseConnectManager(NUnitTelemetryContext.Create());
            try
            {
                manager.StartService(
                    new ReverseConnectClientConfiguration
                    {
                        HoldTime = holdTime,
                        WaitTimeout = kCompletionTimeout
                    });
            }
            catch
            {
                manager.Dispose();
                throw;
            }
            return manager;
        }

        /// <summary>
        /// Await a task, fail the test if it does not complete in time.
        /// </summary>
        private static async Task WaitForCompletionAsync(Task task)
        {
            Task completed = await Task.WhenAny(task, Task.Delay(kCompletionTimeout))
                .ConfigureAwait(false);
            Assert.That(completed, Is.SameAs(task), "The reverse connection was not released.");
            await task.ConfigureAwait(false);
        }
    }
}
