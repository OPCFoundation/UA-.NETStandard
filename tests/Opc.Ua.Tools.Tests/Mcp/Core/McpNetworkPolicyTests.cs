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

#if NET10_0
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua.Mcp;

namespace Opc.Ua.Tools.Tests.McpAuthorization
{
    /// <summary>
    /// Exercises the real network policy and captured grant with deterministic DNS and TCP adapters.
    /// No sockets, DNS queries, runtime leases, or live Kafka connections are created by this fixture.
    /// </summary>
    [TestFixture]
    public sealed class McpNetworkPolicyTests
    {
        [SetUp]
        public void SetUp()
        {
            m_cancellation = new CancellationTokenSource();
            m_authority = new Mock<IMcpOperationPolicy>(MockBehavior.Strict);
            m_authority.Setup(policy => policy.Require(McpCapability.WotRuntime)).Returns(() =>
            {
                m_order.Add("authority");
                return s_owner;
            });
            m_dns = new Mock<IMcpDnsResolver>(MockBehavior.Strict);
            m_connector = new Mock<IMcpTcpConnector>(MockBehavior.Strict);
            m_resolutions.Clear();
            m_connections.Clear();
            m_streams.Clear();
            m_order.Clear();
            m_addresses = [IPAddress.Parse("203.0.113.9")];
            m_resolve = (_, _) => ValueTask.FromResult(m_addresses);
            m_connect = (_, _) =>
            {
                var stream = new ObservedStream();
                m_streams.Add(stream);
                return ValueTask.FromResult<Stream>(stream);
            };
            m_dns.Setup(resolver => resolver.ResolveAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string host, CancellationToken token) =>
                {
                    m_order.Add("dns");
                    m_resolutions.Add((host, token));
                    return m_resolve(host, token);
                });
            m_connector.Setup(connector => connector.ConnectAsync(
                It.IsAny<IPEndPoint>(), It.IsAny<CancellationToken>()))
                .Returns((IPEndPoint endpoint, CancellationToken token) =>
                {
                    m_order.Add("connect");
                    m_connections.Add((endpoint, token));
                    return m_connect(endpoint, token);
                });
        }

        [TearDown]
        public void TearDown()
        {
            foreach (ObservedStream stream in m_streams)
            {
                if (!stream.IsDisposed)
                {
                    stream.Dispose();
                }
            }
            m_cancellation.Dispose();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void EmptyRulesDenyEvenAnAuthorizedCaller(bool defaultArray)
        {
            var policy = new McpNetworkPolicy(
                m_authority.Object, defaultArray ? default : [], m_dns.Object, m_connector.Object);

            AssertRefused(() => policy.Authorize(s_endpoint, "GET", "/device", McpCapability.WotRuntime));

            m_authority.Verify(authority => authority.Require(McpCapability.WotRuntime), Times.Once);
            m_authority.VerifyNoOtherCalls();
            AssertNoNetwork();
        }

        [TestCase("HostDisabled")]
        [TestCase("MissingGrant")]
        [TestCase("WrongCapability")]
        [TestCase("MissingCaller")]
        [TestCase("WrongSubject")]
        [TestCase("WrongIssuer")]
        [TestCase("WrongLocalFlag")]
        public void CapabilityAndExactCallerAreRequiredBeforeAnyDns(string denial)
        {
            var callers = new McpCallerContext();
            var options = new McpExecutionOptions
            {
                EnabledCapabilities = denial == "HostDisabled" ? [] : [McpCapability.WotRuntime],
                CallerGrants = denial == "MissingGrant" ? [] :
                [
                    new McpCallerGrant
                    {
                        Caller = s_owner,
                        Capabilities = denial == "WrongCapability"
                            ? [McpCapability.WotWrite] : [McpCapability.WotRuntime]
                    }
                ]
            };
            McpCallerIdentity selected = denial switch
            {
                "WrongSubject" => s_owner with { Subject = "other-caller" },
                "WrongIssuer" => s_owner with { Issuer = "urn:other-authority" },
                "WrongLocalFlag" => s_owner with { IsLocal = true },
                _ => s_owner
            };
            var authority = new McpExecutionPolicy(options, callers, Telemetry());
            var policy = new McpNetworkPolicy(authority, [Rule()], m_dns.Object, m_connector.Object);
            using IDisposable? scope = denial == "MissingCaller" ? null : callers.EnterTrustedCaller(selected);

            Assert.That(() => policy.Authorize(s_endpoint, "GET", "/device", McpCapability.WotRuntime),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.Code)).EqualTo(StatusCodes.BadUserAccessDenied.Code));

