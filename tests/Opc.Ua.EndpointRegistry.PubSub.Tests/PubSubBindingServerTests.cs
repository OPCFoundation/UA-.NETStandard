/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * SPDX-License-Identifier: MIT
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.EndpointRegistry.Server;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.MetaData;
using Opc.Ua.PubSub.Server;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.SchemaRegistry;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Opc.Ua.XRegistry;
using Opc.Ua.XRegistry.Server;
using Quickstarts.ReferenceServer;
using ISession = Opc.Ua.Client.ISession;

namespace Opc.Ua.EndpointRegistry.PubSub.Tests
{
    [TestFixture]
    [NonParallelizable]
    public sealed partial class PubSubBindingServerTests
    {
        [OneTimeSetUp]
        public async Task StartServerAsync()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_pki = Path.Combine(TestContext.CurrentContext.WorkDirectory, nameof(PubSubBindingServerTests),
                Guid.NewGuid().ToString("N"));
            m_fixture = new ServerFixture<BindingServer>(telemetry => new BindingServer(telemetry))
            {
                AutoAccept = true,
                SecurityNone = true
            };
            m_client = new ClientFixture(m_telemetry);
            await m_fixture.LoadConfigurationAsync(Path.Combine(m_pki, "server")).ConfigureAwait(false);
            m_fixture.Config.ServerConfiguration!.UserTokenPolicies +=
                new UserTokenPolicy(UserTokenType.UserName) { SecurityPolicyUri = SecurityPolicies.Basic256Sha256 };
            await m_fixture.StartAsync().ConfigureAwait(false);
            await m_client.LoadClientConfigurationAsync(Path.Combine(m_pki, "client")).ConfigureAwait(false);
            m_session = await m_client.ConnectAsync(
                new Uri($"opc.tcp://localhost:{m_fixture.Port}/{nameof(BindingServer)}"),
                SecurityPolicies.Basic256Sha256, userIdentity: new UserIdentity("sysadmin", "demo"u8)).ConfigureAwait(false);
            m_session.MessageContext.Factory.Builder.AddOpcUaXRegistry().AddOpcUaSchemaRegistry()
                .AddOpcUaEndpointRegistry().Commit();
        }

        [SetUp]
        public async Task RestoreConfigurationAsync()
        {
            PubSubConfigurationDataType baseline = OracleVector.Find("5.0-JSON-publisher").Configuration();
            Server.Schemas.Metadata = baseline.PublishedDataSets[0].DataSetMetaData;
            await Server.Application.ReplaceConfigurationAsync(baseline).ConfigureAwait(false);
            Server.Schemas.Metadata = Server.Application.GetConfiguration().PublishedDataSets[0].DataSetMetaData;
            await Binding.RefreshAsync().ConfigureAwait(false);
            await ClearRemoteAsync("trusted").ConfigureAwait(false);
            await ClearRemoteAsync("unknown-version").ConfigureAwait(false);
        }

        [OneTimeTearDown]
        public async Task StopServerAsync()
        {
            m_session?.Dispose();
            if (m_client is not null)
            {
                await m_client.DisposeAsync().ConfigureAwait(false);
            }
            if (m_fixture is not null)
            {
                IPubSubApplication application = m_fixture.Server.Application;
                await m_fixture.StopAsync().ConfigureAwait(false);
                await application.DisposeAsync().ConfigureAwait(false);
            }
            if (m_pki is not null && Directory.Exists(m_pki))
            {
                Directory.Delete(m_pki, true);
            }
        }

