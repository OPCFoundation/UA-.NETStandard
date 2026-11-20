/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.XRegistry.Tests
{
    /// <summary>
    /// Verifies the dependency injection wiring for the xRegistry server pieces. Direct
    /// construction remains supported, so these only cover the container path.
    /// </summary>
    [TestFixture]
    [Category("XRegistry")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class XRegistryServiceCollectionExtensionsTests
    {
        [Test]
        public void AddXRegistryServerSuppliesTheDefaultInProcessStore()
        {
            using ServiceProvider provider = new ServiceCollection()
                .AddXRegistryServer()
                .BuildServiceProvider();

            var options = provider.GetRequiredService<XRegistryServerOptions>();
            Assert.Multiple(() =>
            {
                Assert.That(provider.GetRequiredService<IXRegistryResourceStore>(),
                    Is.InstanceOf<InMemoryResourceStore>());
                Assert.That(options.ResourceStore, Is.InstanceOf<InMemoryResourceStore>());
                Assert.That(options.ContentIdProvider, Is.Null,
                    "A concrete registry supplies the identity provider.");
            });
        }

        [Test]
        public void AddXRegistryServerAppliesTheConfigureCallback()
        {
            using ServiceProvider provider = new ServiceCollection()
                .AddXRegistryServer(options =>
                {
                    options.RequireEncryptionForReads = true;
                    options.RegistryId = "urn:example:registry";
                })
                .BuildServiceProvider();

            var options = provider.GetRequiredService<XRegistryServerOptions>();
            Assert.Multiple(() =>
            {
                Assert.That(options.RequireEncryptionForReads, Is.True);
                Assert.That(options.RegistryId, Is.EqualTo("urn:example:registry"));
            });
        }

        [Test]
        public void AFileSystemStoreRegisteredFirstWins()
        {
            using ServiceProvider provider = new ServiceCollection()
                .AddXRegistryFileSystemResourceStore("resources")
                .AddXRegistryServer()
                .BuildServiceProvider();

            Assert.That(provider.GetRequiredService<IXRegistryResourceStore>(),
                Is.InstanceOf<FileSystemResourceStore>(),
                "TryAdd must not overwrite a store the application already supplied.");
        }

        [Test]
        public void TheContentIdProviderFlowsIntoTheOptions()
        {
            using ServiceProvider provider = new ServiceCollection()
                .AddXRegistryContentIdProvider<XRegistryServerTestHarness.FakeContentIdProvider>()
                .AddXRegistryServer()
                .BuildServiceProvider();

            var options = provider.GetRequiredService<XRegistryServerOptions>();
            Assert.That(options.ContentIdProvider,
                Is.InstanceOf<XRegistryServerTestHarness.FakeContentIdProvider>());
        }

        [Test]
        public void TheRegistrationsAreSingletons()
        {
            using ServiceProvider provider = new ServiceCollection()
                .AddXRegistryServer()
                .BuildServiceProvider();

            var firstOptions = provider.GetRequiredService<XRegistryServerOptions>();
            var secondOptions = provider.GetRequiredService<XRegistryServerOptions>();
            var firstStore = provider.GetRequiredService<IXRegistryResourceStore>();
            var secondStore = provider.GetRequiredService<IXRegistryResourceStore>();

            Assert.Multiple(() =>
            {
                Assert.That(secondOptions, Is.SameAs(firstOptions));
                Assert.That(secondStore, Is.SameAs(firstStore));
            });
        }

        [Test]
        public void NullArgumentsAreRejected()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    () => ((IServiceCollection)null!).AddXRegistryServer(),
                    Throws.TypeOf<ArgumentNullException>());
                Assert.That(
                    () => ((IServiceCollection)null!).AddXRegistryFileSystemResourceStore("x"),
                    Throws.TypeOf<ArgumentNullException>());
                Assert.That(
                    () => ((IServiceCollection)null!)
                        .AddXRegistryContentIdProvider<XRegistryServerTestHarness.FakeContentIdProvider>(),
                    Throws.TypeOf<ArgumentNullException>());
                Assert.That(
                    () => new ServiceCollection().AddXRegistryFileSystemResourceStore(string.Empty),
                    Throws.TypeOf<ArgumentException>());
            });
        }
    }
}
