// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings.Native;
using Opc.Ua.DataChannels;
using Opc.Ua.Native.QuicGo;

namespace Opc.Ua.Bindings.WebTransport.Tests
{
    public sealed partial class WebTransportNativeFlowTests
    {
        /// <summary>
        /// Shared application limits refuse extra native/H3/host capacity before any Core context is exposed.
        /// </summary>
        [Test]
        public async Task ApplicationBudgetRefusesWithoutLeakingNativeOwnership()
        {
            var budget = new DataChannelApplicationBudget(
                new DataChannelApplicationLimits { MaxBufferedBytes = 1 });
            (Exception client, _, CountingFactory clientFactory, CountingFactory serverFactory) =
                await RejectAsync(clientBudget: budget).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(client, Is.TypeOf<DataChannelException>());
                Assert.That(((DataChannelException)client).Result, Is.EqualTo(DataChannelResult.WouldBlock));
                Assert.That(clientFactory.Calls, Is.Zero);
                Assert.That(serverFactory.Calls, Is.Zero);
                DataChannelResourceAssertions.AreReleased(budget.Usage);
            });
        }

        /// <summary>
        /// Checks every production H3 layout/version against the actually loaded reviewed image.
        /// </summary>
        [Test]
        public void NativeLayoutMatchesProduction()
        {
            Assert.That(Http3Native.Layout(out Http3Layout layout, 40), Is.Zero);
            Assert.Multiple(() =>
            {
                Assert.That(Http3Native.AbiVersion(), Is.EqualTo(1));
                Assert.That(Http3Native.ApiRevision(), Is.EqualTo(2));
                Assert.That(layout.Size, Is.EqualTo(40));
                Assert.That(layout.PointerSize, Is.EqualTo(8));
                Assert.That(layout.FactsSize, Is.EqualTo(80));
                Assert.That(layout.ConfigurationSize, Is.EqualTo(224));
                Assert.That(layout.EventSize, Is.EqualTo(48));
                Assert.That(layout.ErrorSize, Is.EqualTo(48));
                Assert.That(layout.ResourceSize, Is.EqualTo(64));
                Assert.That(layout.Reserved, Is.Zero);
            });
        }

        /// <summary>
        /// Explicit host denial produces an actual forbidden CONNECT response and no Core context.
        /// </summary>
        [Test]
        public async Task ConnectPolicyDenialCreatesNoContext()
        {
            (Exception client, _, CountingFactory clientFactory, CountingFactory serverFactory) =
                await RejectAsync(authorizer: new WebTransportTestEnvironment.ConnectPolicy(false))
                    .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(client, Is.TypeOf<WebTransportException>());
                Assert.That(((WebTransportException)client).HttpStatus, Is.EqualTo(403));
                Assert.That(clientFactory.Calls, Is.Zero);
                Assert.That(serverFactory.Calls, Is.Zero);
            });
        }

        /// <summary>
        /// Exact path policy is checked by the production H3 parser, not only by application authorization.
        /// </summary>
        [Test]
        public async Task WrongPathIsRejectedBeforeContextCreation()
        {
            (_, Exception server, CountingFactory clientFactory, CountingFactory serverFactory) =
                await RejectAsync(endpoint => new UriBuilder(endpoint) { Path = "/wrong" }.Uri).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(server, Is.TypeOf<WebTransportException>());
                Assert.That(((WebTransportException)server).NativeCode, Is.EqualTo(24));
                Assert.That(clientFactory.Calls, Is.Zero);
                Assert.That(serverFactory.Calls, Is.Zero);
            });
        }

        /// <summary>
        /// CONNECT authority is independent of the local bind address and is never pooled across origins.
        /// </summary>
        [Test]
        public async Task WrongAuthorityIsRejectedAfterRealTls()
        {
            (_, Exception server, CountingFactory clientFactory, CountingFactory serverFactory) =
                await RejectAsync(
                    endpoint => new UriBuilder(endpoint) { Host = "127.0.0.1" }.Uri,
                    endpoint: new Uri("opc.wt://example.invalid:0/flow/v1"),
                    localEndpoint: new IPEndPoint(IPAddress.Loopback, 0)).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(server, Is.TypeOf<WebTransportException>());
                Assert.That(((WebTransportException)server).NativeCode, Is.EqualTo(23));
                Assert.That(clientFactory.Calls, Is.Zero);
                Assert.That(serverFactory.Calls, Is.Zero);
            });
        }

        /// <summary>
        /// Missing and different Origin values are independent rejected partitions of required-exact policy.
        /// </summary>
        [TestCase(null)]
        [TestCase("https://other.test")]
        [Repeat(20)]
        public async Task WrongOriginIsRejectedBeforeContextCreation(string? origin)
        {
            var clientOptions = new WebTransportDataChannelOptions(
                origin == null ? WebTransportOriginPolicy.AbsentOnly : WebTransportOriginPolicy.RequiredExact, origin)
            {
                Native = m_environment!.Options.Native,
                EstablishmentTimeout = TimeSpan.FromSeconds(5)
            };
            var serverOptions = new WebTransportDataChannelOptions(
                WebTransportOriginPolicy.RequiredExact, "https://allowed.test")
            {
                Native = m_environment.Options.Native,
                EstablishmentTimeout = TimeSpan.FromSeconds(5)
            };
            (_, Exception server, CountingFactory clientFactory, CountingFactory serverFactory) =
                await RejectAsync(clientOptions: clientOptions, serverOptions: serverOptions).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(server, Is.TypeOf<WebTransportException>());
                Assert.That(((WebTransportException)server).NativeCode, Is.EqualTo(25));
                Assert.That(clientFactory.Calls, Is.Zero);
                Assert.That(serverFactory.Calls, Is.Zero);
            });
        }

        /// <summary>
        /// A matching explicit Origin works with the same native certificate authentication, not a browser bypass.
        /// </summary>
        [Test]
        public async Task ExactOriginAndIndependentLocalBindEstablish()
        {
            var options = new WebTransportDataChannelOptions(
                WebTransportOriginPolicy.RequiredExact, "https://allowed.test")
            {
                Native = m_environment!.Options.Native,
                EstablishmentTimeout = TimeSpan.FromSeconds(5)
            };
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await using WebTransportDataChannelListener listener = await m_environment.Server(options).ListenAsync(
                new Uri("opc.wt://127.0.0.1:0/flow/v1"), new IPEndPoint(IPAddress.Any, 0), deadline.Token)
                .ConfigureAwait(false);
            Task<WebTransportDataChannelConnection> accepting = listener.AcceptAsync(deadline.Token).AsTask();
            await using WebTransportDataChannelConnection client = await m_environment.Client(options)
                .ConnectAsync(listener.EndpointUrl, deadline.Token).ConfigureAwait(false);
            await using WebTransportDataChannelConnection server = await accepting.ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(listener.EndpointUrl.Host, Is.EqualTo("127.0.0.1"));
                Assert.That(listener.EndpointUrl.Port, Is.GreaterThan(0));
                Assert.That(client.Context.IsReady, Is.True);
                Assert.That(server.Context.IsReady, Is.True);
                Assert.That(client.NativeConnection.Peer.MutualTlsAuthenticated, Is.True);
            });
        }

        /// <summary>
        /// Replaces the exact WT_ENABLED setting with another ID in actual submitted HTTP3 bytes.
        /// </summary>
        [Test]
        public async Task ExactWebTransportSettingIsRequired()
        {
            int changed = 0;
            m_environment!.ServerBackend.MutateSend = (id, request) =>
            {
                if ((id & 3) != 3)
                {
                    return request;
                }
                byte[] payload = request.Payload.Span.ToArray();
                int offset = payload.AsSpan().IndexOf(new byte[] { 0xac, 0x7c, 0xf0, 0x00, 0x01 });
                if (offset < 0)
                {
                    return request;
                }
                payload[offset + 3] = 1;
                Interlocked.Increment(ref changed);
                return ReplacePayload(request, payload);
            };
            (Exception client, _, CountingFactory clientFactory, CountingFactory serverFactory) =
                await RejectAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(changed, Is.GreaterThan(0));
                Assert.That(client, Is.TypeOf<WebTransportException>());
                Assert.That(((WebTransportException)client).NativeCode, Is.EqualTo(22));
                Assert.That(clientFactory.Calls, Is.Zero);
                Assert.That(serverFactory.Calls, Is.Zero);
            });
        }

        /// <summary>
        /// Actual 200 response headers selecting another application protocol cannot establish a WT session.
        /// </summary>
        [Test]
        public async Task ExactSelectedApplicationProtocolIsRequired()
        {
            int changed = 0;
            // RFC7541 encodings of the public quoted tokens "opcua-flow/1" and "opcua-flow/2".
            byte[] expected = Convert.FromHexString("fe4f592d1ad2d07f0c07f9");
            byte[] wrong = Convert.FromHexString("fe4f592d1ad2d07f0c0bf9");
            m_environment!.ServerBackend.MutateSend = (id, request) =>
            {
                byte[] payload = request.Payload.Span.ToArray();
                int offset = id == 0 ? payload.AsSpan().IndexOf(expected) : -1;
                if (offset < 0)
                {
                    return request;
                }
                wrong.CopyTo(payload, offset);
                Interlocked.Increment(ref changed);
                return ReplacePayload(request, payload);
            };
            (Exception client, _, CountingFactory clientFactory, CountingFactory serverFactory) =
                await RejectAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(changed, Is.GreaterThan(0));
                Assert.That(client, Is.TypeOf<WebTransportException>());
                Assert.That(((WebTransportException)client).NativeCode, Is.EqualTo(26));
                Assert.That(clientFactory.Calls, Is.Zero);
                Assert.That(serverFactory.Calls, Is.Zero);
            });
        }

        /// <summary>
        /// An obsolete CONNECT protocol token on real QUIC transport cannot be used as a draft16 fallback.
        /// </summary>
        [Test]
        [Repeat(20)]
        public async Task ExactDraft16ConnectTokenIsRequired()
        {
            int changed = 0;
            // RFC7541 encodings of the public tokens webtransport-h3 and webtransport-h2.
            byte[] expected = Convert.FromHexString("f058d360ea4567b12b4ecf");
            byte[] wrong = Convert.FromHexString("f058d360ea4567b12b4e2f");
            m_environment!.ClientBackend.MutateSend = (id, request) =>
            {
                byte[] payload = request.Payload.Span.ToArray();
                int offset = id == 0 ? payload.AsSpan().IndexOf(expected) : -1;
                if (offset < 0)
                {
                    return request;
                }
                wrong.CopyTo(payload, offset);
                Interlocked.Increment(ref changed);
                return ReplacePayload(request, payload);
            };
            (_, Exception server, CountingFactory clientFactory, CountingFactory serverFactory) =
                await RejectAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(changed, Is.GreaterThan(0));
                Assert.That(server, Is.TypeOf<WebTransportException>());
                Assert.That(((WebTransportException)server).NativeCode, Is.AnyOf(21u, 27u));
                Assert.That(clientFactory.Calls, Is.Zero);
                Assert.That(serverFactory.Calls, Is.Zero);
            });
        }

        /// <summary>
        /// ALPN mismatch fails the real TLS handshake before HTTP3 or Core can be created.
        /// </summary>
        [Test]
        public async Task ExactH3AlpnIsRequired()
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using NativeQuicListener listener = await NativeQuicListener.ListenAsync(
                m_environment!.ServerBackend, m_environment.ServerCredentials, "127.0.0.1:0",
                DataChannelProfiles.QuicAlpn, m_environment.Options.Native, cancellationToken: deadline.Token)
                .ConfigureAwait(false);
            var factory = new CountingFactory(m_environment.ClientFactory);
            NativeQuicTransportException? failure = await Assert.ThrowsAsync<NativeQuicTransportException>(() =>
                m_environment.Client(factory: factory)
                    .ConnectAsync(new Uri("opc.wt://" + listener.Address + "/flow/v1"), deadline.Token).AsTask())
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(failure!.Completion, Is.Not.Null);
                Assert.That(failure.Completion!.Status, Is.EqualTo(NativeQuicStatus.Authentication));
                Assert.That(factory.Calls, Is.Zero);
                Assert.That(m_environment.ClientBackend.Writes, Is.Empty);
            });
        }

        /// <summary>
        /// A truncated prefix on an unassociated nonreserved bidi is a scoped stream rejection, not context loss.
        /// </summary>
        [Test]
        public async Task TruncatedUnassociatedPrefixPreservesOtherFlows()
        {
            await using WebTransportPair pair = await m_environment!.ConnectPairAsync().ConfigureAwait(false);
            (DataChannel healthy, DataChannel sink) = await pair.OpenAsync().ConfigureAwait(false);
            await using NativeQuicStream malformed = await pair.Client.NativeConnection.OpenStreamAsync()
                .ConfigureAwait(false);
            NativeQuicCompletion? sent = await malformed.SendAsync(new ByteString("@"u8.ToArray()), 1)
                .ConfigureAwait(false);
            Assert.That(sent!.NativeStatus, Is.EqualTo(NativeQuicStatus.Success));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            NativeQuicCompletion rejection = await malformed.ReadAsync(deadline.Token).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(rejection.Remote, Is.True);
                Assert.That(rejection.ReadOutcome, Is.EqualTo(2));
                Assert.That(rejection.ErrorCode, Is.EqualTo(0x106));
                Assert.That(pair.Server.Context.IsReady, Is.True);
                Assert.That(pair.Client.Context.IsReady, Is.True);
            });
            Assert.That(await healthy.SendAsync(new ByteString(new byte[] { 29 })).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? received = await sink.ReadAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(received!.Payload.Span.ToArray(), Is.EqualTo(new byte[] { 29 }));
        }

        /// <summary>
        /// A complete foreign-session association remains a connection fault; no payload is delivered to Core.
        /// </summary>
        [Test]
        public async Task ForeignSessionPrefixNeverCreatesFlow()
        {
            await using WebTransportPair pair = await m_environment!.ConnectPairAsync().ConfigureAwait(false);
            int changed = 0;
            m_environment.ClientBackend.MutateSend = (id, request) =>
            {
                if (id == pair.Client.ManagementStreamId || (request.Flags & 2) == 0)
                {
                    return request;
                }
                byte[] payload = request.Payload.Span.ToArray();
                if (!payload.AsSpan().SequenceEqual(new byte[] { 0x40, 0x41, 0 }))
                {
                    throw new InvalidOperationException("The actual WT association prefix changed.");
                }
                payload[2] = 4;
                Interlocked.Increment(ref changed);
                return ReplacePayload(request, payload);
            };
            try
            {
                await pair.Client.OpenStreamAsync().ConfigureAwait(false);
            }
            catch (Exception) when (pair.Client.Completion.IsCompleted || pair.Client.NativeConnection.IsClosed)
            {
                // The real peer can reject before the prefix owner's continuation resumes.
            }
            await pair.Server.Completion.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(changed, Is.GreaterThan(0));
                Assert.That(pair.Server.Failure, Is.TypeOf<WebTransportException>());
                Assert.That(((WebTransportException)pair.Server.Failure!).Scope, Is.EqualTo(4));
                Assert.That(pair.Server.Context.Channels, Is.Empty);
                Assert.That(pair.Client.Context.Channels, Is.Empty);
            });
        }

        /// <summary>
        /// Runs a real negative handshake with independently counted client/server Core factory calls.
        /// </summary>
        private async Task<(Exception Client, Exception Server, CountingFactory ClientFactory,
            CountingFactory ServerFactory)> RejectAsync(
            Func<Uri, Uri>? clientEndpoint = null,
            WebTransportDataChannelOptions? clientOptions = null,
            WebTransportDataChannelOptions? serverOptions = null,
            IWebTransportConnectAuthorizer? authorizer = null,
            Uri? endpoint = null,
            IPEndPoint? localEndpoint = null,
            DataChannelApplicationBudget? clientBudget = null)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            var clientFactory = new CountingFactory(m_environment!.ClientFactory);
            var serverFactory = new CountingFactory(m_environment.ServerFactory);
            WebTransportDataChannelTransport server = m_environment.Server(
                serverOptions, serverFactory, authorizer);
            endpoint ??= new Uri("opc.wt://127.0.0.1:0/flow/v1");
            await using WebTransportDataChannelListener listener = localEndpoint == null
                ? await server.ListenAsync(endpoint, deadline.Token).ConfigureAwait(false)
                : await server.ListenAsync(endpoint, localEndpoint, deadline.Token).ConfigureAwait(false);
            Task<Exception> rejectedServer = CaptureRejectionAsync(listener.AcceptAsync(deadline.Token).AsTask());
            WebTransportDataChannelTransport client = clientBudget == null
                ? m_environment.Client(clientOptions, clientFactory)
                : new WebTransportDataChannelTransport(
                    m_environment.ClientBackend, m_environment.Http3Library, m_environment.ClientCredentials,
                    clientFactory, clientBudget, clientOptions ?? m_environment.Options);
            Task<Exception> rejectedClient = CaptureRejectionAsync(client
                .ConnectAsync(clientEndpoint?.Invoke(listener.EndpointUrl) ?? listener.EndpointUrl, deadline.Token)
                .AsTask());
            await Task.WhenAll(rejectedClient, rejectedServer).WaitAsync(TimeSpan.FromSeconds(15))
                .ConfigureAwait(false);
            return (await rejectedClient.ConfigureAwait(false), await rejectedServer.ConfigureAwait(false),
                clientFactory, serverFactory);
        }

        /// <summary>
        /// Captures rejection without letting an unexpectedly successful connection satisfy the negative test.
        /// </summary>
        /// <exception cref="AssertionException"></exception>
        private static async Task<Exception> CaptureRejectionAsync(Task<WebTransportDataChannelConnection> operation)
        {
            WebTransportDataChannelConnection connection;
            try
            {
                connection = await operation.ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                return exception;
            }
            await connection.DisposeAsync().ConfigureAwait(false);
            throw new AssertionException("The invalid peer unexpectedly established a usable Core context.");
        }

        /// <summary>
        /// Changes only test-specified real outbound bytes, retaining exact native timeout/flags/cancellation control.
        /// </summary>
        private static NativeQuicRequest ReplacePayload(NativeQuicRequest request, byte[] payload)
        {
            return new NativeQuicRequest(request.Timeout, new ByteString(payload), request.Flags, request.Capacity,
                request.ErrorCode, request.CertificateIndex, request.CancellationErrorCode)
            {
                Cancellation = request.Cancellation
            };
        }

        /// <summary>
        /// Counts actual facade creation without substituting a mock carrier or a mock common runtime.
        /// </summary>
        private sealed class CountingFactory(IDataChannelContextFactory inner) : IDataChannelContextFactory
        {
            /// <summary>
            /// Gets the actual number of contexts exposed to Core.
            /// </summary>
            internal int Calls => Volatile.Read(ref m_calls);

            /// <inheritdoc/>
            public DataChannelContext Create(IDataChannelCarrier carrier)
            {
                Interlocked.Increment(ref m_calls);
                return inner.Create(carrier);
            }

            private int m_calls;
        }
    }
}
