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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Identity;

using ManagedSessionClass = Opc.Ua.Client.ManagedSession;

namespace Opc.Ua.Client.Tests.Identity
{
    [TestFixture]
    [Category("Session")]
    [Category("Identity")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class ProactiveRefreshTests : ClientTestFramework
    {
        public ProactiveRefreshTests()
            : base(Utils.UriSchemeOpcTcp)
        {
            SingleSession = false;
        }

        [OneTimeSetUp]
        public override Task OneTimeSetUpAsync()
        {
            SingleSession = false;
            return base.OneTimeSetUpAsync();
        }

        [OneTimeTearDown]
        public override Task OneTimeTearDownAsync()
        {
            return base.OneTimeTearDownAsync();
        }

        [Test]
        public async Task ProactiveRefreshCallsProviderBeforeExpiry()
        {
            var timeProvider = new FakeTimeProvider();
            var provider = new RefreshingUserNameProvider(timeProvider);
            ManagedSessionClass session = await CreateManagedSessionAsync(provider, timeProvider)
                .ConfigureAwait(false);
            await using (session.ConfigureAwait(false))
            {
                Assert.That(provider.CallCount, Is.EqualTo(1));

                timeProvider.Advance(TimeSpan.FromSeconds(60));
                await WaitUntilAsync(() => provider.CallCount >= 2).ConfigureAwait(false);

                Assert.That(session.Connected, Is.True);
                ServerStatusDataType status = await session
                    .ReadValueAsync<ServerStatusDataType>(VariableIds.Server_ServerStatus)
                    .ConfigureAwait(false);
                Assert.That(status, Is.Not.Null);
            }
        }

        [Test]
        public async Task RefreshFailureKeepsSessionAliveAndRetries()
        {
            var timeProvider = new FakeTimeProvider();
            var provider = new RefreshingUserNameProvider(timeProvider)
            {
                ThrowOnCall = 2
            };
            ManagedSessionClass session = await CreateManagedSessionAsync(provider, timeProvider)
                .ConfigureAwait(false);
            await using (session.ConfigureAwait(false))
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

                timeProvider.Advance(TimeSpan.FromSeconds(60));
                await session.EnsureRefreshAsync(timeout.Token).ConfigureAwait(false);

                Assert.That(provider.CallCount, Is.EqualTo(2));
                Assert.That(session.Connected, Is.True);

                timeProvider.Advance(TimeSpan.FromSeconds(6));
                await session.EnsureRefreshAsync(timeout.Token).ConfigureAwait(false);

                Assert.That(provider.CallCount, Is.EqualTo(3));
                Assert.That(session.Connected, Is.True);
            }
        }

        private async Task<ManagedSessionClass> CreateManagedSessionAsync(
            IClientIdentityProvider provider,
            TimeProvider timeProvider)
        {
            Endpoints = await ClientFixture.GetEndpointsAsync(ServerUrl).ConfigureAwait(false);
            ConfiguredEndpoint endpoint = await ClientFixture
                .GetEndpointAsync(ServerUrl, SecurityPolicies.Basic256Sha256, Endpoints)
                .ConfigureAwait(false);
            var userIdentity = new UserIdentity("user1", "password"u8);
            UserTokenPolicy identityPolicy = endpoint.Description.FindUserTokenPolicy(
                userIdentity.TokenType,
                userIdentity.IssuedTokenType,
                endpoint.Description.SecurityPolicyUri);
            if (identityPolicy == null)
            {
                Assert.Ignore("The test server endpoint does not advertise UserName tokens.");
            }

            return await ManagedSessionClass.CreateAsync(
                ClientFixture.Config,
                endpoint,
                new DefaultSessionFactory(Telemetry),
                identity: null,
                telemetry: Telemetry,
                sessionName: "ProactiveRefreshTests",
                sessionTimeout: ClientFixture.SessionTimeout,
                checkDomain: false,
                ct: CancellationToken.None,
                identityProvider: provider,
                timeProvider: timeProvider).ConfigureAwait(false);
        }

        private static async Task WaitUntilAsync(Func<bool> predicate)
        {
            for (int ii = 0; ii < 100; ii++)
            {
                if (predicate())
                {
                    return;
                }
                await Task.Delay(20).ConfigureAwait(false);
            }
            Assert.That(predicate(), Is.True);
        }

        private sealed class RefreshingUserNameProvider : IClientIdentityProvider
        {
            private readonly TimeProvider m_timeProvider;

            public RefreshingUserNameProvider(TimeProvider timeProvider)
            {
                m_timeProvider = timeProvider;
                ExpiresAt = m_timeProvider.GetUtcNow().UtcDateTime.AddMinutes(2);
            }

            public int CallCount { get; private set; }

            public int ThrowOnCall { get; set; }

            public IReadOnlyList<UserTokenType> SupportedTokenTypes => new[] { UserTokenType.UserName };

            public IReadOnlyList<string> SupportedIssuedTokenProfileUris => Array.Empty<string>();

            public DateTime ExpiresAt { get; private set; }

            public bool CanSatisfy(UserTokenPolicy policy, IdentitySelectionContext context)
            {
                return policy.TokenType == UserTokenType.UserName;
            }

            public ValueTask<IUserIdentity> GetIdentityAsync(
                UserTokenPolicy policy,
                IdentitySelectionContext context,
                CancellationToken ct = default)
            {
                CallCount++;
                if (ThrowOnCall == CallCount)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadIdentityTokenRejected,
                        "Synthetic refresh failure.");
                }

                ExpiresAt = m_timeProvider.GetUtcNow().UtcDateTime.AddMinutes(2);
                var identity = new UserIdentity("user1", "password"u8)
                {
                    PolicyId = policy.PolicyId
                };
                return new ValueTask<IUserIdentity>(identity);
            }
        }

        private sealed class FakeTimeProvider : TimeProvider
        {
            private readonly List<FakeTimer> m_timers = [];
            private DateTimeOffset m_utcNow = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

            public override DateTimeOffset GetUtcNow()
            {
                return m_utcNow;
            }

            public void Advance(TimeSpan delta)
            {
                List<FakeTimer> dueTimers;
                lock (m_timers)
                {
                    m_utcNow += delta;
                    dueTimers = [];
                    foreach (FakeTimer timer in m_timers)
                    {
                        if (timer.IsDue(m_utcNow))
                        {
                            dueTimers.Add(timer);
                        }
                    }
                }

                foreach (FakeTimer timer in dueTimers)
                {
                    timer.Fire(m_utcNow);
                }
            }

            public override ITimer CreateTimer(
                TimerCallback callback,
                object state,
                TimeSpan dueTime,
                TimeSpan period)
            {
                var timer = new FakeTimer(this, callback, state, dueTime, period);
                lock (m_timers)
                {
                    m_timers.Add(timer);
                }
                return timer;
            }

            private void Remove(FakeTimer timer)
            {
                lock (m_timers)
                {
                    m_timers.Remove(timer);
                }
            }

            private sealed class FakeTimer : ITimer
            {
                private readonly FakeTimeProvider m_owner;
                private readonly TimerCallback m_callback;
                private readonly object m_state;
                private TimeSpan m_period;
                private DateTimeOffset? m_dueAt;
                private bool m_disposed;

                public FakeTimer(
                    FakeTimeProvider owner,
                    TimerCallback callback,
                    object state,
                    TimeSpan dueTime,
                    TimeSpan period)
                {
                    m_owner = owner;
                    m_callback = callback;
                    m_state = state;
                    Change(dueTime, period);
                }

                public bool Change(TimeSpan dueTime, TimeSpan period)
                {
                    m_period = period;
                    if (dueTime == Timeout.InfiniteTimeSpan)
                    {
                        m_dueAt = null;
                    }
                    else
                    {
                        m_dueAt = m_owner.GetUtcNow() + dueTime;
                    }
                    return true;
                }

                public bool IsDue(DateTimeOffset utcNow)
                {
                    return !m_disposed && m_dueAt.HasValue && m_dueAt.Value <= utcNow;
                }

                public void Fire(DateTimeOffset utcNow)
                {
                    if (!IsDue(utcNow))
                    {
                        return;
                    }

                    if (m_period == Timeout.InfiniteTimeSpan)
                    {
                        m_dueAt = null;
                    }
                    else
                    {
                        m_dueAt = utcNow + m_period;
                    }

                    m_callback(m_state);
                }

                public void Dispose()
                {
                    m_disposed = true;
                    m_owner.Remove(this);
                }

                public ValueTask DisposeAsync()
                {
                    Dispose();
                    return default;
                }
            }
        }
    }
}