        [Test]
        public async Task SecuredTcpExposesExactNativeReferencesAndCompleteSnapshotsWithoutOpeningTransportAsync()
        {
            Assert.That(m_session!.Endpoint.SecurityMode, Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
            PubSubAddressSpaceTarget group = GroupTarget();
            PubSubAddressSpaceTarget writer = WriterTarget();
            string xid = LocalXid();
            BaseObjectState endpoint = Server.Registry.FindCatalogObject(xid)!;
            BaseObjectState message = Server.Registry.FindCatalogObject(xid + "/messages/writer-62541")!;
            ArrayOf<ReferenceDescription> publications = await BrowseAsync(group.Node.NodeId, ReferenceTypeIds.PublishesTo).ConfigureAwait(false);
            ArrayOf<ReferenceDescription> definitions = await BrowseAsync(writer.Node.NodeId, ReferenceTypeIds.HasMessageDefinition).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(publications.Count, Is.EqualTo(1));
                Assert.That(ExpandedNodeId.ToNodeId(publications[0].NodeId, m_session.NamespaceUris), Is.EqualTo(endpoint.NodeId));
                Assert.That(definitions.Count, Is.EqualTo(1));
                Assert.That(ExpandedNodeId.ToNodeId(definitions[0].NodeId, m_session.NamespaceUris), Is.EqualTo(message.NodeId));
                Assert.That(GroupTarget().Node.TypeDefinitionId, Is.EqualTo(Ua.ObjectTypeIds.WriterGroupType));
                Assert.That(WriterTarget().Node.TypeDefinitionId, Is.EqualTo(Ua.ObjectTypeIds.DataSetWriterType));
            });
            NodeId bindingsNode = await ChildAsync(endpoint.NodeId, BrowseNames.PubSubBindings).ConfigureAwait(false);
            DataValue bindingsValue = await m_session.ReadValueAsync(bindingsNode).ConfigureAwait(false);
            Assert.That(bindingsValue.WrappedValue.TryGetValue(out ArrayOf<ExtensionObject> values), Is.True);
            Assert.That(values.Count, Is.GreaterThanOrEqualTo(2));
            bool writerSnapshot = false;
            foreach (ExtensionObject extension in values)
            {
                Assert.That(extension.TryGetValue(out PubSubBindingSnapshotDataType? snapshot, m_session.MessageContext), Is.True);
                Assert.That(snapshot!.CompleteConfiguration, Is.True);
                Assert.That(snapshot.Issues.Count, Is.Zero);
                Assert.That(snapshot.Configuration, Is.TypeOf<PubSubConnectionDataType>());
                Assert.That(snapshot.PublishedDataSets.Count, Is.EqualTo(1));
                Assert.That(snapshot.DataSetMetaData.Count, Is.EqualTo(1));
                if (snapshot.Message.Xid == xid + "/messages/writer-62541")
                {
                    writerSnapshot = true;
                    Assert.That(snapshot.Source, Is.EqualTo(NodeId.ToExpandedNodeId(writer.Node.NodeId, m_session.NamespaceUris)));
                    Assert.That(snapshot.Message.HasNativeTarget, Is.True);
                    Assert.That(snapshot.Schema.SelectedObjectUri, Is.EqualTo(KnownSchemas.SchemaUri));
                    Assert.That(Utils.IsEqual(snapshot.Configuration, Server.Application.GetConfiguration().Connections[0]), Is.True);
                }
            }
            Assert.That(writerSnapshot, Is.True);
            RegistryReadResultDataType native = await ReadNativeAsync(xid).ConfigureAwait(false);
            Assert.That(native.Document.TryGetValue(out EndpointDataType? record), Is.True);
            Assert.That(record!.Usage, Is.EqualTo((ArrayOf<string>)["subscriber", "consumer"]));
            Assert.That(record.Messages.Entries.Count, Is.EqualTo(1));
            Server.Transport.Verify(transport => transport.OpenAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task SurfacedDirectAndAncestorMutationsAreRejectedWhileOrdinaryMetadataStillCommitsAsync()
        {
            string xid = LocalXid();
            uint epoch = (await ReadNativeAsync(xid).ConfigureAwait(false)).Epoch;
            RegistryMutationResultDataType direct = await Host.PatchAsync(xid, ByteString.From("{\"description\":\"forged\"}"u8), 0)
                .ConfigureAwait(false);
            RegistryMutationResultDataType message = await Host.DeleteAsync(xid + "/messages/writer-62541", 0).ConfigureAwait(false);
            RegistryMutationResultDataType ancestor = await Host.PatchAsync(xid, ByteString.From("{\"messages\":null}"u8), 0)
                .ConfigureAwait(false);
            RegistryMutationResultDataType delete = await Host.DeleteAsync(xid, 0).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(direct.StatusCode, Is.EqualTo(StatusCodes.BadNotWritable));
                Assert.That(message.StatusCode, Is.EqualTo(StatusCodes.BadNotWritable));
                Assert.That(ancestor.StatusCode, Is.EqualTo(StatusCodes.BadNotWritable));
                Assert.That(delete.StatusCode, Is.EqualTo(StatusCodes.BadNotWritable));
            });
            var group = new GroupTypeClient(m_session!, Server.Registry.FindCatalogObject(xid)!.NodeId, m_telemetry!);
            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(
                async () => await group.DeleteAsync(0).ConfigureAwait(false))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadNotWritable));
            var root = new EndpointRegistryTypeClient(m_session!,
                ExpandedNodeId.ToNodeId(ObjectIds.EndpointRegistry, m_session!.NamespaceUris), m_telemetry!);
            ServiceResultException directPatch = Assert.ThrowsAsync<ServiceResultException>(
                async () => await root.CommitMetadataAsync(xid, ByteString.From("{\"description\":\"denied\"}"u8), 0)
                    .ConfigureAwait(false))!;
            ServiceResultException ancestorPatch = Assert.ThrowsAsync<ServiceResultException>(
                async () => await root.CommitMetadataAsync(xid, ByteString.From("{\"messages\":null}"u8), 0)
                    .ConfigureAwait(false))!;
            Assert.That(directPatch.StatusCode, Is.EqualTo(StatusCodes.BadNotWritable));
            Assert.That(ancestorPatch.StatusCode, Is.EqualTo(StatusCodes.BadNotWritable));
            Assert.That((await ReadNativeAsync(xid).ConfigureAwait(false)).Epoch, Is.EqualTo(epoch));

            string ordinary = "/endpoints/ordinary-" + Guid.NewGuid().ToString("N");
            EndpointDataType record = OrdinaryEndpoint(ordinary);
            RegistryMutationResultDataType write = await Host.WriteAsync(new RegistryWriteRequestDataType
            {
                TargetXid = ordinary,
                Definition = record
            }).ConfigureAwait(false);
            Assert.That(write.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(write.Epoch, Is.EqualTo(1));
            RegistryMutationResultDataType edit = await Host.PatchAsync(ordinary, ByteString.From("{\"description\":\"allowed\"}"u8), 1)
                .ConfigureAwait(false);
            Assert.That(edit.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(edit.Epoch, Is.EqualTo(2));
            Assert.That((await Host.DeleteAsync(ordinary, 2).ConfigureAwait(false)).StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public async Task PublicationChangesInvalidateBeforeRuntimeRetirementAndReactivateStableEntriesAsync()
        {
            string xid = LocalXid();
            uint epoch = (await ReadNativeAsync(xid).ConfigureAwait(false)).Epoch;
            BaseObjectState oldSource = GroupTarget().Node;
            var observer = new PausingLifecycleObserver();
            ((IPubSubConfigurationLifecycle)Server.Application).AddConfigurationObserver(observer);
            try
            {
                PubSubConfigurationDataType changed = Server.Application.GetConfiguration();
                changed.Connections[0].WriterGroups[0].TransportSettings.TryGetValue(out BrokerWriterGroupTransportDataType? transport);
                transport!.QueueName = "factory/changed/temperature";
                Task<ArrayOf<StatusCode>> mutation = Server.Application.ReplaceConfigurationAsync(changed).AsTask();
                await AwaitWithDeadlineAsync(observer.Retiring.Task).ConfigureAwait(false);
                Assert.That(Binding.Snapshots.Count, Is.Zero);
                Assert.That(Server.PubSub.FindPredefinedNode<BaseObjectState>(oldSource.NodeId), Is.SameAs(oldSource));
                Assert.That((await BrowseAsync(oldSource.NodeId, ReferenceTypeIds.PublishesTo).ConfigureAwait(false)).Count, Is.Zero);
                observer.Release.TrySetResult(true);
                await AwaitWithDeadlineAsync(mutation).ConfigureAwait(false);
                Assert.That(observer.Activated, Is.True);
                Assert.That(LocalXid(), Is.EqualTo(xid));
                Assert.That((await ReadNativeAsync(xid).ConfigureAwait(false)).Epoch, Is.EqualTo(epoch + 1));
                Assert.That(GroupTarget().Node, Is.Not.SameAs(oldSource));
                Assert.That((await BrowseAsync(GroupTarget().Node.NodeId, ReferenceTypeIds.PublishesTo).ConfigureAwait(false)).Count, Is.EqualTo(1));
            }
            finally
            {
                observer.Release.TrySetResult(true);
                ((IPubSubConfigurationLifecycle)Server.Application).RemoveConfigurationObserver(observer);
            }
        }

        [Test]
        public async Task NoOpRefreshPreservesStoreRevisionAndEntityEpochAsync()
        {
            string xid = LocalXid();
            ulong revision = Host.Current.Revision;
            uint epoch = (await ReadNativeAsync(xid).ConfigureAwait(false)).Epoch;
            await Binding.RefreshAsync().ConfigureAwait(false);
            await Binding.RefreshAsync().ConfigureAwait(false);
            Assert.That(Host.Current.Revision, Is.EqualTo(revision));
            Assert.That((await ReadNativeAsync(xid).ConfigureAwait(false)).Epoch, Is.EqualTo(epoch));
        }

        [Test]
        public async Task RemovedSourceAndNonMqttConfigurationRetireSurfacedEntriesAsync()
        {
            string xid = LocalXid();
            PubSubConfigurationDataType empty = Server.Application.GetConfiguration();
            empty.Connections = [];
            await Server.Application.ReplaceConfigurationAsync(empty).ConfigureAwait(false);
            Assert.That(Server.Registry.FindCatalogObject(xid), Is.Null);
            Assert.That(Binding.Snapshots.Count, Is.Zero);
            PubSubConfigurationDataType best = OracleVector.Find("5.0-JSON-publisher-bestavailable").Configuration();
            await Server.Application.ReplaceConfigurationAsync(best).ConfigureAwait(false);
            Assert.That(LocalPaths(), Is.Empty);
            Assert.That(Binding.Snapshots.Count, Is.Zero);
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task MajorAndMinorRuntimeMetadataDisagreementCannotBindAsync(bool major)
        {
            string surfaced = LocalXid();
            uint surfacedEpoch = (await ReadNativeAsync(surfaced).ConfigureAwait(false)).Epoch;
            string ordinary = "/endpoints/version-" + Guid.NewGuid().ToString("N");
            Assert.That((await Host.WriteAsync(new RegistryWriteRequestDataType
            {
                TargetXid = ordinary,
                Definition = OrdinaryEndpoint(ordinary)
            }).ConfigureAwait(false)).StatusCode, Is.EqualTo(StatusCodes.Good));
            PubSubConfigurationDataType configuration = Server.Application.GetConfiguration();
            DataSetMetaDataType metadata = (DataSetMetaDataType)configuration.PublishedDataSets[0].DataSetMetaData.Clone();
            WriterGroupDataType group = configuration.Connections[0].WriterGroups[0];
            DataSetWriterDataType writer = group.DataSetWriters[0];
            uint originalMajor = metadata.ConfigurationVersion.MajorVersion;
            if (major)
            {
                metadata.ConfigurationVersion.MajorVersion++;
            }
            else
            {
                metadata.ConfigurationVersion.MinorVersion++;
            }
            var key = new DataSetMetaDataKey(Opc.Ua.PubSub.Encoding.PublisherId.From(configuration.Connections[0].PublisherId),
                group.WriterGroupId, writer.DataSetWriterId, metadata.DataSetClassId,
                major ? originalMajor + 1 : originalMajor);
            Server.Application.MetaDataRegistry.Register(key, metadata);
            Assert.That((await BrowseAsync(GroupTarget().Node.NodeId, ReferenceTypeIds.PublishesTo).ConfigureAwait(false)).Count,
                Is.Zero, "Metadata notification invalidates synchronously.");
            await Binding.RefreshAsync().ConfigureAwait(false);
            Assert.That(LocalPaths(), Has.Count.EqualTo(1), "Metadata disagreement does not remove a still-configured publishing queue.");
            Assert.That((await ReadNativeAsync(surfaced).ConfigureAwait(false)).Epoch, Is.EqualTo(surfacedEpoch));
            Assert.That((await BrowseAsync(GroupTarget().Node.NodeId, ReferenceTypeIds.PublishesTo).ConfigureAwait(false)).Count, Is.Zero);
            bool incomplete = false;
            foreach (PubSubBindingSnapshotDataType snapshot in Binding.Snapshots)
            {
                if (snapshot.Endpoint.Xid == ordinary)
                {
                    incomplete = !snapshot.CompleteConfiguration &&
                        OracleCorrespondenceTests.Codes(snapshot.Issues)[0] == "E_METADATA_VERSION";
                }
            }
            Assert.That(incomplete, Is.True);
            await Host.DeleteAsync(ordinary, 0).ConfigureAwait(false);
        }

        [Test]
        public async Task SchemaAssociationsRequireExactDataSetMetadataAndSnapshotsArePrivateCopiesAsync()
        {
            PubSubBindingSnapshotDataType original = Binding.Snapshots[0];
            original.Configuration.Name = "caller-mutated";
            original.DataSetMetaData[0].Name = "caller-mutated";
            Assert.That(Binding.Snapshots[0].Configuration.Name, Is.Not.EqualTo("caller-mutated"));
            Assert.That(Binding.Snapshots[0].DataSetMetaData[0].Name, Is.Not.EqualTo("caller-mutated"));
            Server.Schemas.Metadata = (DataSetMetaDataType)Server.Schemas.Metadata.Clone();
            Server.Schemas.Metadata.ConfigurationVersion.MinorVersion++;
            await Binding.RefreshAsync().ConfigureAwait(false);
            Assert.That(LocalPaths(), Has.Count.EqualTo(1), "A publishing source still exists; only its invalid schema association is removed.");
            Assert.That((await BrowseAsync(WriterTarget().Node.NodeId, ReferenceTypeIds.HasMessageDefinition).ConfigureAwait(false)).Count, Is.Zero);
            bool schemaMismatch = false;
            foreach (PubSubBindingSnapshotDataType snapshot in Binding.Snapshots)
            {
                schemaMismatch |= !snapshot.CompleteConfiguration &&
                    OracleCorrespondenceTests.Codes(snapshot.Issues)[0] == "E_SCHEMA_METADATA_VERSION";
            }
            Assert.That(schemaMismatch, Is.True);
        }

        [Test]
        public async Task ConcurrentPublicationChangesAndOrdinaryEditsUseOneCasStateWithoutLosingEitherAsync()
        {
            string ordinary = "/endpoints/race-" + Guid.NewGuid().ToString("N");
            Assert.That((await Host.WriteAsync(new RegistryWriteRequestDataType
            {
                TargetXid = ordinary,
                Definition = OrdinaryEndpoint(ordinary)
            }).ConfigureAwait(false)).StatusCode, Is.EqualTo(StatusCodes.Good));
            for (int index = 0; index < 8; index++)
            {
                PubSubConfigurationDataType configuration = Server.Application.GetConfiguration();
                configuration.Connections[0].WriterGroups[0].PublishingInterval++;
                Task<ArrayOf<StatusCode>> publication = Server.Application.ReplaceConfigurationAsync(configuration).AsTask();
                Task<RegistryMutationResultDataType> edit = Host.PatchAsync(ordinary,
                    ByteString.From(System.Text.Encoding.UTF8.GetBytes("{\"description\":\"iteration-" + index + "\"}")), 0).AsTask();
                await Task.WhenAll(publication, edit).ConfigureAwait(false);
                Assert.That(edit.Result.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That((await ReadNativeAsync(ordinary).ConfigureAwait(false)).Epoch, Is.EqualTo(index + 2));
                Assert.That(LocalPaths(), Has.Count.EqualTo(1));
            }
            Assert.That((await Host.DeleteAsync(ordinary, 0).ConfigureAwait(false)).StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        internal static async Task AwaitWithDeadlineAsync(Task task)
        {
            Task completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(30))).ConfigureAwait(false);
            Assert.That(completed, Is.SameAs(task), "The lifecycle transition did not complete.");
            await task.ConfigureAwait(false);
        }

        private EndpointDataType OrdinaryEndpoint(string xid)
        {
            var record = (EndpointDataType)Host.Mapper.Project(OracleVector.Find("5.0-JSON-publisher").Endpoint, nameof(EndpointDataType));
            record.EndpointId = xid.Substring("/endpoints/".Length);
            return record;
        }

        private async Task<RegistryReadResultDataType> ReadNativeAsync(string xid)
        {
            var access = new NativeRegistryAccessTypeClient(m_session!,
                ExpandedNodeId.ToNodeId(EndpointRegistryWellKnown.EndpointRegistryTypedAccess, m_session!.NamespaceUris), m_telemetry!);
            return await access.ReadDocumentAsync(new RegistryReadRequestDataType
            {
                TargetXid = xid,
                DocumentKind = "metadata",
                View = 1,
                MaxItems = 100
            }).ConfigureAwait(false);
        }

        private async Task<ArrayOf<ReferenceDescription>> BrowseAsync(NodeId source, ExpandedNodeId reference)
        {
            BrowseResponse response = await m_session!.BrowseAsync(null, null, 0,
            [
                new BrowseDescription
                {
                    NodeId = source,
                    BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = ExpandedNodeId.ToNodeId(reference, m_session.NamespaceUris),
                    IncludeSubtypes = false,
                    ResultMask = (uint)BrowseResultMask.All
                }
            ], default).ConfigureAwait(false);
            Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            return response.Results[0].References;
        }

        private async Task<NodeId> ChildAsync(NodeId parent, string name)
        {
            TranslateBrowsePathsToNodeIdsResponse response = await m_session!.TranslateBrowsePathsToNodeIdsAsync(null,
            [
                new BrowsePath
                {
                    StartingNode = parent,
                    RelativePath = new RelativePath
                    {
                        Elements =
                        [
                            new RelativePathElement
                            {
                                ReferenceTypeId = Ua.ReferenceTypeIds.HierarchicalReferences,
                                IncludeSubtypes = true,
                                TargetName = new QualifiedName(name, m_session.NamespaceUris.GetIndexOrAppend(Namespaces.EndpointRegistry))
                            }
                        ]
                    }
                }
            ], default).ConfigureAwait(false);
            Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            return ExpandedNodeId.ToNodeId(response.Results[0].Targets[0].TargetId, m_session.NamespaceUris);
        }

        private string LocalXid()
        {
            List<string> paths = LocalPaths();
            Assert.That(paths, Has.Count.EqualTo(1));
            return paths[0];
        }

        private List<string> LocalPaths()
        {
            var result = new List<string>();
            RegistryObjectValueDataType document = Host.Current.CloneDocument();
            foreach (RegistryMemberDataType member in document.Members)
            {
                if (member.Name == "endpoints" && member.Value is RegistryObjectValueDataType endpoints)
                {
                    foreach (RegistryMemberDataType endpoint in endpoints.Members)
                    {
                        if (endpoint.Name!.StartsWith("pubsub-local-", StringComparison.Ordinal))
                        {
                            result.Add("/endpoints/" + endpoint.Name);
                        }
                    }
                }
            }
            return result;
        }

        private PubSubAddressSpaceTarget GroupTarget()
        {
            foreach (PubSubAddressSpaceTarget target in Server.PubSub.ConfigurationView!.Targets)
            {
                if (target.WriterGroup is not null && target.Writer is null)
                {
                    return target;
                }
            }
            throw new AssertionException("No actual WriterGroup Object.");
        }

        private PubSubAddressSpaceTarget WriterTarget()
        {
            foreach (PubSubAddressSpaceTarget target in Server.PubSub.ConfigurationView!.Targets)
            {
                if (target.Writer is not null)
                {
                    return target;
                }
            }
            throw new AssertionException("No actual DataSetWriter Object.");
        }

        private BindingServer Server => m_fixture!.Server;
        private RegistryNativeHost Host => Server.Registry.Generic!;
        private EndpointRegistryPubSubBinding Binding => Server.Integration.Binding!;

        internal sealed class BindingServer : ReferenceServer
        {
            public BindingServer(ITelemetryContext telemetry) : base(telemetry)
            {
                PubSubConfigurationDataType configuration = OracleVector.Find("5.0-JSON-publisher").Configuration();
                Schemas = new KnownSchemas { Metadata = configuration.PublishedDataSets[0].DataSetMetaData };
                Transport = new Mock<IPubSubTransport>();
                Transport.SetupGet(value => value.TransportProfileUri).Returns(PubSubBindingRules.JsonProfile);
                var factory = new Mock<IPubSubTransportFactory>();
                factory.SetupGet(value => value.TransportProfileUri).Returns(PubSubBindingRules.JsonProfile);
                factory.Setup(value => value.Create(It.IsAny<PubSubConnectionDataType>(),
                    It.IsAny<ITelemetryContext>(), It.IsAny<TimeProvider>())).Returns(Transport.Object);
                var uadpFactory = new Mock<IPubSubTransportFactory>();
                uadpFactory.SetupGet(value => value.TransportProfileUri).Returns(PubSubBindingRules.UadpProfile);
                uadpFactory.Setup(value => value.Create(It.IsAny<PubSubConnectionDataType>(),
                    It.IsAny<ITelemetryContext>(), It.IsAny<TimeProvider>())).Returns(Transport.Object);
                Application = new PubSubApplicationBuilder(telemetry).UseConfiguration(configuration)
                    .AddTransportFactory(factory.Object)
                    .AddTransportFactory(uadpFactory.Object)
                    .AddEncoder(new Opc.Ua.PubSub.Encoding.Json.JsonEncoder())
                    .AddDecoder(new Opc.Ua.PubSub.Encoding.Json.JsonDecoder())
                    .AddEncoder(new Opc.Ua.PubSub.Encoding.Uadp.UadpEncoder())
                    .AddDecoder(new Opc.Ua.PubSub.Encoding.Uadp.UadpDecoder())
                    .AddDataSetSource("Temperature", new Mock<IPublishedDataSetSource>().Object)
                    .Build();
                AddNodeManager(new CapturingFactory([Namespaces.EndpointRegistry], (server, config) =>
                    Registry = new EndpointRegistryNodeManager(server, config)));
                AddNodeManager(new CapturingFactory([PubSubNodeManager.NamespaceUri], (server, config) =>
                    PubSub = new PubSubNodeManager(server, config, Application, null, new PubSubServerOptions(), telemetry)));
                AddNodeManager(new CapturingFactory(["http://opcfoundation.org/UA/EndpointRegistry/PubSub"], (server, config) =>
                    Integration = new EndpointRegistryPubSubNodeManager(server, config, new EndpointRegistryPubSubBindingOptions
                    {
                        Schemas = Schemas,
                        TimeProvider = Clock,
                        RemotePublishers =
                        [
                            new RemotePubSubPublisherBinding
                            {
                                Id = "trusted",
                                PublisherId = Variant.From((ushort)2234),
                                TopicPrefix = "opcua/json",
                                ConnectionTopic = "opcua/json/connection/2234",
                                AuthenticatedIdentity = "publisher-client",
                                BrokerUrl = "mqtts://authorized.example.test",
                                MqttVersion = "5.0"
                            },
                            new RemotePubSubPublisherBinding
                            {
                                Id = "unknown-version",
                                PublisherId = Variant.From((ushort)2234),
                                TopicPrefix = "opcua/json",
                                ConnectionTopic = "opcua/json/connection/2234",
                                AuthenticatedIdentity = "publisher-client",
                                BrokerUrl = "mqtts://authorized.example.test"
                            }
                        ]
                    })));
            }

            public IPubSubApplication Application { get; }
            public Mock<IPubSubTransport> Transport { get; }
            public KnownSchemas Schemas { get; }
            public ManualClock Clock { get; } = new();
            public EndpointRegistryNodeManager Registry { get; private set; } = null!;
            public PubSubNodeManager PubSub { get; private set; } = null!;
            public EndpointRegistryPubSubNodeManager Integration { get; private set; } = null!;
        }

        internal sealed class KnownSchemas : IPubSubBindingSchemaProvider
        {
            public const string SchemaUri = "https://registry.example.test/schemagroups/com.example.UA.Factory/schemas/Temperature.jsonschema/versions/1";
            public DataSetMetaDataType Metadata { get; set; } = null!;
            public PubSubSchemaAssociation? FindSchema(DataSetMetaDataKey key, DataSetMetaDataType metadata)
            {
                return new PubSubSchemaAssociation(Metadata, new SchemaReferenceDataType
                {
                    EntityUri = SchemaUri,
                    SelectedObjectUri = SchemaUri,
                    Format = "JsonSchema/2020-12"
                });
            }
        }

        private sealed class CapturingFactory(
            ArrayOf<string> namespaces,
            Func<IServerInternal, ApplicationConfiguration, IAsyncNodeManager> create) : IAsyncNodeManagerFactory
        {
            public ArrayOf<string> NamespacesUris => namespaces;
            public ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server, ApplicationConfiguration configuration, CancellationToken cancellationToken = default)
            {
                return new ValueTask<IAsyncNodeManager>(create(server, configuration));
            }
        }

        private sealed class PausingLifecycleObserver : IPubSubConfigurationObserver
        {
            public TaskCompletionSource<bool> Retiring { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public bool Activated { get; private set; }
            public async ValueTask ConfigurationRetiringAsync(PubSubConfigurationDataType configuration, CancellationToken cancellationToken)
            {
                Retiring.TrySetResult(true);
                await Release.Task.ConfigureAwait(false);
            }

            public ValueTask ConfigurationActivatedAsync(PubSubConfigurationDataType configuration, CancellationToken cancellationToken)
            {
                Activated = true;
                return default;
            }
        }

        internal sealed class ManualClock : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => m_now;
            public void Advance(TimeSpan interval) => m_now += interval;
            private DateTimeOffset m_now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
        }

        private ITelemetryContext? m_telemetry;
        private string? m_pki;
        private ServerFixture<BindingServer>? m_fixture;
        private ClientFixture? m_client;
        private ISession? m_session;
    }
}
