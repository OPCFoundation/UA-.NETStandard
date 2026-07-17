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
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.MetaData;
using Opc.Ua.PubSub.Scheduling;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Security.Sks;
using Opc.Ua.PubSub.Tests.Security;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.Schema;
using Opc.Ua.Tests;
using PubSubJsonEncoder = Opc.Ua.PubSub.Encoding.Json.JsonEncoder;
using PubSubJsonNetworkMessage = Opc.Ua.PubSub.Encoding.Json.JsonNetworkMessage;
using PubSubJsonDataSetMessage = Opc.Ua.PubSub.Encoding.Json.JsonDataSetMessage;

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
            using ServiceProvider sp = services.BuildServiceProvider();
            Assert.That(sp.GetService<IDataSetMetaDataRegistry>(), Is.Not.Null);
            Assert.That(sp.GetService<IPubSubDiagnostics>(), Is.Not.Null);
            Assert.That(sp.GetService<IPubSubScheduler>(), Is.Not.Null);
        }

        [Test]
        public void AddPubSub_RegistersAvroNetworkMessageEncoderAndDecoder()
        {
            var services = new ServiceCollection();
            services.AddSingleton(NUnitTelemetryContext.Create());
            IOpcUaBuilder builder = services.AddOpcUa();
            builder.AddPubSub();
            using ServiceProvider sp = services.BuildServiceProvider();

            Assert.That(
                sp.GetServices<INetworkMessageEncoder>()
                    .Any(e => e.TransportProfileUri == AvroNetworkMessage.PubSubMqttAvroTransport),
                Is.True,
                "The Avro NetworkMessage encoder should be registered for transcoding.");
            Assert.That(
                sp.GetServices<INetworkMessageDecoder>()
                    .Any(d => d.TransportProfileUri == AvroNetworkMessage.PubSubMqttAvroTransport),
                Is.True,
                "The Avro NetworkMessage decoder should be registered for transcoding.");
        }

        [Test]
        public void AddPubSubAloneKeepsJsonSchemaExchangeDisabled()
        {
            var services = new ServiceCollection();
            services.AddSingleton(NUnitTelemetryContext.Create());
            services.AddOpcUa().AddPubSub();
            using ServiceProvider sp = services.BuildServiceProvider();

            PubSubApplicationOptions options = sp.GetRequiredService<IOptions<PubSubApplicationOptions>>().Value;
            PubSubJsonEncoder encoder = sp.GetServices<INetworkMessageEncoder>().OfType<PubSubJsonEncoder>().Single();

            Assert.Multiple(() =>
            {
                Assert.That(options.JsonSchemaExchange, Is.EqualTo(JsonSchemaExchangeMode.Disabled));
                Assert.That(encoder.EnableSchemaExchange, Is.False);
                Assert.That(encoder.SchemaProvider, Is.Null);
                Assert.That(encoder.LastSchemaAnnouncement, Is.Null);
            });
        }

        [Test]
        public void AddJsonSchemaExchangeRegistersProviderAndEnablesJsonEncoder()
        {
            var services = new ServiceCollection();
            services.AddSingleton(NUnitTelemetryContext.Create());
            services.AddOpcUa().AddPubSub().AddJsonSchemaExchange(options =>
            {
                options.Verbose = true;
                options.DestinationId = "subscriber-a";
            });
            using ServiceProvider sp = services.BuildServiceProvider();

            IDataSetJsonSchemaProvider dataSetProvider = sp.GetRequiredService<IDataSetJsonSchemaProvider>();
            ISchemaProvider schemaProvider = sp.GetRequiredService<ISchemaProvider>();
            DataTypeDefinitionRegistry registry = sp.GetRequiredService<DataTypeDefinitionRegistry>();
            IDataTypeDefinitionResolver resolver = sp.GetRequiredService<IDataTypeDefinitionResolver>();
            PubSubJsonEncoder encoder = sp.GetServices<INetworkMessageEncoder>().OfType<PubSubJsonEncoder>().Single();

            Assert.Multiple(() =>
            {
                Assert.That(dataSetProvider, Is.Not.Null);
                Assert.That(schemaProvider, Is.Not.Null);
                Assert.That(resolver, Is.SameAs(registry));
                Assert.That(GetProviderRegistry(dataSetProvider), Is.SameAs(registry));
                Assert.That(encoder.EnableSchemaExchange, Is.True);
                Assert.That(encoder.SchemaProvider, Is.SameAs(dataSetProvider));
                Assert.That(encoder.SchemaVerbose, Is.True);
                Assert.That(encoder.DestinationId, Is.EqualTo("subscriber-a"));
            });
        }

        [Test]
        public async Task AddJsonSchemaExchangeEncoderProducesCacheableAnnouncementAsync()
        {
            DataSetMetaDataType metaData = CreateJsonSchemaMetaData();
            PubSubNetworkMessageContext context = CreateJsonContext(100, metaData);
            PubSubJsonNetworkMessage message = CreateJsonNetworkMessage(metaData);
            var services = new ServiceCollection();
            services.AddSingleton(NUnitTelemetryContext.Create());
            services.AddOpcUa().AddPubSub().AddJsonSchemaExchange();
            using ServiceProvider sp = services.BuildServiceProvider();
            PubSubJsonEncoder encoder = sp.GetServices<INetworkMessageEncoder>().OfType<PubSubJsonEncoder>().Single();

            _ = await encoder.EncodeAsync(message, context);
            JsonSchemaAnnouncement? announcement = encoder.LastSchemaAnnouncement;
            SchemaCache cache = new();
            cache.Add(announcement!);

            Assert.Multiple(() =>
            {
                Assert.That(announcement, Is.Not.Null);
                Assert.That(cache.TryGet(announcement!.SchemaId, out SchemaCacheEntry entry), Is.True);
                Assert.That(entry.Format, Is.EqualTo(SchemaCache.JsonFormat));
                byte[] schemaBytes = System.Text.Encoding.UTF8.GetBytes(announcement.SchemaJson);
                Assert.That(entry.Schema.Span.SequenceEqual(schemaBytes), Is.True);
            });
        }

        [Test]
        public void AddJsonSchemaExchangeNullBuilderThrows()
        {
            IOpcUaBuilder? builder = null;

            Assert.That(
                () => builder!.AddJsonSchemaExchange(),
                Throws.ArgumentNullException);
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
        public void AddPubSubConfigureCallbackAppliesOptions()
        {
            var services = new ServiceCollection();
            services.AddSingleton(NUnitTelemetryContext.Create());
            IOpcUaBuilder builder = services.AddOpcUa();

            builder.AddPubSub(options =>
            {
                options.DiagnosticsLevel = PubSubDiagnosticsLevel.High;
                options.RegisterUdpTransport = false;
            });

            Assert.That(
                services.Any(static descriptor =>
                    descriptor.ServiceType == typeof(IConfigureOptions<PubSubApplicationOptions>)),
                Is.True);
        }

        [Test]
        public void AddPubSubConfigurationOverloadsBindOptionsAndValidateArguments()
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["OpcUa:PubSub:DiagnosticsLevel"] = "High",
                    ["CustomPubSub:DiagnosticsLevel"] = "Low"
                })
                .Build();
            var services = new ServiceCollection();
            services.AddSingleton(NUnitTelemetryContext.Create());
            IOpcUaBuilder builder = services.AddOpcUa();

            builder.AddPubSub(configuration);
            using ServiceProvider sp = services.BuildServiceProvider();

            Assert.That(
                sp.GetRequiredService<IOptions<PubSubApplicationOptions>>().Value.DiagnosticsLevel,
                Is.EqualTo(PubSubDiagnosticsLevel.High));

            var customServices = new ServiceCollection();
            customServices.AddSingleton(NUnitTelemetryContext.Create());
            IOpcUaBuilder customBuilder = customServices.AddOpcUa();
            customBuilder.AddPubSub(configuration.GetSection("CustomPubSub"));
            using ServiceProvider customSp = customServices.BuildServiceProvider();
            Assert.That(
                customSp.GetRequiredService<IOptions<PubSubApplicationOptions>>().Value.DiagnosticsLevel,
                Is.EqualTo(PubSubDiagnosticsLevel.Low));

            IOpcUaBuilder? nullBuilder = null;
            Assert.That(
                () => nullBuilder!.AddPubSub(configuration),
                Throws.ArgumentNullException);
            Assert.That(
                () => builder.AddPubSub((IConfiguration)null!),
                Throws.ArgumentNullException);
            Assert.That(
                () => nullBuilder!.AddPubSub(configuration.GetSection("CustomPubSub")),
                Throws.ArgumentNullException);
            Assert.That(
                () => builder.AddPubSub((IConfigurationSection)null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public async Task InlineConfigurationStoreCoversVersionAndPublishedDataSetBranchesAsync()
        {
            PubSubConfigurationDataType configuration = CreateConfigurationWithPublishedDataSet();
            var services = new ServiceCollection();
            services.AddSingleton(NUnitTelemetryContext.Create());
            services.AddOpcUa().AddPubSub(options => options.InlineConfiguration = configuration);
            using ServiceProvider sp = services.BuildServiceProvider();
            IPubSubConfigurationStore store = sp.GetRequiredService<IPubSubConfigurationStore>();

            PubSubConfigurationDataType loaded = await store.LoadAsync();
            await store.SaveAsync(new PubSubConfigurationDataType());
            ConfigurationVersionDataType? initialVersion = await store.GetConfigurationVersionAsync();
            var appVersion = new ConfigurationVersionDataType { MajorVersion = 7, MinorVersion = 8 };
            await store.SetConfigurationVersionAsync(appVersion);
            ConfigurationVersionDataType? storedVersion = await store.GetConfigurationVersionAsync();
            ConfigurationVersionDataType? dataSetVersion =
                await store.GetPublishedDataSetConfigurationVersionAsync("PublishedDataSet");
            ConfigurationVersionDataType? missingDataSetVersion =
                await store.GetPublishedDataSetConfigurationVersionAsync("Missing");
            var replacement = new ConfigurationVersionDataType { MajorVersion = 9, MinorVersion = 10 };
            await store.SetPublishedDataSetConfigurationVersionAsync("PublishedDataSet", replacement);

            Assert.Multiple(() =>
            {
                Assert.That(loaded, Is.SameAs(configuration));
                Assert.That(initialVersion, Is.Null);
                Assert.That(storedVersion, Is.Not.Null);
                Assert.That(storedVersion!.MajorVersion, Is.EqualTo(7));
                Assert.That(storedVersion.MinorVersion, Is.EqualTo(8));
                Assert.That(storedVersion, Is.Not.SameAs(appVersion));
                Assert.That(dataSetVersion, Is.Not.Null);
                Assert.That(dataSetVersion!.MajorVersion, Is.EqualTo(1));
                Assert.That(missingDataSetVersion, Is.Null);
                Assert.That(
                    configuration.PublishedDataSets[0].DataSetMetaData!.ConfigurationVersion.MajorVersion,
                    Is.EqualTo(9));
            });

            Assert.That(
                async () => await store.SetConfigurationVersionAsync(null!),
                Throws.ArgumentNullException);
            Assert.That(
                async () => await store.SetPublishedDataSetConfigurationVersionAsync("PublishedDataSet", null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public async Task InlineConfigurationStoreHandlesNullPublishedDataSetsAsync()
        {
            var services = new ServiceCollection();
            services.AddSingleton(NUnitTelemetryContext.Create());
            services.AddOpcUa().AddPubSub();
            using ServiceProvider sp = services.BuildServiceProvider();
            IPubSubConfigurationStore store = sp.GetRequiredService<IPubSubConfigurationStore>();

            ConfigurationVersionDataType? version =
                await store.GetPublishedDataSetConfigurationVersionAsync("PublishedDataSet");
            await store.SetPublishedDataSetConfigurationVersionAsync(
                "PublishedDataSet",
                new ConfigurationVersionDataType { MajorVersion = 1, MinorVersion = 2 });

            Assert.That(version, Is.Null);
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
            services.AddOpcUa().AddPubSub(pubsub =>
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
        public void AddPubSubFluent_AddSecurityKeyProvider_RegistersProvider()
        {
            var keyProvider = new Mock<IPubSubSecurityKeyProvider>();
            var services = new ServiceCollection();
            services.AddSingleton(NUnitTelemetryContext.Create());
            services.AddLogging();
            services.AddOpcUa().AddPubSub(pubsub =>
                pubsub.AddSubscriber().AddSecurityKeyProvider(keyProvider.Object));
            ServiceProvider sp = services.BuildServiceProvider();
            Assert.That(
                sp.GetService<IPubSubSecurityKeyProvider>(),
                Is.SameAs(keyProvider.Object));
        }

        [Test]
        public async Task AddPubSubFluentAddSecurityKeyProviderBuildConsumesProviderAsync()
        {
            var keyProvider = new Mock<IPubSubSecurityKeyProvider>();
            keyProvider.Setup(static p => p.SecurityGroupId).Returns("group-1");
            keyProvider
                .Setup(static p => p.GetCurrentKeyAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<PubSubSecurityKey>(TestSecurityKeyFactory.Create(1)));
            var services = new ServiceCollection();
            services.AddSingleton(NUnitTelemetryContext.Create());
            services.AddLogging();
            services.AddPubSubTransportFactory(_ => new StubTransportFactory());

            services.AddOpcUa().AddPubSub(pubsub => pubsub
                .UseConfiguration(CreateSecuredConfiguration())
                .AddSecurityKeyProvider(keyProvider.Object));

            await using ServiceProvider sp = services.BuildServiceProvider();
            IPubSubApplication app = sp.GetRequiredService<IPubSubApplication>();

            Assert.Multiple(() =>
            {
                Assert.That(app.Connections, Has.Count.EqualTo(1));
                keyProvider.Verify(
                    static p => p.GetCurrentKeyAsync(It.IsAny<CancellationToken>()),
                    Times.Once);
            });
        }

        [Test]
        public void AddPubSubFluent_ExposesServicesAndOpcUaBuilder()
        {
            var services = new ServiceCollection();
            services.AddSingleton(NUnitTelemetryContext.Create());
            IServiceCollection? captured = null;
            IOpcUaBuilder root = services.AddOpcUa();
            root.AddPubSub(pubsub =>
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

            services.AddOpcUa().AddPubSub(pubsub => pubsub
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
        public async Task AddPubSubFluentConfigureConfigurationBuildsAndAppliesConfigurationAsync()
        {
            var services = new ServiceCollection();
            services.AddSingleton(NUnitTelemetryContext.Create());
            services.AddLogging();

            services.AddOpcUa().AddPubSub(pubsub => pubsub.ConfigureConfiguration(configuration =>
                configuration
                    .Enabled(false)
                    .AddConnection("udp-connection", connection =>
                        connection.WithTransportProfile(Profiles.PubSubUdpUadpTransport))));

            await using ServiceProvider serviceProvider = services.BuildServiceProvider();
            PubSubConfigurationDataType configuration = serviceProvider
                .GetRequiredService<IPubSubApplication>()
                .GetConfiguration();

            Assert.Multiple(() =>
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

        private static PubSubConfigurationDataType CreateConfigurationWithPublishedDataSet()
        {
            return new PubSubConfigurationDataType
            {
                PublishedDataSets =
                [
                    new PublishedDataSetDataType
                    {
                        Name = "PublishedDataSet",
                        DataSetMetaData = new DataSetMetaDataType
                        {
                            ConfigurationVersion = new ConfigurationVersionDataType
                            {
                                MajorVersion = 1,
                                MinorVersion = 2
                            }
                        }
                    },
                    new PublishedDataSetDataType
                    {
                        Name = "NoMetaData"
                    }
                ]
            };
        }

        private static DataSetMetaDataType CreateJsonSchemaMetaData()
        {
            return new DataSetMetaDataType
            {
                Name = "JsonSchemaExchangeDataSet",
                ConfigurationVersion = new ConfigurationVersionDataType { MajorVersion = 1, MinorVersion = 0 },
                Fields =
                [
                    new FieldMetaData
                    {
                        Name = "Temperature",
                        BuiltInType = (byte)BuiltInType.Double,
                        ValueRank = ValueRanks.Scalar
                    }
                ]
            };
        }

        private static PubSubJsonNetworkMessage CreateJsonNetworkMessage(DataSetMetaDataType metaData)
        {
            return new PubSubJsonNetworkMessage
            {
                PublisherId = PublisherId.FromString("publisher"),
                WriterGroupId = 1,
                DataSetClassId = new Uuid(new Guid("8c2f8f1c-c9a1-48b0-a90b-7d8f6e725001")),
                MetaData = metaData,
                DataSetMessages =
                [
                    new PubSubJsonDataSetMessage
                    {
                        DataSetWriterId = 100,
                        MessageType = PubSubDataSetMessageType.KeyFrame,
                        MetaDataVersion = new ConfigurationVersionDataType { MajorVersion = 1, MinorVersion = 0 }
                    }
                ]
            };
        }

        private static PubSubNetworkMessageContext CreateJsonContext(ushort writerId, DataSetMetaDataType metaData)
        {
            DataSetMetaDataRegistry registry = new();
            PublisherId publisherId = PublisherId.FromString("publisher");
            Uuid dataSetClassId = new(new Guid("8c2f8f1c-c9a1-48b0-a90b-7d8f6e725001"));
            DataSetMetaDataKey key = new(publisherId, 1, writerId, dataSetClassId, 1);
            registry.Register(in key, metaData);
            return new PubSubNetworkMessageContext(
                ServiceMessageContext.CreateEmpty(null!),
                registry,
                new PubSubDiagnostics(PubSubDiagnosticsLevel.High),
                TimeProvider.System);
        }

        private static DataTypeDefinitionRegistry GetProviderRegistry(IDataSetJsonSchemaProvider provider)
        {
            FieldInfo field = typeof(DataSetJsonSchemaProvider).GetField(
                "m_registry",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            return (DataTypeDefinitionRegistry)field.GetValue(provider)!;
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
