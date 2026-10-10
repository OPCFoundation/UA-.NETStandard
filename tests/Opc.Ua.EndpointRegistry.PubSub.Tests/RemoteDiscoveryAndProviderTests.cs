/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * SPDX-License-Identifier: MIT
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.Server.Hosting;
using Opc.Ua.XRegistry;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.EndpointRegistry.PubSub.Tests
{
    public sealed partial class PubSubBindingServerTests
    {
        [Test]
        public async Task AuthenticatedRemoteConnectionNeedsIndependentVersionAndRetainedMetadataAsync()
        {
            RemotePubSubObservation incomplete = RemoteConnection("unknown-version");
            RemotePubSubObservationResult unknown = await Binding.ObserveRemoteAsync(incomplete).ConfigureAwait(false);
            Assert.That(unknown.Accepted, Is.True);
            Assert.That(unknown.Issues.Count, Is.EqualTo(1));
            Assert.That(unknown.Issues[0].Code, Is.EqualTo("M_MQTT_VERSION"));
            Assert.That(RemotePaths(), Is.Empty);

            RemotePubSubObservation connection = RemoteConnection("trusted");
            RemotePubSubObservationResult awaiting = await Binding.ObserveRemoteAsync(connection).ConfigureAwait(false);
            Assert.That(awaiting.Accepted, Is.True);
            Assert.That(OracleCorrespondenceTests.Codes(awaiting.Issues), Does.Contain("M_METADATA"));
            Assert.That(RemotePaths(), Is.Empty, "The registry must not invent missing native metadata.");
            RemotePubSubObservation metadata = RemoteMetadata();
            Assert.That((await Binding.ObserveRemoteAsync(metadata).ConfigureAwait(false)).Accepted, Is.True);
            Assert.That(RemotePaths(), Has.Count.EqualTo(1));
            string xid = RemotePaths()[0];
            RegistryReadResultDataType result = await ReadNativeAsync(xid).ConfigureAwait(false);
            Assert.That(result.Document.TryGetValue(out EndpointDataType? endpoint), Is.True);
            var options = (EndpointProtocolOptionsMQTT50DataType)endpoint!.ProtocolOptions;
            Assert.That(options.Endpoints[0].Uri, Is.EqualTo("mqtts://authorized.example.test"));
            Assert.That(endpoint.Protocol, Is.EqualTo("MQTT/5.0"));
            Assert.That(endpoint.Messages.Entries.Count, Is.EqualTo(1));
            Assert.That((await BrowseAsync(Server.Registry.FindCatalogObject(xid)!.NodeId,
                ReferenceTypeIds.PublishesTo).ConfigureAwait(false)).Count, Is.Zero);
            foreach (PubSubBindingSnapshotDataType snapshot in Binding.Snapshots)
            {
                Assert.That(Server.PubSub.FindPredefinedNode<BaseObjectState>(
                    ExpandedNodeId.ToNodeId(snapshot.Source, m_session!.NamespaceUris)), Is.Not.Null,
                    "Every native Source is a real hosted Object; matching local Message counterparts remain legal.");
            }
            Assert.That((await Host.PatchAsync(xid, ByteString.From("{\"description\":\"forged\"}"u8), 0)
                .ConfigureAwait(false)).StatusCode, Is.EqualTo(StatusCodes.BadNotWritable));
            await ClearRemoteAsync("trusted").ConfigureAwait(false);
            Assert.That(RemotePaths(), Is.Empty);
            await ClearRemoteAsync("unknown-version").ConfigureAwait(false);
        }

        [Test]
        public async Task RemoteDiscoveryRejectsWrongIdentityTopicPublisherAndDuplicateOrOlderMessagesAsync()
        {
            RemotePubSubObservation identity = RemoteConnection("trusted");
            identity.AuthenticatedIdentity = "untrusted-client";
            Assert.That((await Binding.ObserveRemoteAsync(identity).ConfigureAwait(false)).Accepted, Is.False);
            RemotePubSubObservation topic = RemoteConnection("trusted");
            topic.Topic = "other/json/connection/2234";
            Assert.That((await Binding.ObserveRemoteAsync(topic).ConfigureAwait(false)).Issues[0].Code, Is.EqualTo("E_DISCOVERY_IDENTITY"));
            RemotePubSubObservation publisher = RemoteConnection("trusted");
            publisher.Connection!.PublisherId = Variant.From((ushort)9999);
            Assert.That((await Binding.ObserveRemoteAsync(publisher).ConfigureAwait(false)).Issues[0].Code, Is.EqualTo("E_DISCOVERY_PUBLISHER"));
            RemotePubSubObservation accepted = RemoteConnection("trusted");
            Assert.That((await Binding.ObserveRemoteAsync(accepted).ConfigureAwait(false)).Accepted, Is.True);
            Assert.That((await Binding.ObserveRemoteAsync(RemoteMetadata()).ConfigureAwait(false)).Accepted, Is.True);
            ulong revision = Host.Current.Revision;
            Assert.That((await Binding.ObserveRemoteAsync(accepted).ConfigureAwait(false)).Accepted, Is.False);
            RemotePubSubObservation stale = RemoteConnection("trusted");
            stale.Timestamp = accepted.Timestamp.AddTicks(-1);
            Assert.That((await Binding.ObserveRemoteAsync(stale).ConfigureAwait(false)).Issues[0].Code, Is.EqualTo("E_DISCOVERY_REPLAY"));
            RemotePubSubObservation replay = RemoteConnection("trusted");
            replay.MessageId = accepted.MessageId;
            Assert.That((await Binding.ObserveRemoteAsync(replay).ConfigureAwait(false)).Accepted, Is.False);
            Assert.That(Host.Current.Revision, Is.EqualTo(revision));
            await ClearRemoteAsync("trusted").ConfigureAwait(false);
            Assert.That(RemotePaths(), Is.Empty);
            Assert.That((await Binding.ObserveRemoteAsync(accepted).ConfigureAwait(false)).Accepted, Is.False,
                "A retained tombstone must not be resurrected by an old message.");
        }

        [Test]
        public async Task RemoteMetadataAndConnectionExpireAtTheirOwnBoundariesWithoutReplayResurrectionAsync()
        {
            RemotePubSubObservation connection = RemoteConnection("trusted");
            connection.ExpiresAt = Server.Clock.GetUtcNow().AddMinutes(2);
            Assert.That((await Binding.ObserveRemoteAsync(connection).ConfigureAwait(false)).Accepted, Is.True);
            RemotePubSubObservation metadata = RemoteMetadata();
            metadata.ExpiresAt = Server.Clock.GetUtcNow().AddSeconds(20);
            Assert.That((await Binding.ObserveRemoteAsync(metadata).ConfigureAwait(false)).Accepted, Is.True);
            Assert.That(RemotePaths(), Has.Count.EqualTo(1));
            await Binding.ExpireRemoteAsync(metadata.ExpiresAt.Value.AddTicks(-1)).ConfigureAwait(false);
            Assert.That(RemotePaths(), Has.Count.EqualTo(1));
            await Binding.ExpireRemoteAsync(metadata.ExpiresAt.Value).ConfigureAwait(false);
            Assert.That(RemotePaths(), Is.Empty);
            Assert.That((await Binding.ObserveRemoteAsync(metadata).ConfigureAwait(false)).Accepted, Is.False);
            Assert.That((await Binding.ObserveRemoteAsync(RemoteMetadata()).ConfigureAwait(false)).Accepted, Is.True);
            Assert.That(RemotePaths(), Has.Count.EqualTo(1));
            await Binding.ExpireRemoteAsync(connection.ExpiresAt.Value).ConfigureAwait(false);
            Assert.That(RemotePaths(), Is.Empty);
            Assert.That((await Binding.ObserveRemoteAsync(connection).ConfigureAwait(false)).Accepted, Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task FailedRemoteRemovalIsRetriedWithoutReplayResurrectionAsync(bool retainedClear)
        {
            await Binding.ObserveRemoteAsync(RemoteConnection("trusted")).ConfigureAwait(false);
            RemotePubSubObservation metadata = RemoteMetadata();
            metadata.ExpiresAt = Server.Clock.GetUtcNow().AddMinutes(1);
            await Binding.ObserveRemoteAsync(metadata).ConfigureAwait(false);
            Assert.That(RemotePaths(), Has.Count.EqualTo(1));
            RemotePubSubObservation clear = RemoteConnection("trusted");
            clear.Connection = null;
            clear.RetainedCleared = true;
            Server.RejectCommits = true;
            try
            {
                ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                {
                    if (retainedClear)
                    {
                        await Binding.ObserveRemoteAsync(clear).ConfigureAwait(false);
                    }
                    else
                    {
                        await Binding.ExpireRemoteAsync(metadata.ExpiresAt.Value).ConfigureAwait(false);
                    }
                })!;
                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadResourceUnavailable));
                Assert.That(RemotePaths(), Has.Count.EqualTo(1));
                Assert.That((await Binding.ObserveRemoteAsync(retainedClear ? clear : metadata).ConfigureAwait(false))
                    .Accepted, Is.False);
                Server.RejectCommits = false;
                await Binding.ExpireRemoteAsync(Server.Clock.GetUtcNow()).ConfigureAwait(false);
                Assert.That(RemotePaths(), Is.Empty);
                Assert.That((await Binding.ObserveRemoteAsync(metadata).ConfigureAwait(false)).Accepted, Is.False);
                Assert.That(RemotePaths(), Is.Empty);
            }
            finally
            {
                Server.RejectCommits = false;
                await ClearRemoteAsync("trusted").ConfigureAwait(false);
            }
        }

        [Test]
        public async Task SharedProviderCapabilityPreservesValidationEpochsNoOpsAndProtectedOrdinaryPathsAsync()
        {
            string prefix = "provider-test-" + Guid.NewGuid().ToString("N") + "-";
            RegistryNativeProvider provider = Host.CreateProvider("endpoints", prefix);
            string xid = "/endpoints/" + prefix + "entry";
            EndpointDataType endpoint = OrdinaryEndpoint(xid);
            RegistryMutationResultDataType first = await provider.ReplaceAsync(xid, endpoint).ConfigureAwait(false);
            Assert.That(first.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(first.Epoch, Is.EqualTo(1));
            ulong revision = Host.Current.Revision;
            RegistryMutationResultDataType noOp = await provider.ReplaceAsync(xid, endpoint, 1).ConfigureAwait(false);
            Assert.That(noOp.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(noOp.Epoch, Is.EqualTo(1));
            Assert.That(Host.Current.Revision, Is.EqualTo(revision));
            Assert.That((await Host.PatchAsync(xid, ByteString.From("{\"description\":\"denied\"}"u8), 0)
                .ConfigureAwait(false)).StatusCode, Is.EqualTo(StatusCodes.BadNotWritable));
            Assert.That((await provider.ReplaceAsync(xid, endpoint, 2).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That((await provider.ReplaceAsync("/endpoints/foreign", endpoint).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.BadNotWritable));
            Assert.Throws<InvalidOperationException>(() => Host.CreateProvider("endpoints", prefix));
            EndpointDataType invalid = OrdinaryEndpoint(xid);
            invalid.Protocol = "HTTP";
            Assert.That((await provider.ReplaceAsync(xid, invalid, 1).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(Host.Current.Revision, Is.EqualTo(revision));
            Assert.That((await provider.DeleteAsync(xid, 1).ConfigureAwait(false)).StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That((await Host.WriteAsync(new RegistryWriteRequestDataType
            {
                TargetXid = xid,
                Definition = endpoint
            }).ConfigureAwait(false)).StatusCode, Is.EqualTo(StatusCodes.BadNotWritable),
                "Ordinary writes cannot introduce a Group into a provider's reserved namespace.");
        }

        [Test]
        public void DirectAndDiFactoriesComposeWithoutRegisteringAnotherRuntime()
        {
            var services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa().AddServer(_ => { });
            builder.AddEndpointRegistryPubSubBinding(new EndpointRegistryPubSubBindingOptions { SurfaceLocalPublishers = false });
            bool bindingFactory = false;
            foreach (ServiceDescriptor descriptor in services)
            {
                if (descriptor.ImplementationInstance is Opc.Ua.Server.Hosting.OpcUaServerNodeManagerRegistration
                    { AsyncFactory: EndpointRegistryPubSubNodeManagerFactory factory })
                {
                    bindingFactory = factory.NamespacesUris.Count == 1;
                }
                Assert.That(descriptor.ServiceType, Is.Not.EqualTo(typeof(IPubSubApplication)));
            }
            Assert.That(bindingFactory, Is.True);
            Assert.That(new EndpointRegistryPubSubNodeManagerFactory().NamespacesUris.Count, Is.EqualTo(1));
        }

        private RemotePubSubObservation RemoteConnection(string binding)
        {
            Server.Clock.Advance(TimeSpan.FromSeconds(1));
            PubSubConnectionDataType connection = OracleVector.Find("5.0-JSON-publisher").Connection();
            connection.Address = ExtensionObject.Null;
            connection.ConnectionProperties = [];
            return new RemotePubSubObservation
            {
                BindingId = binding,
                AuthenticatedIdentity = "publisher-client",
                Topic = "opcua/json/connection/2234",
                Timestamp = Server.Clock.GetUtcNow(),
                MessageId = Guid.NewGuid().ToString("N"),
                Connection = connection
            };
        }

        private RemotePubSubObservation RemoteMetadata()
        {
            Server.Clock.Advance(TimeSpan.FromSeconds(1));
            return new RemotePubSubObservation
            {
                BindingId = "trusted",
                AuthenticatedIdentity = "publisher-client",
                Topic = "opcua/json/metadata/2234/Line1/Temperature",
                Timestamp = Server.Clock.GetUtcNow(),
                MessageId = Guid.NewGuid().ToString("N"),
                MetaData =
                [
                    new PubSubDataSetMetaDataDiscoveryResult
                    {
                        PublisherId = PublisherId.From(Variant.From((ushort)2234)),
                        WriterGroupId = 600,
                        DataSetWriterId = 62541,
                        DataSetMetaData = Server.Application.GetConfiguration().PublishedDataSets[0].DataSetMetaData
                    }
                ]
            };
        }

        private async Task ClearRemoteAsync(string binding)
        {
            RemotePubSubObservation clear = RemoteConnection(binding);
            clear.Connection = null;
            clear.RetainedCleared = true;
            Assert.That((await Binding.ObserveRemoteAsync(clear).ConfigureAwait(false)).Accepted, Is.True);
        }

        private List<string> RemotePaths()
        {
            var result = new List<string>();
            foreach (RegistryMemberDataType member in Host.Current.CloneDocument().Members)
            {
                if (member.Name == "endpoints" && member.Value is RegistryObjectValueDataType endpoints)
                {
                    foreach (RegistryMemberDataType endpoint in endpoints.Members)
                    {
                        if (endpoint.Name!.StartsWith("pubsub-remote-", StringComparison.Ordinal))
                        {
                            result.Add("/endpoints/" + endpoint.Name);
                        }
                    }
                }
            }
            return result;
        }
    }
}
