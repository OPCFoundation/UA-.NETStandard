// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings.Native;
using Opc.Ua.DataChannels;
using Opc.Ua.Native.QuicGo;

namespace Opc.Ua.Bindings.WebTransport.Tests
{
    /// <summary>
    /// Direct assertions for endpoint, capacity, hash selection and injectable policy boundaries.
    /// </summary>
    [TestFixture]
    public sealed class WebTransportPolicyTests
    {
        /// <summary>
        /// Defined generated error results survive the full-width WT error mapping without truncation.
        /// </summary>
        [Test]
        public void PeerResetMappingPreservesDefinedResults()
        {
            foreach (DataChannelResult result in Enum.GetValues<DataChannelResult>())
            {
                ulong code = (uint)result;
                ulong wire = 0x52e4a40fa8db + code + code / 30;
                Assert.That(WebTransportDataChannelConnection.MapPeerReset(wire),
                    Is.EqualTo(result == DataChannelResult.Good ? DataChannelResult.Closed : result));
            }
        }

        /// <summary>
        /// Out-of-range, reserved-hole and unknown application errors never become success or a guessed result.
        /// </summary>
        [TestCase(0UL)]
        [TestCase(0x52e4a40fa8daUL)]
        [TestCase(0x52e4a40fa8dbUL + 30)]
        [TestCase(0x52e5ac983162UL)]
        [TestCase(0x52e5ac983163UL)]
        [TestCase(ulong.MaxValue)]
        public void PeerResetMappingRejectsUnmappedValues(ulong wire)
        {
            Assert.That(WebTransportDataChannelConnection.MapPeerReset(wire), Is.EqualTo(DataChannelResult.Closed));
        }

        /// <summary>
        /// Requires a native-only canonical endpoint with explicit port/resource and no embedded credentials.
        /// </summary>
        [TestCase("https://127.0.0.1:4840/flow")]
        [TestCase("opc.quic://127.0.0.1:4840/flow")]
        [TestCase("opc.wt://127.0.0.1/flow")]
        [TestCase("opc.wt://127.0.0.1:0/flow")]
        [TestCase("opc.wt://user@127.0.0.1:4840/flow")]
        [TestCase("opc.wt://127.0.0.1:4840/flow#fragment")]
        [TestCase("opc.wt://127.0.0.1:4840/a/../flow")]
        public void EndpointRejectsForeignOrAmbiguousUri(string address)
        {
            Assert.Throws<ArgumentException>(() => WebTransportEndpoint.Address(new Uri(address), false));
        }

        /// <summary>
        /// Native management endpoints map to canonical CONNECT URLs without changing escaped resources or queries.
        /// </summary>
        [TestCase("opc.wt://example.test:443/ua/wt?resource=pubsub",
            "https://example.test/ua/wt?resource=pubsub", "example.test", 443)]
        [TestCase("opc.wt://example.test:8443/ua/a%2Fb?resource=x%20y&v=1",
            "https://example.test:8443/ua/a%2Fb?resource=x%20y&v=1", "example.test:8443", 8443)]
        [TestCase("opc.wt://[2001:db8::1]:443/ua/wt?resource=pubsub",
            "https://[2001:db8::1]/ua/wt?resource=pubsub", "[2001:db8::1]", 443)]
        [TestCase("opc.wt://127.0.0.1:4840/",
            "https://127.0.0.1:4840/", "127.0.0.1:4840", 4840)]
        public void ManagementToHttpsPreservesResourceAndCanonicalizesDefaultPort(
            string address, string expected, string authority, int port)
        {
            var native = new Uri(address);
            Uri https = WebTransportEndpoint.ToHttps(native);
            Assert.Multiple(() =>
            {
                Assert.That(https.AbsoluteUri, Is.EqualTo(expected));
                Assert.That(https.OriginalString, Is.EqualTo(expected));
                Assert.That(https.PathAndQuery, Is.EqualTo(native.PathAndQuery));
                Assert.That(https.Port, Is.EqualTo(port));
                Assert.That(WebTransportEndpoint.HttpsAddress(https), Is.EqualTo(authority));
            });
        }

