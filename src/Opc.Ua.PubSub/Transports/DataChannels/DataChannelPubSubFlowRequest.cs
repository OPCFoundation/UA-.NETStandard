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
using Opc.Ua.DataChannels;

namespace Opc.Ua.PubSub.Transports.DataChannels
{
    /// <summary>
    /// Describes the carrier, resource and PubSub direction required by one connection.
    /// </summary>
    public sealed class DataChannelPubSubFlowRequest
    {
        /// <summary>
        /// Takes a validated snapshot of a connection's flow configuration.
        /// </summary>
        public DataChannelPubSubFlowRequest(PubSubConnectionDataType connection)
        {
            if (connection is null)
            {
                throw new ArgumentNullException(nameof(connection));
            }
            m_parameters = DataChannelPubSubConfiguration.GetParameters(connection);
            _ = DataChannelPubSubConfiguration.TryGetCarrierKind(
                connection.TransportProfileUri, out DataChannelCarrierKind carrierKind);
            CarrierKind = carrierKind;
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

        private readonly DataChannelParametersDataType m_parameters;
    }
}
