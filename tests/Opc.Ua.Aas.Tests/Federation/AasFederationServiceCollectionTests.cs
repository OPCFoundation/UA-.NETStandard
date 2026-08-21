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
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Aas.Server;
using Opc.Ua.Aas.Server.Federation;
using Opc.Ua.Aas.Server.Packaging;

namespace Opc.Ua.Aas.Tests.Federation
{
    /// <summary>
    /// Exercises the federation and package registrations, and the HTTP transport that is only
    /// reachable through them because it is an internal implementation detail of the package.
    /// </summary>
    [TestFixture]
    [Category("Aas")]
    public sealed class AasFederationServiceCollectionTests
    {
        /// <summary>
        /// The whole ResourceUrl path depends on the four collaborators being resolvable; a missing
        /// one would only surface when a Client actually dereferences a ResourceUrl.
        /// </summary>
        [Test]
        public async Task AddAasFederationRegistersTheResolutionChainAsync()
        {
            using ServiceProvider provider = new ServiceCollection().AddAasFederation()
                .BuildServiceProvider();

            var policy = provider.GetRequiredService<AasFederationEgressPolicy>();
            var dnsResolver = provider.GetRequiredService<IAasFederationDnsResolver>();
            var transport = provider.GetRequiredService<IAasFederationHttpTransport>();
            var resolver = provider.GetRequiredService<AasResourceUrlFederationResolver>();
            ArrayOf<IPAddress> resolved = await dnsResolver
                .ResolveAsync("127.0.0.1", CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(resolved.ToArray(), Is.EqualTo(new[] { IPAddress.Parse("127.0.0.1") }),
                    "The default resolver has to short-circuit an address literal without a lookup.");
                Assert.That(transport, Is.Not.Null);
                Assert.That(resolver.Policy, Is.SameAs(policy),
                    "The resolver has to enforce the container's policy, not a private default.");
                Assert.That(provider.GetRequiredService<AasResourceUrlFederationResolver>(),
                    Is.SameAs(resolver),
                    "A per-call resolver would drop the transport's connection reuse.");
            });
        }

        /// <summary>
        /// A deployment that has already chosen its own egress policy or transport must keep it;
        /// silently replacing the policy would relax the very limits it was installed to enforce.
        /// </summary>
        [Test]
        public void AddAasFederationKeepsAlreadyRegisteredServices()
        {
            var policy = new AasFederationEgressPolicy { MaxRedirects = 0 };
            var transport = new RecordingTransport();
            var services = new ServiceCollection();
            services.AddSingleton(policy);
            services.AddSingleton<IAasFederationHttpTransport>(transport);

            using ServiceProvider provider = services.AddAasFederation().BuildServiceProvider();

            Assert.Multiple(() =>
            {
                Assert.That(provider.GetRequiredService<AasFederationEgressPolicy>(), Is.SameAs(policy));
                Assert.That(provider.GetRequiredService<IAasFederationHttpTransport>(), Is.SameAs(transport));
                Assert.That(provider.GetRequiredService<AasResourceUrlFederationResolver>().Policy.MaxRedirects,
                    Is.Zero);
            });
        }

