// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.ISA95.Server;
using Opc.Ua.ISA95.Server.Providers;

namespace Opc.Ua.ISA95.Tests.Providers
{
    [TestFixture]
    public class Isa95JobControlProviderOptionsTests
    {
        [Test]
        public void ValidateAcceptsDefaults()
        {
            var options = new Isa95JobControlProviderOptions();

            Assert.That(options.Validate, Throws.Nothing);
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void ConstructorRejectsInvalidMaxJobOrders(int value)
        {
            Assert.That(
                () => _ = new InMemoryIsa95JobControlProvider(
                    new Isa95JobControlProviderOptions { MaxJobOrders = value }),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void ConstructorRejectsInvalidMaxJobResponses(int value)
        {
            Assert.That(
                () => _ = new InMemoryIsa95JobControlProvider(
                    new Isa95JobControlProviderOptions { MaxJobResponses = value }),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void ConstructorRejectsNegativeRetention()
        {
            Assert.That(
                () => _ = new InMemoryIsa95JobControlProvider(
                    new Isa95JobControlProviderOptions
                    {
                        ResponseRetention = TimeSpan.FromSeconds(-1)
                    }),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void DependencyInjectionRegistersEveryFacetAsTheSameSingleton()
        {
            var services = new ServiceCollection();
            services.AddInMemoryIsa95JobControlProvider();

            using ServiceProvider provider = services.BuildServiceProvider();
            InMemoryIsa95JobControlProvider engine = provider.GetRequiredService<InMemoryIsa95JobControlProvider>();

            Assert.Multiple(() =>
            {
                Assert.That(provider.GetRequiredService<IIsa95JobOrderReceiverV1>(), Is.SameAs(engine));
                Assert.That(provider.GetRequiredService<IIsa95JobResponseProviderV1>(), Is.SameAs(engine));
                Assert.That(provider.GetRequiredService<IIsa95JobResponseReceiverV1>(), Is.SameAs(engine));
                Assert.That(provider.GetRequiredService<IIsa95JobOrderReceiverV2>(), Is.SameAs(engine));
                Assert.That(provider.GetRequiredService<IIsa95JobResponseProviderV2>(), Is.SameAs(engine));
                Assert.That(provider.GetRequiredService<IIsa95JobResponseReceiverV2>(), Is.SameAs(engine));
                Assert.That(provider.GetRequiredService<IIsa95JobStatusSourceV2>(), Is.SameAs(engine));
                Assert.That(provider.GetRequiredService<IIsa95JobExecutionController>(), Is.SameAs(engine));
                Assert.That(provider.GetRequiredService<IIsa95JobOrderCatalog>(), Is.SameAs(engine));
                Assert.That(provider.GetRequiredService<IIsa95JobOrderCatalogChangeSource>(), Is.SameAs(engine));
            });
        }

        [Test]
        public void DependencyInjectionAppliesConfiguredOptions()
        {
            var services = new ServiceCollection();
            services.AddInMemoryIsa95JobControlProvider(options => options.MaxJobOrders = 1);

            using ServiceProvider provider = services.BuildServiceProvider();

            Assert.That(
                () => provider.GetRequiredService<InMemoryIsa95JobControlProvider>(),
                Throws.Nothing);
        }
    }
}
