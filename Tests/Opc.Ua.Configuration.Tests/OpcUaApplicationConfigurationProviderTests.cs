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
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Configuration.Tests
{
    [TestFixture]
    [Category("ApplicationConfigurationBuilder")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class OpcUaApplicationConfigurationProviderTests
    {
        [Test]
        public void ConfigureApplicationThrowsForNullArguments()
        {
            Assert.That(
                () => OpcUaConfigurationServiceCollectionExtensions.ConfigureApplication(
                    null!,
                    _ => { }),
                Throws.ArgumentNullException);

            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();
            Assert.That(
                () => builder.ConfigureApplication(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public async Task ConfigureApplicationRegistersOptionsAndProviderAsync()
        {
            string pkiRoot = CreatePkiRoot();
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();

            IOpcUaBuilder returned = builder.ConfigureApplication(options =>
            {
                options.ApplicationName = "ConfiguredClient";
                options.ApplicationUri = "urn:localhost:ConfiguredClient";
                options.ProductUri = "uri:opcfoundation.org:ConfiguredClient";
                options.PkiRoot = pkiRoot;
            });
            services.AddSingleton<IOpcUaApplicationConfigurationFeature>(
                new ClientFeature());

            ServiceProvider provider = services.BuildServiceProvider();
            try
            {
                Assert.That(returned, Is.SameAs(builder));
                OpcUaApplicationOptions options =
                    provider.GetRequiredService<OpcUaApplicationOptions>();
                Assert.That(options.ApplicationName, Is.EqualTo("ConfiguredClient"));
                Assert.That(
                    provider.GetRequiredService<IOpcUaApplicationConfigurationProvider>(),
                    Is.InstanceOf<OpcUaApplicationConfigurationProvider>());
            }
            finally
            {
                await provider.DisposeAsync().ConfigureAwait(false);
                DeletePkiRoot(pkiRoot);
            }
        }

        [Test]
        public async Task ProviderBuildsOneCombinedConfigurationAsync()
        {
            string pkiRoot = CreatePkiRoot();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var certificateManager = new CertificateManager(telemetry);
            var provider = new OpcUaApplicationConfigurationProvider(
                new OpcUaApplicationOptions
                {
                    ApplicationName = "CombinedApplication",
                    ApplicationUri = "urn:localhost:CombinedApplication",
                    ProductUri = "uri:opcfoundation.org:CombinedApplication",
                    PkiRoot = pkiRoot,
                    AutoAcceptUntrustedCertificates = true
                },
                new TestApplicationInstanceFactory(),
                telemetry,
                [new ClientFeature(), new ServerFeature()],
                certificateManager);

            try
            {
                Assert.That(provider.Application.ApplicationType,
                    Is.EqualTo(ApplicationType.ClientAndServer));
                Assert.That(provider.Configuration.ApplicationType,
                    Is.EqualTo(ApplicationType.ClientAndServer));
                Assert.That(provider.Configuration.ClientConfiguration, Is.Not.Null);
                Assert.That(provider.Configuration.ServerConfiguration, Is.Not.Null);

                ApplicationConfiguration configuration = await provider
                    .GetAsync()
                    .ConfigureAwait(false);

                Assert.That(configuration, Is.SameAs(provider.Configuration));
                Assert.That(
                    configuration.CertificateManager,
                    Is.SameAs(certificateManager));
                Assert.That(
                    ((ApplicationInstance)provider.Application).CertificateManager,
                    Is.SameAs(certificateManager));
                Assert.That(
                    configuration.SecurityConfiguration.AutoAcceptUntrustedCertificates,
                    Is.True);
            }
            finally
            {
                await provider.DisposeAsync().ConfigureAwait(false);
                DeletePkiRoot(pkiRoot);
            }
        }

        [Test]
        public async Task ProviderEnsuresClientApplicationCertificateAsync()
        {
            string pkiRoot = CreatePkiRoot();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var provider = new OpcUaApplicationConfigurationProvider(
                new OpcUaApplicationOptions
                {
                    ApplicationName = "ClientApplication",
                    ApplicationUri = "urn:localhost:ClientApplication",
                    ProductUri = "uri:opcfoundation.org:ClientApplication",
                    PkiRoot = pkiRoot
                },
                new TestApplicationInstanceFactory(),
                telemetry,
                [new ClientFeature()]);

            try
            {
                await provider.GetAsync().ConfigureAwait(false);

                Assert.That(
                    ((ApplicationInstance)provider.Application).CertificateManager,
                    Is.Not.Null);
            }
            finally
            {
                await provider.DisposeAsync().ConfigureAwait(false);
                DeletePkiRoot(pkiRoot);
            }
        }

        private static string CreatePkiRoot()
        {
            return Path.Combine(
                Path.GetTempPath(),
                nameof(OpcUaApplicationConfigurationProviderTests),
                Guid.NewGuid().ToString("N"));
        }

        private static void DeletePkiRoot(string pkiRoot)
        {
            if (Directory.Exists(pkiRoot))
            {
                Directory.Delete(pkiRoot, recursive: true);
            }
        }

        private sealed class ClientFeature : IOpcUaApplicationConfigurationFeature
        {
            public void ApplyDefaults(OpcUaApplicationOptions options)
            {
            }

            public IApplicationConfigurationBuilderSecurity Configure(
                IApplicationConfigurationBuilderTypes builder)
            {
                return builder.AsClient();
            }
        }

        private sealed class ServerFeature : IOpcUaApplicationConfigurationFeature
        {
            public void ApplyDefaults(OpcUaApplicationOptions options)
            {
            }

            public IApplicationConfigurationBuilderSecurity Configure(
                IApplicationConfigurationBuilderTypes builder)
            {
                return builder
                    .AsServer(["opc.tcp://localhost:4840/CombinedApplication"])
                    .AddUnsecurePolicyNone();
            }
        }

        private sealed class TestApplicationInstanceFactory : IApplicationInstanceFactory
        {
            public IApplicationInstance Create(ITelemetryContext telemetry)
            {
                return new ApplicationInstance(telemetry);
            }
        }
    }
}
