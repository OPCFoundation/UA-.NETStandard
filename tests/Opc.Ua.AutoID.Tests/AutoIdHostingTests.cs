// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Opc.Ua.AutoID.Client;
using Opc.Ua.AutoID.Server;
using Opc.Ua.AutoID.Server.Hosting;
using Opc.Ua.Client;
using Opc.Ua.Di.Server.Hosting;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Tests;

namespace Opc.Ua.AutoID.Tests
{
    /// <summary>
    /// The dependency injection surface: server registration, ownership of the Device Integration
    /// address space, option validation, the node manager factory and the client factory.
    /// </summary>
    [TestFixture]
    [Category("AutoID")]
    [Category("Hosting")]
    public sealed class AutoIdHostingTests
    {
        [Test]
        public void AddAutoIdRegistersTheNodeManagerFactoryAndClaimsDi()
        {
            IOpcUaServerBuilder builder = CreateServerBuilder();

            builder.AddAutoId(options => options.MaximumTagReadLength = 128);

            using ServiceProvider provider = builder.Services.BuildServiceProvider();
            Assert.Multiple(() =>
            {
                Assert.That(
                    builder.Services.Any(descriptor => descriptor.ServiceType == typeof(AutoIdNodeManagerFactory)),
                    Is.True);
                Assert.That(
                    provider.GetServices<DiAddressSpaceOwnership>().Single().OwnerName,
                    Is.EqualTo(nameof(OpcUaAutoIdServerBuilderExtensions.AddAutoId)));
                Assert.That(provider.GetRequiredService<IOptions<AutoIdServerOptions>>().Value.MaximumTagReadLength,
                    Is.EqualTo(128u));
            });
        }

        [Test]
        public void ASecondDiOwningRegistrationIsRefused()
        {
            IOpcUaServerBuilder builder = CreateServerBuilder();
            builder.AddAutoId();

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => builder.AddAutoId())!;

            Assert.That(exception.Message, Does.Contain("already owned by").And.Contain("AddAutoId"));
        }

        [Test]
        public void AnotherOwnerBlocksAutoId()
        {
            IOpcUaServerBuilder builder = CreateServerBuilder();
            builder.Services.AddSingleton(new DiAddressSpaceOwnership("AddSomethingElse"));

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => builder.AddAutoId())!;

            Assert.That(exception.Message, Does.Contain("AddSomethingElse"));
        }

        [Test]
        public void RegistrationRejectsInvalidInput()
        {
            IOpcUaServerBuilder builder = CreateServerBuilder();
            Assert.Multiple(() =>
            {
                Assert.Throws<ArgumentNullException>(() => OpcUaAutoIdServerBuilderExtensions.AddAutoId(null!));
                Assert.Throws<ArgumentNullException>(() => builder.ConfigureAutoId((Action<IAutoIdSetupContext>)null!));
                Assert.Throws<ArgumentNullException>(
                    () => builder.ConfigureAutoId(null!));
                Assert.Throws<ArgumentNullException>(
                    () => OpcUaAutoIdServerBuilderExtensions.ConfigureAutoId(
                        null!, _ => default));
                Assert.Throws<ArgumentOutOfRangeException>(
                    () => builder.AddAutoId(options => options.MaximumScanDuration = TimeSpan.Zero));
                Assert.That(
                    builder.Services.Any(descriptor => descriptor.ServiceType == typeof(DiAddressSpaceOwnership)),
                    Is.False,
                    "a rejected registration claims nothing");
            });
        }

        [Test]
        public void TheFactoryListsTheNamespacesOfTheModelsAndOfTheOptions()
        {
            string[] defaults = [.. new AutoIdNodeManagerFactory().NamespacesUris];
            string[] extended = [.. new AutoIdNodeManagerFactory(
                runner: null,
                Options.Create(new AutoIdServerOptions { AdditionalNamespaceUris = ["urn:vendor:extra"] }))
                .NamespacesUris];

            Assert.Multiple(() =>
            {
                Assert.That(defaults, Is.EquivalentTo([AutoIdModel.NamespaceUri, Di.Namespaces.OpcUaDi]));
                Assert.That(extended, Does.Contain("urn:vendor:extra").And.Contain(AutoIdModel.NamespaceUri));
            });
        }

        [Test]
        public void TheNodeManagerRejectsNullOptions()
        {
            Assert.Throws<ArgumentNullException>(
                () => new AutoIdNodeManager(null!, null!, null!));
        }

        [Test]
        public void AddAutoIdClientRegistersAndResolvesTheFactory()
        {
            var services = new ServiceCollection();
            IOpcUaClientBuilder builder = services.AddOpcUa().AddClient(options =>
            {
                options.ApplicationName = "AutoIdClientCoverage";
                options.ApplicationUri = "urn:localhost:OPCFoundation:AutoIdClientCoverage";
            });

            builder.AddAutoIdClient();
            builder.AddAutoIdClient();

            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.Multiple(() =>
            {
                Assert.That(provider.GetRequiredService<AutoIdClientFactory>(), Is.Not.Null);
                Assert.That(provider.GetRequiredService<Func<CancellationToken, Task<AutoIdClient>>>(), Is.Not.Null);
                Assert.That(
                    services.Count(descriptor => descriptor.ServiceType == typeof(AutoIdClientFactory)),
                    Is.EqualTo(1));
            });
        }

        [Test]
        public void AddAutoIdClientRequiresTheManagedSessionRegistration()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(NUnitTelemetryContext.Create());
            IOpcUaClientBuilder builder = services.AddOpcUa().AddClient(options =>
            {
                options.ApplicationName = "AutoIdClientCoverage";
                options.ApplicationUri = "urn:localhost:OPCFoundation:AutoIdClientCoverage";
            });
            builder.AddAutoIdClient();
            ServiceDescriptor sessionFactory = services.Single(
                descriptor => descriptor.ServiceType == typeof(Func<CancellationToken, Task<ManagedSession>>));
            services.Remove(sessionFactory);

            using ServiceProvider provider = services.BuildServiceProvider();

            Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<AutoIdClientFactory>());
        }

        [Test]
        public void AddAutoIdClientWithNullBuilderThrows()
        {
            Assert.Throws<ArgumentNullException>(() => OpcUaAutoIdClientBuilderExtensions.AddAutoIdClient(null!));
        }

        private static IOpcUaServerBuilder CreateServerBuilder()
        {
            var services = new ServiceCollection();
            return services.AddOpcUa().AddServer(options =>
            {
                options.ApplicationName = "AutoIdHostingTests";
                options.ApplicationUri = "urn:localhost:OPCFoundation:AutoIdHostingTests";
            });
        }
    }
}
