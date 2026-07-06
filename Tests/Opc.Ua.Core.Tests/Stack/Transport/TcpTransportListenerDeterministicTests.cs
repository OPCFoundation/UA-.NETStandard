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

#nullable enable

using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    /// <summary>
    /// Socket-free, timer-free deterministic unit tests for the connection
    /// rate-limiter <see cref="ActiveClientTracker"/>, the <see cref="ActiveClient"/>
    /// state record, the <see cref="TcpTransportListenerFactory"/> and the
    /// <see cref="TcpTransportListener"/> construction path. A
    /// <see cref="FakeTimeProvider"/> drives both the tracker's clock and its
    /// cleanup timer, so no wall-clock, socket, or accept-loop behaviour is ever
    /// exercised and results are identical on every target framework.
    /// </summary>
    [TestFixture]
    [Category("TcpTransportListenerDeterministic")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class TcpTransportListenerDeterministicTests
    {
        private ITelemetryContext m_telemetry = null!;

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
        }

        [Test]
        public void IsBlockedReturnsFalseForUnknownIpAddress()
        {
            var time = new FakeTimeProvider();
            using var tracker = new ActiveClientTracker(m_telemetry, time);

            Assert.That(tracker.IsBlocked(IPAddress.Parse("10.0.0.99")), Is.False);
        }

        [Test]
        public void AddClientActionBlocksIpWhenActionCountExceedsThresholdWithinWindow()
        {
            var time = new FakeTimeProvider();
            using var tracker = new ActiveClientTracker(m_telemetry, time);
            IPAddress ip = IPAddress.Parse("10.0.0.1");

            // First three actions inside the 10s window stay under the block
            // threshold (block happens only once the count exceeds three).
            tracker.AddClientAction(ip);
            Assert.That(tracker.IsBlocked(ip), Is.False);

            time.Advance(TimeSpan.FromSeconds(2));
            tracker.AddClientAction(ip);
            Assert.That(tracker.IsBlocked(ip), Is.False);

            time.Advance(TimeSpan.FromSeconds(2));
            tracker.AddClientAction(ip);
            Assert.That(tracker.IsBlocked(ip), Is.False);

            // The fourth action within the window trips the limiter.
            time.Advance(TimeSpan.FromSeconds(2));
            tracker.AddClientAction(ip);
            Assert.That(tracker.IsBlocked(ip), Is.True);

            // A further action while already blocked keeps the client blocked and
            // exercises the early-return path for clients that are still blocked.
            tracker.AddClientAction(ip);
            Assert.That(tracker.IsBlocked(ip), Is.True);
        }

        [Test]
        public void AddClientActionDoesNotBlockIpAtExactlyThreeActions()
        {
            var time = new FakeTimeProvider();
            using var tracker = new ActiveClientTracker(m_telemetry, time);
            IPAddress ip = IPAddress.Parse("10.0.0.2");

            tracker.AddClientAction(ip);
            tracker.AddClientAction(ip);
            tracker.AddClientAction(ip);

            Assert.That(tracker.IsBlocked(ip), Is.False);
        }

        [Test]
        public void AddClientActionNeverBlocksWhenActionsSpacedBeyondWindow()
        {
            var time = new FakeTimeProvider();
            using var tracker = new ActiveClientTracker(m_telemetry, time);
            IPAddress ip = IPAddress.Parse("10.0.0.3");

            // Each action is more than the 10s window apart, so the counter keeps
            // resetting to one and the client is never blocked, however many
            // actions are recorded.
            for (int i = 0; i < 6; i++)
            {
                tracker.AddClientAction(ip);
                Assert.That(tracker.IsBlocked(ip), Is.False);
                time.Advance(TimeSpan.FromSeconds(11));
            }

            Assert.That(tracker.IsBlocked(ip), Is.False);
        }

        [Test]
        public void CleanupUnblocksIpAfterBlockDurationElapses()
        {
            var time = new FakeTimeProvider();
            using var tracker = new ActiveClientTracker(m_telemetry, time);
            IPAddress ip = IPAddress.Parse("10.0.0.4");

            tracker.AddClientAction(ip);
            tracker.AddClientAction(ip);
            tracker.AddClientAction(ip);
            tracker.AddClientAction(ip);
            Assert.That(tracker.IsBlocked(ip), Is.True);

            // Still inside the 30s block duration: the client remains blocked.
            time.Advance(TimeSpan.FromSeconds(10));
            Assert.That(tracker.IsBlocked(ip), Is.True);

            // Past the block duration and across the 15s cleanup period: the
            // cleanup timer fires and unblocks the client.
            time.Advance(TimeSpan.FromSeconds(50));
            Assert.That(tracker.IsBlocked(ip), Is.False);
        }

        [Test]
        public void CleanupRemovesEntryAfterIdleExpiration()
        {
            var time = new FakeTimeProvider();
            using var tracker = new ActiveClientTracker(m_telemetry, time);
            IPAddress ip = IPAddress.Parse("10.0.0.5");

            tracker.AddClientAction(ip);

            // Idle for more than the 10 minute expiration: the cleanup timer fires
            // and removes the stale entry.
            time.Advance(TimeSpan.FromMinutes(11));
            Assert.That(tracker.IsBlocked(ip), Is.False);

            // A subsequent action starts a fresh count and does not appear blocked
            // from any stale state left behind by the removed entry.
            tracker.AddClientAction(ip);
            Assert.That(tracker.IsBlocked(ip), Is.False);
        }

        [Test]
        public void DisposeDisposesCleanupTimerWithoutThrowing()
        {
            var time = new FakeTimeProvider();
            using var tracker = new ActiveClientTracker(m_telemetry, time);

            // Explicit dispose must not throw; the using block disposes a second
            // time, proving Dispose is idempotent.
            Assert.That(tracker.Dispose, Throws.Nothing);
        }

        [Test]
        public void ActiveClientPropertiesRoundTripAssignedValues()
        {
            var client = new ActiveClient
            {
                LastActionTicks = 111,
                ActiveActionCount = 4,
                BlockedUntilTicks = 222
            };

            Assert.That(client.LastActionTicks, Is.EqualTo(111));
            Assert.That(client.ActiveActionCount, Is.EqualTo(4));
            Assert.That(client.BlockedUntilTicks, Is.EqualTo(222));

            client.LastActionTicks = 333;
            client.ActiveActionCount = 0;
            client.BlockedUntilTicks = 0;

            Assert.That(client.LastActionTicks, Is.EqualTo(333));
            Assert.That(client.ActiveActionCount, Is.Zero);
            Assert.That(client.BlockedUntilTicks, Is.Zero);
        }

        [Test]
        public void FactoryUriSchemeIsOpcTcp()
        {
            var factory = new TcpTransportListenerFactory();

            Assert.That(factory.UriScheme, Is.EqualTo(Utils.UriSchemeOpcTcp));
        }

        [Test]
        public async Task FactoryCreateReturnsTcpTransportListenerInstanceAsync()
        {
            var factory = new TcpTransportListenerFactory();

            await using ITransportListener listener = factory.Create(m_telemetry);

            Assert.That(listener, Is.InstanceOf<TcpTransportListener>());
            Assert.That(listener, Is.InstanceOf<ITransportListener>());
            Assert.That(listener.UriScheme, Is.EqualTo(Utils.UriSchemeOpcTcp));
        }

        [Test]
        public async Task ListenerConstructedWithTelemetryExposesOpcTcpSchemeAsync()
        {
            await using var listener = new TcpTransportListener(m_telemetry);

            Assert.That(listener.UriScheme, Is.EqualTo(Utils.UriSchemeOpcTcp));
        }

        [Test]
        public async Task ListenerConstructedWithTimeProviderExposesOpcTcpSchemeAsync()
        {
            await using var listener = new TcpTransportListener(m_telemetry, new FakeTimeProvider());

            Assert.That(listener.UriScheme, Is.EqualTo(Utils.UriSchemeOpcTcp));
        }
    }
}
