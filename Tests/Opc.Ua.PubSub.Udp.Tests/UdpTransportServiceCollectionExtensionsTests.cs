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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Security.Sks;
using Opc.Ua.PubSub.Tests;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.PubSub.Udp.Dtls;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Udp.Tests
{
    [TestFixture]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [TestSpec("7.3.2", Summary = "UDP transport DI binding")]
    public sealed class UdpTransportServiceCollectionExtensionsTests
    {
        [Test]
        public async Task AddUdpTransport_IConfiguration_BindsOptionsAndRegistersFactoryAsync()
        {
            var services = new ServiceCollection();
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["OpcUa:PubSub:Udp:SendBufferSize"] = "16384",
                    ["OpcUa:PubSub:Udp:ReceiveBufferSize"] = "32768",
                    ["OpcUa:PubSub:Udp:ReceiveQueueCapacity"] = "9",
                    ["OpcUa:PubSub:Udp:Ttl"] = "5",
                    ["OpcUa:PubSub:Udp:MulticastLoopback"] = "true",
                    ["OpcUa:PubSub:Udp:MaxFrameSize"] = "2048",
                    ["OpcUa:PubSub:Udp:MessageRepeatCount"] = "3",
                    ["OpcUa:PubSub:Udp:MessageRepeatDelay"] = "00:00:00.050",
                    ["OpcUa:PubSub:Udp:PreferredNetworkInterface"] = "Loopback Adapter"
                })
                .Build();

            services.AddOpcUa().AddPubSub(pubsub => pubsub.AddUdpTransport(configuration));

            await using ServiceProvider serviceProvider = services.BuildServiceProvider();
            UdpTransportOptions options =
                serviceProvider.GetRequiredService<IOptions<UdpTransportOptions>>().Value;
            IPubSubTransportFactory[] factories =
                serviceProvider.GetServices<IPubSubTransportFactory>().ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(options.SendBufferSize, Is.EqualTo(16384));
                Assert.That(options.ReceiveBufferSize, Is.EqualTo(32768));
                Assert.That(options.ReceiveQueueCapacity, Is.EqualTo(9));
                Assert.That(options.Ttl, Is.EqualTo(5));
                Assert.That(options.MulticastLoopback, Is.True);
                Assert.That(options.MaxFrameSize, Is.EqualTo(2048));
                Assert.That(options.MessageRepeatCount, Is.EqualTo(3));
                Assert.That(options.MessageRepeatDelay, Is.EqualTo(TimeSpan.FromMilliseconds(50)));
                Assert.That(options.PreferredNetworkInterface, Is.EqualTo("Loopback Adapter"));
                Assert.That(factories, Has.Length.EqualTo(1));
                Assert.That(factories[0], Is.InstanceOf<UdpPubSubTransportFactory>());
            });
        }

        [Test]
        [TestSpec("7.3.2.4")]
        public async Task AddUdpTransportReturnsUdpBuilderAndWithDtlsRegistersOptionsRegistryAndFactoryAsync()
        {
            var services = new ServiceCollection();
            IUdpTransportBuilder? udpBuilder = null;

            services.AddOpcUa().AddPubSub(pubsub =>
            {
                udpBuilder = pubsub.AddUdpTransport();
                udpBuilder.WithDtls(options => options.PreferredProfileName = "ECC_nistP256");
            });

            await using ServiceProvider serviceProvider = services.BuildServiceProvider();
            DtlsTransportOptions options =
                serviceProvider.GetRequiredService<IOptions<DtlsTransportOptions>>().Value;

            Assert.Multiple(() =>
            {
                Assert.That(udpBuilder, Is.Not.Null);
                Assert.That(udpBuilder, Is.InstanceOf<IUdpTransportBuilder>());
                Assert.That(options.PreferredProfileName, Is.EqualTo("ECC_nistP256"));
                Assert.That(serviceProvider.GetRequiredService<DtlsProfileRegistry>(), Is.Not.Null);
                Assert.That(serviceProvider.GetRequiredService<IDtlsContextFactory>(),
                    Is.InstanceOf<DefaultDtlsContextFactory>());
            });
        }

        [Test]
        public async Task WithDtlsIConfigurationBindsOptionsAsync()
        {
            var services = new ServiceCollection();
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["OpcUa:PubSub:Udp:PreferredProfileName"] = "ECC_nistP384"
                })
                .Build();

            services.AddOpcUa().AddPubSub(pubsub =>
                pubsub.AddUdpTransport().WithDtls(configuration));

            await using ServiceProvider serviceProvider = services.BuildServiceProvider();
            DtlsTransportOptions options =
                serviceProvider.GetRequiredService<IOptions<DtlsTransportOptions>>().Value;

            Assert.That(options.PreferredProfileName, Is.EqualTo("ECC_nistP384"));
        }

        [Test]
        public async Task AddUdpTransport_IConfigurationSection_BindsExplicitSectionAsync()
        {
            var services = new ServiceCollection();
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["UdpSection:Ttl"] = "2",
                    ["UdpSection:PreferredNetworkInterface"] = "Ethernet 0"
                })
                .Build();

            services.AddOpcUa().AddPubSub(pubsub =>
                pubsub.AddUdpTransport(configuration.GetSection("UdpSection")));

            await using ServiceProvider serviceProvider = services.BuildServiceProvider();
            UdpTransportOptions options =
                serviceProvider.GetRequiredService<IOptions<UdpTransportOptions>>().Value;

            Assert.Multiple(() =>
            {
                Assert.That(options.Ttl, Is.EqualTo(2));
                Assert.That(options.PreferredNetworkInterface, Is.EqualTo("Ethernet 0"));
            });
        }

        [Test]
        public async Task AddUdpPubSubRegistersPublisherSubscriberAndUdpTransportAsync()
        {
            var services = new ServiceCollection();
            bool configured = false;

            services.AddUdpPubSub(udp =>
            {
                configured = true;
                udp.WithDtls(options => options.PreferredProfileName = "ECC_nistP256");
            });

            await using ServiceProvider serviceProvider = services.BuildServiceProvider();
            IPubSubTransportFactory[] factories =
                serviceProvider.GetServices<IPubSubTransportFactory>().ToArray();
            DtlsTransportOptions dtlsOptions =
                serviceProvider.GetRequiredService<IOptions<DtlsTransportOptions>>().Value;

            Assert.Multiple(() =>
            {
                Assert.That(configured, Is.True);
                Assert.That(serviceProvider.GetService<IPubSubApplication>(), Is.Not.Null);
                Assert.That(factories.OfType<UdpPubSubTransportFactory>().Count(), Is.EqualTo(1));
                Assert.That(dtlsOptions.PreferredProfileName, Is.EqualTo("ECC_nistP256"));
            });
        }

        [Test]
        public async Task AddUdpPubSubWithSecurityKeyProviderBuildsSecuredApplicationAsync()
        {
            var services = new ServiceCollection();
            services.AddSingleton(NUnitTelemetryContext.Create());

            services.AddOpcUa().AddUdpPubSub(udp => udp
                .UseConfiguration(CreateSecuredConfiguration())
                .AddSecurityKeyProvider(new PushSecurityKeyProvider(
                    "group-1",
                    NUnitTelemetryContext.Create(),
                    TimeProvider.System)));

            await using ServiceProvider serviceProvider = services.BuildServiceProvider();
            IPubSubApplication app = serviceProvider.GetRequiredService<IPubSubApplication>();

            Assert.That(app.Connections, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task AddSecureUdpPubSubBuildsSecuredApplicationAsync()
        {
            var services = new ServiceCollection();
            services.AddSingleton(NUnitTelemetryContext.Create());

            services.AddSecureUdpPubSub(
                "group-1",
                PubSubSecurityPolicyUri.PubSubAes256Ctr,
                _ => new NoOpSecurityKeyService());

            await using ServiceProvider serviceProvider = services.BuildServiceProvider();
            IPubSubApplication app = serviceProvider.GetRequiredService<IPubSubApplication>();

            Assert.Multiple(() =>
            {
                Assert.That(app.Connections, Has.Count.EqualTo(1));
                Assert.That(
                    serviceProvider.GetServices<IPubSubSecurityKeyProvider>()
                        .Single().SecurityGroupId,
                    Is.EqualTo("group-1"));
            });
        }

        [Test]
        public async Task AddUdpPubSubOnOpcUaBuilderReturnsOpcUaBuilderAsync()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();

            IOpcUaBuilder returned = builder.AddUdpPubSub();

            await using ServiceProvider serviceProvider = services.BuildServiceProvider();
            Assert.Multiple(() =>
            {
                Assert.That(returned, Is.SameAs(builder));
                Assert.That(
                    serviceProvider.GetServices<IPubSubTransportFactory>().OfType<UdpPubSubTransportFactory>(),
                    Is.Not.Empty);
            });
        }

        private static PubSubConfigurationDataType CreateSecuredConfiguration()
        {
            return new PubSubConfigurationDataType
            {
                Connections =
                [
                    new PubSubConnectionDataType
                    {
                        Name = "secured-conn",
                        TransportProfileUri = Profiles.PubSubUdpUadpTransport,
                        PublisherId = new Variant((ushort)7),
                        Address = new ExtensionObject(
                            new NetworkAddressUrlDataType
                            {
                                Url = "opc.udp://224.0.0.22:4840"
                            }),
                        WriterGroups =
                        [
                            new WriterGroupDataType
                            {
                                Name = "wg",
                                WriterGroupId = 1,
                                PublishingInterval = 1000,
                                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                                SecurityGroupId = "group-1",
                                SecurityKeyServices =
                                [
                                    new EndpointDescription
                                    {
                                        EndpointUrl = "opc.tcp://localhost:4840"
                                    }
                                ]
                            }
                        ]
                    }
                ],
                PublishedDataSets = []
            };
        }

        private sealed class NoOpSecurityKeyService : ISecurityKeyService
        {
            public event EventHandler<SksAvailabilityChangedEventArgs>? AvailabilityChanged
            {
                add { }
                remove { }
            }

            public ValueTask<SksKeyResponse> GetSecurityKeysAsync(
                SksKeyRequest request,
                CancellationToken cancellationToken = default)
            {
                throw new InvalidOperationException("The one-shot DI test does not start the SKS pull loop.");
            }
        }
    }
}
