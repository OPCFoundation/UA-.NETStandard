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

namespace UaLens.Plugins.PubSub;

/// <summary>
/// Projects safe document intent into the existing Part 14 configuration builder.
/// This runtime graph is never serialized into a workspace.
/// </summary>
internal static class PubSubStackConfiguration
{
    public static PubSubConfigurationDataType Build(PubSubConfiguration configuration)
    {
        PubSubConfigurationValidation.RequireValid(configuration);
        var builder = PubSubConfigurationBuilder.Create();
        if (configuration.Publication != PubSubPublication.Disabled)
        {
            builder.AddPublishedDataSet(DataSetName, dataSet => ConfigureFields(dataSet, configuration));
        }
        builder.AddConnection(ConnectionName, connection =>
        {
            connection.WithPublisherId(PublisherVariant(configuration, configuration.LocalPublisherId))
                .WithTransportProfile(configuration.TransportProfileUri)
                .WithAddress(configuration.Endpoint, configuration.NetworkInterface);
            if (configuration.Publication != PubSubPublication.Disabled)
            {
                connection.AddWriterGroup("UaLens WriterGroup", group =>
                {
                    group.WithWriterGroupId(configuration.WriterGroupId)
                        .WithPublishingInterval(configuration.PublishingIntervalMs)
                        .WithMaxNetworkMessageSize((uint)configuration.MaxNetworkMessageBytes)
                        .WithSecurity(
                            configuration.SecurityMode, configuration.SecurityGroupId, SecurityEndpoints(configuration))
                        .WithMessageSettings(configuration.IsJson
                            ? new JsonWriterGroupMessageDataType { NetworkMessageContentMask = (uint)JsonNetworkMask }
                            : new UadpWriterGroupMessageDataType
                            {
                                DataSetOrdering = DataSetOrderingType.AscendingWriterId,
                                NetworkMessageContentMask = (uint)UadpNetworkMask
                            })
                        .WithTransportSettings(configuration.IsBroker
                            ? new BrokerWriterGroupTransportDataType { QueueName = configuration.Topic }
                            : new DatagramWriterGroupTransportDataType())
                        .AddDataSetWriter("UaLens Writer", writer =>
                        {
                            writer.WithDataSetWriterId(configuration.DataSetWriterId)
                                .WithDataSetName(DataSetName)
                                .WithKeyFrameCount(1)
                                .WithFieldContentMask(FieldMask(configuration))
                                .WithMessageSettings(configuration.IsJson
                                    ? new JsonDataSetWriterMessageDataType
                                    {
                                        DataSetMessageContentMask = (uint)JsonDataSetMask
                                    }
                                    : new UadpDataSetWriterMessageDataType
                                    {
                                        DataSetMessageContentMask = (uint)UadpDataSetMask
                                    });
                            if (configuration.IsBroker)
                            {
                                writer.WithTransportSettings(new BrokerDataSetWriterTransportDataType
                                {
                                    QueueName = configuration.Topic,
                                    MetaDataQueueName = MetadataTopic(configuration),
                                    RequestedDeliveryGuarantee = BrokerTransportQualityOfService.BestEffort
                                });
                            }
                        });
                });
            }
            if (configuration.ReceiveEnabled)
            {
                connection.AddReaderGroup("UaLens ReaderGroup", group => group
                    .WithMaxNetworkMessageSize((uint)configuration.MaxNetworkMessageBytes)
                    .WithSecurity(
                        configuration.SecurityMode, configuration.SecurityGroupId, SecurityEndpoints(configuration))
                    .AddDataSetReader(ReaderName, reader =>
                    {
                        reader.WithFilter(
                                PublisherVariant(configuration, configuration.PublisherFilter),
                                configuration.WriterGroupId,
                                configuration.DataSetWriterId)
                            .WithFieldContentMask(FieldMask(configuration))
                            .WithMessageReceiveTimeout(5000)
                            .WithMirrorSubscribedDataSet(ReaderName)
                            .WithDataSetMetaData(DataSetName, dataSet => ConfigureFields(dataSet, configuration))
                            .WithMessageSettings(configuration.IsJson
                                ? new JsonDataSetReaderMessageDataType
                                {
                                    NetworkMessageContentMask = (uint)JsonNetworkMask,
                                    DataSetMessageContentMask = (uint)JsonDataSetMask
                                }
                                : new UadpDataSetReaderMessageDataType
                                {
                                    NetworkMessageContentMask = (uint)UadpNetworkMask,
                                    DataSetMessageContentMask = (uint)UadpDataSetMask
                                });
                        if (configuration.IsBroker)
                        {
                            reader.WithTransportSettings(new BrokerDataSetReaderTransportDataType
                            {
                                QueueName = configuration.Topic,
                                MetaDataQueueName = MetadataTopic(configuration),
                                RequestedDeliveryGuarantee = BrokerTransportQualityOfService.BestEffort
                            });
                        }
                    }));
            }
        });
        PubSubConfigurationDataType result = builder.Build();
        foreach (PublishedDataSetDataType dataSet in result.PublishedDataSets)
        {
            BoundMetadata(dataSet.DataSetMetaData);
        }
        foreach (ReaderGroupDataType group in result.Connections[0].ReaderGroups)
        {
            foreach (DataSetReaderDataType reader in group.DataSetReaders)
            {
                reader.SecurityMode = group.SecurityMode;
                reader.SecurityGroupId = group.SecurityGroupId;
                reader.SecurityKeyServices = group.SecurityKeyServices;
                BoundMetadata(reader.DataSetMetaData);
            }
        }
        return result;
    }

