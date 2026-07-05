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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Client;
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
                .AddCertificateManagement();

            Assert.That(builder.Services, Is.SameAs(services));
            Assert.That(services, Has.Some.Matches<ServiceDescriptor>(d =>
                d.ServiceType == typeof(Func<NodeId, CancellationToken, ValueTask<KeyCredentialServiceClient>>)));
            Assert.That(services, Has.Some.Matches<ServiceDescriptor>(d =>
                d.ServiceType == typeof(Func<NodeId, CancellationToken, ValueTask<AuthorizationServiceClient>>)));
            Assert.That(services, Has.Some.Matches<ServiceDescriptor>(d =>
                d.ServiceType == typeof(LocalDiscoveryServerClient)));
            Assert.That(services, Has.Some.Matches<ServiceDescriptor>(d =>
                d.ServiceType == typeof(Func<NodeId, CancellationToken, ValueTask<OnboardingClient>>)));
            Assert.That(services, Has.Some.Matches<ServiceDescriptor>(d =>
                d.ServiceType == typeof(IAccessTokenProvider)));

            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.That(provider.GetService<IAccessTokenProvider>(), Is.Not.Null);
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
