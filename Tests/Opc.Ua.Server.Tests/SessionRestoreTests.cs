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

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Tests for session restoration via <see cref="SessionManager.SupportsSessionRestore"/>.
    /// </summary>
    [TestFixture]
    [Category("Session")]
    [NonParallelizable]
    public sealed class SessionRestoreTests
    {
        [Test]
        public void DefaultSupportsSessionRestoreIsFalse()
        {
            var server = new Mock<IServerInternal>();
            server.Setup(s => s.Telemetry).Returns(NUnitTelemetryContext.Create());

            using var manager = new DefaultSessionManagerAccessor(server.Object, CreateConfiguration());

            Assert.That(manager.SupportsRestore, Is.False);
        }

        [Test]
        public async Task DefaultSessionManagerRejectsUnknownAuthenticationTokenAsync()
        {
            var fixture = new ServerFixture<StandardServer>(t => new StandardServer(t))
            {
                SecurityNone = true
            };

            try
            {
                StandardServer server = await fixture.StartAsync().ConfigureAwait(false);

                ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                    async () => await server.ActivateSessionAsync(
                        CreateChannelContext(server),
                        new RequestHeader
                        {
                            AuthenticationToken = new NodeId("unknown-token", 2)
                        },
                        null,
                        [],
                        [],
                        default,
                        null,
                        RequestLifetime.None).ConfigureAwait(false))!;

                Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task RestoredSessionIsAdmittedAndActivatedAsync()
        {
            var factory = new TestSessionManagerFactory(RestoreBehavior.Restore);
            var fixture = new ServerFixture<StandardServer>(t => new StandardServer(t)
            {
                SessionManagerFactory = factory
            })
            {
                SecurityNone = true
            };

            try
            {
                StandardServer server = await fixture.StartAsync().ConfigureAwait(false);
                var authenticationToken = new NodeId("restored-token", 2);

                ActivateSessionResponse response = await server.ActivateSessionAsync(
                    CreateChannelContext(server),
                    new RequestHeader
                    {
                        AuthenticationToken = authenticationToken
                    },
                    null,
                    [],
                    [],
                    default,
                    null,
                    RequestLifetime.None).ConfigureAwait(false);

                ServerFixtureUtils.ValidateResponse(response.ResponseHeader);
                Assert.That(factory.Manager, Is.Not.Null);
                Assert.That(factory.Manager!.SupportsSessionRestoreForTests, Is.True);
                Assert.That(factory.Manager.GetSession(authenticationToken), Is.Not.Null);
                Assert.That(factory.Manager.RestoreAttempts[authenticationToken.ToString()], Is.EqualTo(1));
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task RestoreOverrideReturningNullStillRejectsUnknownTokenAsync()
        {
            var factory = new TestSessionManagerFactory(RestoreBehavior.ReturnNull);
            var fixture = new ServerFixture<StandardServer>(t => new StandardServer(t)
            {
                SessionManagerFactory = factory
            })
            {
                SecurityNone = true
            };

            try
            {
                StandardServer server = await fixture.StartAsync().ConfigureAwait(false);
                var authenticationToken = new NodeId("missing-token", 2);

                ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                    async () => await server.ActivateSessionAsync(
                        CreateChannelContext(server),
                        new RequestHeader
                        {
                            AuthenticationToken = authenticationToken
                        },
                        null,
                        [],
                        [],
                        default,
                        null,
                        RequestLifetime.None).ConfigureAwait(false))!;

                Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
                Assert.That(factory.Manager, Is.Not.Null);
                Assert.That(factory.Manager!.RestoreAttempts[authenticationToken.ToString()], Is.EqualTo(1));
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task DelayedRestoreDoesNotBlockConcurrentSessionOperationsAsync()
        {
            var factory = new TestSessionManagerFactory(RestoreBehavior.DelayThenRestore);
            var fixture = new ServerFixture<StandardServer>(t => new StandardServer(t)
            {
                SessionManagerFactory = factory
            })
            {
                SecurityNone = true
            };

            try
            {
                StandardServer server = await fixture.StartAsync().ConfigureAwait(false);
                TestRestoreSessionManager manager = factory.Manager!;
                var sameToken = new NodeId("shared-token", 2);
                var differentToken = new NodeId("different-token", 2);

                Task<ActivateSessionResponse> first = ActivateUnknownAsync(server, sameToken);
                await manager.WaitForRestoreAttemptsAsync(1).ConfigureAwait(false);

                Task<ActivateSessionResponse> second = ActivateUnknownAsync(server, sameToken);
                Task<ActivateSessionResponse> third = ActivateUnknownAsync(server, differentToken);
                await manager.WaitForRestoreAttemptsAsync(3).ConfigureAwait(false);

                Task<(RequestHeader, SecureChannelContext)> unrelatedCreate =
                    server.CreateAndActivateSessionAsync("ConcurrentCreate", useSecurity: false);
                Task completed = await Task.WhenAny(
                    unrelatedCreate,
                    Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false);

                Assert.That(completed, Is.SameAs(unrelatedCreate),
                    "Create/ActivateSession should not be blocked by a delayed restore.");

                manager.ReleaseDelayedRestores();

                ServerFixtureUtils.ValidateResponse((await first.ConfigureAwait(false)).ResponseHeader);
                ServerFixtureUtils.ValidateResponse((await second.ConfigureAwait(false)).ResponseHeader);
                ServerFixtureUtils.ValidateResponse((await third.ConfigureAwait(false)).ResponseHeader);

                Assert.That(manager.GetSession(sameToken), Is.Not.Null);
                Assert.That(manager.GetSession(differentToken), Is.Not.Null);
                Assert.That(manager.GetSessions(), Has.Count.EqualTo(3));
                Assert.That(manager.RestoreAttempts[sameToken.ToString()], Is.EqualTo(2));
                Assert.That(manager.RestoreAttempts[differentToken.ToString()], Is.EqualTo(1));
                Assert.That(manager.DisposedRestoredSessions[sameToken.ToString()], Is.EqualTo(1));
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        private static Task<ActivateSessionResponse> ActivateUnknownAsync(
            StandardServer server,
            NodeId authenticationToken)
        {
            return server.ActivateSessionAsync(
                CreateChannelContext(server),
                new RequestHeader
                {
                    AuthenticationToken = authenticationToken
                },
                null,
                [],
                [],
                default,
                null,
                RequestLifetime.None).AsTask();
        }

        private static SecureChannelContext CreateChannelContext(StandardServer server)
        {
            EndpointDescription endpoint = server.GetEndpoints()
                .Find(e => e.SecurityMode == MessageSecurityMode.None)!;
            return new SecureChannelContext(
                "session-restore-channel",
                endpoint,
                RequestEncoding.Binary,
                null,
                null,
                null);
        }

        private enum RestoreBehavior
        {
            ReturnNull,
            Restore,
            DelayThenRestore
        }

        private static ApplicationConfiguration CreateConfiguration()
        {
            return new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration
                {
                    MinSessionTimeout = 1000,
                    MaxSessionTimeout = 3_600_000,
                    MaxSessionCount = 100,
                    MaxRequestAge = 60_000,
                    MaxBrowseContinuationPoints = 10,
                    MaxHistoryContinuationPoints = 10
                }
            };
        }

        private sealed class DefaultSessionManagerAccessor : SessionManager
        {
            public DefaultSessionManagerAccessor(IServerInternal server, ApplicationConfiguration configuration)
                : base(server, configuration)
            {
            }

            public bool SupportsRestore => SupportsSessionRestore;
        }

        private sealed class TestSessionManagerFactory : ISessionManagerFactory
        {
            public TestSessionManagerFactory(RestoreBehavior behavior)
            {
                m_behavior = behavior;
            }

            public TestRestoreSessionManager? Manager { get; private set; }

            public ISessionManager Create(
                IServerInternal server,
                ApplicationConfiguration configuration,
                TimeProvider timeProvider,
                Func<string, Certificate?> serverCertificateProvider)
            {
                Manager = new TestRestoreSessionManager(server, configuration, m_behavior, timeProvider);
                return Manager;
            }

            private readonly RestoreBehavior m_behavior;
        }

        private sealed class TestRestoreSessionManager : SessionManager
        {
            public TestRestoreSessionManager(
                IServerInternal server,
                ApplicationConfiguration configuration,
                RestoreBehavior behavior,
                TimeProvider? timeProvider)
                : base(server, configuration, timeProvider)
            {
                m_server = server;
                m_behavior = behavior;
                m_serverCertificate = CertificateBuilder
                    .Create("CN=SessionRestoreTests")
                    .SetRSAKeySize(CertificateFactory.DefaultKeySize)
                    .CreateForRSA();
            }

            public bool SupportsSessionRestoreForTests => SupportsSessionRestore;

            public ConcurrentDictionary<string, int> RestoreAttempts { get; } = new(StringComparer.Ordinal);

            public ConcurrentDictionary<string, int> DisposedRestoredSessions { get; } = new(StringComparer.Ordinal);

            public Task WaitForRestoreAttemptsAsync(int count)
            {
                return WaitForAsync(
                    () => RestoreAttempts.Values.Sum() >= count,
                    TimeSpan.FromSeconds(10));
            }

            public void ReleaseDelayedRestores()
            {
                m_restoreGate.TrySetResult(true);
            }

            protected override bool SupportsSessionRestore => true;

            protected override async ValueTask<ISession?> RestoreSessionAsync(
                NodeId authenticationToken,
                OperationContext context,
                CancellationToken cancellationToken = default)
            {
                RestoreAttempts.AddOrUpdate(authenticationToken.ToString(), 1, static (_, value) => value + 1);

                if (m_behavior == RestoreBehavior.ReturnNull)
                {
                    return null;
                }

                if (m_behavior == RestoreBehavior.DelayThenRestore)
                {
                    using var registration = cancellationToken.Register(
                        static state => ((TaskCompletionSource<bool>)state!).TrySetCanceled(),
                        m_restoreGate);
                    await m_restoreGate.Task.ConfigureAwait(false);
                }

                return await CreateRestoredSessionAsync(authenticationToken, context, cancellationToken)
                    .ConfigureAwait(false);
            }

            protected override ISession CreateSession(
                OperationContext context,
                IServerInternal server,
                Certificate serverCertificate,
                NodeId sessionCookie,
                ByteString clientNonce,
                Nonce serverNonce,
                string sessionName,
                ApplicationDescription clientDescription,
                string endpointUrl,
                Certificate clientCertificate,
                CertificateCollection clientCertificateChain,
                double sessionTimeout,
                uint maxResponseMessageSize,
                int maxRequestAge,
                int maxContinuationPoints)
            {
                return new TrackingSession(
                    context,
                    server,
                    serverCertificate,
                    sessionCookie,
                    clientNonce,
                    serverNonce,
                    sessionName,
                    clientDescription,
                    endpointUrl,
                    clientCertificate,
                    clientCertificateChain,
                    sessionTimeout,
                    maxBrowseContinuationPoints: 10,
                    maxHistoryContinuationPoints: 10,
                    onDispose: () => DisposedRestoredSessions.AddOrUpdate(
                        sessionCookie.ToString(),
                        1,
                        static (_, value) => value + 1));
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    m_serverCertificate.Dispose();
                }

                base.Dispose(disposing);
            }

            private async ValueTask<ISession> CreateRestoredSessionAsync(
                NodeId authenticationToken,
                OperationContext context,
                CancellationToken cancellationToken)
            {
                ISession session = CreateSession(
                    context,
                    m_server,
                    m_serverCertificate,
                    authenticationToken,
                    ByteString.Empty,
                    Nonce.CreateNonce(SecurityPolicies.None),
                    "restored-" + authenticationToken,
                    new ApplicationDescription
                    {
                        ApplicationUri = "urn:test:restored",
                        ApplicationName = new LocalizedText("RestoredSession"),
                        ApplicationType = ApplicationType.Client
                    },
                    context.ChannelContext!.EndpointDescription!.EndpointUrl!,
                    null!,
                    [],
                    60_000,
                    0,
                    0,
                    0);
                await session.InitializeAsync(context, cancellationToken).ConfigureAwait(false);
                return session;
            }

            private readonly IServerInternal m_server;
            private readonly RestoreBehavior m_behavior;
            private readonly Certificate m_serverCertificate;

            private readonly TaskCompletionSource<bool> m_restoreGate =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private sealed class TrackingSession : Server.Session
        {
            public TrackingSession(
                OperationContext context,
                IServerInternal server,
                Certificate serverCertificate,
                NodeId authenticationToken,
                ByteString clientNonce,
                Nonce serverNonce,
                string sessionName,
                ApplicationDescription clientDescription,
                string endpointUrl,
                Certificate clientCertificate,
                CertificateCollection clientCertificateChain,
                double sessionTimeout,
                int maxBrowseContinuationPoints,
                int maxHistoryContinuationPoints,
                Action onDispose)
                : base(
                    context,
                    server,
                    serverCertificate,
                    authenticationToken,
                    clientNonce,
                    serverNonce,
                    sessionName,
                    clientDescription,
                    endpointUrl,
                    clientCertificate,
                    clientCertificateChain,
                    sessionTimeout,
                    maxBrowseContinuationPoints,
                    maxHistoryContinuationPoints)
            {
                m_onDispose = onDispose;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    m_onDispose();
                }

                base.Dispose(disposing);
            }

            private readonly Action m_onDispose;
        }

        private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                {
                    return;
                }

                await Task.Delay(50).ConfigureAwait(false);
            }

            Assert.That(condition(), Is.True);
        }
    }
}
