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

#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Regression tests for activating a Session whose timeout has elapsed
    /// before the session monitor closed it (CTT Session Base 002.js).
    /// </summary>
    [TestFixture]
    [Category("Session")]
    public class SessionManagerExpiryTests
    {
        private static readonly TimeSpan s_callTimeout = TimeSpan.FromSeconds(30);

        [Test]
        public async Task ActivateExpiredSessionReturnsSessionClosedWithoutBlockingLaterSessionsAsync()
        {
            var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
            var fixture = new ServerFixture<ExpiringSessionServer>(t => new ExpiringSessionServer(t, clock));
            ExpiringSessionServer server = await fixture.StartAsync().ConfigureAwait(false);
            try
            {
                (RequestHeader requestHeader, SecureChannelContext secureChannelContext) =
                    await server.CreateAndActivateSessionAsync("ExpiredActivate").ConfigureAwait(false);
                ISession? session = server.CurrentInstance.SessionManager
                    .GetSession(requestHeader.AuthenticationToken);
                Assert.That(session, Is.Not.Null);
                var serverInternal = (ServerInternalData)server.CurrentInstance;
                uint timeoutsBefore = serverInternal.ServerDiagnostics.SessionTimeoutCount;

                // The session monitor is disabled, so only ActivateSession can notice the expiry.
                // The fake clock only drives the session manager, so jumping past any
                // configured MaxSessionTimeout affects nothing else.
                clock.Advance(TimeSpan.FromDays(2));
                Assert.That(session!.HasExpired, Is.True);

                Task<ActivateSessionResponse> activate = server.ActivateSessionAsync(
                    secureChannelContext,
                    requestHeader,
                    null,
                    [],
                    [],
                    default,
                    null,
                    RequestLifetime.None).AsTask();
                Task completed = await Task.WhenAny(activate, Task.Delay(s_callTimeout)).ConfigureAwait(false);
                Assert.That(completed, Is.SameAs(activate),
                    "ActivateSession on an expired session must not deadlock the session manager.");
                ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(() => activate)!;
                Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadSessionClosed));

                Assert.That(
                    server.CurrentInstance.SessionManager.GetSession(requestHeader.AuthenticationToken),
                    Is.Null);
                Assert.That(
                    serverInternal.ServerDiagnostics.SessionTimeoutCount,
                    Is.EqualTo(timeoutsBefore + 1));

                Task<(RequestHeader, SecureChannelContext)> next =
                    server.CreateAndActivateSessionAsync("AfterExpiredActivate");
                completed = await Task.WhenAny(next, Task.Delay(s_callTimeout)).ConfigureAwait(false);
                Assert.That(completed, Is.SameAs(next),
                    "CreateSession after closing an expired session must not block.");
                (RequestHeader nextHeader, SecureChannelContext nextContext) = await next.ConfigureAwait(false);
                await server.CloseSessionAsync(nextContext, nextHeader, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ConcurrentActivationsOfAnExpiredSessionCountTheTimeoutOnceAsync()
        {
            var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
            var fixture = new ServerFixture<ExpiringSessionServer>(t => new ExpiringSessionServer(t, clock));
            ExpiringSessionServer server = await fixture.StartAsync().ConfigureAwait(false);
            try
            {
                var serverInternal = (ServerInternalData)server.CurrentInstance;
                for (int round = 0; round < 5; round++)
                {
                    (RequestHeader requestHeader, SecureChannelContext secureChannelContext) =
                        await server.CreateAndActivateSessionAsync("ConcurrentExpired" + round)
                            .ConfigureAwait(false);
                    uint timeoutsBefore = serverInternal.ServerDiagnostics.SessionTimeoutCount;
                    clock.Advance(TimeSpan.FromDays(2));

                    var activations = new Task<ActivateSessionResponse>[8];
                    for (int ii = 0; ii < activations.Length; ii++)
                    {
                        activations[ii] = Task.Run(() => server.ActivateSessionAsync(
                            secureChannelContext,
                            requestHeader,
                            null,
                            [],
                            [],
                            default,
                            null,
                            RequestLifetime.None).AsTask());
                    }
                    Task all = Task.WhenAll(activations);
                    Task completed = await Task.WhenAny(all, Task.Delay(s_callTimeout)).ConfigureAwait(false);
                    Assert.That(completed, Is.SameAs(all), "Concurrent activations must not block.");

                    foreach (Task<ActivateSessionResponse> activation in activations)
                    {
                        ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(() => activation)!;
                        Assert.That(
                            ex.StatusCode,
                            Is.EqualTo(StatusCodes.BadSessionClosed).Or.EqualTo(StatusCodes.BadSessionIdInvalid));
                    }
                    Assert.That(
                        serverInternal.ServerDiagnostics.SessionTimeoutCount,
                        Is.EqualTo(timeoutsBefore + 1),
                        "One expired session must be counted as one timeout.");
                }
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Server whose session manager uses a fake clock and no session monitor loop.
        /// </summary>
        public sealed class ExpiringSessionServer : StandardServer
        {
            private readonly TimeProvider m_clock;

            public ExpiringSessionServer(ITelemetryContext telemetry, TimeProvider clock)
                : base(telemetry)
            {
                m_clock = clock;
            }

            protected override ISessionManager CreateSessionManager(
                IServerInternal server,
                ApplicationConfiguration configuration)
            {
                return new MonitorlessSessionManager(server, configuration, m_clock);
            }
        }

        private sealed class MonitorlessSessionManager : SessionManager
        {
            public MonitorlessSessionManager(
                IServerInternal server,
                ApplicationConfiguration configuration,
                TimeProvider clock)
                : base(server, configuration, clock)
            {
            }

            public override ValueTask StartupAsync(CancellationToken cancellationToken = default)
            {
                return default;
            }
        }
    }
}