        /// <summary>
        /// Port zero is listener-only, including a DNS CONNECT authority; clients require an allocated port.
        /// </summary>
        [TestCase("opc.wt://example.test:0/ua/wt?q=1", "https://example.test:0/ua/wt?q=1")]
        [TestCase("opc.wt://127.0.0.1:0/ua/wt", "https://127.0.0.1:0/ua/wt")]
        public void ManagementToHttpsAllowsEphemeralPortOnlyForListener(string address, string expected)
        {
            var endpoint = new Uri(address);
            Uri mapped = WebTransportEndpoint.ToHttps(endpoint, listen: true);
            Assert.That(mapped.AbsoluteUri, Is.EqualTo(expected));
            Assert.That(mapped.Port, Is.Zero);
            Assert.That(mapped.PathAndQuery, Is.EqualTo(endpoint.PathAndQuery));
            Assert.Throws<ArgumentException>(() => WebTransportEndpoint.ToHttps(endpoint));
        }

        /// <summary>
        /// ToHttps must validate the original native URL, not normalize invalid input into an accepted HTTPS URL.
        /// </summary>
        [TestCase("https://example.test:4840/ua/wt")]
        [TestCase("opc.quic://example.test:4840/ua/wt")]
        [TestCase("opc.wt://example.test/ua/wt")]
        [TestCase("opc.wt://user@example.test:4840/ua/wt")]
        [TestCase("opc.wt://example.test:4840/ua/wt#fragment")]
        [TestCase("opc.wt://example.test:4840/a/../ua/wt")]
        [TestCase("opc.wt://EXAMPLE.test:4840/ua/wt")]
        [TestCase("opc.wt://example.test:4840/ua/a b")]
        public void ManagementToHttpsRejectsNoncanonicalNativeInput(string address)
        {
            var endpoint = new Uri(address);
            Assert.Throws<ArgumentException>(() => WebTransportEndpoint.ToHttps(endpoint));
            Assert.Throws<ArgumentException>(() => WebTransportEndpoint.ToHttps(endpoint, listen: true));
        }

        /// <summary>
        /// Null and relative URIs fail at the native validation boundary, before URL mapping.
        /// </summary>
        [Test]
        public void ManagementToHttpsRejectsNullAndRelativeInput()
        {
            Assert.Throws<ArgumentNullException>(() => WebTransportEndpoint.ToHttps(null!));
            Assert.Throws<ArgumentException>(() => WebTransportEndpoint.ToHttps(new Uri("/ua/wt", UriKind.Relative)));
        }

        /// <summary>
        /// Accepts canonical HTTPS browser authorities and preserves the configured path and query separately.
        /// </summary>
        [TestCase("https://example.test/ua/wt?resource=pubsub", "example.test", "/ua/wt?resource=pubsub")]
        [TestCase("https://example.test:8443/ua/wt?resource=pubsub",
            "example.test:8443", "/ua/wt?resource=pubsub")]
        [TestCase("https://[2001:db8::1]:8443/ua/wt", "[2001:db8::1]:8443", "/ua/wt")]
        public void HttpsEndpointPreservesAuthorityAndResource(string address, string authority, string resource)
        {
            Uri endpoint = new(address);
            Assert.That(WebTransportEndpoint.HttpsAddress(endpoint), Is.EqualTo(authority));
            Assert.That(endpoint.PathAndQuery, Is.EqualTo(resource));
        }

        /// <summary>
        /// Rejects non-HTTPS, credential-bearing and noncanonical browser endpoint URLs.
        /// </summary>
        [TestCase("opc.wt://example.test/ua/wt")]
        [TestCase("http://example.test/ua/wt")]
        [TestCase("https://user@example.test/ua/wt")]
        [TestCase("https://example.test/ua/wt#fragment")]
        [TestCase("https://example.test/a/../ua/wt")]
        public void HttpsEndpointRejectsForeignOrAmbiguousUri(string address)
        {
            Assert.Throws<ArgumentException>(() => WebTransportEndpoint.HttpsAddress(new Uri(address)));
        }

        /// <summary>
        /// Preserves exact path/query and uses the real allocated port for ephemeral listeners.
        /// </summary>
        [Test]
        public void EndpointPreservesExactResourceAndActualBoundPort()
        {
            var endpoint = new Uri("opc.wt://127.0.0.1:0/flow/v1?resource=pubsub");
            Assert.That(WebTransportEndpoint.Address(endpoint, true), Is.EqualTo("127.0.0.1:0"));
            Uri actual = WebTransportEndpoint.Bound(endpoint, "127.0.0.1:43219");
            Assert.Multiple(() =>
            {
                Assert.That(actual.AbsoluteUri, Is.EqualTo("opc.wt://127.0.0.1:43219/flow/v1?resource=pubsub"));
                Assert.That(WebTransportEndpoint.Address(actual, false), Is.EqualTo("127.0.0.1:43219"));
            });
        }

