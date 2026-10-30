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
using Opc.Ua.DataChannels;

namespace Opc.Ua.PubSub.Transports.DataChannels
{
    /// <summary>
    /// Describes the carrier, resource and PubSub direction required by one connection.
    /// </summary>
    [global::System.Diagnostics.CodeAnalysis.Experimental(DataChannelFeature.ExperimentalDiagnosticId,
        UrlFormat = DataChannelFeature.ExperimentalUrlFormat)]
    public sealed class DataChannelPubSubFlowRequest
    {
        /// <summary>
        /// Takes a validated snapshot of a connection's flow configuration.
        /// </summary>
        public DataChannelPubSubFlowRequest(PubSubConnectionDataType connection)
            : this(connection, GetConfiguredParameters(connection))
        {
        }

        /// <summary>
        /// Takes a validated snapshot of a connection and its locally resolved flow policy.
        /// </summary>
        internal DataChannelPubSubFlowRequest(
            PubSubConnectionDataType connection,
            DataChannelParametersDataType parameters)
        {
            if (connection is null)
            {
                throw new ArgumentNullException(nameof(connection));
            }
            if (parameters is null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }
            if (connection.TransportProfileUri == DataChannelPubSubConfiguration.UafxTransportProfile)
            {
                DataChannelPubSubConfiguration.ValidateUafxBinding(connection, parameters);
                CarrierKind = DataChannelCarrierKind.SharedUasc;
            }
            else if (DataChannelPubSubConfiguration.TryGetCarrierKind(
                connection.TransportProfileUri, out DataChannelCarrierKind carrierKind))
            {
                DataChannelPubSubConfiguration.ValidateParameters(parameters, carrierKind);
                CarrierKind = carrierKind;
            }
            else
            {
                throw new ArgumentException(
                    "The connection does not select a DataChannel carrier.", nameof(connection));
            }
            m_parameters = DataChannelPubSubConfiguration.CopyParameters(parameters);
            Connection = connection;
            PubSubTransportDirection direction = PubSubTransportDirection.None;
            if (connection.WriterGroups.Count > 0)
            {
                direction |= PubSubTransportDirection.Send;
            }
            if (connection.ReaderGroups.Count > 0)
            {
                direction |= PubSubTransportDirection.Receive;
            }
            Direction = direction;
            var inboundPublisherIds = new List<Variant>();
            var inboundAssociations = new List<DataChannelPubSubRoute>();
            foreach (ReaderGroupDataType group in connection.ReaderGroups)
            {
                foreach (DataSetReaderDataType reader in group.DataSetReaders)
                {
                    Variant publisherId = reader.PublisherId;
                    if (!inboundPublisherIds.Exists(existing => existing.Equals(publisherId)))
                    {
                        inboundPublisherIds.Add(publisherId);
                    }
                    ushort? writerGroupId = reader.WriterGroupId == 0 ? null : reader.WriterGroupId;
                    AddInboundAssociation(
                        inboundAssociations,
                        publisherId,
                        writerGroupId,
                        reader.DataSetWriterId,
                        DataChannelPubSubRoute.DataSetNetworkMessageType,
                        null);
                    AddInboundAssociation(
                        inboundAssociations,
                        publisherId,
                        writerGroupId,
                        reader.DataSetWriterId,
                        DataChannelPubSubRoute.DiscoveryResponseNetworkMessageType,
                        null);
                    foreach (byte discoveryType in DataChannelPubSubRoute.DiscoveryRequestTypes)
                    {
                        AddInboundAssociation(
                            inboundAssociations,
                            publisherId,
                            writerGroupId,
                            reader.DataSetWriterId,
                            DataChannelPubSubRoute.DiscoveryRequestNetworkMessageType,
                            discoveryType);
                    }
                }
            }
            InboundPublisherIds = new ArrayOf<Variant>(inboundPublisherIds.ToArray());
            InboundAssociations = new ArrayOf<DataChannelPubSubRoute>(inboundAssociations.ToArray());
        }

        /// <summary>
        /// Gets the connection, including its optional endpoint address, for a carrier connector.
        /// Providers must not mutate this configuration.
        /// </summary>
        public PubSubConnectionDataType Connection { get; }

        /// <summary>
        /// Gets the explicitly selected carrier.
        /// </summary>
        public DataChannelCarrierKind CarrierKind { get; }

        /// <summary>
        /// Gets a defensive snapshot of the requested descriptor.
        /// </summary>
        public DataChannelParametersDataType Parameters => DataChannelPubSubConfiguration.CopyParameters(m_parameters);

        /// <summary>
        /// Gets the local publishing and subscribing roles, independently of the opener or transport initiator.
        /// </summary>
        public PubSubTransportDirection Direction { get; }

        /// <summary>
        /// Gets the distinct configured PublisherIds used to route inbound UADP FLO messages.
        /// A null element represents a configured wildcard reader.
        /// </summary>
        public ArrayOf<Variant> InboundPublisherIds { get; }

        /// <summary>
        /// Gets the distinct configured inbound UADP source, writer and NetworkMessageType associations.
        /// Discovery requests also include a subtype selector.
        /// </summary>
        internal ArrayOf<DataChannelPubSubRoute> InboundAssociations { get; }

        /// <summary>
        /// Adds one source and message-class route unless an identical reader association is already present.
        /// </summary>
        private static void AddInboundAssociation(
            List<DataChannelPubSubRoute> associations,
            Variant publisherId,
            ushort? writerGroupId,
            ushort dataSetWriterId,
            byte networkMessageType,
            byte? discoveryType)
        {
            var association = new DataChannelPubSubRoute(
                publisherId,
                writerGroupId,
                dataSetWriterId,
                networkMessageType,
                discoveryType);
            if (!associations.Contains(association))
            {
                associations.Add(association);
            }
        }

        private static DataChannelParametersDataType GetConfiguredParameters(
            PubSubConnectionDataType connection)
        {
            if (connection is null)
            {
                throw new ArgumentNullException(nameof(connection));
            }
            return DataChannelPubSubConfiguration.GetParameters(connection);
        }

        private readonly DataChannelParametersDataType m_parameters;
    }

    /// <summary>
    /// Identifies one configured PubSub data reader for UASC flow routing.
    /// </summary>
    /// <param name="PublisherId">The expected PublisherId, or null for a wildcard reader.</param>
    /// <param name="WriterGroupId">The expected WriterGroupId, or null when any group is accepted.</param>
    /// <param name="DataSetWriterId">The expected DataSetWriterId.</param>
    /// <param name="NetworkMessageType">The expected NetworkMessageType.</param>
    /// <param name="DiscoveryType">The expected discovery subtype, or null when any subtype is accepted.</param>
    internal readonly record struct DataChannelPubSubRoute(
        Variant PublisherId,
        ushort? WriterGroupId,
        ushort DataSetWriterId,
        byte NetworkMessageType,
        byte? DiscoveryType)
    {
        internal static ReadOnlySpan<byte> DiscoveryRequestTypes => [1, 2, 3, 4, 5, 6];

        internal const byte DataSetNetworkMessageType = 0;
        internal const byte DiscoveryRequestNetworkMessageType = 1;
        internal const byte DiscoveryResponseNetworkMessageType = 2;
    }
}
