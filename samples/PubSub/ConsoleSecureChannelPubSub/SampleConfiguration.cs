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
using Opc.Ua.DataChannels;
using Opc.Ua.PubSub.Configuration;

namespace Quickstarts.ConsoleSecureChannelPubSub
{
    /// <summary>
    /// Ordinary Part 14 datasets, writers and readers; the carrier changes no PubSub identifiers.
    /// </summary>
    internal static class SampleConfiguration
    {
        /// <summary>
        /// Builds the UADP descriptor with opener-relative direction.
        /// </summary>
        internal static DataChannelParametersDataType Parameters(SampleOptions options)
        {
            return new DataChannelParametersDataType
            {
                ResourceUri = Resource,
                Protocol = DataChannelProfiles.UadpPayload,
                ContentType = "application/opcua+uadp",
                Direction = options.Reverse
                    ? DataChannelDirection.OpenerToAcceptor
                    : DataChannelDirection.AcceptorToOpener,
                Delivery = DataChannelDeliveryMode.Ordered,
                Protection = DataChannelProtectionMode.Confidential,
                MaxMessageSize = 32768,
                MaxReceiveBytes = 262144
            };
        }

        /// <summary>
        /// Builds two actual writers and independent positive and negative reader filters.
        /// </summary>
        internal static PubSubConfigurationDataType Build(SampleOptions options, bool publisher)
        {
            var builder = PubSubConfigurationBuilder.Create();
            if (publisher)
            {
                builder.AddPublishedDataSet(TelemetryDataSet, Fields)
                    .AddPublishedDataSet(OtherDataSet, Fields);
            }
            builder.AddConnection("Secure channel", connection =>
            {
                connection.WithPublisherId(new Variant(PublisherId))
                    .WithDataChannel(options.Carrier, Parameters(options));
                if (publisher)
                {
                    connection.AddWriterGroup("Writers", group =>
                    {
                        group.WithWriterGroupId(WriterGroupId)
                            .WithPublishingInterval(50)
                            .WithMessageSettings(GroupSettings());
                        AddWriter(group, TelemetryDataSet, TelemetryWriterId);
                        AddWriter(group, OtherDataSet, OtherWriterId);
                    });
                }
                else
                {
                    connection.AddReaderGroup("Readers", group =>
                    {
                        group.WithMaxNetworkMessageSize(65536);
                        AddReader(group, TelemetryReader, TelemetryDataSet, PublisherId,
                            WriterGroupId, TelemetryWriterId);
                        AddReader(group, OtherReader, OtherDataSet, PublisherId,
                            WriterGroupId, OtherWriterId);
                        AddReader(group, WrongWriterReader, TelemetryDataSet, PublisherId,
                            WriterGroupId, 999);
                        AddReader(group, WrongPublisherReader, TelemetryDataSet, 999,
                            WriterGroupId, TelemetryWriterId);
                    });
                }
            });
            return builder.Build();
        }

        /// <summary>
        /// Returns the same metadata declaration used by the configured writers and readers.
        /// </summary>
        internal static DataSetMetaDataType MetaData(string name)
        {
            return PubSubConfigurationBuilder.Create().AddPublishedDataSet(name, Fields)
                .Build().PublishedDataSets[0].DataSetMetaData;
        }

        /// <summary>
        /// Specifies stable field names, types and metadata version.
        /// </summary>
        private static void Fields(PublishedDataSetBuilder dataSet)
        {
            dataSet.WithoutFieldIds()
                .WithConfigurationVersion(1, 0)
                .AddField("Counter", (byte)DataTypes.Int32, DataTypeIds.Int32)
                .AddField("Toggle", (byte)DataTypes.Boolean, DataTypeIds.Boolean)
                .AddField("Temperature", (byte)DataTypes.Double, DataTypeIds.Double);
        }

        /// <summary>
        /// Adds a writer with ordinary Part 14 message settings and no broker transport settings.
        /// </summary>
        private static void AddWriter(
            WriterGroupBuilder group,
            string dataSet,
            ushort writerId)
        {
            group.AddDataSetWriter(dataSet, writer => writer
                .WithDataSetName(dataSet)
                .WithDataSetWriterId(writerId)
                .WithKeyFrameCount(1)
                .WithFieldContentMask(DataSetFieldContentMask.RawData)
                .WithMessageSettings(WriterSettings()));
        }

