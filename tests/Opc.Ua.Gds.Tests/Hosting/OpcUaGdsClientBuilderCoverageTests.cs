/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Gds.Client;
using Opc.Ua.Tests;

namespace Opc.Ua.Gds.Tests.Hosting
{
    /// <summary>
    /// Verifies the DI registration surface exposed by the option, configuration
    /// and per-session factory overloads of
    /// <c>OpcUaGdsClientBuilderExtensions</c> without opening a real session.
    /// </summary>
    [TestFixture]
    [Category("GDS")]
    [Category("Hosting")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class OpcUaGdsClientBuilderCoverageTests
    {
        [Test]
        public void AddGdsClientWithConfigureActionBindsOptions()
        {
            IServiceCollection services = new ServiceCollection();

            services.AddOpcUa().AddGdsClient(options => options.MaxConnectAttempts = 9);

            using ServiceProvider provider = services.BuildServiceProvider();

            GdsClientOptions bound = provider.GetRequiredService<IOptions<GdsClientOptions>>().Value;
            Assert.That(bound.MaxConnectAttempts, Is.EqualTo(9));
        }

        [Test]
        public void AddGdsClientFromConfigurationBindsOptions()
        {
            IConfiguration configuration = BuildConfiguration();

            IServiceCollection services = new ServiceCollection();
            services.AddOpcUa().AddGdsClient(configuration);

            using ServiceProvider provider = services.BuildServiceProvider();

            GdsClientOptions bound = provider.GetRequiredService<IOptions<GdsClientOptions>>().Value;
            Assert.That(bound.MaxConnectAttempts, Is.EqualTo(7));
            Assert.That(bound.FileTransferChunkSize, Is.EqualTo(512));
        }

        [Test]
        public void AddGdsClientFromConfigurationSectionBindsOptions()
        {
            IConfigurationSection section = BuildConfiguration().GetSection("OpcUa:Gds:Client");

            IServiceCollection services = new ServiceCollection();
            services.AddOpcUa().AddGdsClient(section);

            using ServiceProvider provider = services.BuildServiceProvider();

            GdsClientOptions bound = provider.GetRequiredService<IOptions<GdsClientOptions>>().Value;
            Assert.That(bound.MaxConnectAttempts, Is.EqualTo(7));
        }

        [Test]
        public void AddGdsClientFromClientBuilderWithConfigureActionBindsOptions()
        {
            IServiceCollection services = new ServiceCollection();

            IOpcUaClientBuilder clientBuilder = services.AddOpcUa()
                .AddClient(options => options.Configuration = CreateConfiguration());
            clientBuilder.AddGdsClient(options => options.MaxConnectAttempts = 11);

            using ServiceProvider provider = services.BuildServiceProvider();

            GdsClientOptions bound = provider.GetRequiredService<IOptions<GdsClientOptions>>().Value;
            Assert.That(bound.MaxConnectAttempts, Is.EqualTo(11));
        }

        [Test]
        public void AddGdsClientFromClientBuilderConfigurationBindsOptions()
        {
            IConfiguration configuration = BuildConfiguration();

            IServiceCollection services = new ServiceCollection();
            IOpcUaClientBuilder clientBuilder = services.AddOpcUa()
                .AddClient(options => options.Configuration = CreateConfiguration());
            clientBuilder.AddGdsClient(configuration);

            using ServiceProvider provider = services.BuildServiceProvider();

            GdsClientOptions bound = provider.GetRequiredService<IOptions<GdsClientOptions>>().Value;
            Assert.That(bound.MaxConnectAttempts, Is.EqualTo(7));
        }

        [Test]
        public void AddGdsClientFromClientBuilderConfigurationSectionBindsOptions()
        {
            IConfigurationSection section = BuildConfiguration().GetSection("OpcUa:Gds:Client");

            IServiceCollection services = new ServiceCollection();
            IOpcUaClientBuilder clientBuilder = services.AddOpcUa()
                .AddClient(options => options.Configuration = CreateConfiguration());
            clientBuilder.AddGdsClient(section);

            using ServiceProvider provider = services.BuildServiceProvider();

            GdsClientOptions bound = provider.GetRequiredService<IOptions<GdsClientOptions>>().Value;
            Assert.That(bound.FileTransferChunkSize, Is.EqualTo(512));
        }

        [Test]
        public void AddGdsClientThrowsForNullBuilder()
        {
            IConfiguration configuration = BuildConfiguration();

            Assert.That(
                () => ((IOpcUaBuilder)null!).AddGdsClient(
(Action<GdsClientOptions>)null!),
                Throws.ArgumentNullException);
            Assert.That(
                () => ((IOpcUaBuilder)null!).AddGdsClient(
configuration),
                Throws.ArgumentNullException);
            Assert.That(
                () => ((IOpcUaBuilder)null!).AddGdsClient(
configuration.GetSection("OpcUa:Gds:Client")),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddGdsClientThrowsForNullConfiguration()
        {
            IOpcUaBuilder builder = new ServiceCollection().AddOpcUa();

            Assert.That(
                () => builder.AddGdsClient((IConfiguration)null!),
                Throws.ArgumentNullException);
            Assert.That(
                () => builder.AddGdsClient((IConfigurationSection)null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddGdsClientFromClientBuilderThrowsForNullArgs()
        {
            Assert.That(
                () => ((IOpcUaClientBuilder)null!).AddGdsClient(
(Action<GdsClientOptions>)null!),
                Throws.ArgumentNullException);
            Assert.That(
                () => ((IOpcUaClientBuilder)null!).AddGdsClient(
(IConfiguration)null!),
                Throws.ArgumentNullException);
            Assert.That(
                () => ((IOpcUaClientBuilder)null!).AddGdsClient(
(IConfigurationSection)null!),
                Throws.ArgumentNullException);

            IOpcUaClientBuilder clientBuilder = new ServiceCollection().AddOpcUa()
                .AddClient(options => options.Configuration = CreateConfiguration());
            Assert.That(
                () => clientBuilder.AddGdsClient((IConfiguration)null!),
                Throws.ArgumentNullException);
            Assert.That(
                () => clientBuilder.AddGdsClient((IConfigurationSection)null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void ResolvingPerSessionClientFactoryWithoutAddClientThrows()
        {
            IServiceCollection services = new ServiceCollection();
            services.AddOpcUa().AddGdsClient().AddKeyCredentialServiceClient();

            using ServiceProvider provider = services.BuildServiceProvider();

            Assert.That(
                () => provider.GetRequiredService<
                    Func<NodeId, CancellationToken, ValueTask<KeyCredentialServiceClient>>>(),
                Throws.InvalidOperationException.With.Message.Contains("AddClient"));
        }

        [Test]
        public void PerSessionClientBuilderMethodsThrowForNullBuilder()
        {
            Assert.That(
                () => OpcUaGdsClientBuilderExtensions.AddKeyCredentialServiceClient(null!),
                Throws.ArgumentNullException);
            Assert.That(
                () => OpcUaGdsClientBuilderExtensions.AddAuthorizationServiceClient(null!),
                Throws.ArgumentNullException);
            Assert.That(
                () => OpcUaGdsClientBuilderExtensions.AddLocalDiscoveryServerClient(null!),
                Throws.ArgumentNullException);
            Assert.That(
                () => OpcUaGdsClientBuilderExtensions.AddOnboardingClient(null!),
                Throws.ArgumentNullException);
            Assert.That(
                () => OpcUaGdsClientBuilderExtensions.AddCertificateManagement(null!),
                Throws.ArgumentNullException);
            Assert.That(
                () => OpcUaGdsClientBuilderExtensions.AddCertificateManagement(
                    null!, NodeId.Null),
                Throws.ArgumentNullException);
        }

        private static IConfiguration BuildConfiguration()
        {
            var settings = new Dictionary<string, string?>
            {
                ["OpcUa:Gds:Client:MaxConnectAttempts"] = "7",
                ["OpcUa:Gds:Client:FileTransferChunkSize"] = "512"
            };

            return new ConfigurationBuilder()
                .AddInMemoryCollection(settings)
                .Build();
        }

        private static ApplicationConfiguration CreateConfiguration()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            return new ApplicationConfiguration(telemetry)
            {
                ApplicationName = "gds-client-coverage-tests",
                ApplicationUri = "urn:gds-client-coverage-tests",
                ClientConfiguration = new ClientConfiguration()
            };
        }
    }
}
