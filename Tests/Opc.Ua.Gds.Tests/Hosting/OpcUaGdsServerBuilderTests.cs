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
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Opc.Ua.Configuration;
using Opc.Ua.Gds.Server.Hosting;

namespace Opc.Ua.Gds.Tests.Hosting
{
    /// <summary>
    /// Verifies the DI registration surface exposed by
    /// <c>OpcUaGdsServerBuilderExtensions.AddGdsServer(...)</c>. Does
    /// not start the hosted service.
    /// </summary>
    [TestFixture]
    [Category("GDS")]
    [Category("Builder")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class OpcUaGdsServerBuilderTests
    {
        [Test]
        public void AddGdsServerThrowsForNullArgs()
        {
            Assert.That(
                () => OpcUaGdsServerBuilderExtensions.AddGdsServer(
                    null, _ => { }),
                Throws.ArgumentNullException);

            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();
            Assert.That(
                () => builder.AddGdsServer((Action<GdsServerOptions>)null),
                Throws.ArgumentNullException);
            Assert.That(
                () => builder.AddGdsServer((IConfiguration)null),
                Throws.ArgumentNullException);
            Assert.That(
                () => builder.AddGdsServer((IConfigurationSection)null),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddGdsServerRegistersExpectedServices()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddGdsServer(opt =>
            {
                opt.ApplicationName = "TestGds";
                opt.ApplicationUri = "urn:test:gds";
            });

            using ServiceProvider sp = services.BuildServiceProvider();

            Assert.That(sp.GetService<IOptions<GdsServerOptions>>(), Is.Not.Null);
            Assert.That(sp.GetService<ITelemetryContext>(), Is.Not.Null);
            Assert.That(sp.GetService<IApplicationInstanceFactory>(), Is.Not.Null);

            ServiceDescriptor hostedDescriptor = services.FirstOrDefault(
                s => s.ImplementationType == typeof(GdsServerHostedService));
            Assert.That(hostedDescriptor, Is.Not.Null);
            Assert.That(hostedDescriptor.ServiceType, Is.EqualTo(typeof(IHostedService)));
        }

        [Test]
        public void AddGdsServerReturnsBuilderWithServices()
        {
            var services = new ServiceCollection();
            IGdsServerBuilder builder = services.AddOpcUa()
                .AddGdsServer(opt => opt.ApplicationName = "TestGds");

            Assert.That(builder, Is.Not.Null);
            Assert.That(builder.Services, Is.SameAs(services));
        }

        [Test]
        public void AddGdsServerThrowsOnDuplicateRegistration()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();

            builder.AddGdsServer(opt => opt.ApplicationName = "First");

            Assert.That(
                () => builder.AddGdsServer(opt => opt.ApplicationName = "Second"),
                Throws.InvalidOperationException);
        }

        [Test]
        public void AddGdsServerWithConfigurationSectionBindsOptions()
        {
            var configData = new Dictionary<string, string>
            {
                ["OpcUa:Gds:Server:ApplicationName"] = "BoundGds",
                ["OpcUa:Gds:Server:ApplicationUri"] = "urn:test:bound:gds",
                ["OpcUa:Gds:Server:AutoApprove"] = "false"
            };

            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(configData)
                .Build();

            var services = new ServiceCollection();
            services.AddOpcUa().AddGdsServer(configuration);

            using ServiceProvider sp = services.BuildServiceProvider();
            GdsServerOptions options = sp.GetRequiredService<IOptions<GdsServerOptions>>().Value;

            Assert.That(options.ApplicationName, Is.EqualTo("BoundGds"));
            Assert.That(options.ApplicationUri, Is.EqualTo("urn:test:bound:gds"));
            Assert.That(options.AutoApprove, Is.False);
        }

        [Test]
        public void AddGdsServerCanCoexistWithAddServer()
        {
            var services = new ServiceCollection();
            Assert.That(
                () => services.AddOpcUa()
                    .AddServer(opt => opt.ApplicationName = "RegularServer")
                    .Services.AddOpcUa()
                    .AddGdsServer(opt => opt.ApplicationName = "GdsServer"),
                Throws.Nothing);

            using ServiceProvider sp = services.BuildServiceProvider();

            // Both server features published an options registration.
            Assert.That(
                sp.GetRequiredService<IOptions<Ua.Server.Hosting.OpcUaServerOptions>>().Value.ApplicationName,
                Is.EqualTo("RegularServer"));
            Assert.That(
                sp.GetRequiredService<IOptions<GdsServerOptions>>().Value.ApplicationName,
                Is.EqualTo("GdsServer"));

            // Both hosted services are registered.
            int hostedCount = services.Count(s =>
                s.ServiceType == typeof(IHostedService) &&
                (s.ImplementationType == typeof(GdsServerHostedService) ||
                    s.ImplementationType?.Name == "OpcUaServerHostedService"));
            Assert.That(hostedCount, Is.EqualTo(2));
        }
    }
}