        /// <summary>
        /// Extension methods are the public surface of the package, so a missing collection has to
        /// be named rather than surfacing as a NullReferenceException.
        /// </summary>
        [Test]
        public void AddAasFederationRejectsAMissingServiceCollection()
        {
            Assert.That(
                () => AasFederationServiceCollectionExtensions.AddAasFederation(null!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("services"));
        }

        /// <summary>
        /// The registry claims AAS-Packages only when a package store is present, so the default
        /// registration has to actually produce a store that publishes and reads back a version.
        /// </summary>
        [Test]
        public async Task AddAasPackageStoreRegistersAUsableInMemoryStoreAsync()
        {
            using ServiceProvider provider = new ServiceCollection().AddAasPackageStore()
                .BuildServiceProvider();

            var store = provider.GetRequiredService<IAasPackageStore>();
            ByteString content = ByteString.From(Encoding.UTF8.GetBytes("package"));
            AasPackageVersion published = await store.PublishAsync(new AasPackagePublishRequest(
                "urn:package",
                "v1",
                content,
                AasPackageIntegrity.ComputeDigest(content, AasPackageIntegrity.Sha256),
                AasPackageIntegrity.Sha256)).ConfigureAwait(false);
            ByteString roundTripped = await provider.GetRequiredService<IAasPackageStore>()
                .ReadAsync("urn:package", "v1").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(store, Is.InstanceOf<InMemoryAasPackageStore>());
                Assert.That(published.DigestAlg, Is.EqualTo(AasPackageIntegrity.Sha256));
                Assert.That(roundTripped.ToArray(), Is.EqualTo(content.ToArray()),
                    "A per-call store would lose every package that was published into it.");
            });
        }

        /// <summary>
        /// A deployment that installed a persistent store must keep it; replacing it with the
        /// in-memory one would silently discard every stored package on restart.
        /// </summary>
        [Test]
        public void AddAasPackageStoreKeepsAnAlreadyRegisteredStore()
        {
            var store = new InMemoryAasPackageStore();
            var services = new ServiceCollection();
            services.AddSingleton<IAasPackageStore>(store);

            using ServiceProvider provider = services.AddAasPackageStore().BuildServiceProvider();

            Assert.That(provider.GetRequiredService<IAasPackageStore>(), Is.SameAs(store));
        }

        /// <summary>
        /// The package store extension shares the public surface contract of the federation one.
        /// </summary>
        [Test]
        public void AddAasPackageStoreRejectsAMissingServiceCollection()
        {
            Assert.That(
                () => AasPackageServiceCollectionExtensions.AddAasPackageStore(null!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("services"));
        }

        /// <summary>
        /// The registered transport is the HttpClient one; it has to surface the status, the
        /// redirect location, the content length and the connected address the egress policy then
        /// re-validates, and it must defer the body to the content reader so a rejected response is
        /// never read.
        /// </summary>
        [Test]
        public async Task RegisteredTransportProjectsTheHttpResponseAndDefersTheBodyAsync()
        {
            byte[] body = Encoding.UTF8.GetBytes("{\"submodel\":true}");
            var handler = new StubHttpMessageHandler(_ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.Found)
                {
                    Content = new ByteArrayContent(body)
                };
                response.Headers.Location = new Uri("https://127.0.0.2/moved");
                return response;
            });
            using ServiceProvider provider = CreateTransportProvider(handler);
            var transport = provider.GetRequiredService<IAasFederationHttpTransport>();

            AasFederationHttpResponse response = await transport.SendAsync(
                new AasFederationHttpRequest(new Uri("https://127.0.0.1/submodel"), null),
                CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo((int)HttpStatusCode.Found));

                // A handler that never opens a socket cannot report a peer, so
                // the transport reports None rather than re-resolving the name.
                // Re-resolving would prove nothing - a name that resolves to a
                // permitted address twice can still have carried a different
                // one into the socket. None means "not observable", and the
                // resolver reads it that way: it falls back to the pre-connect
                // validation of every resolved address rather than refusing,
                // because refusing would disable federation outright on the
                // frameworks that have no connect callback.
                Assert.That(response.ConnectedAddress, Is.EqualTo(IPAddress.None));
                Assert.That(response.RedirectLocation, Is.EqualTo(new Uri("https://127.0.0.2/moved")));
                Assert.That(response.ContentLength, Is.EqualTo(body.Length));
                Assert.That(handler.Requests[0].Method, Is.EqualTo(HttpMethod.Get));
            });
        }

