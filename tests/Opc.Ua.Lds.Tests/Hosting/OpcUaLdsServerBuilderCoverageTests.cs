// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Opc.Ua.Lds.Server;
using Opc.Ua.Lds.Server.Hosting;

namespace Opc.Ua.Lds.Tests.Hosting
{
    /// <summary>
    /// Additional coverage for the <see cref="OpcUaLdsServerBuilderExtensions"/>
    /// dependency-injection surface: configuration-bound overloads, generic
    /// registration-store / multicast-discovery registration, one-shot
    /// transport overloads and argument-validation guards. Does not start the
    /// hosted service.
    /// </summary>
    [TestFixture]
    [Category("Hosting")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class OpcUaLdsServerBuilderCoverageTests
    {
        [Test]
        public void AddLdsServerWithConfigurationBindsOptions()
        {
            var configData = new Dictionary<string, string?>
            {
                ["OpcUa:Lds:ApplicationName"] = "BoundLds",
                ["OpcUa:Lds:ApplicationUri"] = "urn:test:bound:lds"
            };

            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(configData)
                .Build();

            var services = new ServiceCollection();
            services.AddOpcUa().AddLdsServer(configuration);

            using ServiceProvider sp = services.BuildServiceProvider();
            LdsServerOptions options = sp.GetRequiredService<IOptions<LdsServerOptions>>().Value;

            Assert.That(options.ApplicationName, Is.EqualTo("BoundLds"));
            Assert.That(options.ApplicationUri, Is.EqualTo("urn:test:bound:lds"));
        }

        [Test]
        public void AddLdsServerWithConfigurationSectionBindsOptions()
        {
            var configData = new Dictionary<string, string?>
            {
                ["OpcUa:Lds:ApplicationName"] = "SectionLds"
            };

            IConfigurationSection section = new ConfigurationBuilder()
                .AddInMemoryCollection(configData)
                .Build()
                .GetSection(OpcUaLdsServerBuilderExtensions.DefaultConfigurationSection);

            var services = new ServiceCollection();
            services.AddOpcUa().AddLdsServer(section);

            using ServiceProvider sp = services.BuildServiceProvider();
            LdsServerOptions options = sp.GetRequiredService<IOptions<LdsServerOptions>>().Value;

            Assert.That(options.ApplicationName, Is.EqualTo("SectionLds"));
        }

        [Test]
        public void AddLdsServerConfigurationOverloadsThrowForNullArgs()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();

            Assert.That(
                () => builder.AddLdsServer((IConfiguration)null!),
                Throws.ArgumentNullException);
            Assert.That(
                () => builder.AddLdsServer((IConfigurationSection)null!),
                Throws.ArgumentNullException);
            Assert.That(
                () => OpcUaLdsServerBuilderExtensions.AddLdsServer(
                    null!, new ConfigurationBuilder().Build().GetSection("OpcUa")),
                Throws.ArgumentNullException);
        }

        [Test]
        public void LdsBuilderRegistersGenericRegistrationStore()
        {
            var services = new ServiceCollection();
            services.AddOpcUa()
                .AddLdsServer(opt =>
                {
                    opt.ApplicationUri = "urn:localhost:UA:TestLds";
                    opt.ProductUri = "uri:opcfoundation.org:TestLds";
                })
                .AddRegistrationStore<RegisteredServerStore>();

            using ServiceProvider sp = services.BuildServiceProvider();

            Assert.That(
                sp.GetRequiredService<IRegisteredServerStore>(),
                Is.SameAs(sp.GetRequiredService<RegisteredServerStore>()));
        }

        [Test]
        public void LdsBuilderRegistersGenericMulticastFactory()
        {
            var services = new ServiceCollection();
            services.AddOpcUa()
                .AddLdsServer(opt =>
                {
                    opt.ApplicationUri = "urn:localhost:UA:TestLds";
                    opt.ProductUri = "uri:opcfoundation.org:TestLds";
                })
                .AddMulticastDiscovery<TestLdsMulticastDiscoveryFactory>();

            using ServiceProvider sp = services.BuildServiceProvider();

            Assert.That(
                sp.GetRequiredService<ILdsMulticastDiscoveryFactory>(),
                Is.InstanceOf<TestLdsMulticastDiscoveryFactory>());
        }

        [Test]
        public void LdsBuilderRegistersMulticastFactoryDelegateInstance()
        {
            var factory = new TestLdsMulticastDiscoveryFactory();
            var services = new ServiceCollection();
            services.AddOpcUa()
                .AddLdsServer(opt =>
                {
                    opt.ApplicationUri = "urn:localhost:UA:TestLds";
                    opt.ProductUri = "uri:opcfoundation.org:TestLds";
                })
                .AddMulticastDiscovery(_ => factory);

            using ServiceProvider sp = services.BuildServiceProvider();

            Assert.That(sp.GetRequiredService<ILdsMulticastDiscoveryFactory>(), Is.SameAs(factory));
        }

        [Test]
        public void LdsBuilderStoreAndDiscoveryFactoryOverloadsThrowForNullDelegate()
        {
            var services = new ServiceCollection();
            ILdsServerBuilder builder = services.AddOpcUa()
                .AddLdsServer(opt =>
                {
                    opt.ApplicationUri = "urn:localhost:UA:TestLds";
                    opt.ProductUri = "uri:opcfoundation.org:TestLds";
                });

            Assert.That(
                () => builder.AddRegistrationStore(null!),
                Throws.ArgumentNullException);
            Assert.That(
                () => builder.AddMulticastDiscovery(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void LdsHttpsAndWssOneShotOverloadsReturnSameBuilder()
        {
            var services = new ServiceCollection();
            ILdsServerBuilder builder = services.AddOpcUa()
                .AddLdsServer(opt =>
                {
                    opt.ApplicationUri = "urn:localhost:UA:TestLds";
                    opt.ProductUri = "uri:opcfoundation.org:TestLds";
                });

            Assert.That(builder.AddHttpsTransport(_ => { }), Is.SameAs(builder));
            Assert.That(builder.AddWssTransport(_ => { }), Is.SameAs(builder));
        }

        [Test]
        public void LdsTransportForwardersThrowForNullBuilder()
        {
            Assert.That(
                () => OpcUaLdsServerBuilderExtensions.AddOpcTcpTransport(null!),
                Throws.ArgumentNullException);
            Assert.That(
                () => OpcUaLdsServerBuilderExtensions.AddHttpsTransport(null!),
                Throws.ArgumentNullException);
            Assert.That(
                () => OpcUaLdsServerBuilderExtensions.AddHttpsTransport(null!, _ => { }),
                Throws.ArgumentNullException);
            Assert.That(
                () => OpcUaLdsServerBuilderExtensions.AddWssTransport(null!),
                Throws.ArgumentNullException);
            Assert.That(
                () => OpcUaLdsServerBuilderExtensions.AddWssTransport(null!, _ => { }),
                Throws.ArgumentNullException);
            Assert.That(
                () => OpcUaLdsServerBuilderExtensions.AddReverseConnect(null!, _ => { }),
                Throws.ArgumentNullException);
        }

        [Test]
        public void LdsReverseConnectThrowsForNullConfigure()
        {
            var services = new ServiceCollection();
            ILdsServerBuilder builder = services.AddOpcUa()
                .AddLdsServer(opt =>
                {
                    opt.ApplicationUri = "urn:localhost:UA:TestLds";
                    opt.ProductUri = "uri:opcfoundation.org:TestLds";
                });

            Assert.That(
                () => builder.AddReverseConnect(null!),
                Throws.ArgumentNullException);
        }

        private sealed class TestLdsMulticastDiscoveryFactory : ILdsMulticastDiscoveryFactory
        {
            public IMulticastDiscovery Create(LdsServer server)
            {
                throw new NotSupportedException();
            }
        }
    }
}