            Assert.That(authority.IsEnabled(McpCapability.WotRuntime), Is.EqualTo(denial != "HostDisabled"));
            Assert.That(callers.Current, Is.EqualTo(denial == "MissingCaller" ? null : selected));
            AssertNoNetwork();
        }

        [Test]
        public void AuthorizePreservesTheCapabilityRefusalWithoutResolving()
        {
            var refusal = new ServiceResultException(StatusCodes.BadUserAccessDenied, "Host capability refused.");
            m_authority.Setup(policy => policy.Require(McpCapability.WotRuntime)).Throws(refusal);
            McpNetworkPolicy policy = Policy(Rule());

            ServiceResultException? actual = null;
            try
            {
                _ = policy.Authorize(s_endpoint, "GET", "/device", McpCapability.WotRuntime);
            }
            catch (ServiceResultException exception)
            {
                actual = exception;
            }

            Assert.That(actual, Is.SameAs(refusal));
            Assert.That(actual!.Code, Is.EqualTo(StatusCodes.BadUserAccessDenied.Code));

            m_authority.Verify(authority => authority.Require(McpCapability.WotRuntime), Times.Once);
            AssertNoNetwork();
        }

        [Test]
        public async Task AuthorizeDefersDnsAndConnectsOnlyTheApprovedIpAsync()
        {
            McpNetworkPolicy policy = Policy(Rule());
            var endpoint = new Uri("https://api.example/device?version=7");

            McpNetworkGrant grant = policy.Authorize(endpoint, "GET", "/device", McpCapability.WotRuntime);

            AssertGrant(grant, endpoint, "GET", "/device", McpCapability.WotRuntime);
            Assert.That(m_order, Is.EqualTo(s_authorityOnly));
            AssertNoNetwork();
            using Stream stream = await grant.ConnectTcpAsync(m_cancellation.Token).ConfigureAwait(false);
            Assert.That(stream, Is.SameAs(m_streams[0]));
            Assert.That(stream.CanRead, Is.True);
            Assert.That(m_streams[0].IsDisposed, Is.False);
            Assert.That(m_order, Is.EqualTo(s_authorityDnsConnect));
            AssertResolution("api.example", 1);
            AssertConnection(0, "203.0.113.9", 443);
            m_connector.Verify(connector => connector.ConnectAsync(
                It.Is<IPEndPoint>(target => target.Address.Equals(IPAddress.Parse("203.0.113.9")) &&
                    target.Port == 443),
                m_cancellation.Token), Times.Once);
            m_authority.Verify(authority => authority.Require(McpCapability.WotRuntime), Times.Once);
        }

        [TestCase("API.EXAMPLE.", "https://api.example/device", "api.example")]
        [TestCase("api.example", "https://API.EXAMPLE.:443/device", "api.example")]
        [TestCase("bücher.example.", "https://xn--bcher-kva.example/device", "xn--bcher-kva.example")]
        [TestCase("XN--BCHER-KVA.EXAMPLE", "https://bücher.example./device", "xn--bcher-kva.example")]
        public async Task HostNormalizationUsesExactIdnaAndTrailingDotIdentityAsync(
            string ruleHost, string uri, string dnsHost)
        {
            McpNetworkPolicy policy = Policy(Rule() with { Scheme = "HTTPS", Host = ruleHost });
            var endpoint = new Uri(uri);

            McpNetworkGrant grant = policy.Authorize(endpoint, "GET", "/device", McpCapability.WotRuntime);
            using Stream stream = await grant.ConnectTcpAsync(m_cancellation.Token).ConfigureAwait(false);

            AssertGrant(grant, endpoint, "GET", "/device", McpCapability.WotRuntime);
            Assert.That(grant.Endpoint.OriginalString, Is.EqualTo(uri));
            AssertResolution(dnsHost, 1);
            AssertConnection(0, "203.0.113.9", 443);
            Assert.That(stream, Is.SameAs(m_streams[0]));
        }

        [TestCase("https://sub.api.example/device")]
        [TestCase("https://api.example.evil.invalid/device")]
        [TestCase("https://other.example/device")]
        [TestCase("https://api.example:444/device")]
        [TestCase("http://api.example:443/device")]
        public void HostPortAndSchemeMustAllMatchExactly(string uri)
        {
            McpNetworkPolicy policy = Policy(Rule());

            AssertRefused(() => policy.Authorize(new Uri(uri), "GET", "/device", McpCapability.WotRuntime));

            m_authority.Verify(authority => authority.Require(McpCapability.WotRuntime), Times.Once);
            AssertNoNetwork();
        }

        [TestCase("http", 80)]
        [TestCase("https", 443)]
        [TestCase("mqtt", 1883)]
        [TestCase("mqtts", 8883)]
        [TestCase("modbus", 502)]
        [TestCase("modbus+tcp", 502)]
        [TestCase("opc.tcp", 4840)]
        public async Task OmittedAndExplicitProtocolPortsReachTheSameApprovedEndpointAsync(string scheme, int port)
        {
            McpNetworkPolicy policy = Policy(Rule() with { Scheme = scheme, Port = port });
            var implicitPort = new Uri($"{scheme}://api.example/device");
            var explicitPort = new Uri($"{scheme}://api.example:{port}/device");

            using Stream first = await policy.Authorize(
                implicitPort, "GET", "/device", McpCapability.WotRuntime).ConnectTcpAsync(m_cancellation.Token)
                .ConfigureAwait(false);
            using Stream second = await policy.Authorize(
                explicitPort, "GET", "/device", McpCapability.WotRuntime).ConnectTcpAsync(m_cancellation.Token)
                .ConfigureAwait(false);

            AssertResolution("api.example", 2);
            Assert.That(m_connections, Has.Count.EqualTo(2));
            AssertConnection(0, "203.0.113.9", port);
            AssertConnection(1, "203.0.113.9", port);
            Assert.That(first, Is.Not.SameAs(second));
        }

        [TestCase(1)]
        [TestCase(65535)]
        public async Task ExplicitPortBoundariesReachExactlyThatPortAsync(int port)
        {
            McpNetworkPolicy policy = Policy(Rule() with { Scheme = "custom+tcp", Port = port });

            using Stream stream = await policy.Authorize(
                new Uri($"custom+tcp://api.example:{port}/device"), "GET", "/device", McpCapability.WotRuntime)
                .ConnectTcpAsync(m_cancellation.Token).ConfigureAwait(false);

            AssertResolution("api.example", 1);
            AssertConnection(0, "203.0.113.9", port);
            Assert.That(stream, Is.SameAs(m_streams[0]));
        }

        [TestCase("http", 80)]
        [TestCase("https", 443)]
        [TestCase("mqtt", 1883)]
        public void ExplicitZeroPortCannotBecomeTheProtocolDefault(string scheme, int port)
        {
            McpNetworkPolicy policy = Policy(Rule() with { Scheme = scheme, Port = port });
            var endpoint = new Uri($"{scheme}://api.example:0/device");
            Assert.That(endpoint.Port, Is.Zero);

            AssertRefused(() => policy.Authorize(endpoint, "GET", "/device", McpCapability.WotRuntime));

            AssertNoNetwork();
        }

        [Test]
        public void UnknownSchemeWithoutAPortIsRefused()
        {
            McpNetworkPolicy policy = Policy(Rule() with { Scheme = "custom+tcp", Port = 4321 });

            AssertRefused(() => policy.Authorize(
                new Uri("custom+tcp://api.example/device"), "GET", "/device", McpCapability.WotRuntime));

            AssertNoNetwork();
        }

        [TestCase("GET", "/device", true)]
        [TestCase("get", "/device", false)]
        [TestCase("GET ", "/device", false)]
        [TestCase("POST", "/device", false)]
        [TestCase("GET", "/Device", false)]
        [TestCase("GET", "/dev", false)]
        [TestCase("GET", "/device/child", false)]
        [TestCase("GET", "/device?x=1", false)]
        [TestCase("GET", "/device ", false)]
        [TestCase("GET", "", false)]
        [TestCase("*", "/device", false)]
        [TestCase("GET", "*", false)]
        public void OperationAndResourceUseOrdinalExactMatching(string operation, string resource, bool allowed)
        {
            McpNetworkPolicy policy = Policy(Rule());

            if (allowed)
            {
                McpNetworkGrant grant = policy.Authorize(s_endpoint, operation, resource, McpCapability.WotRuntime);
                AssertGrant(grant, s_endpoint, "GET", "/device", McpCapability.WotRuntime);
            }
            else
            {
                AssertRefused(() => policy.Authorize(s_endpoint, operation, resource, McpCapability.WotRuntime));
            }
            AssertNoNetwork();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void EmptyResourceListGrantsOnlyTheEmptySelector(bool defaultArray)
        {
            McpNetworkPolicy policy = Policy(Rule() with { Resources = defaultArray ? default : [] });

            McpNetworkGrant grant = policy.Authorize(s_endpoint, "GET", string.Empty, McpCapability.WotRuntime);

            AssertGrant(grant, s_endpoint, "GET", string.Empty, McpCapability.WotRuntime);
            AssertRefused(() => policy.Authorize(s_endpoint, "GET", "/device", McpCapability.WotRuntime));
            AssertRefused(() => policy.Authorize(s_endpoint, "GET", "*", McpCapability.WotRuntime));
            AssertNoNetwork();
        }

        [Test]
        public void AsteriskSelectorsAreLiteralsNotWildcardGrants()
        {
            McpNetworkPolicy policy = Policy(Rule() with { Operations = ["*"], Resources = ["*"] });

            McpNetworkGrant grant = policy.Authorize(s_endpoint, "*", "*", McpCapability.WotRuntime);

            AssertGrant(grant, s_endpoint, "*", "*", McpCapability.WotRuntime);
            AssertRefused(() => policy.Authorize(s_endpoint, "GET", "*", McpCapability.WotRuntime));
            AssertRefused(() => policy.Authorize(s_endpoint, "*", "/device", McpCapability.WotRuntime));
            AssertRefused(() => policy.Authorize(s_endpoint, "*", string.Empty, McpCapability.WotRuntime));
            AssertNoNetwork();
        }

        [Test]
        public void AGrantForAnotherCapabilityCannotAuthorizeThisEndpoint()
        {
            m_authority.Setup(policy => policy.Require(McpCapability.WotWrite)).Returns(s_owner);
            McpNetworkPolicy policy = Policy(Rule());

            AssertRefused(() => policy.Authorize(s_endpoint, "GET", "/device", McpCapability.WotWrite));

            m_authority.Verify(authority => authority.Require(McpCapability.WotWrite), Times.Once);
            m_authority.Verify(authority => authority.Require(McpCapability.WotRuntime), Times.Never);
            AssertNoNetwork();
        }

        [TestCase("/relative-device")]
        [TestCase("https://synthetic-user:synthetic-password@api.example/device")]
        [TestCase("https://api.example/device#synthetic-fragment")]
        public void RelativeUserInfoAndFragmentUrisFailWithoutAuthorityOrDns(string value)
        {
            McpNetworkPolicy policy = Policy(Rule());
            var endpoint = new Uri(value, UriKind.RelativeOrAbsolute);

            ServiceResultException? failure = null;
            try
            {
                _ = policy.Authorize(endpoint, "GET", "/device", McpCapability.WotRuntime);
            }
            catch (ServiceResultException exception)
            {
                failure = exception;
            }

            Assert.That(failure, Is.TypeOf<ServiceResultException>());
            Assert.That(failure!.Code, Is.EqualTo(StatusCodes.BadUserAccessDenied.Code));
            Assert.That(failure.Message, Is.EqualTo(kNetworkRefusal));
            Assert.That(failure.Message, Does.Not.Contain("synthetic-"));
            m_authority.VerifyNoOtherCalls();
            AssertNoNetwork();
        }

        [TestCase("Endpoint")]
        [TestCase("OperationNull")]
        [TestCase("OperationEmpty")]
        [TestCase("OperationWhitespace")]
        [TestCase("Resource")]
        public void InvalidInvocationArgumentsNeverReachAuthorityOrNetwork(string invalid)
        {
            McpNetworkPolicy policy = Policy(Rule());
            Uri? endpoint = invalid == "Endpoint" ? null : s_endpoint;
            string? operation = invalid switch
            {
                "OperationNull" => null,
                "OperationEmpty" => string.Empty,
                "OperationWhitespace" => " \t ",
                _ => "GET"
            };
            string? resource = invalid == "Resource" ? null : "/device";
            Type exceptionType = invalid is "OperationEmpty" or "OperationWhitespace"
                ? typeof(ArgumentException) : typeof(ArgumentNullException);

            Assert.That(() => policy.Authorize(endpoint!, operation!, resource!, McpCapability.WotRuntime),
                Throws.TypeOf(exceptionType));

            m_authority.VerifyNoOtherCalls();
            AssertNoNetwork();
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task EmptyDnsResultsAreExplicitlyRefusedBeforeConnectingAsync(bool defaultArray)
        {
            m_addresses = defaultArray ? default : [];
            McpNetworkGrant grant = Policy(Rule()).Authorize(s_endpoint, "GET", "/device", McpCapability.WotRuntime);

            await AssertRefusedAsync(() => grant.ConnectTcpAsync(m_cancellation.Token)).ConfigureAwait(false);

            AssertResolution("api.example", 1);
            AssertNoConnections();
        }

        [TestCase(1, true)]
        [TestCase(32, true)]
        [TestCase(33, false)]
        [TestCase(34, false)]
        public async Task DnsAddressCountHasAnExactThirtyTwoAddressBoundaryAsync(int count, bool allowed)
        {
            m_addresses = Enumerable.Range(1, count)
                .Select(index => IPAddress.Parse($"203.0.113.{index}")).ToArray();
            McpNetworkGrant grant = Policy(Rule()).Authorize(s_endpoint, "GET", "/device", McpCapability.WotRuntime);

            if (allowed)
            {
                using Stream stream = await grant.ConnectTcpAsync(m_cancellation.Token).ConfigureAwait(false);
                Assert.That(stream, Is.SameAs(m_streams[0]));
                Assert.That(m_connections, Has.Count.EqualTo(1));
                AssertConnection(0, "203.0.113.1", 443);
            }
            else
            {
                await AssertRefusedAsync(() => grant.ConnectTcpAsync(m_cancellation.Token)).ConfigureAwait(false);
                AssertNoConnections();
            }
            AssertResolution("api.example", 1);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public async Task OneAddressOutsideTheNetworksRefusesTheWholeResolutionAsync(int deniedIndex)
        {
            IPAddress[] addresses =
            [
                IPAddress.Parse("203.0.113.9"), IPAddress.Parse("203.0.113.10"), IPAddress.Parse("203.0.113.11")
            ];
            addresses[deniedIndex] = IPAddress.Parse("198.51.100.19");
            m_addresses = addresses;
            McpNetworkGrant grant = Policy(Rule()).Authorize(s_endpoint, "GET", "/device", McpCapability.WotRuntime);

            await AssertRefusedAsync(() => grant.ConnectTcpAsync(m_cancellation.Token)).ConfigureAwait(false);

            AssertResolution("api.example", 1);
            AssertNoConnections();
            Assert.That(m_order, Is.EqualTo(s_authorityDns));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task AllAddressesMayUseDifferentExplicitNetworksWithoutASecondDnsLookupAsync(bool derivedFailure)
        {
            m_addresses = [IPAddress.Parse("203.0.113.9"), IPAddress.Parse("2001:db8:7::2")];
            SocketException firstFailure = derivedFailure
                ? new AdapterSocketException()
                : new SocketException((int)SocketError.ConnectionRefused);
            m_connect = (_, _) => m_connections.Count == 1
                ? ValueTask.FromException<Stream>(firstFailure) : NewStream();
            McpNetworkGrant grant = Policy(Rule() with { Networks = ["203.0.113.0/24", "2001:db8:7::/64"] })
                .Authorize(s_endpoint, "GET", "/device", McpCapability.WotRuntime);

            using Stream stream = await grant.ConnectTcpAsync(m_cancellation.Token).ConfigureAwait(false);

            AssertResolution("api.example", 1);
            Assert.That(m_connections, Has.Count.EqualTo(2));
            AssertConnection(0, "203.0.113.9", 443);
            AssertConnection(1, "2001:db8:7::2", 443);
            Assert.That(stream, Is.SameAs(m_streams[0]));
            Assert.That(m_order, Is.EqualTo(s_authorityDnsConnectConnect));
        }

        [TestCase("203.0.113.128/25", "203.0.113.128", true)]
        [TestCase("203.0.113.128/25", "203.0.113.255", true)]
        [TestCase("203.0.113.128/25", "203.0.113.127", false)]
        [TestCase("203.0.113.128/25", "203.0.114.0", false)]
        [TestCase("2001:db8:7::/127", "2001:db8:7::", true)]
        [TestCase("2001:db8:7::/127", "2001:db8:7::1", true)]
        [TestCase("2001:db8:7::/127", "2001:db8:7::2", false)]
        [TestCase("2001:db8:7::/127", "2001:db8:6:ffff:ffff:ffff:ffff:ffff", false)]
        public async Task CidrMatchingHonorsExactAndImmediatelyAdjacentAddressesAsync(
            string network, string address, bool allowed)
        {
            m_addresses = [IPAddress.Parse(address)];
            McpNetworkGrant grant = Policy(Rule() with { Networks = [network] })
                .Authorize(s_endpoint, "GET", "/device", McpCapability.WotRuntime);

            if (allowed)
            {
                using Stream stream = await grant.ConnectTcpAsync(m_cancellation.Token).ConfigureAwait(false);
                AssertConnection(0, address, 443);
                Assert.That(stream, Is.SameAs(m_streams[0]));
            }
            else
            {
                await AssertRefusedAsync(() => grant.ConnectTcpAsync(m_cancellation.Token)).ConfigureAwait(false);
                AssertNoConnections();
            }
            AssertResolution("api.example", 1);
        }

        [TestCase("::ffff:203.0.113.9", "203.0.113.0/24", "203.0.113.9", true)]
        [TestCase("::ffff:198.51.100.19", "203.0.113.0/24", "198.51.100.19", false)]
        [TestCase("::ffff:192.168.7.9", "192.168.7.0/24", "192.168.7.9", true)]
        [TestCase("::ffff:127.0.0.1", "127.0.0.1/32", "127.0.0.1", true)]
        public async Task MappedIpv6UsesNativeIpv4NetworksAndConnectorAddressesAsync(
            string mapped, string network, string native, bool allowed)
        {
            m_addresses = [IPAddress.Parse(mapped)];
            McpNetworkGrant grant = Policy(Rule() with { Networks = [network] })
                .Authorize(s_endpoint, "GET", "/device", McpCapability.WotRuntime);

            if (allowed)
            {
                using Stream stream = await grant.ConnectTcpAsync(m_cancellation.Token).ConfigureAwait(false);
                AssertConnection(0, native, 443);
                Assert.That(m_connections[0].Endpoint.AddressFamily, Is.EqualTo(AddressFamily.InterNetwork));
                Assert.That(stream, Is.SameAs(m_streams[0]));
            }
            else
            {
                await AssertRefusedAsync(() => grant.ConnectTcpAsync(m_cancellation.Token)).ConfigureAwait(false);
                AssertNoConnections();
            }
            AssertResolution("api.example", 1);
        }

        [TestCase("203.0.113.9", "https://203.0.113.9/device", "203.0.113.0/24", "203.0.113.9")]
        [TestCase("[::ffff:203.0.113.9]", "https://203.0.113.9/device", "203.0.113.0/24", "203.0.113.9")]
        [TestCase("203.0.113.9", "https://[::ffff:203.0.113.9]/device", "203.0.113.0/24", "203.0.113.9")]
        [TestCase("[2001:db8:7::2]", "https://[2001:db8:7::2]/device", "2001:db8:7::/64", "2001:db8:7::2")]
        public async Task IpLiteralConnectionsBypassDnsWithoutChangingEndpointIdentityAsync(
            string host, string uri, string network, string address)
        {
            var endpoint = new Uri(uri);
            McpNetworkGrant grant = Policy(Rule() with { Host = host, Networks = [network] })
                .Authorize(endpoint, "GET", "/device", McpCapability.WotRuntime);

            using Stream stream = await grant.ConnectTcpAsync(m_cancellation.Token).ConfigureAwait(false);

            AssertGrant(grant, endpoint, "GET", "/device", McpCapability.WotRuntime);
            Assert.That(m_resolutions, Is.Empty);
            m_dns.Verify(resolver => resolver.ResolveAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            AssertConnection(0, address, 443);
            Assert.That(stream, Is.SameAs(m_streams[0]));
        }

        [TestCase("127.0.0.1", "127.0.0.1/32", "203.0.113.0/24")]
        [TestCase("10.2.3.4", "10.0.0.0/8", "203.0.113.0/24")]
        [TestCase("172.16.7.9", "172.16.0.0/12", "203.0.113.0/24")]
        [TestCase("192.168.7.9", "192.168.0.0/16", "203.0.113.0/24")]
        [TestCase("::1", "::1/128", "2001:db8::/32")]
        [TestCase("fd12:3456::9", "fc00::/7", "2001:db8::/32")]
        public async Task NativePrivateAndLoopbackAddressesNeedAnExplicitMatchingCidrAsync(
            string address, string privateNetwork, string otherNetwork)
        {
            m_addresses = [IPAddress.Parse(address)];
            McpNetworkGrant denied = Policy(Rule() with { Networks = [otherNetwork] })
                .Authorize(s_endpoint, "GET", "/device", McpCapability.WotRuntime);

            await AssertRefusedAsync(() => denied.ConnectTcpAsync(m_cancellation.Token)).ConfigureAwait(false);
            AssertNoConnections();

            McpNetworkGrant allowed = Policy(Rule() with { Networks = [privateNetwork] })
                .Authorize(s_endpoint, "GET", "/device", McpCapability.WotRuntime);
            using Stream stream = await allowed.ConnectTcpAsync(m_cancellation.Token).ConfigureAwait(false);

            AssertResolution("api.example", 2);
            Assert.That(m_connections, Has.Count.EqualTo(1));
            AssertConnection(0, address, 443);
            Assert.That(stream, Is.SameAs(m_streams[0]));
        }

        [TestCase("0.0.0.0", "0.0.0.0/0")]
        [TestCase("0.1.2.3", "0.0.0.0/0")]
        [TestCase("224.0.0.1", "0.0.0.0/0")]
        [TestCase("239.1.2.3", "0.0.0.0/0")]
        [TestCase("240.0.0.1", "0.0.0.0/0")]
        [TestCase("255.255.255.255", "0.0.0.0/0")]
        [TestCase("::", "::/0")]
        [TestCase("ff02::1", "::/0")]
        [TestCase("fe80::1", "::/0")]
        [TestCase("fe80::1%7", "::/0")]
        [TestCase("2001:db8:7::2%7", "::/0")]
        [TestCase("::ffff:0.0.0.0", "0.0.0.0/0")]
        [TestCase("::ffff:224.0.0.1", "0.0.0.0/0")]
        public async Task UnspecifiedMulticastAndScopedAddressesAreDeniedEvenByBroadNetworksAsync(
            string address, string network)
        {
            m_addresses = [IPAddress.Parse(address)];
            McpNetworkGrant grant = Policy(Rule() with { Networks = [network] })
                .Authorize(s_endpoint, "GET", "/device", McpCapability.WotRuntime);

            await AssertRefusedAsync(() => grant.ConnectTcpAsync(m_cancellation.Token)).ConfigureAwait(false);

            AssertResolution("api.example", 1);
            AssertNoConnections();
        }

        [Test]
        public async Task ScopedMappedIpv6CannotLoseItsScopeDuringNormalizationAsync()
        {
            var address = new IPAddress(IPAddress.Parse("::ffff:203.0.113.9").GetAddressBytes(), 7);
            Assert.That(address.IsIPv4MappedToIPv6, Is.True);
            Assert.That(address.ScopeId, Is.EqualTo(7L));
            m_addresses = [address];
            McpNetworkGrant grant = Policy(Rule()).Authorize(s_endpoint, "GET", "/device", McpCapability.WotRuntime);

            await AssertRefusedAsync(() => grant.ConnectTcpAsync(m_cancellation.Token)).ConfigureAwait(false);

            AssertResolution("api.example", 1);
            AssertNoConnections();
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task SecondConnectionRevalidatesDnsInsteadOfReusingAnEarlierApprovalAsync(bool secondAllowed)
        {
            McpNetworkGrant grant = Policy(Rule()).Authorize(s_endpoint, "GET", "/device", McpCapability.WotRuntime);
            using Stream first = await grant.ConnectTcpAsync(m_cancellation.Token).ConfigureAwait(false);
            m_addresses = [IPAddress.Parse(secondAllowed ? "203.0.113.10" : "198.51.100.19")];

            if (secondAllowed)
            {
                using Stream second = await grant.ConnectTcpAsync(m_cancellation.Token).ConfigureAwait(false);
                Assert.That(second, Is.Not.SameAs(first));
                AssertConnection(1, "203.0.113.10", 443);
            }
            else
            {
                await AssertRefusedAsync(() => grant.ConnectTcpAsync(m_cancellation.Token)).ConfigureAwait(false);
            }

            AssertResolution("api.example", 2);
            Assert.That(m_connections, Has.Count.EqualTo(secondAllowed ? 2 : 1));
            AssertConnection(0, "203.0.113.9", 443);
            Assert.That(first.CanRead, Is.True);
            m_authority.Verify(authority => authority.Require(McpCapability.WotRuntime), Times.Once);
        }

        [Test]
        public async Task SocketFallbackNeverConnectsToAnUnvalidatedReplacementAddressAsync()
        {
            IPAddress[] addresses = [IPAddress.Parse("203.0.113.9"), IPAddress.Parse("203.0.113.10")];
            m_addresses = new ArrayOf<IPAddress>(addresses.AsMemory());
            m_connect = (_, _) =>
            {
                if (m_connections.Count == 1)
                {
                    // A resolver may reuse its returned storage. The connected endpoint must still be approved.
                    addresses[1] = IPAddress.Parse("198.51.100.19");
                    return ValueTask.FromException<Stream>(new SocketException((int)SocketError.ConnectionRefused));
                }
                return NewStream();
            };
            McpNetworkGrant grant = Policy(Rule()).Authorize(s_endpoint, "GET", "/device", McpCapability.WotRuntime);

            Stream? stream = null;
            ServiceResultException? refusal = null;
            try
            {
                stream = await grant.ConnectTcpAsync(m_cancellation.Token).ConfigureAwait(false);
            }
            catch (ServiceResultException exception)
            {
                refusal = exception;
            }

            using (stream)
            {
                AssertResolution("api.example", 1);
                AssertConnection(0, "203.0.113.9", 443);
                Assert.That(addresses[1], Is.EqualTo(IPAddress.Parse("198.51.100.19")));
                Assert.That(m_connections.Any(connection =>
                    connection.Endpoint.Address.Equals(IPAddress.Parse("198.51.100.19"))), Is.False);
                if (refusal is not null)
                {
                    // Revalidation may refuse the changed result instead of retaining an immutable DNS snapshot.
                    Assert.That(refusal.Code, Is.EqualTo(StatusCodes.BadUserAccessDenied.Code));
                    Assert.That(refusal.Message, Is.EqualTo(kNetworkRefusal));
                    Assert.That(m_connections, Has.Count.EqualTo(1));
                    Assert.That(stream, Is.Null);
                }
                else
                {
                    Assert.That(m_connections, Has.Count.EqualTo(2));
                    AssertConnection(1, "203.0.113.10", 443);
                    Assert.That(stream, Is.SameAs(m_streams[0]));
                }
            }
        }

        [Test]
        public async Task SocketFailuresTryOnlyApprovedAddressesAndPreserveTheLastFailureAsync()
        {
            m_addresses = [IPAddress.Parse("203.0.113.9"), IPAddress.Parse("203.0.113.10")];
            var first = new SocketException((int)SocketError.ConnectionRefused);
            var last = new SocketException((int)SocketError.HostUnreachable);
            m_connect = (_, _) => ValueTask.FromException<Stream>(m_connections.Count == 1 ? first : last);
            McpNetworkGrant grant = Policy(Rule()).Authorize(s_endpoint, "GET", "/device", McpCapability.WotRuntime);

            IOException failure = await CaptureAsync<IOException>(
                () => grant.ConnectTcpAsync(m_cancellation.Token)).ConfigureAwait(false);

            Assert.That(failure.Message, Is.EqualTo(
                "No policy-approved endpoint address accepted the TCP connection."));
            Assert.That(failure.InnerException, Is.SameAs(last));
            AssertResolution("api.example", 1);
            Assert.That(m_connections, Has.Count.EqualTo(2));
            AssertConnection(0, "203.0.113.9", 443);
            AssertConnection(1, "203.0.113.10", 443);
            Assert.That(m_streams, Is.Empty);
        }

        [Test]
        public async Task DnsFailureIsNotRetriedOrConvertedIntoAnApprovedConnectionAsync()
        {
            var expected = new SocketException((int)SocketError.HostNotFound);
            m_resolve = (_, _) => ValueTask.FromException<ArrayOf<IPAddress>>(expected);
            McpNetworkGrant grant = Policy(Rule()).Authorize(s_endpoint, "GET", "/device", McpCapability.WotRuntime);

            SocketException failure = await CaptureAsync<SocketException>(
                () => grant.ConnectTcpAsync(m_cancellation.Token)).ConfigureAwait(false);

            Assert.That(failure, Is.SameAs(expected));
            AssertResolution("api.example", 1);
            AssertNoConnections();
        }

        [TestCase("Timeout")]
        [TestCase("Cancelled")]
        [TestCase("Socket")]
        public async Task ConnectorFailureDoesNotDisposeAStreamAlreadyOwnedByTheCallerAsync(string kind)
        {
            McpNetworkGrant grant = Policy(Rule()).Authorize(s_endpoint, "GET", "/device", McpCapability.WotRuntime);
            using Stream first = await grant.ConnectTcpAsync(m_cancellation.Token).ConfigureAwait(false);
            ObservedStream owned = m_streams[0];
            Exception expected = kind switch
            {
                "Timeout" => new TimeoutException("Synthetic connector deadline."),
                "Cancelled" => new OperationCanceledException(m_cancellation.Token),
                _ => new SocketException((int)SocketError.ConnectionRefused)
            };
            m_connect = (_, _) => ValueTask.FromException<Stream>(expected);

            Exception failure = await CaptureAsync<Exception>(
                () => grant.ConnectTcpAsync(m_cancellation.Token)).ConfigureAwait(false);

            if (kind == "Socket")
            {
                Assert.That(failure, Is.TypeOf<IOException>());
                Assert.That(failure.InnerException, Is.SameAs(expected));
            }
            else
            {
                Assert.That(failure, Is.SameAs(expected));
            }
            AssertResolution("api.example", 2);
            Assert.That(m_connections, Has.Count.EqualTo(2));
            AssertConnection(0, "203.0.113.9", 443);
            AssertConnection(1, "203.0.113.9", 443);
            Assert.That(owned.IsDisposed, Is.False);
            Assert.That(owned.CanRead, Is.True);
            await first.DisposeAsync().ConfigureAwait(false);
            Assert.That(owned.IsDisposed, Is.True);
            Assert.That(owned.DisposalCount, Is.EqualTo(1));
        }

        [Test]
        public async Task ConnectorTimeoutDoesNotRetryTheNextAddressAsync()
        {
            m_addresses = [IPAddress.Parse("203.0.113.9"), IPAddress.Parse("203.0.113.10")];
            var expected = new TimeoutException("Synthetic connector deadline.");
            m_connect = (_, _) => ValueTask.FromException<Stream>(expected);
            McpNetworkGrant grant = Policy(Rule()).Authorize(s_endpoint, "GET", "/device", McpCapability.WotRuntime);

            TimeoutException failure = await CaptureAsync<TimeoutException>(
                () => grant.ConnectTcpAsync(m_cancellation.Token)).ConfigureAwait(false);

            Assert.That(failure, Is.SameAs(expected));
            AssertResolution("api.example", 1);
            Assert.That(m_connections, Has.Count.EqualTo(1));
            AssertConnection(0, "203.0.113.9", 443);
            Assert.That(m_streams, Is.Empty);
        }

        [Test]
        public async Task PreCancellationPreventsDnsAndConnectorDispatchAsync()
        {
            McpNetworkGrant grant = Policy(Rule()).Authorize(s_endpoint, "GET", "/device", McpCapability.WotRuntime);
            await m_cancellation.CancelAsync().ConfigureAwait(false);

            OperationCanceledException failure = await CaptureAsync<OperationCanceledException>(
                () => grant.ConnectTcpAsync(m_cancellation.Token)).ConfigureAwait(false);

            Assert.That(failure.CancellationToken, Is.EqualTo(m_cancellation.Token));
            AssertNoNetwork();
        }

        [Test]
        public async Task CancellationAfterDnsPreventsAnUnawareConnectorFromRunningAsync()
        {
            m_resolve = async (_, _) =>
            {
                await m_cancellation.CancelAsync().ConfigureAwait(false);
                return m_addresses;
            };
            McpNetworkGrant grant = Policy(Rule()).Authorize(s_endpoint, "GET", "/device", McpCapability.WotRuntime);

            OperationCanceledException failure = await CaptureAsync<OperationCanceledException>(
                () => grant.ConnectTcpAsync(m_cancellation.Token)).ConfigureAwait(false);

            Assert.That(failure.CancellationToken, Is.EqualTo(m_cancellation.Token));
            AssertResolution("api.example", 1);
            AssertNoConnections();
        }

        [TestCase(false, 1)]
        [TestCase(false, 2)]
        [TestCase(true, 1)]
        [TestCase(true, 2)]
        public async Task ConnectorCancellationStopsFallbackAndPreservesTheOwnedTokenAsync(
            bool socketFailure, int addressCount)
        {
            m_addresses = addressCount == 1
                ? [IPAddress.Parse("203.0.113.9")]
                : [IPAddress.Parse("203.0.113.9"), IPAddress.Parse("203.0.113.10")];
            m_connect = async (_, token) =>
            {
                await m_cancellation.CancelAsync().ConfigureAwait(false);
                throw socketFailure
                    ? new SocketException((int)SocketError.OperationAborted)
                    : new OperationCanceledException(token);
            };
            McpNetworkGrant grant = Policy(Rule()).Authorize(s_endpoint, "GET", "/device", McpCapability.WotRuntime);

            OperationCanceledException failure = await CaptureAsync<OperationCanceledException>(
                () => grant.ConnectTcpAsync(m_cancellation.Token)).ConfigureAwait(false);

            Assert.That(failure.CancellationToken, Is.EqualTo(m_cancellation.Token));
            AssertResolution("api.example", 1);
            Assert.That(m_connections, Has.Count.EqualTo(1));
            AssertConnection(0, "203.0.113.9", 443);
            Assert.That(m_streams, Is.Empty);
        }

        [Test]
        public async Task CapturedGrantCanConnectInBackgroundWithoutAmbientCallerAuthorityAsync()
        {
            var callers = new McpCallerContext();
            var authority = new McpExecutionPolicy(new McpExecutionOptions
            {
                EnabledCapabilities = [McpCapability.WotRuntime],
                CallerGrants =
                [
                    new McpCallerGrant { Caller = s_owner, Capabilities = [McpCapability.WotRuntime] }
                ]
            }, callers, Telemetry());
            var policy = new McpNetworkPolicy(authority, [Rule()], m_dns.Object, m_connector.Object);
            McpNetworkGrant grant;
            using (callers.EnterTrustedCaller(s_owner))
            {
                grant = policy.Authorize(s_endpoint, "GET", "/device", McpCapability.WotRuntime);
                Assert.That(authority.RequireCaller(), Is.EqualTo(s_owner));
            }
            Assert.That(callers.Current, Is.Null);
            Assert.That(callers.HasRequestScope, Is.False);
            Assert.That(() => authority.RequireCaller(),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.Code)).EqualTo(StatusCodes.BadUserAccessDenied.Code));

            using Stream stream = await Task.Run(() =>
            {
                Assert.That(callers.Current, Is.Null);
                Assert.That(callers.HasRequestScope, Is.False);
                return grant.ConnectTcpAsync(m_cancellation.Token).AsTask();
            }, CancellationToken.None).ConfigureAwait(false);

            AssertGrant(grant, s_endpoint, "GET", "/device", McpCapability.WotRuntime);
            AssertResolution("api.example", 1);
            AssertConnection(0, "203.0.113.9", 443);
            Assert.That(stream, Is.SameAs(m_streams[0]));
            Assert.That(callers.Current, Is.Null);
        }

        [TestCase("RuleSlot", false)]
        [TestCase("RuleSlot", true)]
        [TestCase("Networks", false)]
        [TestCase("Networks", true)]
        [TestCase("Operations", false)]
        [TestCase("Operations", true)]
        [TestCase("Resources", false)]
        [TestCase("Resources", true)]
        public async Task RuleAndNestedArrayMutationsCannotChangeExistingAuthorityAsync(
            string mutation, bool afterGrant)
        {
            string[] networks = ["203.0.113.0/24"];
            string[] operations = ["GET"];
            string[] resources = ["/device"];
            McpNetworkRule[] rules =
            [
                Rule() with
                {
                    Networks = new ArrayOf<string>(networks.AsMemory()),
                    Operations = new ArrayOf<string>(operations.AsMemory()),
                    Resources = new ArrayOf<string>(resources.AsMemory())
                }
            ];
            var policy = new McpNetworkPolicy(
                m_authority.Object, new ArrayOf<McpNetworkRule>(rules.AsMemory()), m_dns.Object, m_connector.Object);
            McpNetworkGrant? issued = afterGrant
                ? policy.Authorize(s_endpoint, "GET", "/device", McpCapability.WotRuntime) : null;
            switch (mutation)
            {
                case "RuleSlot":
                    rules[0] = Rule() with { Host = "other.example", Port = 8443 };
                    Assert.That(rules[0].Host, Is.EqualTo("other.example"));
                    break;
                case "Networks":
                    networks[0] = "198.51.100.0/24";
                    Assert.That(rules[0].Networks[0], Is.EqualTo("198.51.100.0/24"));
                    break;
                case "Operations":
                    operations[0] = "POST";
                    Assert.That(rules[0].Operations[0], Is.EqualTo("POST"));
                    break;
                default:
                    resources[0] = "/admin";
                    Assert.That(rules[0].Resources[0], Is.EqualTo("/admin"));
                    break;
            }
            McpNetworkGrant grant = issued ??
                policy.Authorize(s_endpoint, "GET", "/device", McpCapability.WotRuntime);

            using Stream stream = await grant.ConnectTcpAsync(m_cancellation.Token).ConfigureAwait(false);

            AssertGrant(grant, s_endpoint, "GET", "/device", McpCapability.WotRuntime);
            AssertConnection(0, "203.0.113.9", 443);
            Assert.That(stream, Is.SameAs(m_streams[0]));
            AssertRefused(() => policy.Authorize(
                new Uri("https://other.example:8443/device"), "GET", "/device", McpCapability.WotRuntime));
            AssertRefused(() => policy.Authorize(s_endpoint, "POST", "/device", McpCapability.WotRuntime));
            AssertRefused(() => policy.Authorize(s_endpoint, "GET", "/admin", McpCapability.WotRuntime));
            m_addresses = [IPAddress.Parse("198.51.100.19")];
            await AssertRefusedAsync(() => grant.ConnectTcpAsync(m_cancellation.Token)).ConfigureAwait(false);
            AssertResolution("api.example", 2);
            Assert.That(m_connections, Has.Count.EqualTo(1));
        }

        [TestCase("SchemeNull", typeof(ArgumentNullException))]
        [TestCase("SchemeBlank", typeof(ArgumentException))]
        [TestCase("SchemeInvalid", typeof(ArgumentException))]
        [TestCase("HostNull", typeof(ArgumentNullException))]
        [TestCase("HostBlank", typeof(ArgumentException))]
        [TestCase("WildcardHost", typeof(ArgumentException))]
        [TestCase("NetworksEmpty", typeof(ArgumentException))]
        [TestCase("NetworksDefault", typeof(ArgumentException))]
        [TestCase("OperationsEmpty", typeof(ArgumentException))]
        [TestCase("OperationsDefault", typeof(ArgumentException))]
        [TestCase("OperationNull", typeof(ArgumentNullException))]
        [TestCase("OperationBlank", typeof(ArgumentException))]
        [TestCase("ResourceNull", typeof(ArgumentNullException))]
        [TestCase("CapabilityNegative", typeof(ArgumentException))]
        [TestCase("CapabilityBeyond", typeof(ArgumentException))]
        public void MalformedRulesFailAtConstructionWithoutNetworkAccess(string invalid, Type exceptionType)
        {
            McpNetworkRule rule = invalid switch
            {
                "SchemeNull" => Rule() with { Scheme = null! },
                "SchemeBlank" => Rule() with { Scheme = " \t " },
                "SchemeInvalid" => Rule() with { Scheme = "not a scheme" },
                "HostNull" => Rule() with { Host = null! },
                "HostBlank" => Rule() with { Host = " \t " },
                "WildcardHost" => Rule() with { Host = "*.example" },
                "NetworksEmpty" => Rule() with { Networks = [] },
                "NetworksDefault" => Rule() with { Networks = default },
                "OperationsEmpty" => Rule() with { Operations = [] },
                "OperationsDefault" => Rule() with { Operations = default },
                "OperationNull" => Rule() with { Operations = [null!] },
                "OperationBlank" => Rule() with { Operations = [" \t "] },
                "ResourceNull" => Rule() with { Resources = [null!] },
                "CapabilityNegative" => Rule() with { Capability = (McpCapability)(-1) },
                _ => Rule() with { Capability = (McpCapability)12 }
            };

            Assert.That(() => Policy(rule), Throws.TypeOf(exceptionType));

            m_authority.VerifyNoOtherCalls();
            AssertNoNetwork();
        }

        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(65536)]
        [TestCase(int.MaxValue)]
        public void RulePortsOutsideTheTcpRangeAreRejected(int port)
        {
            Assert.That(() => Policy(Rule() with { Port = port }),
                Throws.TypeOf<ArgumentOutOfRangeException>()
                    .With.Property(nameof(ArgumentOutOfRangeException.ActualValue)).EqualTo(port));

            m_authority.VerifyNoOtherCalls();
            AssertNoNetwork();
        }

        [TestCase("")]
        [TestCase("not-a-cidr")]
        [TestCase("203.0.113.0/33")]
        [TestCase("2001:db8::/129")]
        public void InvalidCidrRulesFailBeforeAuthorization(string network)
        {
            Assert.That(() => Policy(Rule() with { Networks = [network] }), Throws.TypeOf<FormatException>());

            m_authority.VerifyNoOtherCalls();
            AssertNoNetwork();
        }

        [Test]
        public void NullAuthorityAndRuleAreRejected()
        {
            Assert.That(() => new McpNetworkPolicy(null!, [], m_dns.Object, m_connector.Object),
                Throws.ArgumentNullException.With.Property(nameof(ArgumentException.ParamName)).EqualTo("authority"));
            Assert.That(() => Policy((McpNetworkRule)null!), Throws.ArgumentNullException);
            m_authority.VerifyNoOtherCalls();
            AssertNoNetwork();
        }

        [TestCase(1, true)]
        [TestCase(256, true)]
        [TestCase(257, false)]
        [TestCase(258, false)]
        public void RuleCountHasAnExactTwoHundredFiftySixRuleBoundary(int count, bool allowed)
        {
            McpNetworkRule[] rules =
                [.. Enumerable.Range(1, count).Select(index => Rule() with { Host = $"host-{index}.example" })];
            if (allowed)
            {
                McpNetworkPolicy policy = Policy(rules);
                var endpoint = new Uri($"https://host-{count}.example/device");
                McpNetworkGrant grant = policy.Authorize(endpoint, "GET", "/device", McpCapability.WotRuntime);
                AssertGrant(grant, endpoint, "GET", "/device", McpCapability.WotRuntime);
            }
            else
            {
                Assert.That(() => Policy(rules), Throws.TypeOf<ArgumentException>());
                m_authority.VerifyNoOtherCalls();
            }
            AssertNoNetwork();
        }

        private McpNetworkPolicy Policy(params McpNetworkRule[] rules)
        {
            return new McpNetworkPolicy(m_authority.Object, rules, m_dns.Object, m_connector.Object);
        }

        private static McpNetworkRule Rule()
        {
            return new McpNetworkRule
            {
                Scheme = "https",
                Host = "api.example",
                Port = 443,
                Networks = ["203.0.113.0/24"],
                Operations = ["GET"],
                Resources = ["/device"],
                Capability = McpCapability.WotRuntime
            };
        }

        private ValueTask<Stream> NewStream()
        {
            var stream = new ObservedStream();
            m_streams.Add(stream);
            return ValueTask.FromResult<Stream>(stream);
        }

        private static ITelemetryContext Telemetry()
        {
            var telemetry = new Mock<ITelemetryContext>(MockBehavior.Strict);
            telemetry.SetupGet(context => context.LoggerFactory).Returns(NullLoggerFactory.Instance);
            return telemetry.Object;
        }

        private static void AssertGrant(
            McpNetworkGrant grant, Uri endpoint, string operation, string resource, McpCapability capability)
        {
            Assert.That(grant.Owner, Is.EqualTo(new McpCallerIdentity("network-caller", "urn:network-authority")));
            Assert.That(grant.Endpoint, Is.SameAs(endpoint));
            Assert.That(grant.Operation, Is.EqualTo(operation));
            Assert.That(grant.Resource, Is.EqualTo(resource));
            Assert.That(grant.Capability, Is.EqualTo(capability));
        }

        private void AssertResolution(string host, int count)
        {
            Assert.That(m_resolutions, Has.Count.EqualTo(count));
            foreach ((string actualHost, CancellationToken token) in m_resolutions)
            {
                Assert.That(actualHost, Is.EqualTo(host));
                Assert.That(token, Is.EqualTo(m_cancellation.Token));
            }
            m_dns.Verify(resolver => resolver.ResolveAsync(host, m_cancellation.Token), Times.Exactly(count));
            m_dns.VerifyNoOtherCalls();
        }

        private void AssertConnection(int index, string address, int port)
        {
            Assert.That(m_connections, Has.Count.GreaterThan(index));
            (IPEndPoint endpoint, CancellationToken token) = m_connections[index];
            var expected = IPAddress.Parse(address);
            Assert.That(endpoint.Address.GetAddressBytes(), Is.EqualTo(expected.GetAddressBytes()));
            Assert.That(endpoint.AddressFamily, Is.EqualTo(expected.AddressFamily));
            Assert.That(endpoint.Port, Is.EqualTo(port));
            Assert.That(token, Is.EqualTo(m_cancellation.Token));
            Assert.That(token.CanBeCanceled, Is.True);
        }

        private void AssertNoNetwork()
        {
            Assert.That(m_resolutions, Is.Empty);
            m_dns.Verify(resolver => resolver.ResolveAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            AssertNoConnections();
        }

        private void AssertNoConnections()
        {
            Assert.That(m_connections, Is.Empty);
            Assert.That(m_streams, Is.Empty);
            m_connector.Verify(connector => connector.ConnectAsync(
                It.IsAny<IPEndPoint>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        private static void AssertRefused(Action action)
        {
            Assert.That(() => action(), Throws.TypeOf<ServiceResultException>()
                .With.Property(nameof(ServiceResultException.Code)).EqualTo(StatusCodes.BadUserAccessDenied.Code)
                .And.Property(nameof(Exception.Message)).EqualTo(kNetworkRefusal));
        }

        private static async Task AssertRefusedAsync(Func<ValueTask<Stream>> action)
        {
            ServiceResultException failure = await CaptureAsync<ServiceResultException>(action).ConfigureAwait(false);
            Assert.That(failure.Code, Is.EqualTo(StatusCodes.BadUserAccessDenied.Code));
            Assert.That(failure.Message, Is.EqualTo(kNetworkRefusal));
        }

        private static async Task<TException> CaptureAsync<TException>(Func<ValueTask<Stream>> action)
            where TException : Exception
        {
            TException? failure = null;
            try
            {
                using Stream unexpected = await action().ConfigureAwait(false);
            }
            catch (TException exception)
            {
                failure = exception;
            }
            Assert.That(failure, Is.InstanceOf<TException>(), $"Expected {typeof(TException).Name}.");
            return failure!;
        }

        /// <summary>
        /// Observes caller-owned stream lifetime, not the private system connector's socket cleanup.
        /// </summary>
        private sealed class ObservedStream : MemoryStream
        {
            public bool IsDisposed { get; private set; }
            public int DisposalCount { get; private set; }

            protected override void Dispose(bool disposing)
            {
                if (disposing && !IsDisposed)
                {
                    IsDisposed = true;
                    DisposalCount++;
                }
                base.Dispose(disposing);
            }
        }

        private sealed class AdapterSocketException : SocketException
        {
            public AdapterSocketException()
                : base((int)SocketError.ConnectionRefused)
            {
            }

            public AdapterSocketException(string message)
                : base((int)SocketError.ConnectionRefused, message)
            {
            }

            public AdapterSocketException(string message, Exception innerException)
                : this(message)
            {
                // SocketException has no inner-exception constructor; retain the optional diagnostic cause.
                Data[nameof(InnerException)] = innerException;
            }

            public AdapterSocketException(int errorCode)
                : base(errorCode)
            {
            }

            public AdapterSocketException(int errorCode, string? message)
                : base(errorCode, message)
            {
            }
        }

        private const string kNetworkRefusal =
            "The endpoint, operation, resource or resolved address is not allowed by host network policy.";

        private static readonly Uri s_endpoint = new("https://api.example/device");
        private static readonly McpCallerIdentity s_owner = new("network-caller", "urn:network-authority");
        private static readonly string[] s_authorityOnly = ["authority"];
        private static readonly string[] s_authorityDns = ["authority", "dns"];
        private static readonly string[] s_authorityDnsConnect = ["authority", "dns", "connect"];
        private static readonly string[] s_authorityDnsConnectConnect = ["authority", "dns", "connect", "connect"];
        private readonly List<(string Host, CancellationToken Token)> m_resolutions = [];
        private readonly List<(IPEndPoint Endpoint, CancellationToken Token)> m_connections = [];
        private readonly List<ObservedStream> m_streams = [];
        private readonly List<string> m_order = [];
        private CancellationTokenSource m_cancellation = null!;
        private Mock<IMcpOperationPolicy> m_authority = null!;
        private Mock<IMcpDnsResolver> m_dns = null!;
        private Mock<IMcpTcpConnector> m_connector = null!;
        private ArrayOf<IPAddress> m_addresses;
        private Func<string, CancellationToken, ValueTask<ArrayOf<IPAddress>>> m_resolve = null!;
        private Func<IPEndPoint, CancellationToken, ValueTask<Stream>> m_connect = null!;
    }
}
#endif
