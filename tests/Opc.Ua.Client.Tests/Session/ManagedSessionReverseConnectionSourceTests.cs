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
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests.ManagedSession
{
    /// <summary>
    /// Unit tests for <see cref="IReverseConnectionSource"/> on
    /// <see cref="Client.ManagedSession"/> and <see cref="ManagedSessionBuilder"/>.
    /// </summary>
    [TestFixture]
    [Category("ManagedSession")]
    public sealed class ManagedSessionReverseConnectionSourceTests
    {
        [Test]
        public async Task CreateAsyncRejectsASourceCombinedWithAManagerAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var manager = new ReverseConnectManager(telemetry);
            var options = new ManagedSessionOptions
            {
                Endpoint = CreateEndpoint(),
                ReverseConnectionSource = new Mock<IReverseConnectionSource>(MockBehavior.Strict).Object
            };

            ArgumentException exception = (await Assert.ThrowsAsync<ArgumentException>(async () =>
                await Client.ManagedSession.CreateAsync(
                    options,
                    CreateClientConfiguration(telemetry),
                    new DefaultSessionFactory(telemetry),
                    reverseConnectManager: manager).ConfigureAwait(false)))!;

            Assert.That(exception.ParamName, Is.EqualTo("reverseConnectManager"));
        }

        [Test]
        public void BuilderUseReverseConnectWithSourceSetsTheSourceAndServerUri()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            IReverseConnectionSource source = new Mock<IReverseConnectionSource>(MockBehavior.Strict).Object;
            ConfiguredEndpoint endpoint = CreateEndpoint();

            ManagedSessionOptions options = new ManagedSessionBuilder(CreateClientConfiguration(telemetry), telemetry)
                .UseEndpoint(endpoint)
                .UseReverseConnect(source, new Uri("urn:test:server"))
                .Build();

            Assert.That(options.ReverseConnectionSource, Is.SameAs(source));
            Assert.That(endpoint.ReverseConnect, Is.Not.Null);
            Assert.That(endpoint.ReverseConnect!.Enabled, Is.True);
            Assert.That(endpoint.ReverseConnect.ServerUri, Is.EqualTo("urn:test:server"));
        }

        [Test]
        public async Task BuilderUseReverseConnectWithAManagerReplacesTheSourceAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var manager = new ReverseConnectManager(telemetry);
            IReverseConnectionSource source = new Mock<IReverseConnectionSource>(MockBehavior.Strict).Object;

            ManagedSessionOptions options = new ManagedSessionBuilder(CreateClientConfiguration(telemetry), telemetry)
                .UseEndpoint(CreateEndpoint())
                .UseReverseConnect(source, new Uri("urn:test:server"))
                .UseReverseConnect(manager, new Uri("urn:test:server"))
                .Build();

            Assert.That(options.ReverseConnectionSource, Is.Null);
        }

        [Test]
        public void BuilderUseReverseConnectRejectsNullArguments()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var builder = new ManagedSessionBuilder(CreateClientConfiguration(telemetry), telemetry);
            IReverseConnectionSource source = new Mock<IReverseConnectionSource>(MockBehavior.Strict).Object;

            Assert.Throws<ArgumentNullException>(() =>
                builder.UseReverseConnect((IReverseConnectionSource)null!, new Uri("urn:test:server")));
            Assert.Throws<ArgumentNullException>(() => builder.UseReverseConnect(source, null!));
        }

        [Test]
        public async Task ReverseConnectManagerWaitsThroughTheSourceInterfaceAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var manager = new ReverseConnectManager(telemetry);
            IReverseConnectionSource source = manager;
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.CatchAsync<OperationCanceledException>(async () =>
                await source.WaitForConnectionAsync(
                    new Uri("opc.tcp://localhost:4840"), null, cancellation.Token).ConfigureAwait(false));
            await Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await source.WaitForConnectionAsync(null!, null, CancellationToken.None).ConfigureAwait(false));
        }

        [Test]
        [CancelAfter(30_000)]
        public async Task CancelledConnectCancelsTheSourceAndClosesALateConnectionAsync(CancellationToken ct)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var transport = new Mock<IUaSCByteTransport>();
            var closed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            transport.Setup(t => t.Close()).Callback(() => closed.TrySetResult(true));
            var connection = new Mock<ITransportWaitingConnection>();
            connection.SetupGet(c => c.Handle).Returns(transport.Object);
            connection.SetupGet(c => c.EndpointUrl).Returns(new Uri("opc.tcp://localhost:4840"));
            var source = new LateSource(connection.Object);
            using var cancellation = new CancellationTokenSource();

            Task<Client.ManagedSession> creation = Client.ManagedSession.CreateAsync(
                new ManagedSessionOptions
                {
                    Endpoint = CreateEndpoint(),
                    ReverseConnectionSource = source
                },
                CreateClientConfiguration(telemetry),
                new DefaultSessionFactory(telemetry),
                ct: cancellation.Token);

            Assert.That(await CompletesAsync(source.Started, ct).ConfigureAwait(false), Is.True);
            Assert.That(source.Request, Is.EqualTo((new Uri("opc.tcp://localhost:4840"), "urn:test:server")));
            cancellation.Cancel();

            await Assert.CatchAsync<OperationCanceledException>(async () => await creation.ConfigureAwait(false));
            Assert.That(await CompletesAsync(closed.Task, ct).ConfigureAwait(false), Is.True,
                "A connection received after cancellation must be closed.");
            Assert.That(source.CallCount, Is.EqualTo(1));
            transport.Verify(t => t.Close(), Times.Once);
        }

        [Test]
        [CancelAfter(30_000)]
        public async Task ConnectClosesASourceConnectionTheSessionFactoryDidNotBindAsync(CancellationToken ct)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var transport = new Mock<IUaSCByteTransport>();
            var closed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            transport.Setup(t => t.Close()).Callback(() => closed.TrySetResult(true));
            var connection = new Mock<ITransportWaitingConnection>();
            connection.SetupGet(c => c.Handle).Returns(transport.Object);
            connection.SetupGet(c => c.EndpointUrl).Returns(new Uri("opc.tcp://localhost:4840"));
            var source = new FirstCallSource(connection.Object);
            var factory = new UnbindableChannelSessionFactory(telemetry);
            using var cancellation = new CancellationTokenSource();

            Task<Client.ManagedSession> creation = Client.ManagedSession.CreateAsync(
                new ManagedSessionOptions
                {
                    Endpoint = CreateEndpoint(),
                    ReverseConnectionSource = source
                },
                CreateClientConfiguration(telemetry),
                factory,
                ct: cancellation.Token);

            Assert.That(await CompletesAsync(closed.Task, ct).ConfigureAwait(false), Is.True,
                "A connection the session factory failed to bind must be closed.");
            Assert.That(factory.Connections, Is.EqualTo(new[] { connection.Object }));
            cancellation.Cancel();

            await Assert.CatchAsync<OperationCanceledException>(async () => await creation.ConfigureAwait(false));
            transport.Verify(t => t.Close(), Times.Once);
        }

        private static async Task<bool> CompletesAsync(Task task, CancellationToken ct)
        {
            Task completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(10), ct))
                .ConfigureAwait(false);
            return completed == task;
        }

        /// <summary>
        /// Waits until its token is cancelled and then returns a connection anyway.
        /// </summary>
        private sealed class LateSource : IReverseConnectionSource
        {
            public LateSource(ITransportWaitingConnection connection)
            {
                m_connection = connection;
            }

            public Task Started => m_started.Task;

            public int CallCount => Volatile.Read(ref m_callCount);

            public (Uri EndpointUrl, string? ServerUri) Request { get; private set; }

            public async ValueTask<ITransportWaitingConnection> WaitForConnectionAsync(
                Uri endpointUrl,
                string? serverUri,
                CancellationToken ct)
            {
                Interlocked.Increment(ref m_callCount);
                Request = (endpointUrl, serverUri);
                m_started.TrySetResult(true);
                try
                {
                    await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                return m_connection;
            }

            private readonly ITransportWaitingConnection m_connection;
            private readonly TaskCompletionSource<bool> m_started =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private int m_callCount;
        }

        /// <summary>
        /// Returns a connection for the first call and waits for cancellation
        /// on every later call.
        /// </summary>
        private sealed class FirstCallSource : IReverseConnectionSource
        {
            public FirstCallSource(ITransportWaitingConnection connection)
            {
                m_connection = connection;
            }

            public async ValueTask<ITransportWaitingConnection> WaitForConnectionAsync(
                Uri endpointUrl,
                string? serverUri,
                CancellationToken ct)
            {
                if (Interlocked.Increment(ref m_callCount) == 1)
                {
                    return m_connection;
                }
                await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
                throw new InvalidOperationException("The wait completed without cancellation.");
            }

            private readonly ITransportWaitingConnection m_connection;
            private int m_callCount;
        }

        /// <summary>
        /// Fails before a channel binds the supplied connection, as a rejected
        /// server certificate domain does.
        /// </summary>
        private sealed class UnbindableChannelSessionFactory : DefaultSessionFactory
        {
            public UnbindableChannelSessionFactory(ITelemetryContext telemetry)
                : base(telemetry)
            {
            }

            public ITransportWaitingConnection?[] Connections
            {
                get
                {
                    lock (m_connections)
                    {
                        return [.. m_connections];
                    }
                }
            }

            public override Task<ITransportChannel> CreateChannelAsync(
                ApplicationConfiguration configuration,
                ITransportWaitingConnection? connection,
                ConfiguredEndpoint endpoint,
                bool updateBeforeConnect,
                bool checkDomain,
                CancellationToken ct = default)
            {
                lock (m_connections)
                {
                    m_connections.Add(connection);
                }
                throw new ServiceResultException(
                    StatusCodes.BadCertificateHostNameInvalid, "The server certificate domain was rejected.");
            }

            private readonly List<ITransportWaitingConnection?> m_connections = [];
        }

        private static ApplicationConfiguration CreateClientConfiguration(ITelemetryContext telemetry)
        {
            return new ApplicationConfiguration(telemetry)
            {
                ApplicationName = "ManagedSessionReverseConnectionSourceTests",
                ApplicationType = ApplicationType.Client,
                ApplicationUri = "urn:localhost:ManagedSessionReverseConnectionSourceTests",
                ProductUri = "urn:localhost:ManagedSessionReverseConnectionSourceTests",
                ClientConfiguration = new ClientConfiguration
                {
                    DefaultSessionTimeout = 60000,
                    MinSubscriptionLifetime = 10000
                },
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier()
                },
                TransportQuotas = new TransportQuotas
                {
                    OperationTimeout = 6000
                }
            };
        }

        private static ConfiguredEndpoint CreateEndpoint()
        {
            var description = new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840",
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None,
                TransportProfileUri = Profiles.UaTcpTransport,
                UserIdentityTokens =
                [
                    new UserTokenPolicy
                    {
                        PolicyId = "anonymous",
                        TokenType = UserTokenType.Anonymous,
                        SecurityPolicyUri = SecurityPolicies.None
                    }
                ]
            };
            description.Server.ApplicationUri = "urn:test:server";
            description.Server.ApplicationType = ApplicationType.Server;

            return new ConfiguredEndpoint(
                null,
                description,
                new EndpointConfiguration { OperationTimeout = 6000 })
            {
                UpdateBeforeConnect = false
            };
        }
    }
}