        /// <summary>
        /// Adds a metadata-aware reader; unmatched identities must never reach its sink.
        /// </summary>
        private static void AddReader(
            ReaderGroupBuilder group,
            string readerName,
            string dataSet,
            ushort publisherId,
            ushort groupId,
            ushort writerId)
        {
            group.AddDataSetReader(readerName, reader => reader
                .WithFilter(new Variant(publisherId), groupId, writerId)
                .WithMessageReceiveTimeout(15000)
                .WithFieldContentMask(DataSetFieldContentMask.RawData)
                .WithMirrorSubscribedDataSet(readerName)
                .WithDataSetMetaData(dataSet, Fields)
                .WithMessageSettings(ReaderSettings()));
        }

        /// <summary>
        /// Uses the encoder's existing NetworkMessage header model.
        /// </summary>
        private static UadpWriterGroupMessageDataType GroupSettings()
        {
            return new UadpWriterGroupMessageDataType
            {
                DataSetOrdering = DataSetOrderingType.AscendingWriterId,
                NetworkMessageContentMask = (uint)UadpNetworkMask
            };
        }

        /// <summary>
        /// Carries writer identity, sequence and metadata version in the existing codec.
        /// </summary>
        private static UadpDataSetWriterMessageDataType WriterSettings()
        {
            return new UadpDataSetWriterMessageDataType { DataSetMessageContentMask = (uint)UadpDataSetMask };
        }

        /// <summary>
        /// Selects the matching metadata-driven decoder settings.
        /// </summary>
        private static UadpDataSetReaderMessageDataType ReaderSettings()
        {
            return new UadpDataSetReaderMessageDataType
            {
                NetworkMessageContentMask = (uint)UadpNetworkMask,
                DataSetMessageContentMask = (uint)UadpDataSetMask
            };
        }

        /// <summary>The exact allow-listed application resource.</summary>
        internal const string Resource = "urn:opcfoundation:samples:secure-channel-pubsub:telemetry";

        /// <summary>The namespace of the optional source model.</summary>
        internal const string ModelNamespace = "urn:opcfoundation:samples:secure-channel-pubsub";

        /// <summary>The server application identity, never inferred from a HELLO claim.</summary>
        internal const string ServerApplicationUri = "urn:localhost:OPCFoundation:SecureChannelPubSubServer";

        /// <summary>The only admitted client application identity.</summary>
        internal const string ClientApplicationUri = "urn:localhost:OPCFoundation:SecureChannelPubSubClient";

        /// <summary>The primary dataset name.</summary>
        internal const string TelemetryDataSet = "Telemetry";

        /// <summary>The independently filtered second dataset name.</summary>
        internal const string OtherDataSet = "OtherTelemetry";

        /// <summary>The primary reader name.</summary>
        internal const string TelemetryReader = "TelemetryReader";

        /// <summary>The second reader name.</summary>
        internal const string OtherReader = "OtherReader";

        /// <summary>A reader whose writer filter must not match.</summary>
        internal const string WrongWriterReader = "WrongWriter";

        /// <summary>A reader whose PublisherId filter must not match.</summary>
        internal const string WrongPublisherReader = "WrongPublisher";

        /// <summary>Stable PublisherId, unrelated to any flow or SecureChannel identifier.</summary>
        internal const ushort PublisherId = 42;

        /// <summary>Stable WriterGroupId.</summary>
        internal const ushort WriterGroupId = 7;

        /// <summary>Stable first writer identifier.</summary>
        internal const ushort TelemetryWriterId = 101;

        /// <summary>Stable second writer identifier.</summary>
        internal const ushort OtherWriterId = 202;

        /// <summary>Complete headers used by the standard UADP encoder.</summary>
        private const UadpNetworkMessageContentMask UadpNetworkMask =
            UadpNetworkMessageContentMask.PublisherId |
            UadpNetworkMessageContentMask.GroupHeader |
            UadpNetworkMessageContentMask.WriterGroupId |
            UadpNetworkMessageContentMask.PayloadHeader |
            UadpNetworkMessageContentMask.SequenceNumber;

        /// <summary>Versioned UADP DataSetMessage headers.</summary>
        private const UadpDataSetMessageContentMask UadpDataSetMask =
            UadpDataSetMessageContentMask.SequenceNumber |
            UadpDataSetMessageContentMask.MajorVersion |
            UadpDataSetMessageContentMask.MinorVersion;

    }
}
