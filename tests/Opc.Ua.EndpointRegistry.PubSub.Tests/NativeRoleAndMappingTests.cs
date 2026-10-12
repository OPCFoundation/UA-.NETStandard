/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * SPDX-License-Identifier: MIT
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Server;
using Opc.Ua.XRegistry;

namespace Opc.Ua.EndpointRegistry.PubSub.Tests
{
    public sealed partial class PubSubBindingServerTests
    {
        [TestCase("3.1.1", "JSON")]
        [TestCase("5.0", "JSON")]
        [TestCase("3.1.1", "UADP")]
        [TestCase("5.0", "UADP")]
        public async Task LocalNativeSurfacePreservesEveryVersionAndMappingAsync(string version, string mapping)
        {
            PubSubConfigurationDataType configuration = OracleVector.Find(version + "-" + mapping + "-publisher").Configuration();
            Server.Schemas.Metadata = configuration.PublishedDataSets[0].DataSetMetaData;
            await Server.Application.ReplaceConfigurationAsync(configuration).ConfigureAwait(false);
            Server.Schemas.Metadata = Server.Application.GetConfiguration().PublishedDataSets[0].DataSetMetaData;
            await Binding.RefreshAsync().ConfigureAwait(false);
            string xid = LocalXid();
            RegistryReadResultDataType read = await ReadNativeAsync(xid).ConfigureAwait(false);
            Assert.That(read.Document.TryGetValue(out EndpointDataType? endpoint), Is.True);
            Assert.That(endpoint!.Protocol, Is.EqualTo("MQTT/" + version));
            MessageDefinitionDataType message = endpoint.Messages.Entries[0].Value;
            Assert.That(message.DataContentType, Is.EqualTo(mapping == "JSON" ? "application/json" : "application/opcua+uadp"));
            Assert.That((await BrowseAsync(GroupTarget().Node.NodeId, ReferenceTypeIds.PublishesTo).ConfigureAwait(false)).Count,
                Is.EqualTo(1));
            Assert.That((await BrowseAsync(WriterTarget().Node.NodeId, ReferenceTypeIds.HasMessageDefinition).ConfigureAwait(false)).Count,
                Is.EqualTo(1));
            bool metadata = false;
            foreach (PubSubBindingSnapshotDataType snapshot in Binding.Snapshots)
            {
                Assert.That(snapshot.Configuration.TransportProfileUri, Is.EqualTo(configuration.Connections[0].TransportProfileUri));
                Assert.That(snapshot.CompleteConfiguration, Is.True);
                metadata |= snapshot.DataSetMetaData.Count == 1 && snapshot.PublishedDataSets.Count == 1;
            }
            Assert.That(metadata, Is.True);
        }

        [Test]
        public async Task WriterOwnedQueueBindsOnlyActualWriterAndInheritedQueueBindsActualGroupAsync()
        {
            PubSubConfigurationDataType configuration = Server.Application.GetConfiguration();
            WriterGroupDataType group = configuration.Connections[0].WriterGroups[0];
            DataSetWriterDataType writer = group.DataSetWriters[0];
            writer.TransportSettings.TryGetValue(out BrokerDataSetWriterTransportDataType? own);
            own!.QueueName = "factory/temperature/own";
            group.MessageSettings.TryGetValue(out JsonWriterGroupMessageDataType? settings);
            settings!.NetworkMessageContentMask |= (uint)JsonNetworkMessageContentMask.SingleDataSetMessage;
            var second = (DataSetWriterDataType)writer.Clone();
            second.Name = "Pressure";
            second.DataSetWriterId = 62542;
            second.TransportSettings.TryGetValue(out BrokerDataSetWriterTransportDataType? inherited);
            inherited!.QueueName = string.Empty;
            group.DataSetWriters = [writer, second];
            await Server.Application.ReplaceConfigurationAsync(configuration).ConfigureAwait(false);
            Assert.That(LocalPaths(), Has.Count.EqualTo(2));
            PubSubAddressSpaceTarget? ownedWriter = null;
            PubSubAddressSpaceTarget? inheritedWriter = null;
            foreach (PubSubAddressSpaceTarget target in Server.PubSub.ConfigurationView!.Targets)
            {
                if (target.Writer?.DataSetWriterId == 62541)
                {
                    ownedWriter = target;
                }
                if (target.Writer?.DataSetWriterId == 62542)
                {
                    inheritedWriter = target;
                }
            }
            Assert.That(ownedWriter, Is.Not.Null);
            Assert.That(inheritedWriter, Is.Not.Null);
            Assert.That((await BrowseAsync(ownedWriter!.Node.NodeId, ReferenceTypeIds.PublishesTo).ConfigureAwait(false)).Count,
                Is.EqualTo(1));
            Assert.That((await BrowseAsync(inheritedWriter!.Node.NodeId, ReferenceTypeIds.PublishesTo).ConfigureAwait(false)).Count,
                Is.Zero);
            Assert.That((await BrowseAsync(GroupTarget().Node.NodeId, ReferenceTypeIds.PublishesTo).ConfigureAwait(false)).Count,
                Is.EqualTo(1));
            Assert.That((await BrowseAsync(ownedWriter.Node.NodeId, ReferenceTypeIds.HasMessageDefinition).ConfigureAwait(false)).Count,
                Is.EqualTo(1));
            Assert.That((await BrowseAsync(inheritedWriter.Node.NodeId, ReferenceTypeIds.HasMessageDefinition).ConfigureAwait(false)).Count,
                Is.EqualTo(1));
        }

