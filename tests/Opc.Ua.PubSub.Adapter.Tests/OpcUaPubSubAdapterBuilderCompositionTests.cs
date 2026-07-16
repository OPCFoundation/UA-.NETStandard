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

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.PubSub.Adapter.DependencyInjection;
using Opc.Ua.PubSub.Adapter.Publisher;
using Opc.Ua.PubSub.Adapter.Session;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Adapter.Tests
{
    /// <summary>
    /// Verifies that the PubSub adapter DI extensions run their deferred
    /// composition steps and populate the mutable providers used by the runtime.
    /// </summary>
    [TestFixture]
    public sealed class OpcUaPubSubAdapterBuilderCompositionTests
    {
        [Test]
        public async Task ActionPublisherCompositionRegistersCyclicDataSetSourcesAsync()
        {
            PubSubConfigurationDataType configuration = AdapterTestHelpers.Configuration(
                500,
                [
                    AdapterTestHelpers.PublishedDataSet(
                        "PDS1", AdapterTestHelpers.Variable.Value(new NodeId(11u))),
                    AdapterTestHelpers.PublishedDataSet(
                        "PDS2", AdapterTestHelpers.Variable.Value(new NodeId(12u)))
                ]);
            MakeConnectionsBuildable(configuration);
            (ServiceCollection services, Mock<IServerSessionFactory> factory) = NewServices();
            services.AddOpcUa().AddPubSub(pubsub => pubsub
                .UseConfiguration(configuration)
                .AddServerAsPublisher(options =>
                {
                    options.Connection.EndpointUrl = "opc.tcp://publisher:4840";
                    options.ReadMode = ReadMode.Cyclic;
                }));

            await using ServiceProvider provider = services.BuildServiceProvider();
            _ = provider.GetRequiredService<IPubSubApplication>();

            MutableDataSetSourceProvider sources =
                provider.GetRequiredService<MutableDataSetSourceProvider>();
            Assert.That(sources.TryGetSource("PDS1", out IPublishedDataSetSource? first), Is.True);
            Assert.That(sources.TryGetSource("PDS2", out IPublishedDataSetSource? second), Is.True);
            Assert.That(first, Is.InstanceOf<ServerPublishedDataSetSource>());
            Assert.That(second, Is.InstanceOf<ServerPublishedDataSetSource>());
            factory.Verify(
                f => f.Create(It.IsAny<ServerConnectionOptions>(), It.IsAny<ITelemetryContext>()),
                Times.Once);
        }

        [Test]
        public async Task ConfigurationPublisherCompositionRegistersSubscriptionDataSetSourcesAsync()
        {
            PubSubConfigurationDataType configuration = AdapterTestHelpers.Configuration(
                500,
                [
                    AdapterTestHelpers.PublishedDataSet(
                        "Referenced", AdapterTestHelpers.Variable.Value(new NodeId(21u))),
                    AdapterTestHelpers.PublishedDataSet(
                        "Unreferenced", AdapterTestHelpers.Variable.Value(new NodeId(22u)))
                ]);
            MakeConnectionsBuildable(configuration);
            IConfiguration options = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Connection:EndpointUrl"] = "opc.tcp://subscription-publisher:4840",
                    ["ReadMode"] = nameof(ReadMode.Subscription),
                    ["Affinity"] = nameof(SubscriptionAffinity.WriterGroup)
                })
                .Build();
            configuration.Connections[0].WriterGroups[0].DataSetWriters =
                new[]
                {
                    new DataSetWriterDataType
                    {
                        Name = "WriterReferenced",
                        DataSetWriterId = 1,
                        DataSetName = "Referenced"
                    }
                }.ToArrayOf();
            (ServiceCollection services, _) = NewServices();
            services.AddOpcUa().AddPubSub(pubsub => pubsub
                .UseConfiguration(configuration)
                .AddServerAsPublisher("publisher1", options));

            await using ServiceProvider provider = services.BuildServiceProvider();
            _ = provider.GetRequiredService<IPubSubApplication>();

            MutableDataSetSourceProvider sources =
                provider.GetRequiredService<MutableDataSetSourceProvider>();
            Assert.That(sources.TryGetSource("Referenced", out IPublishedDataSetSource? source), Is.True);
            Assert.That(source, Is.InstanceOf<ServerPublishedDataSetSource>());
            Assert.That(sources.TryGetSource("Unreferenced", out _), Is.False);
        }

        [Test]
        public async Task ActionSubscriberCompositionRegistersTargetVariableSinksAsync()
        {
            PubSubConfigurationDataType configuration = SubscriberConfiguration(
                "Reader1", new NodeId(101u));
            MakeConnectionsBuildable(configuration);
            (ServiceCollection services, Mock<IServerSessionFactory> factory) = NewServices();
            services.AddOpcUa().AddPubSub(pubsub => pubsub
                .UseConfiguration(configuration)
                .AddServerAsSubscriber(options =>
                    options.Connection.EndpointUrl = "opc.tcp://subscriber:4840"));

            await using ServiceProvider provider = services.BuildServiceProvider();
            _ = provider.GetRequiredService<IPubSubApplication>();

            MutableDataSetSinkProvider sinks =
                provider.GetRequiredService<MutableDataSetSinkProvider>();
            Assert.That(sinks.TryGetSink("Reader1", out ISubscribedDataSetSink? sink), Is.True);
            Assert.That(sink, Is.Not.Null);
            factory.Verify(
                f => f.Create(It.IsAny<ServerConnectionOptions>(), It.IsAny<ITelemetryContext>()),
                Times.Once);
        }

        [Test]
        public async Task ConfigurationSubscriberCompositionRegistersTargetVariableSinksAsync()
        {
            PubSubConfigurationDataType configuration = SubscriberConfiguration(
                "ConfiguredReader", new NodeId(102u));
            MakeConnectionsBuildable(configuration);
            IConfiguration options = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Connection:EndpointUrl"] = "opc.tcp://configured-subscriber:4840"
                })
                .Build();
            (ServiceCollection services, _) = NewServices();
            services.AddOpcUa().AddPubSub(pubsub => pubsub
                .UseConfiguration(configuration)
                .AddServerAsSubscriber("subscriber1", options));

            await using ServiceProvider provider = services.BuildServiceProvider();
            _ = provider.GetRequiredService<IPubSubApplication>();

            MutableDataSetSinkProvider sinks =
                provider.GetRequiredService<MutableDataSetSinkProvider>();
            Assert.That(sinks.TryGetSink("ConfiguredReader", out ISubscribedDataSetSink? sink), Is.True);
            Assert.That(sink, Is.Not.Null);
        }

        [Test]
        public async Task ActionResponderCompositionAcquiresSessionForConfiguredTargetsAsync()
        {
            var target = new PubSubActionTarget { ActionName = "DoIt" };
            (ServiceCollection services, Mock<IServerSessionFactory> factory) = NewServices();
            services.AddOpcUa().AddPubSub(pubsub => pubsub
                .UseConfiguration(new PubSubConfigurationDataType())
                .AddServerAsActionResponder(options =>
                {
                    options.Connection.EndpointUrl = "opc.tcp://actions:4840";
                    options.AllowUnsecured = true;
                    options.MethodMap.Add("DoIt", new NodeId(1u), new NodeId(2u));
                    options.Targets.Add(target);
                }));

            await using ServiceProvider provider = services.BuildServiceProvider();
            _ = provider.GetRequiredService<IPubSubApplication>();

            factory.Verify(
                f => f.Create(
                    It.Is<ServerConnectionOptions>(
                        options => options.EndpointUrl == "opc.tcp://actions:4840"),
                    It.IsAny<ITelemetryContext>()),
                Times.Once);
        }

        [Test]
        public async Task ConfigurationActionResponderCompositionBindsAndAcquiresSessionAsync()
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Connection:EndpointUrl"] = "opc.tcp://configured-actions:4840",
                    ["AllowUnsecured"] = "true"
                })
                .Build();
            (ServiceCollection services, Mock<IServerSessionFactory> factory) = NewServices();
            services.AddOpcUa().AddPubSub(pubsub => pubsub
                .UseConfiguration(new PubSubConfigurationDataType())
                .AddServerAsActionResponder("actions1", configuration));
            services.Configure<ServerActionResponderOptions>("actions1", options =>
            {
                options.MethodMap.Add("Configured", new NodeId(3u), new NodeId(4u));
                options.Targets.Add(new PubSubActionTarget { ActionName = "Configured" });
            });

            await using ServiceProvider provider = services.BuildServiceProvider();
            _ = provider.GetRequiredService<IPubSubApplication>();

            factory.Verify(
                f => f.Create(
                    It.Is<ServerConnectionOptions>(
                        options => options.EndpointUrl == "opc.tcp://configured-actions:4840"),
                    It.IsAny<ITelemetryContext>()),
                Times.Once);
        }

        private static (ServiceCollection Services, Mock<IServerSessionFactory> Factory)
            NewServices()
        {
            var services = new ServiceCollection();
            services.AddSingleton(NUnitTelemetryContext.Create());
            services.AddLogging();

            var factory = new Mock<IServerSessionFactory>();
            factory
                .Setup(f => f.Create(
                    It.IsAny<ServerConnectionOptions>(), It.IsAny<ITelemetryContext>()))
                .Returns(() => AdapterTestHelpers.ConnectedSession().Object);
            services.AddSingleton(factory.Object);
            return (services, factory);
        }

        private static PubSubConfigurationDataType SubscriberConfiguration(
            string readerName,
            NodeId targetNode)
        {
            var reader = new DataSetReaderDataType
            {
                Name = readerName,
                DataSetWriterId = 1,
                MessageReceiveTimeout = 1000,
                SubscribedDataSet = new ExtensionObject(new TargetVariablesDataType
                {
                    TargetVariables =
                    [
                        new FieldTargetDataType
                        {
                            TargetNodeId = targetNode,
                            AttributeId = Attributes.Value
                        }
                    ]
                })
            };
            var readerGroup = new ReaderGroupDataType
            {
                Name = "ReaderGroup1",
                DataSetReaders = new[] { reader }.ToArrayOf()
            };
            var connection = new PubSubConnectionDataType
            {
                Name = "Connection1",
                ReaderGroups = new[] { readerGroup }.ToArrayOf()
            };
            return new PubSubConfigurationDataType
            {
                Connections = new[] { connection }.ToArrayOf()
            };
        }

        private static void MakeConnectionsBuildable(PubSubConfigurationDataType configuration)
        {
            foreach (PubSubConnectionDataType connection in configuration.Connections)
            {
                connection.TransportProfileUri = Profiles.PubSubUdpUadpTransport;
                connection.Address = new ExtensionObject(new NetworkAddressUrlDataType
                {
                    Url = "opc.udp://239.0.0.1:4840"
                });
            }
        }
    }
}