#if NET6_0_OR_GREATER
        /// <summary>
        /// The peer is observed by a connect callback, which the runtime raises
        /// only when a new connection is established. A second request to the
        /// same origin is served from the pool and raises nothing, so a
        /// transport that reported the peer per request would report "unknown"
        /// for it and fail closed - which would make the second and every
        /// later federation fetch fail with a security-flavoured error. Two
        /// sequential fetches through the real transport is the only thing that
        /// shows the difference; every other federation test substitutes a fake
        /// that hands back a truthful address.
        /// </summary>
        [Test]
        public async Task RealTransportKeepsReportingThePeerAcrossAPooledConnectionAsync()
        {
            if (!AasFederationHttpResponse.IsConnectedAddressObservable)
            {
                Assert.Ignore(
                    "The transport can only observe the connected peer address via " +
                    "SocketsHttpHandler.ConnectCallback (.NET 6+); the netstandard2.1 " +
                    "build reports IPAddress.None instead. See #4282.");
            }

            using var listener = new LoopbackHttpListener();
            var policy = new AasFederationEgressPolicy();
            policy.TrustedRestrictedHosts.Add("127.0.0.1");
            using ServiceProvider provider = CreateRealTransportProvider(policy);
            var transport = provider.GetRequiredService<IAasFederationHttpTransport>();

            AasFederationHttpResponse first = await transport.SendAsync(
                new AasFederationHttpRequest(listener.Uri, null), CancellationToken.None)
                .ConfigureAwait(false);
            ByteString firstBody = await first.ContentReader
                .ReadAsync(1024, CancellationToken.None).ConfigureAwait(false);
            AasFederationHttpResponse second = await transport.SendAsync(
                new AasFederationHttpRequest(listener.Uri, null), CancellationToken.None)
                .ConfigureAwait(false);
            ByteString secondBody = await second.ContentReader
                .ReadAsync(1024, CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(first.StatusCode, Is.EqualTo(200));
                Assert.That(first.ConnectedAddress, Is.EqualTo(IPAddress.Loopback));
                Assert.That(Encoding.UTF8.GetString(firstBody.ToArray()), Is.EqualTo("ok"));
                Assert.That(second.StatusCode, Is.EqualTo(200));
                Assert.That(second.ConnectedAddress, Is.EqualTo(IPAddress.Loopback),
                    "A pooled connection raises no connect callback, so the per-origin record " +
                    "has to answer instead of the request reporting an unknown peer.");
                Assert.That(Encoding.UTF8.GetString(secondBody.ToArray()), Is.EqualTo("ok"));
            });
        }

        /// <summary>
        /// The peer is validated inside the connect callback rather than after
        /// the response, so a restricted peer never becomes a connection at
        /// all. Checking afterwards would leave the rejected connection in the
        /// pool for the next request to pick up and reuse.
        /// </summary>
        [Test]
        public async Task RealTransportRefusesToConnectToARestrictedPeerAsync()
        {
            if (!AasFederationHttpResponse.IsConnectedAddressObservable)
            {
                Assert.Ignore(
                    "Peer validation happens inside SocketsHttpHandler.ConnectCallback " +
                    "(.NET 6+); the netstandard2.1 build cannot refuse the connection " +
                    "before the request is written. See #4282.");
            }

            using var listener = new LoopbackHttpListener();
            using ServiceProvider provider = CreateRealTransportProvider(new AasFederationEgressPolicy());
            var transport = provider.GetRequiredService<IAasFederationHttpTransport>();

            Exception? caught = null;
            try
            {
                await transport.SendAsync(
                    new AasFederationHttpRequest(listener.Uri, null), CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                caught = ex;
            }

            Assert.That(caught, Is.Not.Null);
            Assert.That(listener.AcceptedRequests, Is.Zero,
                "The connection has to be refused before any request is written to it.");
        }

        /// <summary>
        /// A one-connection HTTP/1.1 listener on loopback, so the real transport
        /// can be driven without reaching the network.
        /// </summary>
        private static ServiceProvider CreateRealTransportProvider(AasFederationEgressPolicy policy)
        {
            var services = new ServiceCollection();
            services.AddSingleton(policy);
            return services.AddAasFederation().BuildServiceProvider();
        }

        private sealed class LoopbackHttpListener : IDisposable
        {
            public LoopbackHttpListener()
            {
                m_listener = new TcpListener(IPAddress.Loopback, 0);
                m_listener.Start();
                Uri = new Uri($"http://127.0.0.1:{((IPEndPoint)m_listener.LocalEndpoint).Port}/doc");
                m_serving = ServeAsync(m_cts.Token);
            }

            public Uri Uri { get; }

            public int AcceptedRequests => Volatile.Read(ref m_acceptedRequests);

            public void Dispose()
            {
                m_cts.Cancel();
                m_listener.Stop();
                try
                {
                    m_serving.GetAwaiter().GetResult();
                }
                catch (Exception)
                {
                    // The serve loop is torn down by cancelling the listener,
                    // which surfaces as whichever socket error the platform
                    // chooses; none of them mean anything to the test.
                }
                m_listener.Dispose();
                m_cts.Dispose();
            }

            private async Task ServeAsync(CancellationToken cancellationToken)
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    TcpClient client = await m_listener
                        .AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                    _ = RespondAsync(client, cancellationToken);
                }
            }

            private async Task RespondAsync(TcpClient client, CancellationToken cancellationToken)
            {
                using (client)
                {
                    NetworkStream stream = client.GetStream();
                    var buffer = new byte[4096];
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        int read = await stream
                            .ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                        if (read <= 0)
                        {
                            return;
                        }

                        Interlocked.Increment(ref m_acceptedRequests);
                        byte[] response = Encoding.ASCII.GetBytes(
                            "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nContent-Type: text/plain\r\n\r\nok");
                        await stream
                            .WriteAsync(response.AsMemory(), cancellationToken).ConfigureAwait(false);
                        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            private int m_acceptedRequests;
            private readonly TcpListener m_listener;
            private readonly CancellationTokenSource m_cts = new();
            private readonly Task m_serving;
        }
#endif

        /// <summary>
        /// The body is only readable through the deferred reader, which is what lets the resolver
        /// enforce its size and address bounds before a single byte is materialized.
        /// </summary>
        [Test]
        public async Task DeferredContentReaderReturnsTheExactResponseBytesAsync()
        {
            byte[] body = Encoding.UTF8.GetBytes("federated-document");
            var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(body)
            });
            using ServiceProvider provider = CreateTransportProvider(handler);
            var transport = provider.GetRequiredService<IAasFederationHttpTransport>();

            AasFederationHttpResponse response = await transport.SendAsync(
                new AasFederationHttpRequest(new Uri("https://127.0.0.1/doc"), null),
                CancellationToken.None).ConfigureAwait(false);
            ByteString content = await response.ContentReader
                .ReadAsync(body.Length, CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(response.RedirectLocation, Is.Null);
                Assert.That(content.ToArray(), Is.EqualTo(body));
            });
        }

        /// <summary>
        /// A peer credential must only travel when the caller supplied one; an ambient or
        /// invented Authorization header would leak the Server's own identity to the peer.
        /// </summary>
        [Test]
        public async Task AuthorizationHeaderTravelsOnlyWhenTheCallerSuppliedOneAsync()
        {
            var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(s_emptyBody)
            });
            using ServiceProvider provider = CreateTransportProvider(handler);
            var transport = provider.GetRequiredService<IAasFederationHttpTransport>();

            await transport.SendAsync(
                new AasFederationHttpRequest(new Uri("https://127.0.0.1/a"), "Bearer peer-token"),
                CancellationToken.None).ConfigureAwait(false);
            await transport.SendAsync(
                new AasFederationHttpRequest(new Uri("https://127.0.0.1/b"), null),
                CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(handler.Requests[0].Headers.TryGetValues("Authorization", out
                    IEnumerable<string>? sent), Is.True);
                Assert.That(sent, Is.EqualTo(s_expectedAuthorization));
                Assert.That(handler.Requests[1].Headers.Contains("Authorization"), Is.False);
            });
        }

        private static readonly byte[] s_emptyBody = [];

        private static readonly string[] s_expectedAuthorization = ["Bearer peer-token"];

        private static ServiceProvider CreateTransportProvider(HttpMessageHandler handler)
        {
            var services = new ServiceCollection();
            services.AddSingleton(new HttpClient(handler));
            return services.AddAasFederation().BuildServiceProvider();
        }

        /// <summary>
        /// Answers every request from a canned factory so no socket is ever opened.
        /// </summary>
        private sealed class StubHttpMessageHandler : HttpMessageHandler
        {
            public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            {
                m_responder = responder;
            }

            public List<HttpRequestMessage> Requests { get; } = [];

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                Requests.Add(request);
                return Task.FromResult(m_responder(request));
            }

            private readonly Func<HttpRequestMessage, HttpResponseMessage> m_responder;
        }

        /// <summary>
        /// A transport that records requests so a pre-registered instance can be recognized.
        /// </summary>
        private sealed class RecordingTransport : IAasFederationHttpTransport
        {
            public ValueTask<AasFederationHttpResponse> SendAsync(
                AasFederationHttpRequest request,
                CancellationToken cancellationToken)
            {
                throw new NotSupportedException("The recording transport is never sent through.");
            }
        }
    }
}
