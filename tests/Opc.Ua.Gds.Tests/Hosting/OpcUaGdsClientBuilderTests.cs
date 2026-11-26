// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Gds.Client;
using Opc.Ua.Identity;
using Opc.Ua.Tests;

namespace Opc.Ua.Gds.Tests.Hosting
{
    [TestFixture]
    [Category("GDS")]
    [Category("Hosting")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class OpcUaGdsClientBuilderTests
    {
        [Test]
        public void AddGdsClientRegistersInterfacesAndConcreteClients()
        {
            IServiceCollection services = new ServiceCollection();
            services.AddSingleton(CreateConfiguration());

            services.AddOpcUa().AddGdsClient();

            using ServiceProvider provider = services.BuildServiceProvider();

            Assert.That(provider.GetService<GlobalDiscoveryServerClient>(), Is.Not.Null);
            Assert.That(provider.GetService<IGlobalDiscoveryServerClient>(), Is.Not.Null);
            Assert.That(provider.GetService<ServerPushConfigurationClient>(), Is.Not.Null);
            Assert.That(provider.GetService<IServerPushConfigurationClient>(), Is.Not.Null);
        }

        [Test]
        public void AddGdsClientChainsFromClientBuilderAndRegistersFactories()
        {
            IServiceCollection services = new ServiceCollection();
            services.AddSingleton(CreateConfiguration());

            IGdsClientBuilder builder = services.AddOpcUa()
                .AddClient(options => options.Configuration = CreateConfiguration())
                .AddGdsClient()
                .AddKeyCredentialServiceClient()
                .AddAuthorizationServiceClient()
                .AddLocalDiscoveryServerClient()
                .AddOnboardingClient()
                .AddDeviceRegistrarClient()
                .AddCertificateManagement();

            Assert.That(builder.Services, Is.SameAs(services));
            Assert.That(services, Has.Some.Matches<ServiceDescriptor>(d =>
                d is not null && d.ServiceType == typeof(Func<NodeId, CancellationToken, ValueTask<KeyCredentialServiceClient>>)));
            Assert.That(services, Has.Some.Matches<ServiceDescriptor>(d =>
                d is not null && d.ServiceType == typeof(Func<NodeId, CancellationToken, ValueTask<AuthorizationServiceClient>>)));
            Assert.That(services, Has.Some.Matches<ServiceDescriptor>(d =>
                d is not null && d.ServiceType == typeof(LocalDiscoveryServerClient)));
            Assert.That(services, Has.Some.Matches<ServiceDescriptor>(d =>
                d is not null && d.ServiceType == typeof(Func<NodeId, CancellationToken, ValueTask<OnboardingClient>>)));
            Assert.That(services, Has.Some.Matches<ServiceDescriptor>(d =>
                d is not null && d.ServiceType ==
                    typeof(Func<NodeId, CancellationToken, ValueTask<Opc.Ua.Onboarding.DeviceRegistrarTypeClient>>)));
            Assert.That(services, Has.Some.Matches<ServiceDescriptor>(d =>
                d is not null && d.ServiceType == typeof(IAccessTokenProvider)));

            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.That(provider.GetService<IAccessTokenProvider>(), Is.Not.Null);
        }

        [Test]
        public void AddDeviceRegistrarClientRejectsNullBuilder()
        {
            Assert.That(
                () => OpcUaGdsClientBuilderExtensions.AddDeviceRegistrarClient(null!),
                Throws.ArgumentNullException);
        }

        private static ApplicationConfiguration CreateConfiguration()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            return new ApplicationConfiguration(telemetry)
            {
                ApplicationName = "gds-hosting-tests",
                ApplicationUri = "urn:gds-hosting-tests",
                ClientConfiguration = new ClientConfiguration()
            };
        }
    }
}
