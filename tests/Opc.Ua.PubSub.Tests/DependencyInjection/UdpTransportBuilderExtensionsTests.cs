/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.PubSub.Udp;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Tests.DependencyInjection
{
    /// <summary>
    /// Unit tests for
    /// <see cref="UdpTransportServiceCollectionExtensions"/>.
    /// </summary>
    [TestFixture]
    [TestSpec("7.3.2", Summary = "UDP transport DI registration")]
    public class UdpTransportBuilderExtensionsTests
    {
        private static (IPubSubBuilder Builder, ServiceCollection Services) CreatePubSubBuilder()
        {
            var services = new ServiceCollection();
            services.AddSingleton(NUnitTelemetryContext.Create());
            IPubSubBuilder captured = null!;
            services.AddOpcUa().AddPubSub(pubsub => captured = pubsub);
            return (captured, services);
        }

        [Test]
        public void AddUdpTransportRegistersFactoryAsSingleton()
        {
            (IPubSubBuilder builder, ServiceCollection services) = CreatePubSubBuilder();
            builder.AddUdpTransport();
            ServiceProvider sp = services.BuildServiceProvider();
            IPubSubTransportFactory[] factories =
                [.. sp.GetServices<IPubSubTransportFactory>()];
            Assert.That(
                factories.OfType<UdpPubSubTransportFactory>().Count(),
                Is.EqualTo(1));
        }

        [Test]
        public void AddUdpTransportBindsOptions()
        {
            (IPubSubBuilder builder, ServiceCollection services) = CreatePubSubBuilder();
            builder.AddUdpTransport(o => o.Ttl = 7);
            ServiceProvider sp = services.BuildServiceProvider();
            UdpTransportOptions options =
                sp.GetRequiredService<IOptions<UdpTransportOptions>>().Value;
            Assert.That(options.Ttl, Is.EqualTo(7));
        }

        [Test]
        public void AddUdpTransportNullBuilderThrows()
        {
            IPubSubBuilder? builder = null;
            Assert.That(
                () => builder!.AddUdpTransport(),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddUdpTransportNullBuilderIConfigurationOverloadThrows()
        {
            IPubSubBuilder? builder = null;
            IConfiguration cfg = new ConfigurationBuilder().Build();
            Assert.That(
                () => builder!.AddUdpTransport(cfg),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddUdpTransportNullConfigurationThrows()
        {
            (IPubSubBuilder builder, _) = CreatePubSubBuilder();
            Assert.That(
                () => builder.AddUdpTransport(configuration: null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddUdpTransportNullSectionThrows()
        {
            (IPubSubBuilder builder, _) = CreatePubSubBuilder();
            Assert.That(
                () => builder.AddUdpTransport(section: null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddUdpTransportNullBuilderIConfigurationSectionOverloadThrows()
        {
            IPubSubBuilder? builder = null;
            IConfigurationSection section = new ConfigurationBuilder()
                .AddInMemoryCollection([])
                .Build()
                .GetSection("X");
            Assert.That(
                () => builder!.AddUdpTransport(section),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddUdpTransportFromIConfigurationBindsDefaultSection()
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [$"{UdpTransportServiceCollectionExtensions.DefaultConfigurationSection}:Ttl"] = "11",
                    [$"{UdpTransportServiceCollectionExtensions.DefaultConfigurationSection}:MaxFrameSize"] = "777"
                })
                .Build();

            (IPubSubBuilder builder, ServiceCollection services) = CreatePubSubBuilder();
            builder.AddUdpTransport(configuration);

            ServiceProvider sp = services.BuildServiceProvider();
            UdpTransportOptions options =
                sp.GetRequiredService<IOptions<UdpTransportOptions>>().Value;
            Assert.Multiple(() =>
            {
                Assert.That(options.Ttl, Is.EqualTo(11));
                Assert.That(options.MaxFrameSize, Is.EqualTo(777));
            });
        }

        [Test]
        public void AddUdpTransportFromSectionBindsValues()
        {
            IConfigurationRoot root = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MyUdp:Ttl"] = "21",
                    ["MyUdp:MulticastLoopback"] = "true"
                })
                .Build();
            IConfigurationSection section = root.GetSection("MyUdp");

            (IPubSubBuilder builder, ServiceCollection services) = CreatePubSubBuilder();
            builder.AddUdpTransport(section);

            ServiceProvider sp = services.BuildServiceProvider();
            UdpTransportOptions options =
                sp.GetRequiredService<IOptions<UdpTransportOptions>>().Value;
            Assert.Multiple(() =>
            {
                Assert.That(options.Ttl, Is.EqualTo(21));
                Assert.That(options.MulticastLoopback, Is.True);
            });
        }

        [Test]
        public void AddUdpTransportTwiceDoesNotDuplicateFactory()
        {
            (IPubSubBuilder builder, ServiceCollection services) = CreatePubSubBuilder();
            builder.AddUdpTransport();
            builder.AddUdpTransport();

            ServiceProvider sp = services.BuildServiceProvider();
            IPubSubTransportFactory[] factories =
                [.. sp.GetServices<IPubSubTransportFactory>().OfType<UdpPubSubTransportFactory>()];
            Assert.That(factories, Has.Length.EqualTo(1));
        }
    }
}
