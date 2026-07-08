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

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

#nullable enable

using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Opc.Ua.Configuration;
using Opc.Ua.Redundancy.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Tests for redundant server-set discovery integration.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public sealed class RedundancyDiscoveryTests
    {
        [Test]
        public void AddServerRedundancyAddsNtrsCapabilityForNonTransparentMode()
        {
            OpcUaServerOptions options = CreateOptionsWithRedundancy(RedundancySupport.Hot);
            var configurationBuilder = new Mock<IApplicationConfigurationBuilderServerSelected>();
            configurationBuilder
                .Setup(builder => builder.AddServerCapabilities("NTRS"))
                .Returns(configurationBuilder.Object);

            options.ConfigureBuilder!(configurationBuilder.Object);

            configurationBuilder.Verify(builder => builder.AddServerCapabilities("NTRS"), Times.Once);
        }

        [Test]
        public void AddServerRedundancyDoesNotAddNtrsCapabilityForTransparentOrNone()
        {
            foreach (RedundancySupport mode in new[] { RedundancySupport.None, RedundancySupport.Transparent })
            {
                OpcUaServerOptions options = CreateOptionsWithRedundancy(mode);
                var configurationBuilder = new Mock<IApplicationConfigurationBuilderServerSelected>();

                options.ConfigureBuilder!(configurationBuilder.Object);

                configurationBuilder.Verify(
                    builder => builder.AddServerCapabilities("NTRS"),
                    Times.Never);
            }
        }

        [Test]
        public async Task FindServersReturnsRedundantPeerDescriptionsAsync()
        {
            using var server = new TestStandardServer();
            server.RedundantServerSetProvider = CreateProvider();

            FindServersResponse response = await server.FindServersAsync(
                null!,
                new RequestHeader(),
                null,
                [],
                [],
                RequestLifetime.None).ConfigureAwait(false);

            ApplicationDescription[] servers = response.Servers.Memory.ToArray();
            Assert.That(servers.Select(description => description.ApplicationUri),
                Is.EqualTo(["urn:local", "urn:peer-a", "urn:peer-b"]));
            ApplicationDescription peer = servers.Single(
                description => description.ApplicationUri == "urn:peer-a");
            Assert.That(peer.DiscoveryUrls, Is.EqualTo(["opc.tcp://peer-a:4840"]));
        }

        [Test]
        public async Task FindServersWithoutProviderReturnsOnlyLocalDescriptionAsync()
        {
            using var server = new TestStandardServer();

            FindServersResponse response = await server.FindServersAsync(
                null!,
                new RequestHeader(),
                null,
                [],
                [],
                RequestLifetime.None).ConfigureAwait(false);

            Assert.That(response.Servers.Memory.ToArray().Select(description => description.ApplicationUri),
                Is.EqualTo(["urn:local"]));
        }

        [Test]
        public async Task FindServersFiltersRedundantPeerDescriptionsByServerUriAsync()
        {
            using var server = new TestStandardServer();
            server.RedundantServerSetProvider = CreateProvider();

            FindServersResponse response = await server.FindServersAsync(
                null!,
                new RequestHeader(),
                null,
                [],
                ["urn:peer-b"],
                RequestLifetime.None).ConfigureAwait(false);

            Assert.That(response.Servers.Memory.ToArray().Select(description => description.ApplicationUri),
                Is.EqualTo(["urn:peer-b"]));
        }

        [Test]
        public void ProviderReturnsEmptyForTransparentAndNoneModes()
        {
            foreach (RedundancySupport mode in new[] { RedundancySupport.None, RedundancySupport.Transparent })
            {
                var options = new ServerRedundancyOptions
                {
                    Mode = mode
                };
                options.AddRedundantPeer("urn:peer-a", ["opc.tcp://peer-a:4840"]);
                var provider = new ConfiguredRedundantServerSetProvider(options);

                Assert.That(provider.GetRedundantServerSet(), Is.Empty);
            }
        }

        [Test]
        public void ProviderSkipsPeersWithoutApplicationUriOrDiscoveryUrls()
        {
            var options = new ServerRedundancyOptions
            {
                Mode = RedundancySupport.Hot
            };
            options.RedundantPeers.Add(new RedundantPeer(string.Empty, ["opc.tcp://missing-uri:4840"]));
            options.RedundantPeers.Add(new RedundantPeer
            {
                ApplicationUri = "urn:missing-urls",
                DiscoveryUrls = default
            });
            options.AddRedundantPeer("urn:peer-a", ["opc.tcp://peer-a:4840"]);
            var provider = new ConfiguredRedundantServerSetProvider(options);

            Assert.That(
                provider.GetRedundantServerSet().Memory.ToArray().Select(description => description.ApplicationUri),
                Is.EqualTo(["urn:peer-a"]));
        }

        [Test]
        public async Task FindServersDeduplicatesPeerAlreadyPresentLocallyAsync()
        {
            using var server = new TestStandardServer();
            server.RedundantServerSetProvider = CreateProvider(new ApplicationDescription
            {
                ApplicationUri = "urn:local",
                DiscoveryUrls = ["opc.tcp://peer-local:4840"]
            });

            FindServersResponse response = await server.FindServersAsync(
                null!,
                new RequestHeader(),
                null,
                [],
                [],
                RequestLifetime.None).ConfigureAwait(false);

            Assert.That(
                response.Servers.Memory.ToArray().Select(description => description.ApplicationUri),
                Is.EqualTo(["urn:local", "urn:peer-a", "urn:peer-b"]));
        }

        [Test]
        public async Task GetEndpointsReturnsDirectedEndpointsWhenDirectorRedirectsAsync()
        {
            using var server = new TestStandardServer();
            server.GetEndpointsDirector = new StubGetEndpointsDirector(
                redirect: true,
                [
                    new EndpointDescription
                    {
                        EndpointUrl = "opc.tcp://peer-a:4840",
                        Server = new ApplicationDescription
                        {
                            ApplicationUri = "urn:peer-a"
                        }
                    }
                ]);

            GetEndpointsResponse response = await server.GetEndpointsAsync(
                new SecureChannelContext("directed", new EndpointDescription(), RequestEncoding.Binary),
                new RequestHeader(),
                null,
                [],
                [],
                RequestLifetime.None).ConfigureAwait(false);

            Assert.That(response.Endpoints.Count, Is.EqualTo(1));
            Assert.That(response.Endpoints[0].EndpointUrl, Is.EqualTo("opc.tcp://peer-a:4840"));
        }

        [Test]
        public async Task GetEndpointsReturnsLocalEndpointsWhenDirectorDoesNotRedirectAsync()
        {
            using var server = new TestStandardServer();
            server.GetEndpointsDirector = new StubGetEndpointsDirector(
                redirect: false,
                [
                    new EndpointDescription
                    {
                        EndpointUrl = "opc.tcp://peer-a:4840",
                        Server = new ApplicationDescription
                        {
                            ApplicationUri = "urn:peer-a"
                        }
                    }
                ]);

            GetEndpointsResponse response = await server.GetEndpointsAsync(
                new SecureChannelContext("local", new EndpointDescription(), RequestEncoding.Binary),
                new RequestHeader(),
                null,
                [],
                [],
                RequestLifetime.None).ConfigureAwait(false);

            Assert.That(response.Endpoints.Count, Is.EqualTo(1));
            Assert.That(response.Endpoints[0].EndpointUrl, Is.EqualTo("opc.tcp://localhost:4840/"));
        }

        private static OpcUaServerOptions CreateOptionsWithRedundancy(RedundancySupport mode)
        {
            var services = new ServiceCollection();
            var builder = new TestServerBuilder(services);
            builder.AddServerRedundancy(options => options.Mode = mode);
            using ServiceProvider provider = services.BuildServiceProvider();
            return provider.GetRequiredService<IOptions<OpcUaServerOptions>>().Value;
        }

        private static ConfiguredRedundantServerSetProvider CreateProvider(params ApplicationDescription[] extraPeers)
        {
            var options = new ServerRedundancyOptions
            {
                Mode = RedundancySupport.Hot
            };
            options.AddRedundantPeer("urn:peer-a", ["opc.tcp://peer-a:4840"]);
            options.AddRedundantPeer("urn:peer-b", ["opc.tcp://peer-b:4840"]);
            foreach (ApplicationDescription peer in extraPeers)
            {
                options.RedundantPeers.Add(new RedundantPeer
                {
                    ApplicationUri = peer.ApplicationUri ?? string.Empty,
                    ApplicationName = peer.ApplicationName,
                    DiscoveryUrls = peer.DiscoveryUrls
                });
            }
            return new ConfiguredRedundantServerSetProvider(options);
        }

        private sealed class TestServerBuilder : IOpcUaServerBuilder
        {
            public TestServerBuilder(IServiceCollection services)
            {
                Services = services;
            }

            public IServiceCollection Services { get; }

            public IOpcUaServerBuilder AddNodeManager<TFactory>()
                where TFactory : class, IAsyncNodeManagerFactory
            {
                return this;
            }

            public IOpcUaServerBuilder AddSyncNodeManager<TFactory>()
                where TFactory : class, INodeManagerFactory
            {
                return this;
            }

            public IOpcUaServerBuilder AddNodeManager(
                string namespaceUri,
                Action<Opc.Ua.Server.Fluent.INodeManagerBuilder> build)
            {
                return this;
            }
        }

        private sealed class TestStandardServer : StandardServer
        {
            public TestStandardServer()
                : base(NUnitTelemetryContext.Create())
            {
                var localDescription = new ApplicationDescription
                {
                    ApplicationUri = "urn:local",
                    ApplicationName = new LocalizedText("Local"),
                    ApplicationType = ApplicationType.Server,
                    DiscoveryUrls = ["opc.tcp://localhost:4840"]
                };
                typeof(global::Opc.Ua.ServerBase)
                    .GetProperty("ServerDescription", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .SetValue(this, localDescription);
                EndpointDescription[] localEndpoints =
                [
                    new EndpointDescription
                    {
                        EndpointUrl = "opc.tcp://localhost:4840",
                        Server = localDescription
                    }
                ];
                typeof(global::Opc.Ua.ServerBase)
                    .GetProperty("Endpoints", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .SetValue(
                        this,
                        new ArrayOf<EndpointDescription>(localEndpoints));
                BaseAddresses =
                [
                    new BaseAddress
                    {
                        Url = new Uri("opc.tcp://localhost:4840"),
                        DiscoveryUrl = new Uri("opc.tcp://localhost:4840")
                    }
                ];
                m_endpoints = new ArrayOf<EndpointDescription>(localEndpoints);
            }

            public override ArrayOf<EndpointDescription> GetEndpoints()
            {
                return m_endpoints;
            }

            protected override void ValidateRequest([NotNull] RequestHeader? requestHeader)
            {
                if (requestHeader == null)
                {
                    throw new ServiceResultException(StatusCodes.BadRequestHeaderInvalid);
                }
            }

            private readonly ArrayOf<EndpointDescription> m_endpoints;
        }

        private sealed class StubGetEndpointsDirector : IGetEndpointsDirector
        {
            public StubGetEndpointsDirector(bool redirect, ArrayOf<EndpointDescription> endpoints)
            {
                m_redirect = redirect;
                m_endpoints = endpoints;
            }

            public ValueTask<(bool Redirect, ArrayOf<EndpointDescription> Endpoints)> TryGetDirectedEndpointsAsync(
                string? endpointUrl,
                ArrayOf<EndpointDescription> localEndpoints,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<(bool Redirect, ArrayOf<EndpointDescription> Endpoints)>(
                    (m_redirect, m_endpoints));
            }

            private readonly bool m_redirect;
            private readonly ArrayOf<EndpointDescription> m_endpoints;
        }
    }
}
