// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.MetaData;
using Opc.Ua.PubSub.Redundancy;
using Opc.Ua.PubSub.Scheduling;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.Tests;
using RuntimeApplication = Opc.Ua.PubSub.Application.PubSubApplication;

namespace Opc.Ua.PubSub.Tests.Application
{
    /// <summary>
    /// Verifies shared provider stores can rebuild a second PubSub runtime.
    /// </summary>
    [TestFixture]
    public class PubSubApplicationProviderFailoverTests
    {
        private const ushort NamespaceIndex = 2;

        [Test]
        [TestSpec("9.1", Summary = "Shared stores rebuild configuration, NodeIds, and run-state")]
        [Description("OPC 10000-14 §9.1 and §6.2.3: failover runtimes resume configuration and PubSubState.")]
        public async Task SharedStoresRebuildSecondApplicationWithIdenticalStateAsync()
        {
            var configurationStore = new InMemoryPubSubConfigurationStore(new PubSubConfigurationDataType
            {
                Connections = [],
                PublishedDataSets = []
            });
            var runtimeStateStore = new InMemoryPubSubRuntimeStateStore();
            _ = new InMemoryPubSubIdAllocator();

            await using RuntimeApplication first =
                await NewApplicationAsync(configurationStore, runtimeStateStore).ConfigureAwait(false);
            first.SetAddressSpaceNamespaceIndex(NamespaceIndex);

            NodeId publishedDataSetId = await first.AddPublishedDataSetAsync(
                new PublishedDataSetDataType { Name = "DataSet1" }).ConfigureAwait(false);
            NodeId connectionId = await first.AddConnectionAsync(NewConnection()).ConfigureAwait(false);
            NodeId writerGroupId = await first.AddWriterGroupAsync(
                connectionId,
                new WriterGroupDataType
                {
                    Name = "WriterGroup1",
                    WriterGroupId = 1,
                    PublishingInterval = 1000
                }).ConfigureAwait(false);
            NodeId writerId = await first.AddDataSetWriterAsync(
                writerGroupId,
                new DataSetWriterDataType
                {
                    Name = "Writer1",
                    DataSetName = "DataSet1",
                    DataSetWriterId = 1
                }).ConfigureAwait(false);
            NodeId readerGroupId = await first.AddReaderGroupAsync(
                connectionId,
                new ReaderGroupDataType { Name = "ReaderGroup1" }).ConfigureAwait(false);
            NodeId readerId = await first.AddDataSetReaderAsync(
                readerGroupId,
                new DataSetReaderDataType
                {
                    Name = "Reader1",
                    DataSetWriterId = 1,
                    MessageReceiveTimeout = 1000,
                    SubscribedDataSet = new ExtensionObject(new TargetVariablesDataType())
                }).ConfigureAwait(false);

            await first.StartAsync().ConfigureAwait(false);
            ConfigurationVersionDataType firstVersion = first.ConfigurationVersion;
            PubSubConfigurationDataType firstConfiguration = first.GetConfiguration();

            await using RuntimeApplication second =
                await NewApplicationAsync(configurationStore, runtimeStateStore).ConfigureAwait(false);
            second.SetAddressSpaceNamespaceIndex(NamespaceIndex);

            PubSubConfigurationDataType secondConfiguration = second.GetConfiguration();

            Assert.That(secondConfiguration.Connections.Count, Is.EqualTo(firstConfiguration.Connections.Count));
            Assert.That(secondConfiguration.PublishedDataSets.Count, Is.EqualTo(firstConfiguration.PublishedDataSets.Count));
            Assert.That(second.ConfigurationVersion.MajorVersion, Is.EqualTo(firstVersion.MajorVersion));
            Assert.That(second.ConfigurationVersion.MinorVersion, Is.EqualTo(firstVersion.MinorVersion));
            Assert.That(publishedDataSetId, Is.EqualTo(new NodeId("pubsub:published-data-set:DataSet1", NamespaceIndex)));
            Assert.That(connectionId, Is.EqualTo(new NodeId("pubsub:connection:Connection1", NamespaceIndex)));
            Assert.That(writerGroupId, Is.EqualTo(new NodeId("pubsub:writer-group:Connection1:WriterGroup1", NamespaceIndex)));
            Assert.That(writerId, Is.EqualTo(new NodeId("pubsub:writer:Connection1:WriterGroup1:Writer1", NamespaceIndex)));
            Assert.That(readerGroupId, Is.EqualTo(new NodeId("pubsub:reader-group:Connection1:ReaderGroup1", NamespaceIndex)));
            Assert.That(readerId, Is.EqualTo(new NodeId("pubsub:reader:Connection1:ReaderGroup1:Reader1", NamespaceIndex)));
            Assert.That(second.Connections[0].State.State, Is.EqualTo(PubSubState.Operational));
            Assert.That(second.Connections[0].WriterGroups[0].State.State, Is.EqualTo(PubSubState.Operational));
            Assert.That(second.Connections[0].ReaderGroups[0].State.State, Is.EqualTo(PubSubState.Operational));
        }