        [Test]
        public async Task ActualDataSetReaderUsesExactSubscribesToAndHasMessageDefinitionReferencesAsync()
        {
            OracleVector vector = OracleVector.Find("5.0-JSON-reader");
            PubSubConfigurationDataType configuration = vector.Configuration();
            Server.Schemas.Metadata = configuration.Connections[0].ReaderGroups[0].DataSetReaders[0].DataSetMetaData;
            await Server.Application.ReplaceConfigurationAsync(configuration).ConfigureAwait(false);
            string xid = "/endpoints/reader-" + Guid.NewGuid().ToString("N");
            RegistryObjectValueDataType generic = vector.Endpoint;
            var members = new List<RegistryMemberDataType>();
            foreach (RegistryMemberDataType member in generic.Members)
            {
                members.Add(member.Name == "endpointid" ? new RegistryMemberDataType
                {
                    Name = "endpointid",
                    Value = new RegistryStringValueDataType { Kind = 2, Value = xid.Substring("/endpoints/".Length) }
                } : member);
            }
            members.Add(new RegistryMemberDataType
            {
                Name = "messages",
                Value = new RegistryObjectValueDataType
                {
                    Kind = 5,
                    Members = [new RegistryMemberDataType { Name = "temperature", Value = vector.Message }]
                }
            });
            generic.Members = members.ToArray();
            RegistryMutationResultDataType commit = await Host.WriteAsync(new RegistryWriteRequestDataType
            {
                TargetXid = xid,
                Definition = Host.Mapper.Project(generic, nameof(EndpointDataType))
            }).ConfigureAwait(false);
            Assert.That(commit.StatusCode, Is.EqualTo(StatusCodes.Good));
            PubSubAddressSpaceTarget target = Server.PubSub.ConfigurationView!.Targets[0];
            Assert.That(target.Reader, Is.Not.Null);
            Assert.That(target.Node.TypeDefinitionId, Is.EqualTo(Ua.ObjectTypeIds.DataSetReaderType));
            Assert.That((await BrowseAsync(target.Node.NodeId, ReferenceTypeIds.SubscribesTo).ConfigureAwait(false)).Count, Is.EqualTo(1));
            Assert.That((await BrowseAsync(target.Node.NodeId, ReferenceTypeIds.HasMessageDefinition).ConfigureAwait(false)).Count, Is.EqualTo(1));
            foreach (PubSubBindingSnapshotDataType snapshot in Binding.Snapshots)
            {
                Assert.That(snapshot.PublishedDataSets.Count, Is.Zero);
                Assert.That(snapshot.DataSetMetaData.Count, Is.EqualTo(1));
                Assert.That(snapshot.CompleteConfiguration, Is.True);
            }
            Assert.That((await Host.DeleteAsync(xid, 1).ConfigureAwait(false)).StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That((await BrowseAsync(target.Node.NodeId, ReferenceTypeIds.SubscribesTo).ConfigureAwait(false)).Count, Is.Zero);
        }
    }
}