        /// <summary>
        /// Origin matching is exact HTTPS authority matching, never wildcard/browser authentication.
        /// </summary>
        [TestCase("http://example.test")]
        [TestCase("https://example.test/")]
        [TestCase("https://example.test/path")]
        [TestCase("https://example.test?query")]
        [TestCase("https://name@example.test")]
        [TestCase("https://EXAMPLE.test")]
        [TestCase("*")]
        [TestCase(null)]
        public void OriginRejectsAmbiguousOrMissingRequiredPolicy(string? origin)
        {
            var options = new WebTransportDataChannelOptions(WebTransportOriginPolicy.RequiredExact, origin);
            Assert.Throws<ArgumentException>(options.Validate);
        }

        /// <summary>
        /// Tests each explicit Origin mode's allowed partition and forbids data in absent-only mode.
        /// </summary>
        [Test]
        public void ExplicitOriginModesEnforceTheirOwnPartitions()
        {
            Assert.DoesNotThrow(new WebTransportDataChannelOptions(WebTransportOriginPolicy.AbsentOnly).Validate);
            Assert.DoesNotThrow(new WebTransportDataChannelOptions(
                WebTransportOriginPolicy.AbsentOrExact, "https://example.test").Validate);
            Assert.DoesNotThrow(new WebTransportDataChannelOptions(
                WebTransportOriginPolicy.RequiredExact, "https://example.test:8443").Validate);
            Assert.Throws<ArgumentException>(new WebTransportDataChannelOptions(
                WebTransportOriginPolicy.AbsentOnly, "https://example.test").Validate);
            Assert.Throws<ArgumentException>(new WebTransportDataChannelOptions(
                (WebTransportOriginPolicy)3).Validate);
        }

        /// <summary>
        /// Each finite queue/native/deadline boundary is independently asserted.
        /// </summary>
        [Test]
        public void OptionsRejectInvalidLimits()
        {
            var options = new WebTransportDataChannelOptions(WebTransportOriginPolicy.AbsentOnly);
            Assert.Multiple(() =>
            {
                Assert.Throws<ArgumentOutOfRangeException>((options with { MaxRecordSize = 1023 }).Validate);
                Assert.Throws<ArgumentOutOfRangeException>((options with { MaxRecordSize = 16777217 }).Validate);
                Assert.Throws<ArgumentOutOfRangeException>((options with { MaxPendingStreams = 0 }).Validate);
                Assert.Throws<ArgumentOutOfRangeException>((options with { MaxStreamHistory = 4097 }).Validate);
                Assert.Throws<ArgumentOutOfRangeException>((options with { Http3OutputBytes = 4095 }).Validate);
                Assert.Throws<ArgumentOutOfRangeException>((options with { Http3OutputEvents = 527 }).Validate);
                Assert.Throws<ArgumentOutOfRangeException>((options with { Http3PendingSends = 7 }).Validate);
                Assert.Throws<ArgumentOutOfRangeException>((options with { InitialReceiveBytes = 0 }).Validate);
                Assert.Throws<ArgumentOutOfRangeException>(
                    (options with { InitialBidirectionalStreams = 129 }).Validate);
                Assert.Throws<ArgumentOutOfRangeException>((options with { MaxHostBufferedBytes = 1 }).Validate);
                Assert.Throws<ArgumentOutOfRangeException>((options with { ConcurrentStreamSends = 0 }).Validate);
                Assert.Throws<ArgumentOutOfRangeException>(
                    (options with { EstablishmentTimeout = TimeSpan.Zero }).Validate);
                Assert.Throws<ArgumentNullException>((options with { Native = null! }).Validate);
                Assert.Throws<ArgumentOutOfRangeException>((options with
                {
                    Native = options.Native with { MaxPendingOperations = 16 }
                }).Validate);
            });
        }

