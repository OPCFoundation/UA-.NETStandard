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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Aas.Server.Federation;

namespace Opc.Ua.Aas.Tests.Federation
{
    /// <summary>
    /// Tests AAS federation identity and fail-closed egress rules.
    /// </summary>
    [TestFixture]
    [Category("Aas")]
    public sealed class AasFederationTests
    {
        [Test]
        public void ProxyIdentityRetainsRemoteAttributesAndIgnoresLocalEndpoint()
        {
            ArrayOf<AasFederatedIdentifierAttribute> attributes = Attributes("shell", "urn:remote:shell");

            AasFederatedEntityIdentity first = AasFederationIdentity.CreateProxyIdentity(
                attributes, new Uri("opc.tcp://registry-a.example:4840"));
            AasFederatedEntityIdentity second = AasFederationIdentity.CreateProxyIdentity(
                attributes, new Uri("opc.tcp://registry-b.example:4840"));

            Assert.Multiple(() =>
            {
                Assert.That(first.IdentifierAttributes[0].Value, Is.EqualTo("urn:remote:shell"));
                Assert.That(first.DerivedIdentifier, Is.EqualTo(second.DerivedIdentifier));
                Assert.That(first.DerivedIdentifier, Does.Not.Contain("registry-a"));
                Assert.That(first.DerivedIdentifier, Does.Not.Contain("registry-b"));
            });
        }

