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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Opc.Ua.Fx.Ac;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Security.Sks;
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
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
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
        public async Task AddPubSubServerOneShotRegistersRuntimeBeforeNodeManagerAsync()
        {
            var services = new ServiceCollection();
            services.AddSingleton(NUnitTelemetryContext.Create());

            IPubSubServerBuilder builder = services
                .AddOpcUa()
                .AddPubSubServer(
                    server => { },
                    pubsub => pubsub.ConfigureConfiguration(configuration =>
                        configuration.Enabled(false)),
                    pubsubServer => pubsubServer.DefaultSecurityGroupId = "one-shot-group");

            await using ServiceProvider sp = services.BuildServiceProvider();
            PubSubServerOptions options = sp.GetRequiredService<IOptions<PubSubServerOptions>>().Value;

            Assert.Multiple(() =>
            {
                Assert.That(builder.Services, Is.SameAs(services));
                Assert.That(sp.GetService<IPubSubApplication>(), Is.Not.Null);
                Assert.That(sp.GetService<PubSubNodeManagerFactory>(), Is.Not.Null);
                Assert.That(options.DefaultSecurityGroupId, Is.EqualTo("one-shot-group"));
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
        public async Task FxReservationBuilderRegistersOneSingletonWithoutEnablingModelOrMethods()
        {
            ServiceCollection services = BuildServicesWithRuntime();
            IPubSubServerBuilder builder = services.AddOpcUa().AddServer(_ => { })
                .AddPubSub(options => options.ExposeConfigurationMethods = false);
            Assert.That(builder.WithFxCommunicationIdReservations(), Is.SameAs(builder));
            builder.WithFxCommunicationIdReservations();
            Assert.That(services.Count(descriptor => descriptor.ServiceType == typeof(PubSubFxReservationMethods)),
                Is.EqualTo(1));
            await using ServiceProvider provider = services.BuildServiceProvider();
            PubSubFxReservationMethods methods = provider.GetRequiredService<PubSubFxReservationMethods>();
            Assert.That(provider.GetRequiredService<PubSubFxReservationMethods>(), Is.SameAs(methods));
            PubSubServerOptions options = provider.GetRequiredService<IOptions<PubSubServerOptions>>().Value;
            Assert.That(options.ExposeFxModel, Is.False);
            Assert.That(options.ExposeConfigurationMethods, Is.False);
        }

        [Test]
        public async Task FxLifecycleBuilderRegistersHostTransactionForDependencyInjection()
        {
            ServiceCollection services = BuildServicesWithRuntime();
            IPubSubServerBuilder builder = services.AddOpcUa().AddServer(_ => { })
                .AddPubSub(options => options.ExposeConfigurationMethods = true);
            var lifecycle = new Mock<IPubSubFxConnectionLifecycle>().Object;

            Assert.That(builder.WithFxConnectionLifecycle(lifecycle), Is.SameAs(builder));
            builder.WithFxCommunicationIdReservations();

            await using ServiceProvider provider = services.BuildServiceProvider();
            Assert.That(provider.GetRequiredService<IPubSubFxConnectionLifecycle>(), Is.SameAs(lifecycle));
            PubSubFxReservationMethods methods = provider.GetRequiredService<PubSubFxReservationMethods>();
            Assert.That(provider.GetRequiredService<PubSubFxReservationMethods>(), Is.SameAs(methods));
        }

        [Test]
        public void FxReservationBuilderRejectsMissingBuilder()
        {
            Assert.That(() => PubSubServerBuilderExtensions.WithFxCommunicationIdReservations(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void FxLifecycleBuilderRejectsNullArguments()
        {
            ServiceCollection services = BuildServicesWithRuntime();
            IPubSubServerBuilder builder = services.AddOpcUa().AddServer(_ => { })
                .AddPubSub(_ => { });

            Assert.That(() => PubSubServerBuilderExtensions.WithFxConnectionLifecycle(null!, new Mock<IPubSubFxConnectionLifecycle>().Object),
                Throws.ArgumentNullException);
            Assert.That(() => builder.WithFxConnectionLifecycle(null!),
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

        [TestCase(true)]
        [TestCase(false)]
        public async Task FxDiResolvedBinderDispatchesRegisteredLifecycleAsync(bool registerLifecycleFirst)
        {
            ServiceCollection services = BuildServicesWithRuntime();
            IPubSubServerBuilder builder = services.AddOpcUa().AddServer(_ => { })
                .AddPubSub(options => options.ExposeConfigurationMethods = true);
            var lifecycle = new Mock<IPubSubFxConnectionLifecycle>();
            if (registerLifecycleFirst)
            {
                builder.WithFxConnectionLifecycle(lifecycle.Object);
                builder.WithFxCommunicationIdReservations();
            }
            else
            {
                builder.WithFxCommunicationIdReservations();
                builder.WithFxConnectionLifecycle(lifecycle.Object);
            }

            await using ServiceProvider provider = services.BuildServiceProvider();
            PubSubFxReservationMethods binder = provider.GetRequiredService<PubSubFxReservationMethods>();
            PubSubServerOptions registeredOptions =
                provider.GetRequiredService<IOptions<PubSubServerOptions>>().Value;
            Assert.Multiple(() =>
            {
                Assert.That(provider.GetRequiredService<IPubSubFxConnectionLifecycle>(), Is.SameAs(lifecycle.Object));
                Assert.That(registeredOptions.ExposeConfigurationMethods, Is.True,
                    "The authorized call requires the explicitly configured method option.");
                Assert.That(registeredOptions.ExposeFxModel, Is.False,
                    "Registering the FX binder and lifecycle must not enable the FX model.");
            });

            await using var harness = new PubSubCommunicationIdMethodTests.Harness(
                options: new PubSubServerOptions { ExposeFxModel = true, ExposeConfigurationMethods = true });
            await harness.InitializeAsync().ConfigureAwait(false);
            AutomationComponentState component = harness.CreateFxComponentWithCloseConnections();
            CloseConnectionsMethodState method = component.CloseConnections!;
            ArrayOf<NodeId> endpoints =
                [new NodeId("endpoint:z", 1), new NodeId("endpoint:a", 1), new NodeId("endpoint:z", 1)];
            ArrayOf<StatusCode> expected =
                [StatusCodes.BadInvalidState, StatusCodes.Good, StatusCodes.BadNodeIdUnknown];
            const bool remove = true;
            binder.Bind(component);

            lifecycle.Setup(value => value.CloseConnectionsAsync(
                It.IsAny<ISystemContext>(), It.IsAny<AutomationComponentState>(), It.IsAny<ArrayOf<NodeId>>(),
                It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Returns((ISystemContext context, AutomationComponentState actualComponent,
                    ArrayOf<NodeId> actualEndpoints, bool actualRemove, CancellationToken cancellationToken) =>
                {
                    Assert.That(context, Is.SameAs(harness.Context));
                    Assert.That(actualComponent, Is.SameAs(component));
                    Assert.That(actualEndpoints, Is.EqualTo(endpoints));
                    Assert.That(actualRemove, Is.EqualTo(remove));
                    Assert.That(cancellationToken, Is.EqualTo(CancellationToken.None));
                    return new ValueTask<ArrayOf<StatusCode>>(expected);
                });

            (ServiceResult result, List<Variant> outputs) = await harness.CallAsync(
                method, harness.Context, [Variant.From(endpoints), Variant.From(remove)]).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(outputs, Has.Count.EqualTo(1));
            Assert.That(outputs[0].TryGetValue(out ArrayOf<StatusCode> actualResults), Is.True);
            Assert.That(actualResults, Is.EqualTo(expected));
            lifecycle.Verify(value => value.CloseConnectionsAsync(
                It.IsAny<ISystemContext>(), It.IsAny<AutomationComponentState>(), It.IsAny<ArrayOf<NodeId>>(),
                It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        internal static ServiceCollection BuildServicesWithRuntime()
        {
            var services = new ServiceCollection();
            services.AddSingleton(NUnitTelemetryContext.Create());
            services.AddSingleton(
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
