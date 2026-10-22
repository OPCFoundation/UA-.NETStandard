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
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
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
        public void ConnectedReverseConnection_IsNotReconnected_WhenMaxSessionCountIsZero()
        {
            var fakeTime = new FakeTimeProvider();
            using var server = new TestableReverseConnectServer(NUnitTelemetryContext.Create(), fakeTime);

            server.AddReverseConnection(s_reverseClientUri, timeout: 5000, maxSessionCount: 0, enabled: true);
            ReverseConnectProperty property = server.GetReverseConnections()[s_reverseClientUri];

            // Simulate the reverse connection has successfully established
            property.LastState = ReverseConnectState.Connected;

            // Trigger the reverse connect tick
            server.InvokeOnReverseConnect(0);

            // A connection in Connected state must remain Connected and NOT be re-initiated
            Assert.That(
                property.LastState,
                Is.EqualTo(ReverseConnectState.Connected),
                "A reverse connection that is already Connected must not be re-initiated when MaxSessionCount is 0.");
        }

        [Test]
        public void ConnectingReverseConnection_IsNotReconnected_WhileConnectionInProgress()
        {
            var fakeTime = new FakeTimeProvider();
            using var server = new TestableReverseConnectServer(NUnitTelemetryContext.Create(), fakeTime);

            server.AddReverseConnection(s_reverseClientUri, timeout: 5000, maxSessionCount: 0, enabled: true);
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
        public void ConnectedReverseConnection_IsNotReconnected_WhenServerHasZeroSessionsAndMaxSessionCountIsOne()
        {
            var fakeTime = new FakeTimeProvider();
            using var server = new TestableReverseConnectServer(NUnitTelemetryContext.Create(), fakeTime);

            server.AddReverseConnection(s_reverseClientUri, timeout: 5000, maxSessionCount: 1, enabled: true);
            ReverseConnectProperty property = server.GetReverseConnections()[s_reverseClientUri];

            // Simulate connection established, but SessionManager has 0 sessions (not yet activated)
            property.LastState = ReverseConnectState.Connected;

            server.InvokeOnReverseConnect(0);

            Assert.That(
                property.LastState,
                Is.EqualTo(ReverseConnectState.Connected),
                "A reverse connection that is already Connected must not be re-initiated even if SessionManager has 0 sessions.");
        }

        [Test]
        public void ClosedReverseConnection_InitiatesConnection_RegardlessOfMaxSessionCount()
        {
            var fakeTime = new FakeTimeProvider();
            using var server = new TestableReverseConnectServer(NUnitTelemetryContext.Create(), fakeTime);

            // Configure with MaxSessionCount = 2 (previously blocked if sessions >= 2)
            server.AddReverseConnection(s_reverseClientUri, timeout: 5000, maxSessionCount: 2, enabled: true);
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

            server.AddReverseConnection(s_reverseClientUri, timeout: 5000, maxSessionCount: 1, enabled: true);
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

            server.AddReverseConnection(s_reverseClientUri, timeout: 5000, maxSessionCount: 0, enabled: true);
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

            server.AddReverseConnection(s_reverseClientUri, timeout: 5000, maxSessionCount: 0, enabled: false);
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
