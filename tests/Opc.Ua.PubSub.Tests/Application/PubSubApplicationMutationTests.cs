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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.StateMachine;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Tests.Application
{
    [TestFixture]
    [TestSpec("9.1.6", Summary = "PubSub configuration mutation")]
    public class PubSubApplicationMutationTests
    {
        [Test]
        public async Task RuntimeControlResolvesExactIdsAndRejectsForeignOrStaleIds()
        {
            await using IPubSubApplication app = BuildApp();
            var concrete = (PubSubApplication)app;
            concrete.SetAddressSpaceNamespaceIndex(17);
            NodeId componentId = await app.AddConnectionAsync(new PubSubConnectionDataType
            {
                Name = "connection:with:delimiters",
                TransportProfileUri = Profiles.PubSubUdpUadpTransport,
                Address = new ExtensionObject(
                    new NetworkAddressUrlDataType { Url = "opc.udp://224.0.0.22:4840" }),
                WriterGroups =
                [
                    new WriterGroupDataType
                    {
                        Name = "group:with:delimiters",
                        WriterGroupId = 1,
                        PublishingInterval = 1000
                    }
                ]
            }).ConfigureAwait(false);
            var runtimeControl = (IPubSubRuntimeControlApplication)app;

            PubSubState disabled = await runtimeControl.SetComponentEnabledAsync(
                componentId, false).ConfigureAwait(false);
            Assert.That(disabled, Is.EqualTo(PubSubState.Disabled));

            NodeId groupId = new(
                "pubsub:writer-group:connection:with:delimiters:group:with:delimiters", 17);
            PubSubState groupDisabled = await runtimeControl.SetComponentEnabledAsync(
                groupId, false).ConfigureAwait(false);
            Assert.That(groupDisabled, Is.EqualTo(PubSubState.Disabled));

            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            _ = await Assert.ThrowsAsync<TaskCanceledException>(async () =>
                await runtimeControl.SetComponentEnabledAsync(
                    componentId, true, cancellation.Token).ConfigureAwait(false));

            NodeId foreignId = new(componentId.Identifier, 18);
            _ = await Assert.ThrowsAsync<ArgumentException>(async () =>
                await runtimeControl.SetComponentEnabledAsync(foreignId, true).ConfigureAwait(false));

            await app.RemoveConnectionAsync(componentId).ConfigureAwait(false);
            _ = await Assert.ThrowsAsync<ArgumentException>(async () =>
                await runtimeControl.SetComponentEnabledAsync(componentId, true).ConfigureAwait(false));
        }

        [Test]
        public async Task RuntimeControlRejectsAmbiguousDelimiterBasedNodeIds()
        {
            await using IPubSubApplication app = BuildApp();
            var concrete = (PubSubApplication)app;
            concrete.SetAddressSpaceNamespaceIndex(17);
            _ = await app.AddConnectionAsync(new PubSubConnectionDataType
            {
                Name = "left:right",
                TransportProfileUri = Profiles.PubSubUdpUadpTransport,
                Address = new ExtensionObject(
                    new NetworkAddressUrlDataType { Url = "opc.udp://224.0.0.22:4840" }),
                WriterGroups =
                [
                    new WriterGroupDataType { Name = "leaf", WriterGroupId = 1, PublishingInterval = 1000 }
                ]
            }).ConfigureAwait(false);
            _ = await app.AddConnectionAsync(new PubSubConnectionDataType
            {
                Name = "left",
                TransportProfileUri = Profiles.PubSubUdpUadpTransport,
                Address = new ExtensionObject(
                    new NetworkAddressUrlDataType { Url = "opc.udp://224.0.0.23:4840" }),
                WriterGroups =
                [
                    new WriterGroupDataType { Name = "right:leaf", WriterGroupId = 2, PublishingInterval = 1000 }
                ]
            }).ConfigureAwait(false);
            var runtimeControl = (IPubSubRuntimeControlApplication)app;
            var ambiguousId = new NodeId("pubsub:writer-group:left:right:leaf", 17);

            ArgumentException? ambiguous = await Assert.ThrowsAsync<ArgumentException>(async () =>
                await runtimeControl.SetComponentEnabledAsync(ambiguousId, true).ConfigureAwait(false));
            Assert.That(ambiguous!.Message, Does.Contain("multiple"));
        }

        [Test]
        public async Task RuntimeControlWaitsForConfigurationMutationTransaction()
        {
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var provider = new Mock<IPubSubCommunicationIdReservations>();
            provider.Setup(p => p.PersistAsync(It.IsAny<PubSubConfigurationDataType>(),
                It.IsAny<Func<CancellationToken, ValueTask>>(), It.IsAny<CancellationToken>()))
                .Returns(async (PubSubConfigurationDataType configuration,
                    Func<CancellationToken, ValueTask> persist, CancellationToken ct) =>
                {
                    entered.SetResult(true);
                    await release.Task.ConfigureAwait(false);
                    await persist(ct).ConfigureAwait(false);
                });
            await using IPubSubApplication app = new PubSubApplicationBuilder(NUnitTelemetryContext.Create())
                .UseConfiguration(new PubSubConfigurationDataType
                {
                    Connections =
                    [
                        new PubSubConnectionDataType
                        {
                            Name = "runtime-control",
                            TransportProfileUri = Profiles.PubSubUdpUadpTransport,
                            Address = new ExtensionObject(
                                new NetworkAddressUrlDataType { Url = "opc.udp://224.0.0.22:4840" })
                        }
                    ]
                })
                .WithCommunicationIdReservations(provider.Object)
                .UseAllStandardEncoders()
                .AddTransportFactory(new StubTransportFactory())
                .Build();
            ((PubSubApplication)app).SetAddressSpaceNamespaceIndex(17);
            var runtimeControl = (IPubSubRuntimeControlApplication)app;
            Task<NodeId> mutation = app.AddConnectionAsync(new PubSubConnectionDataType
            {
                Name = "pending",
                TransportProfileUri = Profiles.PubSubUdpUadpTransport,
                Address = new ExtensionObject(
                    new NetworkAddressUrlDataType { Url = "opc.udp://224.0.0.23:4840" })
            }).AsTask();
            _ = await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

            Task<PubSubState> transition = runtimeControl.SetComponentEnabledAsync(
                new NodeId("pubsub:connection:runtime-control", 17), false).AsTask();
            Assert.That(transition.IsCompleted, Is.False);

            release.SetResult(true);
            _ = await mutation.ConfigureAwait(false);
            Assert.That(await transition.ConfigureAwait(false), Is.EqualTo(PubSubState.Disabled));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public async Task CommunicationIdReservationsProtectEveryConsumingMutation(int path)
        {
            using var reservations = new InMemoryPubSubCommunicationIdReservations(
                new PubSubDefaultPublisherIdPolicy(
                [
                    new PubSubDefaultPublisherId(Profiles.PubSubUdpUadpTransport, Variant.From(7u))
                ]));
            await using IPubSubApplication app = new PubSubApplicationBuilder(NUnitTelemetryContext.Create())
                .UseConfiguration(new PubSubConfigurationDataType
                {
                    PublishedDataSets = [new PublishedDataSetDataType { Name = "pds" }]
                })
                .WithCommunicationIdReservations(reservations)
                .UseAllStandardEncoders()
                .AddTransportFactory(new StubTransportFactory())
                .Build();
            var capability = (IPubSubCommunicationIdApplication)app;
            NodeId owner = new(123u);
            var connection = new PubSubConnectionDataType
            {
                Name = "reserved",
                TransportProfileUri = Profiles.PubSubUdpUadpTransport,
                PublisherId = Variant.From(7u),
                Address = new ExtensionObject(new NetworkAddressUrlDataType { Url = "opc.udp://224.0.0.22:4840" })
            };
            var group = new WriterGroupDataType
            {
                Name = "reserved-group",
                WriterGroupId = path == 3 ? (ushort)1 : (ushort)0x8000,
                PublishingInterval = 1000
            };
            var writer = new DataSetWriterDataType
            {
                Name = "reserved-writer",
                DataSetWriterId = 0x8000,
                DataSetName = "pds"
            };
            NodeId connectionId = default;
            NodeId groupId = default;
            if (path >= 2)
            {
                connectionId = await app.AddConnectionAsync(connection).ConfigureAwait(false);
                if (path == 3)
                {
                    groupId = await app.AddWriterGroupAsync(connectionId, group).ConfigureAwait(false);
                }
            }
            else
            {
                group.DataSetWriters = [writer];
                connection.WriterGroups = [group];
            }
            _ = await capability.ReserveCommunicationIdsAsync(owner,
                Profiles.PubSubUdpUadpTransport, 1, 1).ConfigureAwait(false);
            async ValueTask<NodeId> Consume(CancellationToken ct)
            {
                switch (path)
                {
                    case 0:
                        return await app.AddConnectionAsync(connection, ct).ConfigureAwait(false);
                    case 1:
                        PubSubConfigurationDataType configuration = app.GetConfiguration();
                        configuration.Connections = [connection];
                        _ = await app.ReplaceConfigurationAsync(configuration, ct).ConfigureAwait(false);
                        return new NodeId(1u);
                    case 2:
                        return await app.AddWriterGroupAsync(connectionId, group, ct).ConfigureAwait(false);
                    default:
                        return await app.AddDataSetWriterAsync(groupId, writer, ct).ConfigureAwait(false);
                }
            }
            ServiceResultException? denied = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await Consume(default).ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(denied!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            ServiceResultException? foreign = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await PubSubConfigurationSession.ExecuteAsync(new NodeId(456u), Consume).ConfigureAwait(false))
                .ConfigureAwait(false);
            Assert.That(foreign!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            NodeId added = await PubSubConfigurationSession.ExecuteAsync(owner, Consume).ConfigureAwait(false);
            Assert.That(added.IsNull, Is.False);
            PubSubConfigurationDataType committed = app.GetConfiguration();
            Assert.That(committed.Connections.Count, Is.EqualTo(1));
            Assert.That(committed.Connections[0].WriterGroups[0].WriterGroupId, Is.EqualTo(group.WriterGroupId));
            if (path != 2)
            {
                Assert.That(committed.Connections[0].WriterGroups[0].DataSetWriters[0].DataSetWriterId,
                    Is.EqualTo((ushort)0x8000));
            }
        }

        [Test]
        public async Task CommunicationIdReservationWaitsForCentralMutationTransaction()
        {
            using var inner = new InMemoryPubSubCommunicationIdReservations(
                new PubSubDefaultPublisherIdPolicy(
                [
                    new PubSubDefaultPublisherId(Profiles.PubSubUdpUadpTransport, Variant.From(7u))
                ]));
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var provider = new Mock<IPubSubCommunicationIdReservations>();
            provider.Setup(p => p.PersistAsync(It.IsAny<PubSubConfigurationDataType>(),
                It.IsAny<Func<CancellationToken, ValueTask>>(), It.IsAny<CancellationToken>()))
                .Returns(async (PubSubConfigurationDataType configuration,
                    Func<CancellationToken, ValueTask> persist, CancellationToken ct) =>
                {
                    entered.SetResult(true);
                    _ = await release.Task.ConfigureAwait(false);
                    await inner.PersistAsync(configuration, persist, ct).ConfigureAwait(false);
                });
            provider.Setup(p => p.ReserveAsync(It.IsAny<NodeId>(), It.IsAny<string>(), It.IsAny<ushort>(),
                It.IsAny<ushort>(), It.IsAny<PubSubConfigurationDataType>(), It.IsAny<CancellationToken>()))
                .Returns((NodeId owner, string scope, ushort groups, ushort writers,
                    PubSubConfigurationDataType configuration, CancellationToken ct) =>
                    inner.ReserveAsync(owner, scope, groups, writers, configuration, ct));
            await using IPubSubApplication app = new PubSubApplicationBuilder(NUnitTelemetryContext.Create())
                .UseConfiguration(new PubSubConfigurationDataType())
                .WithCommunicationIdReservations(provider.Object)
                .UseAllStandardEncoders()
                .AddTransportFactory(new StubTransportFactory())
                .Build();
            Task<NodeId> mutation = app.AddConnectionAsync(new PubSubConnectionDataType
            {
                Name = "pending",
                TransportProfileUri = Profiles.PubSubUdpUadpTransport,
                PublisherId = Variant.From(7u),
                Address = new ExtensionObject(new NetworkAddressUrlDataType { Url = "opc.udp://224.0.0.22:4840" }),
                WriterGroups =
                [
                    new WriterGroupDataType { Name = "pending", WriterGroupId = 0x8000, PublishingInterval = 1000 }
                ]
            }).AsTask();
            _ = await Task.WhenAny(entered.Task, mutation).ConfigureAwait(false);
            if (mutation.IsCompleted)
            {
                _ = await mutation.ConfigureAwait(false);
            }
            _ = await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Task<PubSubCommunicationIdReservation> reservation =
                ((IPubSubCommunicationIdApplication)app).ReserveCommunicationIdsAsync(new NodeId(123u),
                    Profiles.PubSubUdpUadpTransport, 1, 0).AsTask();
            Assert.That(reservation.IsCompleted, Is.False);
            provider.Verify(p => p.ReserveAsync(It.IsAny<NodeId>(), It.IsAny<string>(), It.IsAny<ushort>(),
                It.IsAny<ushort>(), It.IsAny<PubSubConfigurationDataType>(), It.IsAny<CancellationToken>()),
                Times.Never);
            release.SetResult(true);
            _ = await mutation.ConfigureAwait(false);
            PubSubCommunicationIdReservation result = await reservation.ConfigureAwait(false);
            Assert.That(result.WriterGroupIds[0], Is.EqualTo((ushort)0x8001));
        }

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
            NodeId id = await app.AddConnectionAsync(connCfg).ConfigureAwait(false);
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
            await app.AddConnectionAsync(connCfg).ConfigureAwait(false);
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
            await app.AddConnectionAsync(connCfg).ConfigureAwait(false);
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
            NodeId id = await app.AddConnectionAsync(connCfg).ConfigureAwait(false);
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
            NodeId id = await app.AddConnectionAsync(connCfg).ConfigureAwait(false);
            await app.RemoveConnectionAsync(id).ConfigureAwait(false);
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
            NodeId id = await app.AddConnectionAsync(connCfg).ConfigureAwait(false);
            ConfigurationVersionDataType vBefore = app.ConfigurationVersion;
            await Task.Delay(1100).ConfigureAwait(false);
            await app.RemoveConnectionAsync(id).ConfigureAwait(false);
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
            ArrayOf<StatusCode> results = await app.ReplaceConfigurationAsync(newCfg).ConfigureAwait(false);
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
                async () => await app.ReplaceConfigurationAsync(badCfg).ConfigureAwait(false),
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
            await app.AddConnectionAsync(connCfg).ConfigureAwait(false);
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
            NodeId connId = await app.AddConnectionAsync(connCfg).ConfigureAwait(false);
            var wgCfg = new WriterGroupDataType
            {
                Name = "wg-1",
                WriterGroupId = 1,
                PublishingInterval = 1000
            };
            NodeId wgId = await app.AddWriterGroupAsync(connId, wgCfg).ConfigureAwait(false);
            Assert.That(wgId.IsNull, Is.False);
            Assert.That(app.Connections[0].WriterGroups.Count, Is.EqualTo(1));
        }

        [Test]
        [TestSpec("9.1.7", Summary = "AddDataSetWriter attaches")]
        public async Task AddDataSetWriterAsyncAttachesToWriterGroup()
        {
            await using IPubSubApplication app = BuildAppWithPds();
            NodeId connId = await AddConnectionAsync(app).ConfigureAwait(false);
            var wgCfg = new WriterGroupDataType
            {
                Name = "wg-w",
                WriterGroupId = 1,
                PublishingInterval = 1000
            };
            NodeId wgId = await app.AddWriterGroupAsync(connId, wgCfg).ConfigureAwait(false);
            var dwCfg = new DataSetWriterDataType
            {
                Name = "writer-1",
                DataSetWriterId = 1,
                DataSetName = "pds-1"
            };
            NodeId dwId = await app.AddDataSetWriterAsync(wgId, dwCfg).ConfigureAwait(false);
            Assert.That(dwId.IsNull, Is.False);
        }

        [Test]
        [TestSpec("9.1.6", Summary = "AddReaderGroup attaches")]
        public async Task AddReaderGroupAsyncAttachesToConnection()
        {
            await using IPubSubApplication app = BuildApp();
            NodeId connId = await AddConnectionAsync(app).ConfigureAwait(false);
            var rgCfg = new ReaderGroupDataType { Name = "rg-1" };
            NodeId rgId = await app.AddReaderGroupAsync(connId, rgCfg).ConfigureAwait(false);
            Assert.That(rgId.IsNull, Is.False);
            Assert.That(app.Connections[0].ReaderGroups.Count, Is.EqualTo(1));
        }

        [Test]
        [TestSpec("9.1.8", Summary = "AddDataSetReader attaches")]
        public async Task AddDataSetReaderAsyncAttachesToReaderGroup()
        {
            await using IPubSubApplication app = BuildApp();
            NodeId connId = await AddConnectionAsync(app).ConfigureAwait(false);
            var rgCfg = new ReaderGroupDataType { Name = "rg-r" };
            NodeId rgId = await app.AddReaderGroupAsync(connId, rgCfg).ConfigureAwait(false);
            var drCfg = new DataSetReaderDataType
            {
                Name = "reader-1",
                DataSetWriterId = 1,
                MessageReceiveTimeout = 5000,
                SubscribedDataSet = new ExtensionObject(
                    new TargetVariablesDataType())
            };
            NodeId drId = await app.AddDataSetReaderAsync(rgId, drCfg).ConfigureAwait(false);
            Assert.That(drId.IsNull, Is.False);
        }

        [Test]
        [TestSpec("9.1.6", Summary = "Mutation disable/re-enable")]
        public async Task MutationDisablesThenReEnablesIfStarted()
        {
            await using IPubSubApplication app = BuildApp();
            await app.StartAsync().ConfigureAwait(false);
            var connCfg = new PubSubConnectionDataType
            {
                Name = "runtime-conn",
                TransportProfileUri = "http://opcfoundation.org/UA-Profile/Transport/pubsub-udp-uadp",
                Address = new ExtensionObject(
                    new NetworkAddressUrlDataType { Url = "opc.udp://224.0.0.22:4840" })
            };
            NodeId id = await app.AddConnectionAsync(connCfg).ConfigureAwait(false);
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
            return await app.AddConnectionAsync(connCfg).ConfigureAwait(false);
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
                m_isConnected = false;
                return default;
            }
        }
    }
}
