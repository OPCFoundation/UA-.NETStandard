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
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Test Standard Server startup.
    /// </summary>
    [TestFixture]
    [Category("Server")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ServerStartupTests
    {
        [DatapointSource]
        public string[] UriSchemes =
        [
            Utils.UriSchemeOpcTcp,
            Utils.UriSchemeHttps,
            Utils.UriSchemeOpcHttps
        ];

        /// <summary>
        /// Start a server fixture with different uri schemes.
        /// </summary>
        [Theory]
        public async Task StartServerAsync(string uriScheme)
        {
            var fixture = new ServerFixture<StandardServer>(t => new StandardServer(t));
            Assert.That(fixture, Is.Not.Null);
            fixture.UriScheme = uriScheme;
            StandardServer server = await fixture.StartAsync().ConfigureAwait(false);
            Assert.That(server, Is.Not.Null);
            await Task.Delay(1000).ConfigureAwait(false);
            await fixture.StopAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// When the port is already taken, the first start fails and the
        /// fixture retries on another port. The listener of the failed start
        /// must release what it acquired: the https listeners pin their TLS
        /// certificate before Kestrel binds, and a listener dropped with the
        /// certificate still pinned fails the assembly's certificate leak check.
        /// </summary>
        /// <remarks>
        /// opc.tcp is not covered: on Windows the UA TCP listener binds a port
        /// held by another socket's wildcard binding, so its start does not fail.
        /// </remarks>
        [TestCase(Utils.UriSchemeHttps)]
        [TestCase(Utils.UriSchemeOpcHttps)]
        public async Task StartServerOnTakenPortRetriesOnAnotherPortAsync(string uriScheme)
        {
            // Dual-mode on [::], so it also holds the port Kestrel binds for a
            // host name endpoint.
            TcpListener blocker = TcpListener.Create(0);
            blocker.Start();
            try
            {
                int takenPort = ((IPEndPoint)blocker.LocalEndpoint).Port;
                var fixture = new ServerFixture<StandardServer>(t => new StandardServer(t))
                {
                    UriScheme = uriScheme
                };
                try
                {
                    StandardServer server = await fixture.StartAsync(takenPort).ConfigureAwait(false);
                    Assert.That(server, Is.Not.Null);
                    Assert.That(fixture.Port, Is.Not.EqualTo(takenPort));
                }
                finally
                {
                    await fixture.StopAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                blocker.Stop();
            }
        }

        /// <summary>
        /// A second port collision during a fixed-port fallback still retries.
        /// </summary>
        [TestCase(SocketError.AddressAlreadyInUse)]
        [TestCase(SocketError.AccessDenied)]
        public async Task StartServerRetriesSuccessivePortFailuresAsync(SocketError socketError)
        {
            int attempts = 0;
            var fixture = new ServerFixture<StandardServer>(telemetry =>
            {
                if (++attempts <= 2)
                {
                    throw new SocketException((int)socketError);
                }
                return new StandardServer(telemetry);
            });
            try
            {
                int initialPort = ServerFixtureUtils.GetNextFreeIPPort();
                StandardServer server = await fixture.StartAsync(initialPort).ConfigureAwait(false);
                Assert.That(server, Is.Not.Null);
                Assert.That(attempts, Is.EqualTo(3));
                Assert.That(fixture.Port, Is.Not.EqualTo(initialPort));
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A non-port startup failure is surfaced without trying another port.
        /// </summary>
        [Test]
        public async Task StartServerDoesNotRetryUnrelatedFailureAsync()
        {
            int attempts = 0;
            var failure = new InvalidOperationException("Controlled server factory failure.");
            var fixture = new ServerFixture<StandardServer>(_ =>
            {
                attempts++;
                throw failure;
            });
            try
            {
                InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await fixture.StartAsync(ServerFixtureUtils.GetNextFreeIPPort()).ConfigureAwait(false));
                Assert.That(thrown, Is.SameAs(failure));
                Assert.That(attempts, Is.EqualTo(1));
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Startup retains the original error and both independent session-manager cleanup errors.
        /// </summary>
        [Test]
        public async Task FailedStartupReportsSessionShutdownAndDisposalFailuresAsync()
        {
            var startupFailure = new InvalidOperationException("Controlled session startup failure.");
            var shutdownFailure = new InvalidOperationException("Controlled session shutdown failure.");
            var disposalFailure = new InvalidOperationException("Controlled session disposal failure.");
            var sessions = new Mock<ISessionManager>();
            sessions.Setup(value => value.StartupAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask(Task.FromException(startupFailure)));
            sessions.Setup(value => value.ShutdownAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask(Task.FromException(shutdownFailure)));
            sessions.Setup(value => value.Dispose()).Throws(disposalFailure);
            var fixture = new ServerFixture<FailingSessionStartupServer>(
                telemetry => new FailingSessionStartupServer(telemetry, sessions.Object));
            try
            {
                ServiceResultException failure = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await fixture.StartAsync().ConfigureAwait(false));
                Assert.That(failure.InnerException, Is.InstanceOf<AggregateException>());
                var aggregate = (AggregateException)failure.InnerException!;
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadInternalError));
                    Assert.That(aggregate.Flatten().InnerExceptions,
                        Is.EquivalentTo(new[] { startupFailure, shutdownFailure, disposalFailure }));
                }
                sessions.Verify(value => value.ShutdownAsync(It.IsAny<CancellationToken>()), Times.Once);
                sessions.Verify(value => value.Dispose(), Times.Once);
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// The server only tracks a listener once it opened, so a listener
        /// that fails to open has no other owner: the server disposes it
        /// before it rethrows the error. A dispose that fails as well is
        /// logged and does not replace the open error.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task CreateServiceHostEndpointDisposesListenerThatFailedToOpenAsync(bool disposeFails)
        {
            var fixture = new ServerFixture<StandardServer>(t => new StandardServer(t));
            StandardServer server = await fixture.StartAsync().ConfigureAwait(false);
            try
            {
                var bindError = new IOException("Failed to bind to address: address already in use.");
                var listener = new Mock<ITransportListener>();
                listener
                    .Setup(value => value.OpenAsync(
                        It.IsAny<Uri>(),
                        It.IsAny<TransportListenerSettings>(),
                        It.IsAny<ITransportListenerCallback>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask(Task.FromException(bindError)));
                if (disposeFails)
                {
                    listener
                        .Setup(value => value.DisposeAsync())
                        .Returns(new ValueTask(Task.FromException(
                            new InvalidOperationException("Dispose failed."))));
                }

                IOException thrown = await Assert.ThrowsAsync<IOException>(async () =>
                    await server.CreateServiceHostEndpointAsync(
                        new Uri("opc.tcp://localhost:4840/failed"),
                        [],
                        EndpointConfiguration.Create(fixture.Config),
                        listener.Object,
                        Mock.Of<ICertificateValidatorEx>()).ConfigureAwait(false));

                Assert.That(thrown, Is.SameAs(bindError));
                listener.Verify(value => value.DisposeAsync(), Times.Once);
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// The port a fixture is handed is only known to be free at the moment
        /// it is picked, so a parallel fixture or an unrelated process on the
        /// agent can take it before the server binds. Every transport's way of
        /// reporting that has to be recognised as retryable: the https
        /// listeners bind through Kestrel, which surfaces the collision as an
        /// IOException wrapping an AddressInUseException rather than as a
        /// ServiceResultException, and used to escape the retry and fail the
        /// caller's whole OneTimeSetUp.
        /// </summary>
        [Test]
        public void IsPortUnavailableRecognisesEveryTransportsPortCollision()
        {
            var uaTcp = new ServiceResultException(
                StatusCodes.BadNoCommunication, "Failed to open a listening socket.");
            var inUse = new SocketException((int)SocketError.AddressAlreadyInUse);
            var kestrel = new IOException(
                "Failed to bind to address https://[::]:49254: address already in use.",
                new AddressInUseException("Address already in use.", inUse));

            Assert.Multiple(() =>
            {
                Assert.That(ServerFixtureUtils.IsPortUnavailable(uaTcp), Is.True);
                Assert.That(ServerFixtureUtils.IsPortUnavailable(inUse), Is.True);
                Assert.That(ServerFixtureUtils.IsPortUnavailable(kestrel), Is.True);
                Assert.That(
                    ServerFixtureUtils.IsPortUnavailable(new AggregateException(kestrel)),
                    Is.True);
            });
        }

        /// <summary>
        /// A failure that has nothing to do with the port must not be retried
        /// on a different one - it would only hide the real error behind a
        /// second, identical failure.
        /// </summary>
        [Test]
        public void IsPortUnavailableIgnoresUnrelatedFailures()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    ServerFixtureUtils.IsPortUnavailable(
                        new InvalidOperationException("Application instance certificate invalid!")),
                    Is.False);
                Assert.That(
                    ServerFixtureUtils.IsPortUnavailable(
                        new ServiceResultException(StatusCodes.BadCertificateInvalid)),
                    Is.False);
                Assert.That(
                    ServerFixtureUtils.IsPortUnavailable(
                        new SocketException((int)SocketError.ConnectionRefused)),
                    Is.False);
            });
        }

        /// <summary>
        /// Supplies a failing session manager before runtime ownership transfers to the server.
        /// </summary>
        private sealed class FailingSessionStartupServer : StandardServer
        {
            /// <summary>
            /// Creates a server with the supplied fault-injection session manager.
            /// </summary>
            public FailingSessionStartupServer(ITelemetryContext telemetry, ISessionManager sessions)
                : base(telemetry)
            {
                m_sessions = sessions;
            }

            /// <inheritdoc/>
            protected override ISessionManager CreateSessionManager(
                IServerInternal server,
                ApplicationConfiguration configuration)
            {
                return m_sessions;
            }

            /// <summary>The session manager whose independent failure stages are observed.</summary>
            private readonly ISessionManager m_sessions;
        }
    }
}
