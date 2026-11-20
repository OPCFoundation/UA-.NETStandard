// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Pcap.Replay;

namespace Opc.Ua.Pcap.Tests.Replay
{
    /// <summary>
    /// Unit tests for the unified <see cref="ReplaySession"/> wrapper that
    /// hosts either a <see cref="MockServerReplay"/> or
    /// <see cref="MockClientReplay"/>. These tests do not spin up real
    /// network listeners — they only exercise the wrapper's constructors,
    /// projection properties, and state machine.
    /// </summary>
    [TestFixture]
    public sealed class ReplaySessionTests
    {
        [Test]
        public void MockServerConstructorRejectsNullId()
        {
            var source = new InMemoryCaptureSource();
            using (source)
            {
                var mockServer = new MockServerReplay(source);
                using var disposeGuard = new MockServerReplayDisposeGuard(mockServer);

                Assert.That(
                    () => new ReplaySession(id: null!, mockServer, listenScheme: "opc.tcp", listenPort: null),
                    Throws.InstanceOf<ArgumentException>()
                        .With.Property("ParamName").EqualTo("id"));
            }
        }

        [Test]
        public void MockServerConstructorRejectsNullServer()
        {
            Assert.That(
                () => new ReplaySession("id", mockServer: null!, listenScheme: "opc.tcp", listenPort: null),
                Throws.TypeOf<ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("mockServer"));
        }

        [Test]
        public void MockServerConstructorRejectsEmptyListenScheme()
        {
            var source = new InMemoryCaptureSource();
            using (source)
            {
                var mockServer = new MockServerReplay(source);
                using var disposeGuard = new MockServerReplayDisposeGuard(mockServer);

                Assert.That(
                    () => new ReplaySession("id", mockServer, listenScheme: string.Empty, listenPort: null),
                    Throws.InstanceOf<ArgumentException>()
                        .With.Property("ParamName").EqualTo("listenScheme"));
            }
        }

        [Test]
        public void MockClientConstructorRejectsNullClient()
        {
            Assert.That(
                () => new ReplaySession("id", mockClient: null!, targetEndpointUrl: "opc.tcp://x"),
                Throws.TypeOf<ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("mockClient"));
        }

        [Test]
        public void MockClientConstructorRejectsEmptyTargetEndpoint()
        {
            var source = new InMemoryCaptureSource();
            using (source)
            {
                var mockClient = new MockClientReplay(source, "opc.tcp://target:1");
                using var disposeGuard = new MockClientReplayDisposeGuard(mockClient);

                Assert.That(
                    () => new ReplaySession("id", mockClient, targetEndpointUrl: string.Empty),
                    Throws.InstanceOf<ArgumentException>()
                        .With.Property("ParamName").EqualTo("targetEndpointUrl"));
            }
        }

        [Test]
        public async Task MockServerSessionInitialPropertiesAreSet()
        {
            var source = new InMemoryCaptureSource();
            await using (source.ConfigureAwait(false))
            {
                var mockServer = new MockServerReplay(source);
                await using (mockServer.ConfigureAwait(false))
                {
                    DateTimeOffset before = DateTimeOffset.UtcNow.AddSeconds(-1);
                    var session = new ReplaySession(
                        "session-1",
                        mockServer,
                        listenScheme: "opc.tcp",
                        listenPort: 4840);

                    await using (session.ConfigureAwait(false))
                    {
                        DateTimeOffset after = DateTimeOffset.UtcNow.AddSeconds(1);

                        Assert.That(session.Id, Is.EqualTo("session-1"));
                        Assert.That(session.Mode, Is.EqualTo(ReplayMode.MockServer));
                        Assert.That(session.IsRunning, Is.False);
                        Assert.That(session.TargetEndpointUrl, Is.Null);
                        Assert.That(session.Result, Is.Null);
                        Assert.That(session.StartedAt, Is.GreaterThanOrEqualTo(before));
                        Assert.That(session.StartedAt, Is.LessThanOrEqualTo(after));
                        Assert.That(session.ListenUri, Is.Null,
                            "ListenUri stays null until StartAsync binds a port.");
                    }
                }
            }
        }

