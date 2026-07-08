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

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Eth.Channels;
using Opc.Ua.PubSub.Tests;
using Opc.Ua.PubSub.Transports;

namespace Opc.Ua.PubSub.Eth.Tests
{
    /// <summary>
    /// Validates the Ethernet transport DI registration extensions,
    /// including the SharpPcap <c>WithPcap()</c> backend swap.
    /// </summary>
    [TestFixture]
    [Category("Unit")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [TestSpec("7.3.3", Summary = "Ethernet transport DI binding")]
    public sealed class EthTransportServiceCollectionExtensionsTests
    {
        [Test]
        public async Task AddEthTransportRegistersFactoryAndDefaultChannelFactory()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddPubSub(pubsub => pubsub.AddEthTransport());

            await using ServiceProvider serviceProvider = services.BuildServiceProvider();
            IPubSubTransportFactory[] factories =
                [.. serviceProvider.GetServices<IPubSubTransportFactory>()];
            IEthernetFrameChannelFactory channelFactory =
                serviceProvider.GetRequiredService<IEthernetFrameChannelFactory>();

            Assert.Multiple(() =>
            {
                Assert.That(
                    factories.Any(f => f is EthPubSubTransportFactory),
                    Is.True);
                Assert.That(channelFactory, Is.InstanceOf<DefaultEthernetFrameChannelFactory>());
            });
        }

        [Test]
        public async Task AddEthPubSubRegistersPublisherSubscriberAndEthTransportAsync()
        {
            var services = new ServiceCollection();
            bool configured = false;

            services.AddEthPubSub(eth =>
            {
                configured = true;
                eth.Services.Configure<EthTransportOptions>(options =>
                    options.PreferredNetworkInterface = "eth0");
            });

            await using ServiceProvider serviceProvider = services.BuildServiceProvider();
            EthTransportOptions options =
                serviceProvider.GetRequiredService<IOptions<EthTransportOptions>>().Value;
            IPubSubTransportFactory[] factories =
                serviceProvider.GetServices<IPubSubTransportFactory>().ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(configured, Is.True);
                Assert.That(serviceProvider.GetService<IPubSubApplication>(), Is.Not.Null);
                Assert.That(factories.OfType<EthPubSubTransportFactory>().Count(), Is.EqualTo(1));
                Assert.That(options.PreferredNetworkInterface, Is.EqualTo("eth0"));
            });
        }

        [Test]
        public async Task AddEthTransportConfigurationBindsOptions()
        {
            var services = new ServiceCollection();
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["OpcUa:PubSub:Eth:ReceiveQueueCapacity"] = "9",
                    ["OpcUa:PubSub:Eth:MaxFrameSize"] = "2048",
                    ["OpcUa:PubSub:Eth:PreferredNetworkInterface"] = "eth9",
                    ["OpcUa:PubSub:Eth:DefaultVlanId"] = "7",
                    ["OpcUa:PubSub:Eth:DefaultPriority"] = "3",
                    ["OpcUa:PubSub:Eth:Promiscuous"] = "true",
                    ["OpcUa:PubSub:Eth:DiscoveryAnnounceRate"] = "1500",
                    ["OpcUa:PubSub:Eth:DiscoveryMulticastAddress"] = "01-1B-19-00-00-00"
                })
                .Build();

            services.AddOpcUa().AddPubSub(pubsub => pubsub.AddEthTransport(configuration));

            await using ServiceProvider serviceProvider = services.BuildServiceProvider();
            EthTransportOptions options =
                serviceProvider.GetRequiredService<IOptions<EthTransportOptions>>().Value;

            Assert.Multiple(() =>
            {
                Assert.That(options.ReceiveQueueCapacity, Is.EqualTo(9));
                Assert.That(options.MaxFrameSize, Is.EqualTo(2048));
                Assert.That(options.PreferredNetworkInterface, Is.EqualTo("eth9"));
                Assert.That(options.DefaultVlanId, Is.EqualTo((ushort)7));
                Assert.That(options.DefaultPriority, Is.EqualTo((byte)3));
                Assert.That(options.Promiscuous, Is.True);
                Assert.That(options.DiscoveryAnnounceRate, Is.EqualTo(1500u));
                Assert.That(options.DiscoveryMulticastAddress, Is.EqualTo("01-1B-19-00-00-00"));
            });
        }

#if ETH_PCAP
        [Test]
        public async Task WithPcapReplacesChannelFactory()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddPubSub(pubsub => pubsub.AddEthTransport().WithPcap());

            await using ServiceProvider serviceProvider = services.BuildServiceProvider();
            IEthernetFrameChannelFactory channelFactory =
                serviceProvider.GetRequiredService<IEthernetFrameChannelFactory>();

            Assert.That(
                channelFactory,
                Is.InstanceOf<PcapEthernetFrameChannelFactory>());
        }
#endif
    }
}
