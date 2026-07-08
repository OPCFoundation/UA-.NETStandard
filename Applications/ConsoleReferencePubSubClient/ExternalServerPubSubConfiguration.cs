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

using Opc.Ua;
using Opc.Ua.PubSub.Configuration;

namespace Quickstarts.ConsoleReferencePubSubClient
{
    /// <summary>
    /// Builds the small, self-contained Part 14
    /// <see cref="PubSubConfigurationDataType"/> payloads consumed by the
    /// external-server PubSub adapters. The publisher payload maps three
    /// PublishedDataSet variables onto well-known Server status nodes that
    /// exist on every OPC UA server, and the subscriber payload maps the same
    /// field set onto writable nodes on the target server.
    /// </summary>
    /// <remarks>
    /// The fluent <see cref="PubSubConfigurationBuilder"/> assembles the
    /// connection / writer-group / reader-group structure and the DataSet
    /// metadata. The two adapter-specific pieces it does not model directly -
    /// the PublishedDataSet <see cref="PublishedDataItemsDataType"/> source
    /// node ids and the DataSetReader <see cref="TargetVariablesDataType"/>
    /// write targets - are attached afterwards, because those node ids are
    /// exactly what the <c>Opc.Ua.PubSub.Adapter</c> reads from and writes to
    /// on the external server.
    /// </remarks>
    public static class ExternalServerPubSubConfiguration
    {
        /// <summary>
        /// Name shared by the PublishedDataSet and the DataSetWriter / reader
        /// metadata. The adapter matches DataSetWriters to PublishedDataSets and
        /// registers sinks per DataSetReader by this name.
        /// </summary>
        public const string DataSetName = "ExternalServerDataSet";

        /// <summary>
        /// DataSetReader name. The subscriber adapter registers one external
        /// write sink per reader using this name.
        /// </summary>
        public const string ReaderName = "ExternalServerReader";

        /// <summary>
        /// Default UDP/UADP multicast transport endpoint for the PubSub wire.
        /// </summary>
        public const string DefaultPubSubEndpoint = "opc.udp://239.0.0.1:4840";

        private const ushort PublisherId = 1;
        private const ushort WriterGroupId = 100;
        private const ushort DataSetWriterId = 1;
        private const int PublishingIntervalMs = 1000;

        /// <summary>
        /// Builds the external bridge configuration for the selected publisher,
        /// subscriber and responder directions.
        /// </summary>
        /// <param name="modes">
        /// The external bridge directions to include.
        /// </param>
        /// <param name="pubSubEndpoint">
        /// The UDP/UADP transport endpoint the bridge uses.
        /// </param>
        /// <returns>
        /// A configuration with one PubSubConnection containing the selected
        /// writer and reader groups.
        /// </returns>
        public static PubSubConfigurationDataType BuildConfiguration(BridgeMode modes, string pubSubEndpoint)
        {
            if (modes == BridgeMode.None)
            {
                modes = BridgeMode.Publisher;
            }

            bool includePublisher = modes.HasFlag(BridgeMode.Publisher);
            bool includeSubscriber = modes.HasFlag(BridgeMode.Subscriber) ||
                modes.HasFlag(BridgeMode.Responder);

            var builder = PubSubConfigurationBuilder.Create();
            if (includePublisher)
            {
                AddExternalServerDataSet(builder);
            }

            builder.AddConnection(ConnectionName(modes), connection =>
            {
                connection
                    .WithPublisherId(new Variant(PublisherId))
                    .WithTransportProfile(Profiles.PubSubUdpUadpTransport)
                    .WithAddress(pubSubEndpoint);

                if (includePublisher)
                {
                    AddWriterGroup(connection);
                }
                if (includeSubscriber)
                {
                    AddReaderGroup(connection);
                }
            });

            PubSubConfigurationDataType configuration = builder.Build();
            if (includePublisher)
            {
                AttachExternalReadSource(configuration);
            }
            if (includeSubscriber)
            {
                AttachExternalWriteTargets(configuration);
            }

            return configuration;
        }