        [Test]
        public async Task RuntimeStateUsesGroupFenceAndSkipsUnownedTransitionsAsync()
        {
            const string leaseScope = "pubsub:writergroup:Connection1:WriterGroup1";
            const string runtimeId = "pubsub:writer-group:Connection1:WriterGroup1";
            var configurationStore = new InMemoryPubSubConfigurationStore(new PubSubConfigurationDataType
            {
                Connections = [],
                PublishedDataSets = []
            });
            var store = new Mock<IPubSubRuntimeStateStore>();
            var coordinator = new Mock<IPubSubActivationCoordinator>();
            coordinator.Setup(x => x.GetRoleAsync(leaseScope, It.IsAny<CancellationToken>()))
                .ReturnsAsync(PubSubComponentRole.Active);
            Mock<IPubSubActivationFenceProvider> fences = coordinator.As<IPubSubActivationFenceProvider>();
            long fence = 17;
            fences.Setup(x => x.TryGetActiveFence(leaseScope, out fence)).Returns(true);
            await using RuntimeApplication application =
                await NewApplicationAsync(configurationStore, store.Object, coordinator.Object).ConfigureAwait(false);
            application.SetAddressSpaceNamespaceIndex(NamespaceIndex);
            NodeId connectionId = await application.AddConnectionAsync(NewConnection()).ConfigureAwait(false);
            await application.AddWriterGroupAsync(
                connectionId,
                new WriterGroupDataType { Name = "WriterGroup1", WriterGroupId = 1, PublishingInterval = 1000 })
                .ConfigureAwait(false);
            await application.StartAsync().ConfigureAwait(false);

            store.Verify(x => x.SetStateAsync(runtimeId, PubSubState.Operational, fence, CancellationToken.None),
                Times.AtLeastOnce);

            store.Invocations.Clear();
            fences.Setup(x => x.TryGetActiveFence(leaseScope, out fence)).Returns(false);
            Assert.That(application.Connections[0].WriterGroups[0].State.TryPause(), Is.True);
            store.Verify(x => x.SetStateAsync(runtimeId, PubSubState.Paused, fence, CancellationToken.None),
                Times.Never);
            store.Verify(x => x.SetStateAsync(runtimeId, PubSubState.Paused, CancellationToken.None), Times.Never);
        }

        private static async ValueTask<RuntimeApplication> NewApplicationAsync(
            InMemoryPubSubConfigurationStore configurationStore,
            IPubSubRuntimeStateStore runtimeStateStore,
            IPubSubActivationCoordinator? coordinator = null)
        {
            TimeProvider timeProvider = TimeProvider.System;
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            PubSubConfigurationDataType configuration =
                await configurationStore.LoadAsync().ConfigureAwait(false);
            var snapshot =
                PubSubConfigurationSnapshot.Create(configuration, timeProvider);
            return new RuntimeApplication(
                snapshot,
                [new StubTransportFactory()],
                [new PubSub.Encoding.Uadp.UadpEncoder(), new PubSub.Encoding.Json.JsonEncoder()],
                [new PubSub.Encoding.Uadp.UadpDecoder(), new PubSub.Encoding.Json.JsonDecoder()],
                [],
                new PubSubScheduler(telemetry, timeProvider),
                new DataSetMetaDataRegistry(),
                new PubSubDiagnostics(PubSubDiagnosticsLevel.Low, timeProvider),
                telemetry,
                timeProvider,
                new Dictionary<string, IPublishedDataSetSource>(StringComparer.Ordinal),
                new Dictionary<string, ISubscribedDataSetSink>(StringComparer.Ordinal),
                configurationStore: configurationStore,
                runtimeStateStore: runtimeStateStore,
                activationCoordinator: coordinator);
        }

        private static PubSubConnectionDataType NewConnection()
        {
            return new PubSubConnectionDataType
            {
                Name = "Connection1",
                TransportProfileUri = Profiles.PubSubUdpUadpTransport,
                Address = new ExtensionObject(
                    new NetworkAddressUrlDataType { Url = "opc.udp://224.0.0.22:4840" })
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
                return TestAsyncEnumerable.Empty<PubSubTransportFrame>();
            }

            public ValueTask DisposeAsync()
            {
                return default;
            }
        }
    }
}
