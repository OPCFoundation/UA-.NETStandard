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
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Opc.Ua.Aas.Server;
using Opc.Ua.Aas.Server.Hosting;
using Opc.Ua.Aas.Server.Materialization;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Aas.Tests.Server
{
    /// <summary>
    /// Tests the AAS server dependency-injection surface.
    /// </summary>
    [TestFixture]
    [Category("Aas")]
    public sealed class AasHostingTests
    {
        [Test]
        public void AasServerOptionsDefaultRetirementPolicyIsGraceful()
        {
            var options = new AasServerOptions();

            Assert.That(options.RetirementPolicy, Is.EqualTo(AasProjectionRetirementPolicy.Graceful));
        }

        [Test]
        public void AddAasServerActionRegistersServerServices()
        {
            var services = new ServiceCollection();
            services.AddSingleton(Mock.Of<INodeManagerLifecycle>());

            IAasServerBuilder builder = services
                .AddOpcUa()
                .AddAasServer(options => options.EnvironmentFolder = "aas");
            using ServiceProvider provider = services.BuildServiceProvider();

            Assert.Multiple(() =>
            {
                Assert.That(builder.Services, Is.SameAs(services));
                Assert.That(provider.GetRequiredService<IOptions<AasServerOptions>>().Value.EnvironmentFolder,
                    Is.EqualTo("aas"));
                Assert.That(provider.GetService<IAasValueProvider>(), Is.Not.Null);
                Assert.That(provider.GetService<IAasOperationHandler>(), Is.Not.Null);
                Assert.That(provider.GetService<IAasEnvironmentProvider>(), Is.Not.Null);
                Assert.That(provider.GetService<IAasEnvironmentProjectionHost>(), Is.Not.Null);
                Assert.That(provider.GetService<AasEnvironmentNodeManagerFactory>(), Is.Not.Null);
                Assert.That(provider.GetService<OpcUaServerNodeManagerRegistration>(), Is.Not.Null);
            });
        }

        [Test]
        public void AddAasServerConfigurationRegistersServerServices()
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["OpcUa:Aas:Server:EnvironmentFolder"] = "configured"
                })
                .Build();
            var services = new ServiceCollection();

            services.AddOpcUa().AddAasServer(configuration);
            using ServiceProvider provider = services.BuildServiceProvider();

            Assert.That(
                provider.GetRequiredService<IOptions<AasServerOptions>>().Value.EnvironmentFolder,
                Is.EqualTo("configured"));
        }

        [Test]
        public void AddAasServerConfigurationSectionRegistersServerServices()
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Aas:EnvironmentFolder"] = "section",
                    ["Aas:RetirementPolicy"] = "Immediate"
                })
                .Build();
            var services = new ServiceCollection();

            services.AddOpcUa().AddAasServer(configuration.GetSection("Aas"));
            using ServiceProvider provider = services.BuildServiceProvider();
            AasServerOptions options = provider.GetRequiredService<IOptions<AasServerOptions>>().Value;

            Assert.Multiple(() =>
            {
                Assert.That(options.EnvironmentFolder, Is.EqualTo("section"));
                Assert.That(options.RetirementPolicy, Is.EqualTo(AasProjectionRetirementPolicy.Immediate));
            });
        }

        [Test]
        public void AddAasServerRejectsNullBuilder()
        {
            Assert.That(
                () => OpcUaAasServerBuilderExtensions.AddAasServer((IOpcUaBuilder)null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void AddAasServerRejectsNullConfiguration()
        {
            IOpcUaBuilder builder = new ServiceCollection().AddOpcUa();

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => builder.AddAasServer((IConfiguration)null!),
                    Throws.TypeOf<ArgumentNullException>());
                Assert.That(
                    () => builder.AddAasServer((IConfigurationSection)null!),
                    Throws.TypeOf<ArgumentNullException>());
            });
        }
    }
}
