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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using NUnit.Framework;
using Opc.Ua.PubSub.Adapter;
using Opc.Ua.PubSub.Adapter.DependencyInjection;
using Opc.Ua.PubSub.Adapter.Session;
using Opc.Ua.PubSub.Application;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Adapter.Tests.Unit
{
    /// <summary>
    /// Unit tests for <see cref="OpcUaPubSubAdapterBuilderExtensions"/>: argument
    /// validation, core-service registration and that the deferred composition
    /// step creates external sessions for publishers, subscribers and responders.
    /// </summary>
    [TestFixture]
    public sealed class OpcUaPubSubAdapterBuilderExtensionsTests
    {
        private static (ServiceCollection Services, Mock<IServerSessionFactory> Factory)
            NewServices()
        {
            var services = new ServiceCollection();
            services.AddSingleton<ITelemetryContext>(NUnitTelemetryContext.Create());
            services.AddLogging();

            var factory = new Mock<IServerSessionFactory>();
            factory
                .Setup(f => f.Create(
                    It.IsAny<ServerConnectionOptions>(), It.IsAny<ITelemetryContext>()))
                .Returns(() => AdapterTestHelpers.ConnectedSession().Object);
            services.AddSingleton(factory.Object);
            return (services, factory);
        }

        private static PubSubConfigurationDataType SubscriberConfiguration(NodeId targetNode)
        {
            var reader = new DataSetReaderDataType
            {
                Name = "Reader1",
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

        [Test]
        public void AddServerAsPublisherNullBuilderThrows()
        {
            IPubSubBuilder? builder = null;

            Assert.That(
                () => builder!.AddServerAsPublisher(_ => { }),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddServerAsPublisherNullConfigureThrows()
        {
            (ServiceCollection services, _) = NewServices();

            services.AddOpcUa().AddPubSub(pubsub =>
                Assert.That(
                    () => pubsub.AddServerAsPublisher(null!),
                    Throws.ArgumentNullException));
        }

        [Test]
        public void AddServerAsSubscriberNullConfigureThrows()
        {
            (ServiceCollection services, _) = NewServices();

            services.AddOpcUa().AddPubSub(pubsub =>
                Assert.That(
                    () => pubsub.AddServerAsSubscriber(null!),
                    Throws.ArgumentNullException));
        }

        [Test]
        public void AddServerAsActionResponderNullConfigureThrows()
        {
            (ServiceCollection services, _) = NewServices();

            services.AddOpcUa().AddPubSub(pubsub =>
                Assert.That(
                    () => pubsub.AddServerAsActionResponder(null!),
                    Throws.ArgumentNullException));
        }

        [Test]
        public void AddServerAsPublisherRegistersCoreServices()
        {
            (ServiceCollection services, _) = NewServices();
            services.AddOpcUa().AddPubSub(pubsub =>
                pubsub.AddServerAsPublisher(o => o.Connection.EndpointUrl =
                    "opc.tcp://localhost:4840"));

            ServiceProvider sp = services.BuildServiceProvider();

            Assert.That(sp.GetService<IServerSessionFactory>(), Is.Not.Null);
            Assert.That(sp.GetService<ServerAdapterRuntime>(), Is.Not.Null);
            Assert.That(
                sp.GetServices<IHostedService>().OfType<ServerAdapterHostedService>(),
                Is.Not.Empty);
        }

        [Test]
        public void AddServerAsPublisherWithConfigurationRegistersFactory()
        {
            (ServiceCollection services, _) = NewServices();
            PubSubConfigurationDataType config = AdapterTestHelpers.Configuration(
                500,
                new[]
                {
                    AdapterTestHelpers.PublishedDataSet(
                        "PDS", AdapterTestHelpers.Variable.Value(new NodeId(11u)))
                });

            services.AddOpcUa().AddPubSub(pubsub => pubsub
                .UseConfiguration(config)
                .AddServerAsPublisher(o =>
                {
                    o.Connection.EndpointUrl = "opc.tcp://localhost:4840";
                    o.ReadMode = ReadMode.Subscription;
                    o.Affinity = SubscriptionAffinity.DataSetWriter;
                }));
            ServiceProvider sp = services.BuildServiceProvider();

            Assert.That(sp.GetService<IServerSessionFactory>(), Is.Not.Null);
            Assert.That(sp.GetService<ServerAdapterRuntime>(), Is.Not.Null);
        }

        [Test]
        public void AddServerAsSubscriberWithConfigurationRegistersFactory()
        {
            (ServiceCollection services, _) = NewServices();
            PubSubConfigurationDataType config = SubscriberConfiguration(new NodeId(7u));

            services.AddOpcUa().AddPubSub(pubsub => pubsub
                .UseConfiguration(config)
                .AddServerAsSubscriber(o =>
                    o.Connection.EndpointUrl = "opc.tcp://localhost:4840"));
            ServiceProvider sp = services.BuildServiceProvider();

            Assert.That(sp.GetService<IServerSessionFactory>(), Is.Not.Null);
            Assert.That(sp.GetService<ServerAdapterRuntime>(), Is.Not.Null);
        }

        [Test]
        public void AddServerAsActionResponderRegistersFactory()
        {
            (ServiceCollection services, _) = NewServices();

            services.AddOpcUa().AddPubSub(pubsub => pubsub
                .AddServerAsActionResponder(o =>
                {
                    o.Connection.EndpointUrl = "opc.tcp://localhost:4840";
                    o.AllowUnsecured = true;
                    o.MethodMap.Add("DoIt", new NodeId(1u), new NodeId(2u));
                    o.Targets = new List<PubSubActionTarget>
                    {
                        new() { ActionName = "DoIt" }
                    };
                }));
            ServiceProvider sp = services.BuildServiceProvider();

            Assert.That(sp.GetService<IServerSessionFactory>(), Is.Not.Null);
            Assert.That(sp.GetService<ServerAdapterRuntime>(), Is.Not.Null);
        }
    }
}