        /// <summary>
        /// Builds the publisher configuration. The PublishedDataSet fields are
        /// sourced from the external server's <c>Server</c> status nodes so the
        /// sample produces meaningful data against any compliant server without
        /// prior address-space knowledge.
        /// </summary>
        /// <param name="pubSubEndpoint">
        /// The UDP/UADP transport endpoint the publisher emits on.
        /// </param>
        /// <returns>
        /// A configuration with one PublishedDataSet, one PubSubConnection, one
        /// WriterGroup and one DataSetWriter.
        /// </returns>
        public static PubSubConfigurationDataType BuildPublisherConfiguration(string pubSubEndpoint)
        {
            return BuildConfiguration(BridgeMode.Publisher, pubSubEndpoint);
        }

        /// <summary>
        /// Builds the subscriber configuration. Each received DataSet field is
        /// written back to the external server through the DataSetReader's
        /// TargetVariables, mapped positionally to the placeholder writable
        /// nodes below.
        /// </summary>
        /// <param name="pubSubEndpoint">
        /// The UDP/UADP transport endpoint the subscriber listens on.
        /// </param>
        /// <returns>
        /// A configuration with one PubSubConnection, one ReaderGroup and one
        /// DataSetReader whose SubscribedDataSet is a
        /// <see cref="TargetVariablesDataType"/>.
        /// </returns>
        public static PubSubConfigurationDataType BuildSubscriberConfiguration(string pubSubEndpoint)
        {
            return BuildConfiguration(BridgeMode.Subscriber, pubSubEndpoint);
        }

        private static void AddExternalServerDataSet(PubSubConfigurationBuilder builder)
        {
            builder.AddPublishedDataSet(DataSetName, dataSet => dataSet
                .AddField("CurrentTime", (byte)DataTypes.DateTime, DataTypeIds.DateTime)
                .AddField("State", (byte)DataTypes.Int32, DataTypeIds.Int32)
                .AddField("ServiceLevel", (byte)DataTypes.Byte, DataTypeIds.Byte));
        }

        private static void AddWriterGroup(PubSubConnectionBuilder connection)
        {
            connection.AddWriterGroup("WriterGroup 1", group =>
            {
                group
                    .WithWriterGroupId(WriterGroupId)
                    .WithPublishingInterval(PublishingIntervalMs)
                    .WithMessageSettings(WriterGroupMessageSettings())
                    .WithTransportSettings(new DatagramWriterGroupTransportDataType())
                    .AddDataSetWriter("Writer 1", writer => writer
                        .WithDataSetWriterId(DataSetWriterId)
                        .WithDataSetName(DataSetName)
                        .WithKeyFrameCount(1)
                        .WithFieldContentMask(DataSetFieldContentMask.RawData)
                        .WithMessageSettings(WriterMessageSettings()));
            });
        }

        private static void AddReaderGroup(PubSubConnectionBuilder connection)
        {
            connection.AddReaderGroup("ReaderGroup 1", group => group
                .WithMaxNetworkMessageSize(1500)
                .AddDataSetReader(ReaderName, reader => reader
                    .WithFilter(new Variant(PublisherId), WriterGroupId, DataSetWriterId)
                    .WithFieldContentMask(DataSetFieldContentMask.RawData)
                    .WithMessageReceiveTimeout(5000)
                    .WithMessageSettings(ReaderMessageSettings())
                    .WithDataSetMetaData(DataSetName, metaData => metaData
                        .WithoutFieldIds()
                        .AddField("CurrentTime", (byte)DataTypes.DateTime, DataTypeIds.DateTime)
                        .AddField("State", (byte)DataTypes.Int32, DataTypeIds.Int32)
                        .AddField("ServiceLevel", (byte)DataTypes.Byte, DataTypeIds.Byte))));
        }

        private static string ConnectionName(BridgeMode modes)
        {
            if (modes == BridgeMode.Publisher)
            {
                return "External Server Publisher Connection";
            }
            if (modes is BridgeMode.Subscriber or BridgeMode.Responder)
            {
                return "External Server Subscriber Connection";
            }

            return "External Server Bridge Connection";
        }

