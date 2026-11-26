// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Groups;
using Opc.Ua.PubSub.MetaData;
using Opc.Ua.PubSub.Redundancy;
using Opc.Ua.PubSub.Scheduling;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Security.Sks;
using Opc.Ua.PubSub.Tests.Security;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Tests.DependencyInjection
{
    /// <summary>
    /// Unit tests for
    /// <see cref="OpcUaPubSubBuilderExtensions"/>.
    /// </summary>
    [TestFixture]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class OpcUaPubSubBuilderExtensionsTests
    {
        [Test]
        public void AddPubSub_RegistersCoreServices()
        {
            var services = new ServiceCollection();
            services.AddSingleton(NUnitTelemetryContext.Create());
            IOpcUaBuilder builder = services.AddOpcUa();
            builder.AddPubSub();
            ServiceProvider sp = services.BuildServiceProvider();
            Assert.That(sp.GetService<IDataSetMetaDataRegistry>(), Is.Not.Null);
            Assert.That(sp.GetService<IPubSubDiagnostics>(), Is.Not.Null);
            Assert.That(sp.GetService<IPubSubScheduler>(), Is.Not.Null);
        }

        [Test]
        public void AddPubSub_RegistersHostedService()
        {
            var services = new ServiceCollection();
            services.AddSingleton(NUnitTelemetryContext.Create());
            services.AddLogging();
            IOpcUaBuilder builder = services.AddOpcUa();
            builder.AddPubSub();
            ServiceProvider sp = services.BuildServiceProvider();
            IEnumerable<IHostedService> hosted = sp.GetServices<IHostedService>();
            Assert.That(
                hosted.OfType<PubSubApplicationHostedService>(),
                Is.Not.Empty);
        }

        [Test]
        public void AddPubSub_ResolvesIPubSubApplication()
        {
            var services = new ServiceCollection();
            services.AddSingleton(NUnitTelemetryContext.Create());
            services.AddLogging();
            IOpcUaBuilder builder = services.AddOpcUa();
            builder.AddPubSub();
            ServiceProvider sp = services.BuildServiceProvider();
            IPubSubApplication? app = sp.GetService<IPubSubApplication>();
            Assert.That(app, Is.Not.Null);
        }

        [Test]
        public void AddPubSubPublisher_RegistersServices()
        {
            var services = new ServiceCollection();
            services.AddSingleton(NUnitTelemetryContext.Create());
            services.AddLogging();
            IOpcUaBuilder builder = services.AddOpcUa();
            builder.AddPubSubPublisher();
            ServiceProvider sp = services.BuildServiceProvider();
            Assert.That(sp.GetService<IPubSubApplication>(), Is.Not.Null);
        }

        [Test]
        public void AddPubSubSubscriber_RegistersServices()
        {
            var services = new ServiceCollection();
            services.AddSingleton(NUnitTelemetryContext.Create());
            services.AddLogging();
            IOpcUaBuilder builder = services.AddOpcUa();
            builder.AddPubSubSubscriber();
            ServiceProvider sp = services.BuildServiceProvider();
            Assert.That(sp.GetService<IPubSubApplication>(), Is.Not.Null);
        }

        [Test]
        public void AddPubSub_NullBuilder_Throws()
        {
            IOpcUaBuilder? builder = null;
            Assert.That(
                () => builder!.AddPubSub(),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddPubSubFluent_ResolvesIPubSubApplication()
        {
            var services = new ServiceCollection();
            services.AddSingleton(NUnitTelemetryContext.Create());
            services.AddLogging();
            services.AddOpcUa().AddPubSub(pubsub => pubsub.AddPublisher());
            ServiceProvider sp = services.BuildServiceProvider();
            Assert.That(sp.GetService<IPubSubApplication>(), Is.Not.Null);
        }

        [Test]
        public void AddPubSubFluent_NullConfigure_Throws()
        {
            var services = new ServiceCollection();
            services.AddSingleton(NUnitTelemetryContext.Create());
            IOpcUaBuilder builder = services.AddOpcUa();
            Assert.That(
                () => builder.AddPubSub((Action<IPubSubBuilder>)null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddPubSubFluent_NullBuilder_Throws()
        {
            IOpcUaBuilder? builder = null;
            Assert.That(
                () => builder!.AddPubSub(pubsub => pubsub.AddPublisher()),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddPubSubFluent_ConfigureApplication_IsApplied()
        {
            var services = new ServiceCollection();
            services.AddSingleton(NUnitTelemetryContext.Create());
            services.AddLogging();
            bool configureApplicationInvoked = false;
            services.AddOpcUa().AddPubSub(
                pubsub =>
                    pubsub.AddPublisher().ConfigureApplication(
                        app =>
                        {
                            configureApplicationInvoked = true;
                            app.WithApplicationId("urn:test:application");
                        }));
            ServiceProvider sp = services.BuildServiceProvider();
            _ = sp.GetRequiredService<IPubSubApplication>();
            Assert.That(configureApplicationInvoked, Is.True);
        }

        [Test]
        public void AddPubSubFluentAppliesUnauthenticatedMetaDataLimits()
        {
            var services = new ServiceCollection();
            services.AddSingleton(NUnitTelemetryContext.Create());
            services.AddLogging();
            services.AddOpcUa().AddPubSub(
                pubsub =>
                    pubsub.AddSubscriber().Configure(
                        options =>
                        {
                            options.MaxUnauthenticatedMetaDataCost = 4096;
                            options.UnauthenticatedMetaDataIdleTimeout = TimeSpan.Zero;
                        }));
            ServiceProvider sp = services.BuildServiceProvider();

            IPubSubApplication app = sp.GetRequiredService<IPubSubApplication>();
            var registry = (DataSetMetaDataRegistry)app.MetaDataRegistry;
            Assert.That(registry.MaxUnauthenticatedCost, Is.EqualTo(4096));
            Assert.That(registry.UnauthenticatedIdleTimeout, Is.EqualTo(TimeSpan.Zero));
        }

        [Test]
        public void AddPubSubFluent_AddSecurityKeyProvider_RegistersProvider()
        {
            var keyProvider = new Mock<IPubSubSecurityKeyProvider>();
            var services = new ServiceCollection();
            services.AddSingleton(NUnitTelemetryContext.Create());
            services.AddLogging();
            services.AddOpcUa().AddPubSub(
                pubsub =>
                    pubsub.AddSubscriber().AddSecurityKeyProvider(keyProvider.Object));
            ServiceProvider sp = services.BuildServiceProvider();
            Assert.That(
                sp.GetService<IPubSubSecurityKeyProvider>(),
                Is.SameAs(keyProvider.Object));
        }

        /// <summary>
        /// Retains the configured provider without fetching a key until a frame needs it.
        /// </summary>
        [Test]
        public async Task AddPubSubFluentAddSecurityKeyProviderDefersKeyLookupUntilUseAsync()
        {
            using PubSubSecurityKey key = TestSecurityKeyFactory.Create(1, encryptingKeyLength: 32);
            var keyProvider = new Mock<IPubSubSecurityKeyProvider>();
            keyProvider.Setup(static p => p.SecurityGroupId).Returns("group-1");
            keyProvider
                .Setup(static p => p.GetCurrentKeyAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<PubSubSecurityKey>(key));
            keyProvider
                .Setup(static p => p.TryGetKeyAsync(1, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<PubSubSecurityKey?>(key));
            var services = new ServiceCollection();
            services.AddSingleton(NUnitTelemetryContext.Create());
            services.AddLogging();
            services.AddPubSubTransportFactory(_ => new StubTransportFactory());

            services.AddOpcUa().AddPubSub(
                pubsub => pubsub
                    .UseConfiguration(CreateSecuredConfiguration())
                    .AddSecurityKeyProvider(keyProvider.Object));

            ServiceProvider sp = services.BuildServiceProvider();
            await using (sp.ConfigureAwait(false))
            {
                IPubSubApplication app = sp.GetRequiredService<IPubSubApplication>();
                Assert.That(app.Connections, Has.Count.EqualTo(1));
                keyProvider.Verify(
                    static p => p.GetCurrentKeyAsync(It.IsAny<CancellationToken>()),
                    Times.Never);

                PubSubSecurityContext security = sp.GetRequiredService<IPubSubSecurityWrapperResolver>()
                    .Resolve(app.Connections[0].Configuration) ??
                    throw new AssertionException("The configured connection has no security context.");
                byte[] prefix = [0xb1, 0x10, 0, 1, 0, 0, 0, 0];
                byte[] plaintext = [10, 20, 30, 40];
                ReadOnlyMemory<byte> encoded = await security.Wrapper.WrapAsync(prefix, plaintext)
                    .ConfigureAwait(false);
                UadpSecurityWrapper.UnwrapResult result = await security.Wrapper.TryUnwrapAsync(
                    prefix,
                    encoded[prefix.Length..]).ConfigureAwait(false);

                Assert.That(result.IsSuccess, Is.True, result.Reason);
                Assert.That(result.InnerPayload!.Value.ToArray(), Is.EqualTo(plaintext));
                keyProvider.Verify(
                    static p => p.GetCurrentKeyAsync(It.IsAny<CancellationToken>()),
                    Times.Once);
                keyProvider.Verify(
                    static p => p.TryGetKeyAsync(1, It.IsAny<CancellationToken>()),
                    Times.Once);
            }
        }

        [Test]
        public void AddPubSubFluent_ExposesServicesAndOpcUaBuilder()
        {
            var services = new ServiceCollection();
            services.AddSingleton(NUnitTelemetryContext.Create());
            IServiceCollection? captured = null;
            IOpcUaBuilder root = services.AddOpcUa();
            root.AddPubSub(
                pubsub =>
                {
                    captured = pubsub.Services;
                    Assert.That(pubsub.OpcUaBuilder, Is.SameAs(root));
                });
            Assert.That(captured, Is.SameAs(services));
        }

        [Test]
        [Description("OPC 10000-14 §9.1.6: HA deployments can replace PubSub state providers.")]
        public void AddPubSubFluent_WithProviders_RegistersProviderInstances()
        {
            var configurationStore = new InMemoryPubSubConfigurationStore();
            var idAllocator = new InMemoryPubSubIdAllocator();
            var runtimeStateStore = new InMemoryPubSubRuntimeStateStore();
            var securityKeyStore = new InMemoryPubSubSecurityKeyStore();
            var services = new ServiceCollection();
            services.AddSingleton(NUnitTelemetryContext.Create());
            services.AddLogging();

            services.AddOpcUa().AddPubSub(
                pubsub => pubsub
                    .WithConfigurationStore(configurationStore)
                    .WithIdAllocator(idAllocator)
                    .WithRuntimeStateStore(runtimeStateStore)
                    .WithSecurityKeyStore(securityKeyStore));

            ServiceProvider sp = services.BuildServiceProvider();

            Assert.That(sp.GetRequiredService<IPubSubConfigurationStore>(), Is.SameAs(configurationStore));
            Assert.That(sp.GetRequiredService<IPubSubIdAllocator>(), Is.SameAs(idAllocator));
            Assert.That(sp.GetRequiredService<IPubSubRuntimeStateStore>(), Is.SameAs(runtimeStateStore));
            Assert.That(sp.GetRequiredService<IPubSubSecurityKeyStore>(), Is.SameAs(securityKeyStore));
        }

        [Test]
        public async Task FluentPubSubRestoresRuntimeStateAndReadsCheckpointOnActivationAsync()
        {
            const string runtimeId = "pubsub:writer:Connection:Group:Writer";
            const string activationId = "pubsub:writergroup:Connection:Group";
            const ushort writerId = 7;
            var stateStore = new Mock<IPubSubRuntimeStateStore>();
            stateStore.Setup(store => store.GetStateAsync(runtimeId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(PubSubState.Paused);
            var checkpointStore = new Mock<IPubSubWriterCheckpointStore>();
            checkpointStore.Setup(store =>
                store.GetSequenceNumberAsync(activationId, writerId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(42u);
            var coordinator = new Mock<IPubSubActivationCoordinator>();
            coordinator.Setup(value => value.GetRoleAsync(activationId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(PubSubComponentRole.Active);
            var services = new ServiceCollection();
            services.AddSingleton(NUnitTelemetryContext.Create());
            services.AddLogging();
            services.AddSingleton<TimeProvider>(new FakeTimeProvider());
            services.AddSingleton(checkpointStore.Object);
            services.AddPubSubTransportFactory(_ => new StubTransportFactory());
            services.AddOpcUa().AddPubSub(pubsub => pubsub
                .WithRuntimeStateStore(stateStore.Object)
                .WithActivationCoordinator(coordinator.Object)
                .UseConfiguration(new PubSubConfigurationDataType
                {
                    Enabled = true,
                    PublishedDataSets = [new PublishedDataSetDataType { Name = "DataSet" }],
                    Connections =
                    [
                        new PubSubConnectionDataType
                        {
                            Name = "Connection",
                            Enabled = true,
                            TransportProfileUri = Profiles.PubSubUdpUadpTransport,
                            Address = new ExtensionObject(
                                new NetworkAddressUrlDataType { Url = "opc.udp://224.0.0.22:4840" }),
                            WriterGroups =
                            [
                                new WriterGroupDataType
                                {
                                    Name = "Group",
                                    Enabled = true,
                                    WriterGroupId = 1,
                                    PublishingInterval = 1000,
                                    DataSetWriters =
                                    [
                                        new DataSetWriterDataType
                                        {
                                            Name = "Writer",
                                            Enabled = true,
                                            DataSetName = "DataSet",
                                            DataSetWriterId = writerId
                                        }
                                    ]
                                }
                            ]
                        }
                    ]
                }));

            await using ServiceProvider provider = services.BuildServiceProvider();
            IPubSubApplication application = provider.GetRequiredService<IPubSubApplication>();
            var group = (WriterGroup)application.Connections[0].WriterGroups[0];
            Assert.That(group.DataSetWriters[0].State.State, Is.EqualTo(PubSubState.Paused));

            await application.StartAsync().ConfigureAwait(false);
            await group.PublishOnceAsync().ConfigureAwait(false);

            checkpointStore.Verify(store =>
                store.GetSequenceNumberAsync(activationId, writerId, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task AddPubSubFluentConfigureConfigurationBuildsAndAppliesConfigurationAsync()
        {
            var services = new ServiceCollection();
            services.AddSingleton(NUnitTelemetryContext.Create());
            services.AddLogging();

            services.AddOpcUa().AddPubSub(
                pubsub => pubsub.ConfigureConfiguration(
                    configuration =>
                        configuration
                            .Enabled(false)
                            .AddConnection(
                                "udp-connection",
                                connection =>
                                    connection.WithTransportProfile(Profiles.PubSubUdpUadpTransport))));

            await using ServiceProvider serviceProvider = services.BuildServiceProvider();
            PubSubConfigurationDataType configuration = serviceProvider
                .GetRequiredService<IPubSubApplication>()
                .GetConfiguration();

            Assert.Multiple(
                () =>
                {
                    Assert.That(configuration.Enabled, Is.False);
                    Assert.That(configuration.Connections, Has.Count.EqualTo(1));
                    Assert.That(configuration.Connections[0].Name, Is.EqualTo("udp-connection"));
                    Assert.That(
                        configuration.Connections[0].TransportProfileUri,
                        Is.EqualTo(Profiles.PubSubUdpUadpTransport));
                });
        }

        [Test]
        public void AddPubSubFluentConfigureConfigurationNullBuilderThrows()
        {
            IPubSubBuilder? builder = null;

            Assert.That(
                () => builder!.ConfigureConfiguration(_ => { }),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddPubSubFluentConfigureConfigurationNullConfigureThrows()
        {
            var services = new ServiceCollection();
            services.AddSingleton(NUnitTelemetryContext.Create());
            IPubSubBuilder captured = null!;
            services.AddOpcUa().AddPubSub(pubsub => captured = pubsub);

            Assert.That(
                () => captured.ConfigureConfiguration(null!),
                Throws.ArgumentNullException);
        }

        private static PubSubConfigurationDataType CreateSecuredConfiguration()
        {
            return new PubSubConfigurationDataType
            {
                Connections =
                [
                    new PubSubConnectionDataType
                    {
                        Name = "secured-conn",
                        TransportProfileUri = Profiles.PubSubUdpUadpTransport,
                        PublisherId = new Variant((ushort)7),
                        Address = new ExtensionObject(
                            new NetworkAddressUrlDataType
                            {
                                Url = "opc.udp://224.0.0.22:4840"
                            }),
                        WriterGroups =
                        [
                            new WriterGroupDataType
                            {
                                Name = "wg",
                                WriterGroupId = 1,
                                PublishingInterval = 1000,
                                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                                SecurityGroupId = "group-1",
                                SecurityKeyServices =
                                [
                                    new EndpointDescription
                                    {
                                        EndpointUrl = "opc.tcp://localhost:4840"
                                    }
                                ]
                            }
                        ]
                    }
                ],
                PublishedDataSets = []
            };
        }

        private sealed class StubTransportFactory : IPubSubTransportFactory
        {
            public string TransportProfileUri => Profiles.PubSubUdpUadpTransport;

            public IPubSubTransport Create(
                PubSubConnectionDataType connection,
                ITelemetryContext telemetry,
                TimeProvider timeProvider)
            {
                return new StubTransport();
            }
        }

        private sealed class StubTransport : IPubSubTransport
        {
            public string TransportProfileUri => Profiles.PubSubUdpUadpTransport;

            public PubSubTransportDirection Direction => PubSubTransportDirection.SendReceive;

            public bool IsConnected => false;

            public event EventHandler<PubSubTransportStateChangedEventArgs>? StateChanged
            {
                add { }
                remove { }
            }

            public ValueTask OpenAsync(CancellationToken cancellationToken = default)
            {
                return default;
            }

            public ValueTask CloseAsync(CancellationToken cancellationToken = default)
            {
                return default;
            }

            public ValueTask SendAsync(
                ReadOnlyMemory<byte> payload,
                string? topic = null,
                CancellationToken cancellationToken = default)
            {
                return default;
            }

            public IAsyncEnumerable<PubSubTransportFrame> ReceiveAsync(
                CancellationToken cancellationToken = default)
            {
                return TestAsyncEnumerable.Empty<PubSubTransportFrame>();
            }

            public ValueTask DisposeAsync()
            {
                return default;
            }
        }
    }
}
