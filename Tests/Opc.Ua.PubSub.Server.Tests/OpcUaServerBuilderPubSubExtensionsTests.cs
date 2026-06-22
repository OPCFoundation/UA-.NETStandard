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
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.Security.Sks;
using Opc.Ua.PubSub.Server;
using Opc.Ua.PubSub.Server.Hosting;
using Opc.Ua.PubSub.Tests;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Server.Tests
{
    /// <summary>
    /// Coverage for the DI extensions in
    /// <see cref="OpcUaServerBuilderPubSubExtensions"/>.
    /// </summary>
    [TestFixture]
    [TestSpec("9.1", Summary = "DI registration of PubSub server")]
    public class OpcUaServerBuilderPubSubExtensionsTests
    {
        [Test]
        public async Task AddPubSub_RegistersNodeManagerRegistration()
        {
            ServiceCollection services = BuildServicesWithRuntime();
            IOpcUaServerBuilder serverBuilder = services
                .AddOpcUa()
                .AddServer(opt => { });
            IPubSubServerBuilder builder = serverBuilder.AddPubSub();

            Assert.That(builder, Is.Not.Null);
            Assert.That(builder.Services, Is.SameAs(services));

            await using ServiceProvider sp = services.BuildServiceProvider();
            IEnumerable<OpcUaServerNodeManagerRegistration> regs =
                sp.GetServices<OpcUaServerNodeManagerRegistration>();
            Assert.That(
                regs.Any(r => r.SyncFactory is PubSubNodeManagerFactory),
                Is.True,
                "Expected PubSubNodeManagerFactory to be registered as a sync OpcUaServerNodeManagerRegistration.");
        }

        [Test]
        public void AddPubSub_TwiceOnSameCollection_Throws()
        {
            ServiceCollection services = BuildServicesWithRuntime();
            IOpcUaServerBuilder serverBuilder = services
                .AddOpcUa()
                .AddServer(opt => { });
            serverBuilder.AddPubSub();
            Assert.That(
                () => serverBuilder.AddPubSub(),
                Throws.InvalidOperationException);
        }

        [Test]
        public async Task AddPubSub_ConfigureOverload_AppliesOptions()
        {
            ServiceCollection services = BuildServicesWithRuntime();
            services
                .AddOpcUa()
                .AddServer(opt => { })
                .AddPubSub(opt =>
                {
                    opt.ExposeSecurityKeyService = true;
                    opt.DefaultSecurityGroupId = "g42";
                });

            await using ServiceProvider sp = services.BuildServiceProvider();
            PubSubServerOptions opts = sp.GetRequiredService<IOptions<PubSubServerOptions>>().Value;

            Assert.Multiple(() =>
            {
                Assert.That(opts.ExposeSecurityKeyService, Is.True);
                Assert.That(opts.DefaultSecurityGroupId, Is.EqualTo("g42"));
            });
        }

        [Test]
        public async Task AddPubSub_IConfigurationOverload_BindsSection()
        {
            ServiceCollection services = BuildServicesWithRuntime();
            IConfigurationRoot config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["OpcUa:Server:PubSub:DefaultSecurityGroupId"] = "config-grp"
                })
                .Build();
            services
                .AddOpcUa()
                .AddServer(opt => { })
                .AddPubSub(config);

            await using ServiceProvider sp = services.BuildServiceProvider();
            PubSubServerOptions opts = sp.GetRequiredService<IOptions<PubSubServerOptions>>().Value;
            Assert.That(opts.DefaultSecurityGroupId, Is.EqualTo("config-grp"));
        }

        [Test]
        public async Task AddPubSub_IConfigurationSectionOverload_BindsSection()
        {
            ServiceCollection services = BuildServicesWithRuntime();
            IConfigurationRoot config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["X:DefaultSecurityGroupId"] = "explicit-section"
                })
                .Build();
            services
                .AddOpcUa()
                .AddServer(opt => { })
                .AddPubSub(config.GetSection("X"));

            await using ServiceProvider sp = services.BuildServiceProvider();
            PubSubServerOptions opts = sp.GetRequiredService<IOptions<PubSubServerOptions>>().Value;
            Assert.That(opts.DefaultSecurityGroupId, Is.EqualTo("explicit-section"));
        }

        [Test]
        public async Task AddPubSub_IConfiguration_BindsAllServerOptions()
        {
            ServiceCollection services = BuildServicesWithRuntime();
            IConfigurationRoot config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["OpcUa:Server:PubSub:ExposeSecurityKeyService"] = "true",
                    ["OpcUa:Server:PubSub:ExposeConfigurationMethods"] = "false",
                    ["OpcUa:Server:PubSub:DefaultSecurityGroupId"] = "bound-group",
                    ["OpcUa:Server:PubSub:DefaultSecurityPolicyUri"] =
                        "http://opcfoundation.org/UA/SecurityPolicy#PubSub-Aes128-CTR",
                    ["OpcUa:Server:PubSub:DefaultKeyLifetimeMs"] = "1250.5",
                    ["OpcUa:Server:PubSub:DiagnosticsExposure"] = "Full"
                })
                .Build();
            services
                .AddOpcUa()
                .AddServer(opt => { })
                .AddPubSub(config);

            await using ServiceProvider sp = services.BuildServiceProvider();
            PubSubServerOptions opts = sp.GetRequiredService<IOptions<PubSubServerOptions>>().Value;

            Assert.Multiple(() =>
            {
                Assert.That(opts.ExposeSecurityKeyService, Is.True);
                Assert.That(opts.ExposeConfigurationMethods, Is.False);
                Assert.That(opts.DefaultSecurityGroupId, Is.EqualTo("bound-group"));
                Assert.That(
                    opts.DefaultSecurityPolicyUri,
                    Is.EqualTo("http://opcfoundation.org/UA/SecurityPolicy#PubSub-Aes128-CTR"));
                Assert.That(opts.DefaultKeyLifetimeMs, Is.EqualTo(1250.5d));
                Assert.That(opts.DiagnosticsExposure, Is.EqualTo(PubSubDiagnosticsExposure.Full));
            });
        }

        [Test]
        public void AddPubSub_WithoutRuntime_ThrowsInvalidOperation()
        {
            // No prior AddPubSub on the IOpcUaBuilder; only AddServer.
            var services = new ServiceCollection();
            IOpcUaServerBuilder serverBuilder = services
                .AddOpcUa()
                .AddServer(opt => { });
            Assert.That(
                () => serverBuilder.AddPubSub(),
                Throws.InvalidOperationException);
        }

        [Test]
        public void AddPubSub_NullBuilder_Throws()
        {
            IOpcUaServerBuilder? builder = null;
            Assert.That(
                () => OpcUaServerBuilderPubSubExtensions.AddPubSub(builder!, (Action<PubSubServerOptions>?)null),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddPubSub_NullConfiguration_Throws()
        {
            ServiceCollection services = BuildServicesWithRuntime();
            IOpcUaServerBuilder serverBuilder = services
                .AddOpcUa()
                .AddServer(opt => { });
            Assert.That(
                () => serverBuilder.AddPubSub((IConfiguration)null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddPubSub_NullSection_Throws()
        {
            ServiceCollection services = BuildServicesWithRuntime();
            IOpcUaServerBuilder serverBuilder = services
                .AddOpcUa()
                .AddServer(opt => { });
            Assert.That(
                () => serverBuilder.AddPubSub((IConfigurationSection)null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void Builder_Configure_NullCallback_Throws()
        {
            ServiceCollection services = BuildServicesWithRuntime();
            IPubSubServerBuilder builder = services
                .AddOpcUa()
                .AddServer(opt => { })
                .AddPubSub();
            Assert.That(
                () => builder.Configure(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void Builder_WithDefaultSecurityGroup_NullId_Throws()
        {
            ServiceCollection services = BuildServicesWithRuntime();
            IPubSubServerBuilder builder = services
                .AddOpcUa()
                .AddServer(opt => { })
                .AddPubSub();
            Assert.That(
                () => builder.WithDefaultSecurityGroup(string.Empty),
                Throws.ArgumentException);
        }

        [Test]
        public async Task Builder_FluentSetters_Compose()
        {
            ServiceCollection services = BuildServicesWithRuntime();
            services
                .AddOpcUa()
                .AddServer(opt => { })
                .AddPubSub()
                .ExposeSecurityKeyService()
                .WithDefaultSecurityGroup("seed")
                .Configure(o => o.DiagnosticsExposure = PubSubDiagnosticsExposure.Full);

            await using ServiceProvider sp = services.BuildServiceProvider();
            PubSubServerOptions opts = sp.GetRequiredService<IOptions<PubSubServerOptions>>().Value;

            Assert.Multiple(() =>
            {
                Assert.That(opts.ExposeSecurityKeyService, Is.True);
                Assert.That(opts.DefaultSecurityGroupId, Is.EqualTo("seed"));
                Assert.That(opts.DiagnosticsExposure, Is.EqualTo(PubSubDiagnosticsExposure.Full));
            });
        }

        [Test]
        public async Task Builder_WithSecurityKeyServiceServer_RegistersInterface()
        {
            ServiceCollection services = BuildServicesWithRuntime();
            services
                .AddOpcUa()
                .AddServer(opt => { })
                .AddPubSub()
                .WithSecurityKeyServiceServer();

            await using ServiceProvider sp = services.BuildServiceProvider();
            IPubSubKeyServiceServer? service = sp.GetService<IPubSubKeyServiceServer>();
            InMemoryPubSubKeyServiceServer? memory = sp.GetService<InMemoryPubSubKeyServiceServer>();
            Assert.Multiple(() =>
            {
                Assert.That(service, Is.Not.Null);
                Assert.That(memory, Is.Not.Null);
                Assert.That(service, Is.SameAs(memory));
                Assert.That(
                    sp.GetRequiredService<IOptions<PubSubServerOptions>>().Value.ExposeSecurityKeyService,
                    Is.True);
            });
        }

        [Test]
        public void Builder_WithSecurityKeyServiceServer_NullBuilder_Throws()
        {
            Assert.That(
                () => PubSubServerBuilderExtensions.WithSecurityKeyServiceServer(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public async Task Builder_WithActionMethodHandlers_RegistersRegistration()
        {
            ServiceCollection services = BuildServicesWithRuntime();
            var action = new PublishedActionMethodDataType
            {
                ActionTargets =
                [
                    new ActionTargetDataType
                    {
                        ActionTargetId = 1,
                        Name = "Target"
                    }
                ],
                ActionMethods =
                [
                    new ActionMethodDataType
                    {
                        ObjectId = ObjectIds.Server,
                        MethodId = MethodIds.Server_GetMonitoredItems
                    }
                ]
            };

            services
                .AddOpcUa()
                .AddServer(opt => { })
                .AddPubSub()
                .WithActionMethodHandlers(12, action, "conn");

            await using ServiceProvider sp = services.BuildServiceProvider();
            PubSubActionMethodRegistration registration =
                sp.GetRequiredService<PubSubActionMethodRegistration>();

            Assert.Multiple(() =>
            {
                Assert.That(registration.DataSetWriterId, Is.EqualTo(12));
                Assert.That(registration.ConnectionName, Is.EqualTo("conn"));
                Assert.That(registration.PublishedAction, Is.SameAs(action));
            });
        }

        [Test]
        public async Task Factory_CanBeResolved_AndProducesNamespace()
        {
            ServiceCollection services = BuildServicesWithRuntime();
            services
                .AddOpcUa()
                .AddServer(opt => { })
                .AddPubSub();

            await using ServiceProvider sp = services.BuildServiceProvider();
            PubSubNodeManagerFactory factory = sp.GetRequiredService<PubSubNodeManagerFactory>();
            Assert.That(factory.NamespacesUris.ToArray(), Contains.Item(PubSubNodeManager.NamespaceUri));
        }

        internal static ServiceCollection BuildServicesWithRuntime()
        {
            var services = new ServiceCollection();
            services.AddSingleton<ITelemetryContext>(NUnitTelemetryContext.Create());
            services.AddSingleton<IPubSubApplication>(
                _ => new PubSubApplicationBuilder(NUnitTelemetryContext.Create())
                    .WithApplicationId("test-pubsub-server")
                    .UseConfiguration(new PubSubConfigurationDataType
                    {
                        Connections = [],
                        PublishedDataSets = []
                    })
                    .UseAllStandardEncoders()
                    .Build());
            return services;
        }
    }
}