        /// <summary>
        /// The safe segmentation formula leaves space for worst-case unknown-stream header events.
        /// </summary>
        [Test]
        public void InputSegmentationRespectsExactEmptyQueueBounds()
        {
            var minimum = new WebTransportDataChannelOptions(WebTransportOriginPolicy.AbsentOnly)
            {
                Http3OutputEvents = 528,
                Http3OutputBytes = 4096
            };
            minimum.Validate();
            Assert.That(minimum.InputSegmentSize, Is.EqualTo(785));
            var adjacent = minimum with { Http3OutputEvents = 529 };
            adjacent.Validate();
            Assert.That(adjacent.InputSegmentSize, Is.EqualTo(787));
            var capped = minimum with { Http3OutputEvents = 4096 };
            capped.Validate();
            Assert.That(capped.InputSegmentSize, Is.EqualTo(3968));
        }

        /// <summary>
        /// Native loading cannot use relative search paths, absent hashes or wrong-size digests.
        /// </summary>
        [Test]
        public void NativeLibraryRejectsUntrustedSelection()
        {
            string fullPath = System.IO.Path.GetFullPath("reviewed-native-image");
            Assert.Multiple(() =>
            {
                Assert.Throws<ArgumentException>(() =>
                    new NativeHttp3Library("relative.dll", new ByteString(new byte[32])));
                Assert.Throws<ArgumentException>(() => new NativeHttp3Library(fullPath, default));
                Assert.Throws<ArgumentException>(() => new NativeHttp3Library(fullPath, new ByteString(new byte[31])));
                Assert.Throws<ArgumentException>(() => new NativeHttp3Library(fullPath, new ByteString(new byte[33])));
            });
            byte[] hash = new byte[32];
            hash[0] = 1;
            var library = new NativeHttp3Library(fullPath, new ByteString(hash));
            hash[0] = 2;
            Assert.That(library.Sha256.Span[0], Is.EqualTo(1));
        }

        /// <summary>
        /// The optional factory composes with a pre-registered Server/custom Core context factory.
        /// </summary>
        [Test]
        public void RegistrationPreservesSuppliedContextFactory()
        {
            var services = new ServiceCollection();
            var builder = new Mock<IOpcUaBuilder>();
            builder.SetupGet(value => value.Services).Returns(services);
            var factory = new Mock<IDataChannelContextFactory>();
            services.AddSingleton(factory.Object);
            services.AddSingleton(Mock.Of<INativeQuicBackend>());
            services.AddSingleton(Mock.Of<INativeQuicCredentialProvider>());
            services.AddSingleton(new DataChannelApplicationBudget());
            var library = new NativeHttp3Library(
                System.IO.Path.GetFullPath("reviewed-native-image"), new ByteString(new byte[32]));
            IOpcUaBuilder returned = builder.Object.AddWebTransportDataChannels(
                library, new WebTransportDataChannelOptions(WebTransportOriginPolicy.AbsentOnly));
            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.Multiple(() =>
            {
                Assert.That(returned, Is.SameAs(builder.Object));
                Assert.That(provider.GetRequiredService<IDataChannelContextFactory>(), Is.SameAs(factory.Object));
                Assert.That(provider.GetRequiredService<NativeHttp3Library>(), Is.SameAs(library));
                Assert.That(provider.GetRequiredService<WebTransportDataChannelTransport>(), Is.Not.Null);
                Assert.That(provider.GetService<IWebTransportConnectAuthorizer>(), Is.Null);
            });
        }

        /// <summary>
        /// The listener refuses to create a socket without an explicit CONNECT authorization provider.
        /// </summary>
        [Test]
        public async Task ListenerCannotDefaultToAllowConnectAsync()
        {
            var backend = new Mock<INativeQuicBackend>(MockBehavior.Strict);
            var transport = new WebTransportDataChannelTransport(
                backend.Object,
                new NativeHttp3Library(
                    System.IO.Path.GetFullPath("reviewed-native-image"), new ByteString(new byte[32])),
                Mock.Of<INativeQuicCredentialProvider>(), Mock.Of<IDataChannelContextFactory>(),
                new DataChannelApplicationBudget(),
                new WebTransportDataChannelOptions(WebTransportOriginPolicy.AbsentOnly));
            await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await transport.ListenAsync(new Uri("opc.wt://127.0.0.1:0/flow")).ConfigureAwait(false))
                .ConfigureAwait(false);
            backend.VerifyNoOtherCalls();
        }
    }
}
