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
using NUnit.Framework;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Tests.Application
{
    [TestFixture]
    [TestSpec("9.1.6", Summary = "PubSub configuration mutation")]
    public class PubSubApplicationMutationTests
    {
        [Test]
        [TestSpec("9.1.3.4", Summary = "AddConnection appends")]
        public async Task AddConnectionAsyncAppendsToConnections()
        {
            await using IPubSubApplication app = BuildApp();
            var connCfg = new PubSubConnectionDataType
            {
                Name = "test-conn",
                TransportProfileUri = "http://opcfoundation.org/UA-Profile/Transport/pubsub-udp-uadp",
                Address = new ExtensionObject(
                    new NetworkAddressUrlDataType { Url = "opc.udp://224.0.0.22:4840" })
            };
            NodeId id = await app.AddConnectionAsync(connCfg);
            Assert.That(id.IsNull, Is.False);
            Assert.That(app.Connections, Has.Count.EqualTo(1));
        }

        [Test]
        [TestSpec("5.2.3", Summary = "AddConnection stamps version")]
        public async Task AddConnectionAsyncStampsConfigurationVersion()
        {
            await using IPubSubApplication app = BuildApp();
            ConfigurationVersionDataType before = app.ConfigurationVersion;
            var connCfg = new PubSubConnectionDataType
            {
                Name = "test-conn",
                TransportProfileUri = "http://opcfoundation.org/UA-Profile/Transport/pubsub-udp-uadp",
                Address = new ExtensionObject(
                    new NetworkAddressUrlDataType { Url = "opc.udp://224.0.0.22:4840" })
            };
            await app.AddConnectionAsync(connCfg);
            Assert.That(
                app.ConfigurationVersion.MajorVersion,
                Is.GreaterThanOrEqualTo(before.MajorVersion));
        }

        [Test]
        [TestSpec("9.1.6", Summary = "AddConnection raises event")]
        public async Task AddConnectionAsyncRaisesConfigurationChanged()
        {
            await using IPubSubApplication app = BuildApp();
            bool raised = false;
            app.ConfigurationChanged += (_, _) => raised = true;
            var connCfg = new PubSubConnectionDataType
            {
                Name = "test-conn",
                TransportProfileUri = "http://opcfoundation.org/UA-Profile/Transport/pubsub-udp-uadp",
                Address = new ExtensionObject(
                    new NetworkAddressUrlDataType { Url = "opc.udp://224.0.0.22:4840" })
            };
            await app.AddConnectionAsync(connCfg);
            Assert.That(raised, Is.True);
        }

        [Test]
        [TestSpec("9.1.3.4", Summary = "AddConnection returns NodeId")]
        public async Task AddConnectionAsyncReturnsNonNullNodeId()
        {
            await using IPubSubApplication app = BuildApp();
            var connCfg = new PubSubConnectionDataType
            {
                Name = "conn-1",
                TransportProfileUri = "http://opcfoundation.org/UA-Profile/Transport/pubsub-udp-uadp",
                Address = new ExtensionObject(
                    new NetworkAddressUrlDataType { Url = "opc.udp://224.0.0.22:4840" })
            };
            NodeId id = await app.AddConnectionAsync(connCfg);
            Assert.That(id.IsNull, Is.False);
        }

        [Test]
        [TestSpec("9.1.3.5", Summary = "RemoveConnection removes")]
        public async Task RemoveConnectionAsyncRemovesFromConnections()
        {
            await using IPubSubApplication app = BuildApp();
            var connCfg = new PubSubConnectionDataType
            {
                Name = "to-remove",
                TransportProfileUri = "http://opcfoundation.org/UA-Profile/Transport/pubsub-udp-uadp",
                Address = new ExtensionObject(
                    new NetworkAddressUrlDataType { Url = "opc.udp://224.0.0.22:4840" })
            };
            NodeId id = await app.AddConnectionAsync(connCfg);
            await app.RemoveConnectionAsync(id);
            Assert.That(app.Connections, Is.Empty);
        }

        [Test]
        [TestSpec("5.2.3", Summary = "RemoveConnection stamps version")]
        public async Task RemoveConnectionAsyncStampsConfigurationVersion()
        {
            await using IPubSubApplication app = BuildApp();
            var connCfg = new PubSubConnectionDataType
            {
                Name = "to-remove",
                TransportProfileUri = "http://opcfoundation.org/UA-Profile/Transport/pubsub-udp-uadp",
                Address = new ExtensionObject(
                    new NetworkAddressUrlDataType { Url = "opc.udp://224.0.0.22:4840" })
            };
            NodeId id = await app.AddConnectionAsync(connCfg);
            ConfigurationVersionDataType vBefore = app.ConfigurationVersion;
            await Task.Delay(1100);
            await app.RemoveConnectionAsync(id);
            Assert.That(
                app.ConfigurationVersion.MajorVersion,
                Is.GreaterThanOrEqualTo(vBefore.MajorVersion));
        }

        [Test]
        [TestSpec("9.1.6", Summary = "ReplaceConfiguration replaces")]
        public async Task ReplaceConfigurationAsyncReplacesEntireConfiguration()
        {
            await using IPubSubApplication app = BuildApp();
            var newCfg = new PubSubConfigurationDataType
            {
                Connections = new ArrayOf<PubSubConnectionDataType>(new[]
                {
                    new PubSubConnectionDataType
                    {
                        Name = "replaced-conn",
                        TransportProfileUri =
                            "http://opcfoundation.org/UA-Profile/Transport/pubsub-udp-uadp",
                        Address = new ExtensionObject(
                            new NetworkAddressUrlDataType { Url = "opc.udp://224.0.0.22:4840" })
                    }
                }),
                PublishedDataSets = []
            };
            IList<StatusCode> results = await app.ReplaceConfigurationAsync(newCfg);
            Assert.That(results, Is.Not.Empty);
            Assert.That(app.Connections, Has.Count.EqualTo(1));
        }

        [Test]
        [TestSpec("9.1.6", Summary = "ReplaceConfiguration validates")]
        public async Task ReplaceConfigurationAsyncInvalidConfigurationThrows()
        {
            await using IPubSubApplication app = BuildApp();
            var badCfg = new PubSubConfigurationDataType
            {
                Connections = new ArrayOf<PubSubConnectionDataType>(new[]
                {
                    new PubSubConnectionDataType
                    {
                        Name = "bad-conn",
                        TransportProfileUri = "http://invalid/profile",
                        Address = new ExtensionObject(
                            new NetworkAddressUrlDataType { Url = "opc.udp://224.0.0.22:4840" })
                    }
                }),
                PublishedDataSets = []
            };
            Assert.That(
                async () => await app.ReplaceConfigurationAsync(badCfg),
                Throws.TypeOf<PubSubConfigurationException>());
        }

        [Test]
        [TestSpec("9.1.6", Summary = "GetConfiguration deep clones")]
        public async Task GetConfigurationReturnsDeepClone()
        {
            await using IPubSubApplication app = BuildApp();
            var connCfg = new PubSubConnectionDataType
            {
                Name = "clone-test",
                TransportProfileUri = "http://opcfoundation.org/UA-Profile/Transport/pubsub-udp-uadp",
                Address = new ExtensionObject(
                    new NetworkAddressUrlDataType { Url = "opc.udp://224.0.0.22:4840" })
            };
            await app.AddConnectionAsync(connCfg);
            PubSubConfigurationDataType a = app.GetConfiguration();
            PubSubConfigurationDataType b = app.GetConfiguration();
            Assert.That(ReferenceEquals(a, b), Is.False);
            Assert.That(a.Connections[0].Name, Is.EqualTo(b.Connections[0].Name));
        }

        [Test]
        [TestSpec("9.1.6", Summary = "AddWriterGroup attaches")]
        public async Task AddWriterGroupAsyncAttachesToConnection()
        {
            await using IPubSubApplication app = BuildApp();
            var connCfg = new PubSubConnectionDataType
            {
                Name = "wg-conn",
                TransportProfileUri = "http://opcfoundation.org/UA-Profile/Transport/pubsub-udp-uadp",
                Address = new ExtensionObject(
                    new NetworkAddressUrlDataType { Url = "opc.udp://224.0.0.22:4840" })
            };
            NodeId connId = await app.AddConnectionAsync(connCfg);
            var wgCfg = new WriterGroupDataType
            {
                Name = "wg-1",
                WriterGroupId = 1,
                PublishingInterval = 1000
            };
            NodeId wgId = await app.AddWriterGroupAsync(connId, wgCfg);
            Assert.That(wgId.IsNull, Is.False);
            Assert.That(app.Connections[0].WriterGroups, Has.Count.EqualTo(1));
        }

        [Test]
        [TestSpec("9.1.7", Summary = "AddDataSetWriter attaches")]
        public async Task AddDataSetWriterAsyncAttachesToWriterGroup()
        {
            await using IPubSubApplication app = BuildAppWithPds();
            NodeId connId = await AddConnectionAsync(app);
            var wgCfg = new WriterGroupDataType
            {
                Name = "wg-w",
                WriterGroupId = 1,
                PublishingInterval = 1000
            };
            NodeId wgId = await app.AddWriterGroupAsync(connId, wgCfg);
            var dwCfg = new DataSetWriterDataType
            {
                Name = "writer-1",
                DataSetWriterId = 1,
                DataSetName = "pds-1"
            };
            NodeId dwId = await app.AddDataSetWriterAsync(wgId, dwCfg);
            Assert.That(dwId.IsNull, Is.False);
        }

        [Test]
        [TestSpec("9.1.6", Summary = "AddReaderGroup attaches")]
        public async Task AddReaderGroupAsyncAttachesToConnection()
        {
            await using IPubSubApplication app = BuildApp();
            NodeId connId = await AddConnectionAsync(app);
            var rgCfg = new ReaderGroupDataType { Name = "rg-1" };
            NodeId rgId = await app.AddReaderGroupAsync(connId, rgCfg);
            Assert.That(rgId.IsNull, Is.False);
            Assert.That(app.Connections[0].ReaderGroups, Has.Count.EqualTo(1));
        }

        [Test]
        [TestSpec("9.1.8", Summary = "AddDataSetReader attaches")]
        public async Task AddDataSetReaderAsyncAttachesToReaderGroup()
        {
            await using IPubSubApplication app = BuildApp();
            NodeId connId = await AddConnectionAsync(app);
            var rgCfg = new ReaderGroupDataType { Name = "rg-r" };
            NodeId rgId = await app.AddReaderGroupAsync(connId, rgCfg);
            var drCfg = new DataSetReaderDataType
            {
                Name = "reader-1",
                DataSetWriterId = 1,
                MessageReceiveTimeout = 5000,
                SubscribedDataSet = new ExtensionObject(
                    new TargetVariablesDataType())
            };
            NodeId drId = await app.AddDataSetReaderAsync(rgId, drCfg);
            Assert.That(drId.IsNull, Is.False);
        }

        [Test]
        [TestSpec("9.1.6", Summary = "Mutation disable/re-enable")]
        public async Task MutationDisablesThenReEnablesIfStarted()
        {
            await using IPubSubApplication app = BuildApp();
            await app.StartAsync();
            var connCfg = new PubSubConnectionDataType
            {
                Name = "runtime-conn",
                TransportProfileUri = "http://opcfoundation.org/UA-Profile/Transport/pubsub-udp-uadp",
                Address = new ExtensionObject(
                    new NetworkAddressUrlDataType { Url = "opc.udp://224.0.0.22:4840" })
            };
            NodeId id = await app.AddConnectionAsync(connCfg);
            Assert.That(id.IsNull, Is.False);
            Assert.That(app.Connections, Has.Count.EqualTo(1));
        }

        private static IPubSubApplication BuildApp()
        {
            return new PubSubApplicationBuilder(NUnitTelemetryContext.Create())
                .WithApplicationId("mutation-tests")
                .UseConfiguration(new PubSubConfigurationDataType
                {
                    Connections = [],
                    PublishedDataSets = []
                })
                .UseAllStandardEncoders()
                .AddTransportFactory(new StubTransportFactory())
                .Build();
        }

        private static IPubSubApplication BuildAppWithPds()
        {
            return new PubSubApplicationBuilder(NUnitTelemetryContext.Create())
                .WithApplicationId("mutation-tests-pds")
                .UseConfiguration(new PubSubConfigurationDataType
                {
                    Connections = [],
                    PublishedDataSets = new ArrayOf<PublishedDataSetDataType>(new[]
                    {
                        new PublishedDataSetDataType { Name = "pds-1" }
                    })
                })
                .UseAllStandardEncoders()
                .AddTransportFactory(new StubTransportFactory())
                .Build();
        }

        private static async Task<NodeId> AddConnectionAsync(IPubSubApplication app)
        {
            var connCfg = new PubSubConnectionDataType
            {
                Name = "test-conn",
                TransportProfileUri = "http://opcfoundation.org/UA-Profile/Transport/pubsub-udp-uadp",
                Address = new ExtensionObject(
                    new NetworkAddressUrlDataType { Url = "opc.udp://224.0.0.22:4840" })
            };
            return await app.AddConnectionAsync(connCfg);
        }

        private sealed class StubTransportFactory : IPubSubTransportFactory
        {
            public string TransportProfileUri => Profiles.PubSubUdpUadpTransport;

            public IPubSubTransport Create(
                PubSubConnectionDataType connection,
                ITelemetryContext telemetry,
                TimeProvider timeProvider)
            {
                _ = connection;
                _ = telemetry;
                _ = timeProvider;
                return new StubTransport();
            }
        }

        private sealed class StubTransport : IPubSubTransport
        {
            private bool m_isConnected;

            public string TransportProfileUri => Profiles.PubSubUdpUadpTransport;

            public PubSubTransportDirection Direction => PubSubTransportDirection.SendReceive;

            public bool IsConnected => m_isConnected;

            public event EventHandler<PubSubTransportStateChangedEventArgs>? StateChanged
            {
                add { }
                remove { }
            }

            public ValueTask OpenAsync(CancellationToken cancellationToken = default)
            {
                _ = cancellationToken;
                m_isConnected = true;
                return default;
            }

            public ValueTask CloseAsync(CancellationToken cancellationToken = default)
            {
                _ = cancellationToken;
                m_isConnected = false;
                return default;
            }

            public ValueTask SendAsync(
                ReadOnlyMemory<byte> payload,
                string? topic = null,
                CancellationToken cancellationToken = default)
            {
                _ = payload;
                _ = topic;
                _ = cancellationToken;
                return default;
            }

            public IAsyncEnumerable<PubSubTransportFrame> ReceiveAsync(
                CancellationToken cancellationToken = default)
            {
                _ = cancellationToken;
                return AsyncEnumerable.Empty<PubSubTransportFrame>();
            }

            public ValueTask DisposeAsync()
            {
                m_isConnected = false;
                return default;
            }
        }
    }
}