        [Test]
        public async Task MockClientSessionInitialPropertiesAreSet()
        {
            var source = new InMemoryCaptureSource();
            await using (source.ConfigureAwait(false))
            {
                var mockClient = new MockClientReplay(source, "opc.tcp://target:1");
                await using (mockClient.ConfigureAwait(false))
                {
                    var session = new ReplaySession(
                        "client-session",
                        mockClient,
                        targetEndpointUrl: "opc.tcp://target:1");

                    await using (session.ConfigureAwait(false))
                    {
                        Assert.That(session.Id, Is.EqualTo("client-session"));
                        Assert.That(session.Mode, Is.EqualTo(ReplayMode.MockClient));
                        Assert.That(session.TargetEndpointUrl, Is.EqualTo("opc.tcp://target:1"));
                        Assert.That(session.IsRunning, Is.False);
                        Assert.That(session.Result, Is.Null);
                        Assert.That(session.ListenUri, Is.Null,
                            "ListenUri is only populated for MockServer mode.");
                    }
                }
            }
        }

        [Test]
        public async Task StartAsyncHonoursCancellation()
        {
            var source = new InMemoryCaptureSource();
            await using (source.ConfigureAwait(false))
            {
                var mockClient = new MockClientReplay(source, "opc.tcp://target:1");
                await using (mockClient.ConfigureAwait(false))
                {
                    var session = new ReplaySession("s", mockClient, "opc.tcp://target:1");
                    await using (session.ConfigureAwait(false))
                    {
                        using var cts = new CancellationTokenSource();
                        cts.Cancel();

                        Assert.That(
                            async () => await session.StartAsync(cts.Token).ConfigureAwait(false),
                            Throws.InstanceOf<OperationCanceledException>());

                        Assert.That(session.IsRunning, Is.False,
                            "Cancelling at the start gate must leave the running flag clear.");
                    }
                }
            }
        }

        [Test]
        public async Task StopAsyncIsSafeBeforeStart()
        {
            var source = new InMemoryCaptureSource();
            await using (source.ConfigureAwait(false))
            {
                var mockServer = new MockServerReplay(source);
                await using (mockServer.ConfigureAwait(false))
                {
                    var session = new ReplaySession(
                        "stop-before-start",
                        mockServer,
                        listenScheme: "opc.tcp",
                        listenPort: null);

                    await using (session.ConfigureAwait(false))
                    {
                        Assert.That(
                            async () => await session.StopAsync(CancellationToken.None).ConfigureAwait(false),
                            Throws.Nothing);
                        Assert.That(session.IsRunning, Is.False);
                    }
                }
            }
        }

        [Test]
        public async Task StopAsyncHonoursCancellation()
        {
            var source = new InMemoryCaptureSource();
            await using (source.ConfigureAwait(false))
            {
                var mockServer = new MockServerReplay(source);
                await using (mockServer.ConfigureAwait(false))
                {
                    var session = new ReplaySession(
                        "stop-cancel",
                        mockServer,
                        listenScheme: "opc.tcp",
                        listenPort: null);

                    await using (session.ConfigureAwait(false))
                    {
                        using var cts = new CancellationTokenSource();
                        cts.Cancel();

                        Assert.That(
                            async () => await session.StopAsync(cts.Token).ConfigureAwait(false),
                            Throws.InstanceOf<OperationCanceledException>());
                    }
                }
            }
        }

        [Test]
        public async Task DisposeAsyncIsIdempotent()
        {
            var source = new InMemoryCaptureSource();
            await using (source.ConfigureAwait(false))
            {
                var mockClient = new MockClientReplay(source, "opc.tcp://target:1");
                await using (mockClient.ConfigureAwait(false))
                {
                    var session = new ReplaySession("s", mockClient, "opc.tcp://target:1");

                    Assert.That(async () => await session.DisposeAsync().ConfigureAwait(false), Throws.Nothing);
                    Assert.That(async () => await session.DisposeAsync().ConfigureAwait(false), Throws.Nothing);
                }
            }
        }

        // ---- helpers -----

        private sealed class MockServerReplayDisposeGuard : IDisposable
        {
            private readonly MockServerReplay m_server;

            public MockServerReplayDisposeGuard(MockServerReplay server)
            {
                m_server = server;
            }

            public void Dispose()
            {
                m_server.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }

        private sealed class MockClientReplayDisposeGuard : IDisposable
        {
            private readonly MockClientReplay m_client;

            public MockClientReplayDisposeGuard(MockClientReplay client)
            {
                m_client = client;
            }

            public void Dispose()
            {
                m_client.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
    }
}