        [TestCase("127.0.0.1")]
        [TestCase("169.254.1.1")]
        [TestCase("10.0.0.1")]
        [TestCase("fc00::1")]
        [TestCase("0.0.0.0")]
        [TestCase("224.0.0.1")]
        [TestCase("203.0.113.1")]
        [TestCase("169.254.169.254")]
        public async Task EgressPolicyRejectsRestrictedAddressAndReturnsNoBytes(string address)
        {
            Uri uri = new Uri("https://peer.example/aas");
            var dns = new FakeDnsResolver(uri.Host, IPAddress.Parse(address));
            var reader = new FakeContentReader(Bytes("secret"));
            var transport = new FakeHttpTransport(new AasFederationHttpResponse(
                200, IPAddress.Parse(address), null, 6, reader));
            var resolver = new AasResourceUrlFederationResolver(
                new AasFederationEgressPolicy(), dns, transport);

            AasFederationResolutionResult result = await resolver.ResolveAsync(uri);

            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.False);
                Assert.That(result.Content.IsEmpty, Is.True);
                Assert.That(reader.Reads, Is.Zero);
            });
        }

        [Test]
        public async Task RedirectToRejectedAddressIsBlockedAfterInitialTargetPassed()
        {
            Uri initial = new Uri("https://peer.example/aas");
            var dns = new FakeDnsResolver();
            dns.Add("peer.example", IPAddress.Parse("93.184.216.34"));
            dns.Add("127.0.0.1", IPAddress.Parse("127.0.0.1"));
            var rejectedReader = new FakeContentReader(Bytes("secret"));
            var transport = new QueueHttpTransport(
                new AasFederationHttpResponse(
                    302,
                    IPAddress.Parse("93.184.216.34"),
                    new Uri("https://127.0.0.1/aas"),
                    null,
                    new FakeContentReader(ByteString.Empty)),
                new AasFederationHttpResponse(
                    200,
                    IPAddress.Parse("127.0.0.1"),
                    null,
                    6,
                    rejectedReader));
            var resolver = new AasResourceUrlFederationResolver(
                new AasFederationEgressPolicy(), dns, transport);

            AasFederationResolutionResult result = await resolver.ResolveAsync(initial);

            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.False);
                Assert.That(result.Content.IsEmpty, Is.True);
                Assert.That(rejectedReader.Reads, Is.Zero);
            });
        }

        [Test]
        public async Task ConnectedAddressRevalidationBlocksDnsRebinding()
        {
            Uri uri = new Uri("https://peer.example/aas");
            var dns = new FakeDnsResolver(uri.Host, IPAddress.Parse("93.184.216.34"));
            var reader = new FakeContentReader(Bytes("secret"));
            var transport = new FakeHttpTransport(new AasFederationHttpResponse(
                200, IPAddress.Parse("10.0.0.1"), null, 6, reader));
            var resolver = new AasResourceUrlFederationResolver(
                new AasFederationEgressPolicy(), dns, transport);

            AasFederationResolutionResult result = await resolver.ResolveAsync(uri);

            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.False);
                Assert.That(result.Content.IsEmpty, Is.True);
                Assert.That(reader.Reads, Is.Zero);
            });
        }

        [Test]
        public async Task RedirectLimitIsEnforcedWithoutReturningBytes()
        {
            Uri uri = new Uri("https://peer.example/aas");
            var policy = new AasFederationEgressPolicy { MaxRedirects = 0 };
            var dns = new FakeDnsResolver(uri.Host, IPAddress.Parse("93.184.216.34"));
            var reader = new FakeContentReader(Bytes("secret"));
            var transport = new FakeHttpTransport(new AasFederationHttpResponse(
                302, IPAddress.Parse("93.184.216.34"), new Uri("https://other.example/aas"), null, reader));
            var resolver = new AasResourceUrlFederationResolver(policy, dns, transport);

            AasFederationResolutionResult result = await resolver.ResolveAsync(uri);

            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.False);
                Assert.That(result.Content.IsEmpty, Is.True);
                Assert.That(reader.Reads, Is.Zero);
            });
        }

        [Test]
        public void TimeBoundTerminatesWithoutReturningBytes()
        {
            Uri uri = new Uri("https://peer.example/aas");
            var policy = new AasFederationEgressPolicy { Timeout = TimeSpan.FromMilliseconds(10) };
            var dns = new FakeDnsResolver(uri.Host, IPAddress.Parse("93.184.216.34"));
            var reader = new FakeContentReader(Bytes("secret"));
            var transport = new DelayedHttpTransport(reader);
            var resolver = new AasResourceUrlFederationResolver(policy, dns, transport);

            Assert.Multiple(() =>
            {
                Assert.That(
                    async () => await resolver.ResolveAsync(uri),
                    Throws.TypeOf<TaskCanceledException>().Or.TypeOf<OperationCanceledException>());
                Assert.That(reader.Reads, Is.Zero);
            });
        }

        [Test]
        public async Task ResponseSizeBoundIsEnforcedBeforeRead()
        {
            Uri uri = new Uri("https://peer.example/aas");
            var policy = new AasFederationEgressPolicy { MaxDecompressedBytes = 4 };
            var dns = new FakeDnsResolver(uri.Host, IPAddress.Parse("93.184.216.34"));
            var reader = new FakeContentReader(Bytes("secret"));
            var transport = new FakeHttpTransport(new AasFederationHttpResponse(
                200, IPAddress.Parse("93.184.216.34"), null, 6, reader));
            var resolver = new AasResourceUrlFederationResolver(policy, dns, transport);

            AasFederationResolutionResult result = await resolver.ResolveAsync(uri);

            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.False);
                Assert.That(result.Content.IsEmpty, Is.True);
                Assert.That(reader.Reads, Is.Zero);
            });
        }

        [Test]
        public async Task CredentialIsNotSentToRedirectTarget()
        {
            Uri initial = new Uri("https://peer.example/aas");
            var policy = new AasFederationEgressPolicy
            {
                PeerCredentials = new ArrayOf<AasFederationPeerCredential>(new[]
                {
                    new AasFederationPeerCredential(new Uri("https://peer.example"), "Bearer one")
                })
            };
            var dns = new FakeDnsResolver();
            dns.Add("peer.example", IPAddress.Parse("93.184.216.34"));
            dns.Add("other.example", IPAddress.Parse("93.184.216.35"));
            var transport = new QueueHttpTransport(
                new AasFederationHttpResponse(
                    302,
                    IPAddress.Parse("93.184.216.34"),
                    new Uri("https://other.example/aas"),
                    null,
                    new FakeContentReader(ByteString.Empty)),
                new AasFederationHttpResponse(
                    200,
                    IPAddress.Parse("93.184.216.35"),
                    null,
                    2,
                    new FakeContentReader(Bytes("ok"))));
            var resolver = new AasResourceUrlFederationResolver(policy, dns, transport);

            AasFederationResolutionResult result = await resolver.ResolveAsync(initial);

            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.True);
                Assert.That(transport.Requests[0].AuthorizationHeader, Is.EqualTo("Bearer one"));
                Assert.That(transport.Requests[1].AuthorizationHeader, Is.Null);
            });
        }

        [TestCase("urn:wrong", "urn:peer")]
        [TestCase("urn:peer", "urn:wrong")]
        public async Task OpcUaPeerIdentityMismatchTerminatesWithoutRemoteRead(
            string certificateApplicationUri,
            string serverApplicationUri)
        {
            var policy = new AasFederationEgressPolicy
            {
                OpcUaPeers = new ArrayOf<AasOpcUaPeerPolicy>(new[]
                {
                    new AasOpcUaPeerPolicy(
                        "urn:server",
                        new Uri("opc.tcp://peer.example:4840"),
                        "urn:peer")
                })
            };
            policy.TrustedRestrictedHosts.Add("peer.example");
            var dns = new FakeDnsResolver("peer.example", IPAddress.Parse("93.184.216.34"));
            var client = new FakeOpcUaClient(new AasOpcUaPeerIdentity(
                certificateApplicationUri,
                serverApplicationUri,
                IPAddress.Parse("93.184.216.34")));
            var resolver = new AasOpcUaFederationResolver(policy, dns, client);

            AasFederationResolutionResult result = await resolver.ResolveAsync(
                new AasOpcUaExternalReference(
                    "urn:server",
                    new ExpandedNodeId("entity", "urn:namespace")),
                "urn:local");

            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.False);
                Assert.That(result.Content.IsEmpty, Is.True);
                Assert.That(client.RemoteReads, Is.Zero);
            });
        }

        private static ArrayOf<AasFederatedIdentifierAttribute> Attributes(string name, string value)
        {
            return new ArrayOf<AasFederatedIdentifierAttribute>(new[]
            {
                new AasFederatedIdentifierAttribute(name, value)
            });
        }

        private static ByteString Bytes(string value)
        {
            return ByteString.From(Encoding.UTF8.GetBytes(value));
        }

        private sealed class FakeDnsResolver : IAasFederationDnsResolver
        {
            public FakeDnsResolver()
            {
            }

            public FakeDnsResolver(string host, IPAddress address)
            {
                Add(host, address);
            }

            public void Add(string host, IPAddress address)
            {
                m_addresses[host] = new ArrayOf<IPAddress>(new[] { address });
            }

            public ValueTask<ArrayOf<IPAddress>> ResolveAsync(
                string host,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ValueTask<ArrayOf<IPAddress>>(
                    m_addresses.TryGetValue(host, out ArrayOf<IPAddress> addresses)
                        ? addresses
                        : new ArrayOf<IPAddress>(new[] { IPAddress.Parse("93.184.216.34") }));
            }

            private readonly Dictionary<string, ArrayOf<IPAddress>> m_addresses =
                new Dictionary<string, ArrayOf<IPAddress>>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class FakeHttpTransport : IAasFederationHttpTransport
        {
            public FakeHttpTransport(AasFederationHttpResponse response)
            {
                m_response = response;
            }

            public ValueTask<AasFederationHttpResponse> SendAsync(
                AasFederationHttpRequest request,
                CancellationToken cancellationToken)
            {
                Requests.Add(request);
                cancellationToken.ThrowIfCancellationRequested();
                return new ValueTask<AasFederationHttpResponse>(m_response);
            }

            public List<AasFederationHttpRequest> Requests { get; } =
                new List<AasFederationHttpRequest>();

            private readonly AasFederationHttpResponse m_response;
        }

        private sealed class QueueHttpTransport : IAasFederationHttpTransport
        {
            public QueueHttpTransport(params AasFederationHttpResponse[] responses)
            {
                m_responses = new Queue<AasFederationHttpResponse>(responses);
            }

            public ValueTask<AasFederationHttpResponse> SendAsync(
                AasFederationHttpRequest request,
                CancellationToken cancellationToken)
            {
                Requests.Add(request);
                cancellationToken.ThrowIfCancellationRequested();
                return new ValueTask<AasFederationHttpResponse>(m_responses.Dequeue());
            }

            public List<AasFederationHttpRequest> Requests { get; } =
                new List<AasFederationHttpRequest>();

            private readonly Queue<AasFederationHttpResponse> m_responses;
        }

        private sealed class DelayedHttpTransport : IAasFederationHttpTransport
        {
            public DelayedHttpTransport(FakeContentReader reader)
            {
                m_reader = reader;
            }

            public async ValueTask<AasFederationHttpResponse> SendAsync(
                AasFederationHttpRequest request,
                CancellationToken cancellationToken)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
                return new AasFederationHttpResponse(
                    200,
                    IPAddress.Parse("93.184.216.34"),
                    null,
                    6,
                    m_reader);
            }

            private readonly FakeContentReader m_reader;
        }

        private sealed class FakeContentReader : IAasFederationContentReader
        {
            public FakeContentReader(ByteString content)
            {
                m_content = content;
            }

            public int Reads { get; private set; }

            public ValueTask<ByteString> ReadAsync(
                int maxDecompressedBytes,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Reads++;
                return new ValueTask<ByteString>(m_content);
            }

            private readonly ByteString m_content;
        }

        private sealed class FakeOpcUaClient : IAasOpcUaFederationClient
        {
            public FakeOpcUaClient(AasOpcUaPeerIdentity identity)
            {
                m_identity = identity;
            }

            public int RemoteReads { get; private set; }

            public ValueTask<AasOpcUaPeerIdentity> DiscoverAsync(
                Uri endpointUrl,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ValueTask<AasOpcUaPeerIdentity>(m_identity);
            }

            public ValueTask<AasFederationResolutionResult> ReadLocalAsync(
                ExpandedNodeId nodeId,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ValueTask<AasFederationResolutionResult>(
                    AasFederationResolutionResult.Success(Bytes("local")));
            }

            public ValueTask<AasFederationResolutionResult> ReadRemoteAsync(
                Uri endpointUrl,
                AasOpcUaExternalReference externalReference,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                RemoteReads++;
                return new ValueTask<AasFederationResolutionResult>(
                    AasFederationResolutionResult.Success(Bytes("remote")));
            }

            private readonly AasOpcUaPeerIdentity m_identity;
        }
    }
}
