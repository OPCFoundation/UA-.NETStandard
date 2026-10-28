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
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Regression tests for Issue #4863: reverse connect connection admission must not
    /// initiate redundant connections when already Connected/Connecting and must not be
    /// blocked by unrelated server sessions.
    /// </summary>
    [TestFixture]
    [Category("ReverseConnect")]
    [NonParallelizable]
    public class ReverseConnectServerSessionAccountingTests
    {
        private static readonly Uri s_reverseClientUri = new("opc.tcp://reverseclient.local:4840");

        private sealed class TestableReverseConnectServer : ReverseConnectServer
        {
            public TestableReverseConnectServer(ITelemetryContext telemetry)
                : this(telemetry, new FakeTimeProvider())
            {
            }

            public TestableReverseConnectServer(ITelemetryContext telemetry, TimeProvider timeProvider)
                : base(telemetry, timeProvider)
            {
            }

            public void InvokeOnReverseConnect(long version)
            {
                MethodInfo method = typeof(ReverseConnectServer).GetMethod(
                    "OnReverseConnect",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                method.Invoke(this, [version]);
            }
        }

        [Test]
        public void ConnectedReverseConnection_IsNotReconnected()
        {
            var fakeTime = new FakeTimeProvider();
            using var server = new TestableReverseConnectServer(NUnitTelemetryContext.Create(), fakeTime);

            server.AddReverseConnection(s_reverseClientUri, timeout: 5000, enabled: true);
            ReverseConnectProperty property = server.GetReverseConnections()[s_reverseClientUri];

            // Simulate the reverse connection has successfully established
            property.LastState = ReverseConnectState.Connected;

            // Trigger the reverse connect tick
            server.InvokeOnReverseConnect(0);

            // An established connection must not trigger a redundant connection attempt
            Assert.That(
                property.LastState,
                Is.EqualTo(ReverseConnectState.Connected),
                "A reverse connection that is already Connected must not be re-initiated.");
        }

        /// <summary>
        /// Addresses Copilot and maintainer review comments on PR #4889: verifies that on an initialized and started server
        /// with an active SessionManager that has 0 sessions, an already Connected reverse connection
        /// is NOT re-initiated.
        /// </summary>
        [Test]
        public async Task ConnectedReverseConnection_IsNotReconnected_WhenStartedServerHasZeroSessionsAsync()
        {
            var fakeTime = new FakeTimeProvider();
            var fixture = new ServerFixture<TestableReverseConnectServer>(
                telemetry => new TestableReverseConnectServer(telemetry, fakeTime));
            try
            {
                await fixture.LoadConfigurationAsync().ConfigureAwait(false);
                fixture.Config.ServerConfiguration!.ReverseConnect = new ReverseConnectServerConfiguration
                {
                    Clients = []
                };
                TestableReverseConnectServer server = await fixture.StartAsync().ConfigureAwait(false);

                server.AddReverseConnection(s_reverseClientUri, timeout: 5000, enabled: true);
                ReverseConnectProperty property = server.GetReverseConnections()[s_reverseClientUri];

                // Connection is already established, but server has 0 active sessions
                property.LastState = ReverseConnectState.Connected;

                // Fire the server's current timer (initial delay is one second)
                fakeTime.Advance(TimeSpan.FromSeconds(2));

                Assert.That(
                    property.LastState,
                    Is.EqualTo(ReverseConnectState.Connected),
                    "A reverse connection that is already Connected must not be re-initiated on a started server with 0 sessions.");
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public void ConnectingReverseConnection_IsNotReconnected_WhileConnectionInProgress()
        {
            var fakeTime = new FakeTimeProvider();
            using var server = new TestableReverseConnectServer(NUnitTelemetryContext.Create(), fakeTime);

            server.AddReverseConnection(s_reverseClientUri, timeout: 5000, enabled: true);
            ReverseConnectProperty property = server.GetReverseConnections()[s_reverseClientUri];

            // Simulate connection attempt in flight
            property.LastState = ReverseConnectState.Connecting;

            server.InvokeOnReverseConnect(0);

            Assert.That(
                property.LastState,
                Is.EqualTo(ReverseConnectState.Connecting),
                "A reverse connection that is currently Connecting must not trigger a redundant connection attempt.");
        }

        [Test]
        public void ClosedReverseConnection_InitiatesConnection_RegardlessOfServerSessions()
        {
            var fakeTime = new FakeTimeProvider();
            using var server = new TestableReverseConnectServer(NUnitTelemetryContext.Create(), fakeTime);

            server.AddReverseConnection(s_reverseClientUri, timeout: 5000, enabled: true);
            ReverseConnectProperty property = server.GetReverseConnections()[s_reverseClientUri];

            Assert.That(property.LastState, Is.EqualTo(ReverseConnectState.Closed));

            // Tick should attempt connection (which throws ArgumentException because no listener is configured in unit test)
            server.InvokeOnReverseConnect(0);

            Assert.That(
                property.LastState,
                Is.EqualTo(ReverseConnectState.Errored),
                "A Closed connection must attempt connection on tick.");
            Assert.That(
                property.ServiceResult?.AdditionalInfo,
                Does.Contain("No suitable listener found"));
        }

        [Test]
        public void ErroredReverseConnection_IsRetried_OnSubsequentTick()
        {
            var fakeTime = new FakeTimeProvider();
            using var server = new TestableReverseConnectServer(NUnitTelemetryContext.Create(), fakeTime);

            server.AddReverseConnection(s_reverseClientUri, timeout: 5000, enabled: true);
            ReverseConnectProperty property = server.GetReverseConnections()[s_reverseClientUri];

            // Simulate that an earlier connection attempt errored
            property.LastState = ReverseConnectState.Errored;
            property.ServiceResult = new ServiceResult(StatusCodes.BadCommunicationError);

            // On the next tick, it should attempt to reconnect (which updates ServiceResult)
            server.InvokeOnReverseConnect(0);

            Assert.That(property.ServiceResult, Is.Not.Null);
            Assert.That(
                property.ServiceResult!.AdditionalInfo,
                Does.Contain("No suitable listener found"),
                "An Errored reverse connection should be retried on subsequent ticks.");
        }

        [Test]
        public void RejectedReverseConnection_DoesNotConnect_UntilRejectTimeoutExpires()
        {
            var fakeTime = new FakeTimeProvider();
            using var server = new TestableReverseConnectServer(NUnitTelemetryContext.Create(), fakeTime);

            server.AddReverseConnection(s_reverseClientUri, timeout: 5000, enabled: true);
            ReverseConnectProperty property = server.GetReverseConnections()[s_reverseClientUri];

            property.LastState = ReverseConnectState.Rejected;
            property.RejectTime = fakeTime.GetUtcNow().UtcDateTime;

            // Advance time only by 10s (default reject timeout is 60s)
            fakeTime.Advance(TimeSpan.FromSeconds(10));
            server.InvokeOnReverseConnect(0);

            Assert.That(
                property.LastState,
                Is.EqualTo(ReverseConnectState.Rejected),
                "Rejected connection must not connect before reject timeout elapses.");

            // Advance time past the 60s reject timeout
            fakeTime.Advance(TimeSpan.FromSeconds(51));
            server.InvokeOnReverseConnect(0);

            // It should now have recharged to Closed and attempted connect (becoming Errored due to no listener in test)
            Assert.That(
                property.LastState,
                Is.EqualTo(ReverseConnectState.Errored),
                "Rejected connection should attempt connect once reject timeout expires.");
        }

        [Test]
        public void DisabledReverseConnection_DoesNotConnect()
        {
            var fakeTime = new FakeTimeProvider();
            using var server = new TestableReverseConnectServer(NUnitTelemetryContext.Create(), fakeTime);

            server.AddReverseConnection(s_reverseClientUri, timeout: 5000, enabled: false);
            ReverseConnectProperty property = server.GetReverseConnections()[s_reverseClientUri];

            Assert.That(property.LastState, Is.EqualTo(ReverseConnectState.Closed));

            server.InvokeOnReverseConnect(0);

            Assert.That(
                property.LastState,
                Is.EqualTo(ReverseConnectState.Closed),
                "Disabled reverse connection must not attempt connection.");
        }
    }
}
