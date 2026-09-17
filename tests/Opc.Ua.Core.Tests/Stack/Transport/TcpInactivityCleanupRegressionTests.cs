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
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    /// <summary>
    /// Verifies that the listener's inactivity cleanup keeps an open SecureChannel while its
    /// security token is valid (OPC 10000-4 §5.6.2.1) and still closes stale channels.
    /// </summary>
    /// <remarks>
    /// CTT Security None 007.js keeps SecureChannels without a Session idle for about 50 s and
    /// expects the newest one, whose token is valid for 60 s, to still be open.
    /// </remarks>
    [TestFixture]
    [Category("TcpTransportListenerDeterministic")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class TcpInactivityCleanupRegressionTests
    {
        private const int kChannelLifetime = 30000;
        private const int kTokenLifetime = 60000;

        private ITelemetryContext m_telemetry = null!;
        private BufferManager m_buffers = null!;
        private ChannelQuotas m_quotas = null!;

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_buffers = new BufferManager("inactivity-cleanup-test", 8192, m_telemetry);
            m_quotas = new ChannelQuotas(ServiceMessageContext.Create(m_telemetry))
            {
                ChannelLifetime = kChannelLifetime
            };
        }

        [Test]
        public void OpenChannelWithValidTokenIsNotDueAfterChannelLifetime()
        {
            var clock = new FakeTimeProvider();
            using TestChannel channel = CreateChannel(clock);
            channel.OpenWithToken(kTokenLifetime);

            clock.Advance(TimeSpan.FromSeconds(45));

            Assert.That(channel.ElapsedSinceLastActiveTime, Is.GreaterThan(kChannelLifetime));
            Assert.That(channel.IsInactivityCleanupDue(kChannelLifetime), Is.False);
        }

        [Test]
        public void OpenChannelIsDueOnceTokenHasExpired()
        {
            var clock = new FakeTimeProvider();
            using TestChannel channel = CreateChannel(clock);
            channel.OpenWithToken(kTokenLifetime);

            clock.Advance(TimeSpan.FromMilliseconds(kTokenLifetime + 1));

            Assert.That(channel.IsInactivityCleanupDue(kChannelLifetime), Is.True);
        }

        [Test]
        public void OpenChannelUsedBySessionIsDueAfterChannelLifetimeWithValidToken()
        {
            // a silent channel that carries a Session keeps the inactivity timeout
            // (Opc.Ua.Sessions.Tests ClientTest.ConnectCloseSessionCloseChannelAsync).
            var clock = new FakeTimeProvider();
            using TestChannel channel = CreateChannel(clock);
            channel.OpenWithToken(kTokenLifetime);
            channel.AttachSession();

            clock.Advance(TimeSpan.FromMilliseconds(kChannelLifetime + 1));

            Assert.That(channel.IsInactivityCleanupDue(kChannelLifetime), Is.True);
        }

        [Test]
        public void OpenChannelWithoutTokenIsDueAfterChannelLifetime()
        {
            var clock = new FakeTimeProvider();
            using TestChannel channel = CreateChannel(clock);
            channel.SetState(TcpChannelState.Open);

            clock.Advance(TimeSpan.FromMilliseconds(kChannelLifetime + 1));

            Assert.That(channel.IsInactivityCleanupDue(kChannelLifetime), Is.True);
        }

        [Test]
        public void ChannelThatDidNotOpenIsDueAfterChannelLifetime(
            [Values(TcpChannelState.Connecting, TcpChannelState.Opening, TcpChannelState.Faulted)] TcpChannelState state)
        {
            var clock = new FakeTimeProvider();
            using TestChannel channel = CreateChannel(clock);
            channel.OpenWithToken(kTokenLifetime);
            channel.SetState(state);

            clock.Advance(TimeSpan.FromMilliseconds(kChannelLifetime + 1));

            Assert.That(channel.IsInactivityCleanupDue(kChannelLifetime), Is.True);
        }

        [Test]
        public void ActiveChannelIsNotDue()
        {
            var clock = new FakeTimeProvider();
            using TestChannel channel = CreateChannel(clock);
            channel.SetState(TcpChannelState.Open);

            clock.Advance(TimeSpan.FromMilliseconds(kChannelLifetime + 1));
            channel.UpdateLastActiveTime();

            Assert.That(channel.IsInactivityCleanupDue(kChannelLifetime), Is.False);
        }

        [Test]
        public async Task DetectInactiveChannelsKeepsIdleChannelWithValidTokenAndClosesStaleChannelAsync()
        {
            var clock = new FakeTimeProvider();
            await using var listener = new TcpTransportListener(m_telemetry, clock);
            var channels = new ConcurrentDictionary<uint, TcpListenerChannel>();
            SetField(listener, "m_channels", channels);
            SetField(listener, "m_quotas", m_quotas);

            using TestChannel stale = CreateChannel(clock);
            stale.SetState(TcpChannelState.Open);
            channels[1] = stale;

            clock.Advance(TimeSpan.FromSeconds(10));
            using TestChannel idle = CreateChannel(clock);
            idle.OpenWithToken(kTokenLifetime);
            channels[2] = idle;

            // 51 s of silence for the channel with the valid token, 61 s for the stale one.
            clock.Advance(TimeSpan.FromSeconds(51));
            InvokeDetectInactiveChannels(listener);

            Assert.That(idle.CurrentState, Is.EqualTo(TcpChannelState.Open));
            Assert.That(stale.CurrentState, Is.EqualTo(TcpChannelState.Closed));

            // once the token has expired the idle channel is cleaned up as well.
            clock.Advance(TimeSpan.FromSeconds(10));
            InvokeDetectInactiveChannels(listener);

            Assert.That(idle.CurrentState, Is.EqualTo(TcpChannelState.Closed));
        }

        private TestChannel CreateChannel(FakeTimeProvider clock)
        {
            var listener = new Mock<ITcpChannelListener>();
            listener.Setup(l => l.EndpointUrl).Returns(new Uri("opc.tcp://localhost:4840"));
            var channel = new TestChannel(listener.Object, m_buffers, m_quotas, m_telemetry, clock);
            channel.UpdateLastActiveTime();
            return channel;
        }

        private static void InvokeDetectInactiveChannels(TcpTransportListener listener)
        {
            MethodInfo detect = typeof(TcpTransportListener).GetMethod(
                "DetectInactiveChannels",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            detect.Invoke(listener, [null]);
        }

        private static void SetField<T>(TcpTransportListener listener, string name, T value)
        {
            FieldInfo field = typeof(TcpTransportListener).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException($"Missing listener field {name}.");
            field.SetValue(listener, value);
        }

        /// <summary>
        /// Listener channel whose state and security token can be set without a handshake.
        /// </summary>
        private sealed class TestChannel : TcpListenerChannel
        {
            public TestChannel(
                ITcpChannelListener listener,
                BufferManager buffers,
                ChannelQuotas quotas,
                ITelemetryContext telemetry,
                TimeProvider timeProvider)
                : base("inactivity", listener, buffers, quotas, null!, [], telemetry, timeProvider)
            {
            }

            /// <summary>
            /// Gets the channel state.
            /// </summary>
            public TcpChannelState CurrentState => State;

            /// <summary>
            /// Sets the channel state.
            /// </summary>
            public void SetState(TcpChannelState state)
            {
                State = state;
            }

            /// <summary>
            /// Records a Session on the channel.
            /// </summary>
            public void AttachSession()
            {
                AddSession();
            }

            /// <summary>
            /// Opens the channel with a token created now.
            /// </summary>
            public void OpenWithToken(int lifetime)
            {
                State = TcpChannelState.Open;
                ((IDiagnosticsChannelMutation)this).LoadTokensForOfflineDecode(
                    new ChannelToken
                    {
                        ChannelId = 1,
                        TokenId = 1,
                        SecurityPolicy = SecurityPolicyInfo.None,
                        CreatedAt = DateTime.UtcNow,
                        CreatedAtTimestamp = TimeProvider.GetTimestamp(),
                        Lifetime = lifetime
                    },
                    previous: null);
            }
        }
    }
}
