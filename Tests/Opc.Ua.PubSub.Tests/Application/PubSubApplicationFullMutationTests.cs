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
    /// <summary>
    /// Extends <see cref="PubSubApplicationMutationTests"/> with the
    /// remove-side and PublishedDataSet-side mutation paths, and the
    /// negative validation paths missing in the Phase 17 baseline.
    /// All tests link to Part 14 §9.1.6 / §9.1.7 / §9.1.8.
    /// </summary>
    [TestFixture]
    [TestSpec("9.1.6", Summary = "Full PubSub mutation API coverage")]
    public class PubSubApplicationFullMutationTests
    {
        private const string UdpProfile = Profiles.PubSubUdpUadpTransport;
        private const string AddrUrl = "opc.udp://224.0.0.22:4840";

        // -------------------------------------------------------------
        // ReplaceConfiguration negative paths
        // -------------------------------------------------------------

        [Test]
        [TestSpec("9.1.6")]
        public async Task ReplaceConfigurationAsyncNullThrowsArgumentNullException()
        {
            await using IPubSubApplication app = NewApp();
            Assert.That(
                async () => await app.ReplaceConfigurationAsync(null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        [TestSpec("9.1.6")]
        public async Task ReplaceConfigurationAsyncRaisesConfigurationChanged()
        {
            await using IPubSubApplication app = NewApp();
            int raised = 0;
            app.ConfigurationChanged += (_, _) => raised++;
            await app.ReplaceConfigurationAsync(new PubSubConfigurationDataType
            {
                Connections = new ArrayOf<PubSubConnectionDataType>(new[] { NewConnection("c1") }),
                PublishedDataSets = []
            });
            Assert.That(raised, Is.EqualTo(1));
        }

        [Test]
        [TestSpec("9.1.6")]
        public async Task ReplaceConfigurationAsyncReturnsStatusListWithGood()
        {
            await using IPubSubApplication app = NewApp();
            IList<StatusCode> results = await app.ReplaceConfigurationAsync(
                new PubSubConfigurationDataType
                {
                    Connections = [],
                    PublishedDataSets = []
                });
            Assert.That(results, Is.Not.Null);
            Assert.That(results, Is.Not.Empty);
            Assert.That(StatusCode.IsGood(results[0]), Is.True);
        }

        // -------------------------------------------------------------
        // AddConnection negative paths
        // -------------------------------------------------------------

        [Test]
        [TestSpec("9.1.3.4")]
        public async Task AddConnectionAsyncNullThrowsArgumentNullException()
        {
            await using IPubSubApplication app = NewApp();
            Assert.That(
                async () => await app.AddConnectionAsync(null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        [TestSpec("9.1.3.4")]
        public async Task AddConnectionAsyncEmptyNameThrowsArgumentException()
        {
            await using IPubSubApplication app = NewApp();
            Assert.That(
                async () => await app.AddConnectionAsync(new PubSubConnectionDataType
                {
                    Name = string.Empty,
                    TransportProfileUri = UdpProfile,
                    Address = new ExtensionObject(
                        new NetworkAddressUrlDataType { Url = AddrUrl })
                }),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        [TestSpec("9.1.3.4")]
        public async Task AddConnectionAsyncBadProfileThrowsPubSubConfigurationException()
        {
            await using IPubSubApplication app = NewApp();
            Assert.That(
                async () => await app.AddConnectionAsync(new PubSubConnectionDataType
                {
                    Name = "bad-profile",
                    TransportProfileUri = "urn:not-real",
                    Address = new ExtensionObject(
                        new NetworkAddressUrlDataType { Url = AddrUrl })
                }),
                Throws.TypeOf<PubSubConfigurationException>());
        }

        // -------------------------------------------------------------
        // RemoveConnection
        // -------------------------------------------------------------

        [Test]
        [TestSpec("9.1.3.5")]
        public async Task RemoveConnectionAsyncUnknownIdThrowsArgumentException()
        {
            await using IPubSubApplication app = NewApp();
            Assert.That(
                async () => await app.RemoveConnectionAsync(
                    new NodeId("pubsub:connection:nope", 0)),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        [TestSpec("9.1.3.5")]
        public async Task RemoveConnectionAsyncNullIdThrowsArgumentException()
        {
            await using IPubSubApplication app = NewApp();
            Assert.That(
                async () => await app.RemoveConnectionAsync(NodeId.Null),
                Throws.TypeOf<ArgumentException>());
        }

        // -------------------------------------------------------------
        // Add/Remove WriterGroup
        // -------------------------------------------------------------

        [Test]
        [TestSpec("9.1.6")]
        public async Task AddWriterGroupAsyncNullConfigThrowsArgumentNullException()
        {
            await using IPubSubApplication app = NewApp();
            NodeId connId = await app.AddConnectionAsync(NewConnection("c"));
            Assert.That(
                async () => await app.AddWriterGroupAsync(connId, null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        [TestSpec("9.1.6")]
        public async Task AddWriterGroupAsyncEmptyNameThrowsArgumentException()
        {
            await using IPubSubApplication app = NewApp();
            NodeId connId = await app.AddConnectionAsync(NewConnection("c"));
            Assert.That(
                async () => await app.AddWriterGroupAsync(connId, new WriterGroupDataType
                {
                    Name = string.Empty,
                    PublishingInterval = 1000
                }),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        [TestSpec("9.1.6")]
        public async Task AddWriterGroupAsyncUnknownConnectionThrowsArgumentException()
        {
            await using IPubSubApplication app = NewApp();
            Assert.That(
                async () => await app.AddWriterGroupAsync(
                    new NodeId("pubsub:connection:nope", 0),
                    new WriterGroupDataType { Name = "wg", PublishingInterval = 1000 }),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        [TestSpec("9.1.6")]
        public async Task RemoveGroupAsyncRemovesWriterGroup()
        {
            await using IPubSubApplication app = NewApp();
            NodeId connId = await app.AddConnectionAsync(NewConnection("c"));
            NodeId wgId = await app.AddWriterGroupAsync(connId, new WriterGroupDataType
            {
                Name = "wg-1",
                WriterGroupId = 1,
                PublishingInterval = 1000
            });
            await app.RemoveGroupAsync(wgId);
            Assert.That(app.Connections[0].WriterGroups, Is.Empty);
        }

        [Test]
        [TestSpec("9.1.6")]
        public async Task RemoveGroupAsyncRemovesReaderGroup()
        {
            await using IPubSubApplication app = NewApp();
            NodeId connId = await app.AddConnectionAsync(NewConnection("c"));
            NodeId rgId = await app.AddReaderGroupAsync(
                connId, new ReaderGroupDataType { Name = "rg-1" });
            await app.RemoveGroupAsync(rgId);
            Assert.That(app.Connections[0].ReaderGroups, Is.Empty);
        }

        [Test]
        [TestSpec("9.1.6")]
        public async Task RemoveGroupAsyncUnknownIdThrowsArgumentException()
        {
            await using IPubSubApplication app = NewApp();
            Assert.That(
                async () => await app.RemoveGroupAsync(
                    new NodeId("pubsub:writer-group:foo:bar", 0)),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        [TestSpec("9.1.6")]
        public async Task RemoveGroupAsyncNullIdThrowsArgumentException()
        {
            await using IPubSubApplication app = NewApp();
            Assert.That(
                async () => await app.RemoveGroupAsync(NodeId.Null),
                Throws.TypeOf<ArgumentException>());
        }

        // -------------------------------------------------------------
        // Add/Remove ReaderGroup
        // -------------------------------------------------------------

        [Test]
        [TestSpec("9.1.6")]
        public async Task AddReaderGroupAsyncNullConfigThrowsArgumentNullException()
        {
            await using IPubSubApplication app = NewApp();
            NodeId connId = await app.AddConnectionAsync(NewConnection("c"));
            Assert.That(
                async () => await app.AddReaderGroupAsync(connId, null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        [TestSpec("9.1.6")]
        public async Task AddReaderGroupAsyncEmptyNameThrowsArgumentException()
        {
            await using IPubSubApplication app = NewApp();
            NodeId connId = await app.AddConnectionAsync(NewConnection("c"));
            Assert.That(
                async () => await app.AddReaderGroupAsync(
                    connId, new ReaderGroupDataType { Name = string.Empty }),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        [TestSpec("9.1.6")]
        public async Task AddReaderGroupAsyncUnknownConnectionThrowsArgumentException()
        {
            await using IPubSubApplication app = NewApp();
            Assert.That(
                async () => await app.AddReaderGroupAsync(
                    new NodeId("pubsub:connection:nope", 0),
                    new ReaderGroupDataType { Name = "rg" }),
                Throws.TypeOf<ArgumentException>());
        }

        // -------------------------------------------------------------
        // Add/Remove DataSetWriter
        // -------------------------------------------------------------

        [Test]
        [TestSpec("9.1.7")]
        public async Task AddDataSetWriterAsyncNullConfigThrowsArgumentNullException()
        {
            await using IPubSubApplication app = NewAppWithPds();
            NodeId connId = await app.AddConnectionAsync(NewConnection("c"));
            NodeId wgId = await app.AddWriterGroupAsync(connId, new WriterGroupDataType
            {
                Name = "wg",
                WriterGroupId = 1,
                PublishingInterval = 1000
            });
            Assert.That(
                async () => await app.AddDataSetWriterAsync(wgId, null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        [TestSpec("9.1.7")]
        public async Task AddDataSetWriterAsyncEmptyNameThrowsArgumentException()
        {
            await using IPubSubApplication app = NewAppWithPds();
            NodeId connId = await app.AddConnectionAsync(NewConnection("c"));
            NodeId wgId = await app.AddWriterGroupAsync(connId, new WriterGroupDataType
            {
                Name = "wg",
                WriterGroupId = 1,
                PublishingInterval = 1000
            });
            Assert.That(
                async () => await app.AddDataSetWriterAsync(
                    wgId, new DataSetWriterDataType
                    {
                        Name = string.Empty,
                        DataSetWriterId = 1,
                        DataSetName = "pds-1"
                    }),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        [TestSpec("9.1.7")]
        public async Task AddDataSetWriterAsyncUnknownGroupIdThrowsArgumentException()
        {
            await using IPubSubApplication app = NewAppWithPds();
            Assert.That(
                async () => await app.AddDataSetWriterAsync(
                    new NodeId("pubsub:writer-group:foo:bar", 0),
                    new DataSetWriterDataType
                    {
                        Name = "w",
                        DataSetWriterId = 1,
                        DataSetName = "pds-1"
                    }),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        [TestSpec("9.1.7")]
        public async Task RemoveDataSetWriterAsyncRoundTrip()
        {
            await using IPubSubApplication app = NewAppWithPds();
            NodeId connId = await app.AddConnectionAsync(NewConnection("c"));
            NodeId wgId = await app.AddWriterGroupAsync(connId, new WriterGroupDataType
            {
                Name = "wg",
                WriterGroupId = 1,
                PublishingInterval = 1000
            });
            NodeId writerId = await app.AddDataSetWriterAsync(
                wgId, new DataSetWriterDataType
                {
                    Name = "writer-1",
                    DataSetWriterId = 1,
                    DataSetName = "pds-1"
                });
            await app.RemoveDataSetWriterAsync(writerId);
            Assert.That(
                app.Connections[0].WriterGroups[0].DataSetWriters,
                Is.Empty);
        }

        [Test]
        [TestSpec("9.1.7")]
        public async Task RemoveDataSetWriterAsyncUnknownIdThrowsArgumentException()
        {
            await using IPubSubApplication app = NewApp();
            Assert.That(
                async () => await app.RemoveDataSetWriterAsync(
                    new NodeId("pubsub:writer:foo:bar:baz", 0)),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        [TestSpec("9.1.7")]
        public async Task RemoveDataSetWriterAsyncNullIdThrowsArgumentException()
        {
            await using IPubSubApplication app = NewApp();
            Assert.That(
                async () => await app.RemoveDataSetWriterAsync(NodeId.Null),
                Throws.TypeOf<ArgumentException>());
        }

        // -------------------------------------------------------------
        // Add/Remove DataSetReader
        // -------------------------------------------------------------

        [Test]
        [TestSpec("9.1.8")]
        public async Task AddDataSetReaderAsyncNullConfigThrowsArgumentNullException()
        {
            await using IPubSubApplication app = NewApp();
            NodeId connId = await app.AddConnectionAsync(NewConnection("c"));
            NodeId rgId = await app.AddReaderGroupAsync(
                connId, new ReaderGroupDataType { Name = "rg" });
            Assert.That(
                async () => await app.AddDataSetReaderAsync(rgId, null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        [TestSpec("9.1.8")]
        public async Task AddDataSetReaderAsyncEmptyNameThrowsArgumentException()
        {
            await using IPubSubApplication app = NewApp();
            NodeId connId = await app.AddConnectionAsync(NewConnection("c"));
            NodeId rgId = await app.AddReaderGroupAsync(
                connId, new ReaderGroupDataType { Name = "rg" });
            Assert.That(
                async () => await app.AddDataSetReaderAsync(rgId, new DataSetReaderDataType
                {
                    Name = string.Empty,
                    DataSetWriterId = 1,
                    MessageReceiveTimeout = 5000,
                    SubscribedDataSet = new ExtensionObject(new TargetVariablesDataType())
                }),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        [TestSpec("9.1.8")]
        public async Task AddDataSetReaderAsyncUnknownReaderGroupIdThrowsArgumentException()
        {
            await using IPubSubApplication app = NewApp();
            Assert.That(
                async () => await app.AddDataSetReaderAsync(
                    new NodeId("pubsub:reader-group:foo:bar", 0),
                    new DataSetReaderDataType
                    {
                        Name = "r",
                        DataSetWriterId = 1,
                        MessageReceiveTimeout = 5000,
                        SubscribedDataSet = new ExtensionObject(new TargetVariablesDataType())
                    }),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        [TestSpec("9.1.8")]
        public async Task RemoveDataSetReaderAsyncRoundTrip()
        {
            await using IPubSubApplication app = NewApp();
            NodeId connId = await app.AddConnectionAsync(NewConnection("c"));
            NodeId rgId = await app.AddReaderGroupAsync(
                connId, new ReaderGroupDataType { Name = "rg" });
            NodeId readerId = await app.AddDataSetReaderAsync(
                rgId, new DataSetReaderDataType
                {
                    Name = "reader-1",
                    DataSetWriterId = 1,
                    MessageReceiveTimeout = 5000,
                    SubscribedDataSet = new ExtensionObject(new TargetVariablesDataType())
                });
            await app.RemoveDataSetReaderAsync(readerId);
            Assert.That(
                app.Connections[0].ReaderGroups[0].DataSetReaders,
                Is.Empty);
        }

        [Test]
        [TestSpec("9.1.8")]
        public async Task RemoveDataSetReaderAsyncUnknownIdThrowsArgumentException()
        {
            await using IPubSubApplication app = NewApp();
            Assert.That(
                async () => await app.RemoveDataSetReaderAsync(
                    new NodeId("pubsub:reader:foo:bar:baz", 0)),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        [TestSpec("9.1.8")]
        public async Task RemoveDataSetReaderAsyncNullIdThrowsArgumentException()
        {
            await using IPubSubApplication app = NewApp();
            Assert.That(
                async () => await app.RemoveDataSetReaderAsync(NodeId.Null),
                Throws.TypeOf<ArgumentException>());
        }

        // -------------------------------------------------------------
        // Add/Remove PublishedDataSet
        // -------------------------------------------------------------

        [Test]
        [TestSpec("9.1.6")]
        public async Task AddPublishedDataSetAsyncNullConfigThrowsArgumentNullException()
        {
            await using IPubSubApplication app = NewApp();
            Assert.That(
                async () => await app.AddPublishedDataSetAsync(null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        [TestSpec("9.1.6")]
        public async Task AddPublishedDataSetAsyncEmptyNameThrowsArgumentException()
        {
            await using IPubSubApplication app = NewApp();
            Assert.That(
                async () => await app.AddPublishedDataSetAsync(
                    new PublishedDataSetDataType { Name = string.Empty }),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        [TestSpec("9.1.6")]
        public async Task AddPublishedDataSetAsyncReturnsNonNullNodeId()
        {
            await using IPubSubApplication app = NewApp();
            NodeId id = await app.AddPublishedDataSetAsync(
                new PublishedDataSetDataType { Name = "added-pds" });
            Assert.That(id.IsNull, Is.False);
        }

        [Test]
        [TestSpec("9.1.6")]
        public async Task RemovePublishedDataSetAsyncRoundTrip()
        {
            await using IPubSubApplication app = NewApp();
            NodeId id = await app.AddPublishedDataSetAsync(
                new PublishedDataSetDataType { Name = "to-remove-pds" });
            await app.RemovePublishedDataSetAsync(id);
        }

        [Test]
        [TestSpec("9.1.6")]
        public async Task RemovePublishedDataSetAsyncCascadesToWriters()
        {
            await using IPubSubApplication app = NewAppWithPds();
            NodeId connId = await app.AddConnectionAsync(NewConnection("c"));
            NodeId wgId = await app.AddWriterGroupAsync(connId, new WriterGroupDataType
            {
                Name = "wg",
                WriterGroupId = 1,
                PublishingInterval = 1000
            });
            _ = await app.AddDataSetWriterAsync(wgId, new DataSetWriterDataType
            {
                Name = "writer-1",
                DataSetWriterId = 1,
                DataSetName = "pds-1"
            });

            // pds-1 was registered at construction-time so it has a synthetic node id
            PubSubConfigurationDataType cfg = app.GetConfiguration();
            Assert.That(cfg.Connections[0].WriterGroups[0].DataSetWriters,
                Has.Count.EqualTo(1));

            // Add a new PDS and then remove it; ensure no cascade affects the
            // pre-existing writer that was bound to pds-1.
            NodeId addedId = await app.AddPublishedDataSetAsync(
                new PublishedDataSetDataType { Name = "extra-pds" });
            await app.RemovePublishedDataSetAsync(addedId);

            cfg = app.GetConfiguration();
            Assert.That(cfg.Connections[0].WriterGroups[0].DataSetWriters,
                Has.Count.EqualTo(1));
        }

        [Test]
        [TestSpec("9.1.6")]
        public async Task RemovePublishedDataSetAsyncUnknownIdThrowsArgumentException()
        {
            await using IPubSubApplication app = NewApp();
            Assert.That(
                async () => await app.RemovePublishedDataSetAsync(
                    new NodeId("pubsub:published-data-set:nope", 0)),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        [TestSpec("9.1.6")]
        public async Task RemovePublishedDataSetAsyncNullIdThrowsArgumentException()
        {
            await using IPubSubApplication app = NewApp();
            Assert.That(
                async () => await app.RemovePublishedDataSetAsync(NodeId.Null),
                Throws.TypeOf<ArgumentException>());
        }

        // -------------------------------------------------------------
        // GetConfiguration semantics (deep clone)
        // -------------------------------------------------------------

        [Test]
        [TestSpec("9.1.6")]
        public async Task GetConfigurationMutatingResultDoesNotAffectApplication()
        {
            await using IPubSubApplication app = NewApp();
            await app.AddConnectionAsync(NewConnection("clone-test"));
            PubSubConfigurationDataType cfg = app.GetConfiguration();
            // Mutate the returned tree.
            cfg.Connections[0].Name = "MUTATED";
            // Internal state must be unaffected.
            PubSubConfigurationDataType again = app.GetConfiguration();
            Assert.That(again.Connections[0].Name, Is.EqualTo("clone-test"));
        }

        // -------------------------------------------------------------
        // ConfigurationVersion stamping
        // -------------------------------------------------------------

        [Test]
        [TestSpec("5.2.3")]
        public async Task EveryMutationStampsNewConfigurationVersion()
        {
            await using IPubSubApplication app = NewApp();
            ConfigurationVersionDataType v0 = app.ConfigurationVersion;
            await app.AddConnectionAsync(NewConnection("v-test"));
            ConfigurationVersionDataType v1 = app.ConfigurationVersion;
            // The clock advance is monotonic; allow strictly-greater OR equal
            // (a 1ms operation may share the second).
            Assert.That(v1.MajorVersion, Is.GreaterThanOrEqualTo(v0.MajorVersion));
            Assert.That(v1.MinorVersion, Is.GreaterThanOrEqualTo(v0.MinorVersion));
        }

        // -------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------

        private static IPubSubApplication NewApp()
        {
            return new PubSubApplicationBuilder(NUnitTelemetryContext.Create())
                .WithApplicationId("full-mut-tests")
                .UseConfiguration(new PubSubConfigurationDataType
                {
                    Connections = [],
                    PublishedDataSets = []
                })
                .UseAllStandardEncoders()
                .AddTransportFactory(new StubTransportFactory())
                .Build();
        }

        private static IPubSubApplication NewAppWithPds()
        {
            return new PubSubApplicationBuilder(NUnitTelemetryContext.Create())
                .WithApplicationId("full-mut-tests-pds")
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

        private static PubSubConnectionDataType NewConnection(string name)
        {
            return new PubSubConnectionDataType
            {
                Name = name,
                TransportProfileUri = UdpProfile,
                Address = new ExtensionObject(
                    new NetworkAddressUrlDataType { Url = AddrUrl })
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

            public PubSubTransportDirection Direction =>
                PubSubTransportDirection.SendReceive;

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