        private static void AttachExternalReadSource(PubSubConfigurationDataType configuration)
        {
            if (configuration.PublishedDataSets.IsNull || configuration.PublishedDataSets.Count == 0)
            {
                return;
            }

            var source = new PublishedDataItemsDataType
            {
                PublishedData =
                [
                    ReadFrom(VariableIds.Server_ServerStatus_CurrentTime),
                    ReadFrom(VariableIds.Server_ServerStatus_State),
                    ReadFrom(VariableIds.Server_ServiceLevel)
                ]
            };
            configuration.PublishedDataSets[0].DataSetSource = new ExtensionObject(source);
        }

        private static void AttachExternalWriteTargets(PubSubConfigurationDataType configuration)
        {
            DataSetReaderDataType? reader = FindFirstReader(configuration);
            if (reader is null)
            {
                return;
            }

            // Placeholder writable nodes on the external (target) server. Point
            // these at any writable variables of matching type - for example the
            // Scalar simulation nodes exposed by the repository's
            // ConsoleReferenceServer.
            var targets = new TargetVariablesDataType
            {
                TargetVariables =
                [
                    WriteTo("Demo.External.CurrentTime"),
                    WriteTo("Demo.External.State"),
                    WriteTo("Demo.External.ServiceLevel")
                ]
            };
            reader.SubscribedDataSet = new ExtensionObject(targets);
        }

        private static PublishedVariableDataType ReadFrom(NodeId variableId)
        {
            return new PublishedVariableDataType
            {
                PublishedVariable = variableId,
                AttributeId = Attributes.Value
            };
        }

        private static FieldTargetDataType WriteTo(string nodeIdentifier)
        {
            return new FieldTargetDataType
            {
                TargetNodeId = NodeId.Parse($"ns=2;s={nodeIdentifier}"),
                AttributeId = Attributes.Value,
                OverrideValueHandling = OverrideValueHandling.LastUsableValue
            };
        }

        private static DataSetReaderDataType? FindFirstReader(PubSubConfigurationDataType configuration)
        {
            if (configuration.Connections.IsNull)
            {
                return null;
            }
            foreach (PubSubConnectionDataType connection in configuration.Connections)
            {
                if (connection?.ReaderGroups is null || connection.ReaderGroups.IsNull)
                {
                    continue;
                }
                foreach (ReaderGroupDataType group in connection.ReaderGroups)
                {
                    if (group is null || group.DataSetReaders.IsNull || group.DataSetReaders.Count == 0)
                    {
                        continue;
                    }
                    return group.DataSetReaders[0];
                }
            }
            return null;
        }

        private static UadpWriterGroupMessageDataType WriterGroupMessageSettings()
        {
            return new UadpWriterGroupMessageDataType
            {
                DataSetOrdering = DataSetOrderingType.AscendingWriterId,
                NetworkMessageContentMask = (uint)(
                    UadpNetworkMessageContentMask.PublisherId |
                    UadpNetworkMessageContentMask.GroupHeader |
                    UadpNetworkMessageContentMask.WriterGroupId |
                    UadpNetworkMessageContentMask.PayloadHeader |
                    UadpNetworkMessageContentMask.NetworkMessageNumber |
                    UadpNetworkMessageContentMask.SequenceNumber)
            };
        }

        private static UadpDataSetWriterMessageDataType WriterMessageSettings()
        {
            return new UadpDataSetWriterMessageDataType
            {
                DataSetMessageContentMask = (uint)(
                    UadpDataSetMessageContentMask.Status |
                    UadpDataSetMessageContentMask.SequenceNumber)
            };
        }

        private static UadpDataSetReaderMessageDataType ReaderMessageSettings()
        {
            return new UadpDataSetReaderMessageDataType
            {
                NetworkMessageContentMask = (uint)(
                    UadpNetworkMessageContentMask.PublisherId |
                    UadpNetworkMessageContentMask.GroupHeader |
                    UadpNetworkMessageContentMask.WriterGroupId |
                    UadpNetworkMessageContentMask.PayloadHeader |
                    UadpNetworkMessageContentMask.NetworkMessageNumber |
                    UadpNetworkMessageContentMask.SequenceNumber),
                DataSetMessageContentMask = (uint)(
                    UadpDataSetMessageContentMask.Status |
                    UadpDataSetMessageContentMask.SequenceNumber)
            };
        }
    }
}