    public static DataSetMetaDataType CreateMetadata(PubSubConfiguration configuration)
    {
        DataSetMetaDataType metadata = PubSubConfigurationBuilder.Create()
            .AddPublishedDataSet(DataSetName, dataSet => ConfigureFields(dataSet, configuration))
            .Build().PublishedDataSets[0].DataSetMetaData;
        BoundMetadata(metadata);
        return metadata;
    }

    /// <summary>
    /// JSON numbers do not carry an integer width. Match the stack JSON decoder's
    /// smallest numeric representation; UADP keeps the explicitly configured UInt16 width.
    /// </summary>
    public static Variant PublisherVariant(PubSubConfiguration configuration, ushort value)
    {
        return configuration.IsJson && value <= byte.MaxValue ? new Variant((byte)value) : new Variant(value);
    }

    private static void ConfigureFields(PublishedDataSetBuilder builder, PubSubConfiguration configuration)
    {
        builder.WithoutFieldIds().WithConfigurationVersion(
            configuration.MetadataMajorVersion, configuration.MetadataMinorVersion);
        foreach (PubSubFieldConfiguration field in configuration.Fields)
        {
            builder.AddField(field.Name, (byte)field.Type, new NodeId((uint)field.Type));
        }
    }

    private static DataSetFieldContentMask FieldMask(PubSubConfiguration configuration)
    {
        return configuration.RawDataEncoding
            ? DataSetFieldContentMask.RawData
            : DataSetFieldContentMask.StatusCode | DataSetFieldContentMask.SourceTimestamp;
    }

    private static string MetadataTopic(PubSubConfiguration configuration)
    {
        return configuration.Topic + (configuration.Profile is PubSubProfile.KafkaJson or PubSubProfile.KafkaUadp
            ? ".metadata" : "/metadata");
    }

    private static string[] SecurityEndpoints(PubSubConfiguration configuration)
    {
        return configuration.SecurityMode == MessageSecurityMode.None ? [] : [configuration.SecurityKeyServiceEndpoint];
    }

    private static void BoundMetadata(DataSetMetaDataType metadata)
    {
        foreach (FieldMetaData field in metadata.Fields)
        {
            if (field.BuiltInType == (byte)BuiltInType.String || field.BuiltInType == (byte)BuiltInType.ByteString)
            {
                field.MaxStringLength = PubSubConfigurationValidation.MaxValueCharacters;
            }
        }
    }

    public const string ConnectionName = "UaLens PubSub";
    public const string DataSetName = "UaLens Data";
    public const string ReaderName = "UaLens Reader";

    private const UadpNetworkMessageContentMask UadpNetworkMask =
        UadpNetworkMessageContentMask.PublisherId | UadpNetworkMessageContentMask.GroupHeader |
        UadpNetworkMessageContentMask.WriterGroupId | UadpNetworkMessageContentMask.PayloadHeader |
        UadpNetworkMessageContentMask.NetworkMessageNumber | UadpNetworkMessageContentMask.SequenceNumber;

    private const UadpDataSetMessageContentMask UadpDataSetMask =
        UadpDataSetMessageContentMask.Status | UadpDataSetMessageContentMask.SequenceNumber |
        UadpDataSetMessageContentMask.Timestamp | UadpDataSetMessageContentMask.MajorVersion |
        UadpDataSetMessageContentMask.MinorVersion;

    private const JsonNetworkMessageContentMask JsonNetworkMask =
        JsonNetworkMessageContentMask.NetworkMessageHeader | JsonNetworkMessageContentMask.DataSetMessageHeader |
        JsonNetworkMessageContentMask.PublisherId;

    private const JsonDataSetMessageContentMask JsonDataSetMask =
        JsonDataSetMessageContentMask.DataSetWriterId | JsonDataSetMessageContentMask.SequenceNumber |
        JsonDataSetMessageContentMask.Status | JsonDataSetMessageContentMask.Timestamp |
        JsonDataSetMessageContentMask.MetaDataVersion;
}
