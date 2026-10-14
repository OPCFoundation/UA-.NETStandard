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
using System.Linq;
using Opc.Ua.PubSub.Eth;
using Opc.Ua.PubSub.Kafka;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Authorizes configuring exact remote transport targets, without claiming control of the remote server's sockets.
    /// The remote server remains responsible for its own reconnect and advertised-broker outbound policy.
    /// </summary>
    internal static class McpPubSubRemoteTargets
    {
        /// <summary>
        /// Checks every configured connection and explicit topic before dispatching a configuration mutation.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal static void Require(
            PubSubConfigurationDataType configuration, McpPubSubOptions options, McpNetworkPolicy network)
        {
            foreach (PubSubConnectionDataType connection in configuration.Connections)
            {
                if (!connection.Address.TryGetValue(out NetworkAddressUrlDataType? address) || address is null)
                {
                    throw McpPubSubTransportProvider.Refused();
                }
                bool broker = connection.TransportProfileUri is Profiles.PubSubMqttJsonTransport or
                    Profiles.PubSubMqttUadpTransport or KafkaProfiles.PubSubKafkaJsonTransport or
                    KafkaProfiles.PubSubKafkaUadpTransport;
                foreach (WriterGroupDataType group in connection.WriterGroups)
                {
                    if (broker)
                    {
                        foreach (DataSetWriterDataType writer in group.DataSetWriters)
                        {
                            if (!writer.TransportSettings.TryGetValue(
                                out BrokerDataSetWriterTransportDataType? settings) ||
                                settings is null)
                            {
                                throw McpPubSubTransportProvider.Refused();
                            }
                            Broker(network, address.Url, settings.QueueName, "configure-publish");
                            Broker(network, address.Url, settings.MetaDataQueueName, "configure-publish");
                        }
                    }
                    else
                    {
                        Datagram(options, connection.TransportProfileUri, address, "configure-publish");
                    }
                }
                foreach (ReaderGroupDataType group in connection.ReaderGroups)
                {
                    if (broker)
                    {
                        foreach (DataSetReaderDataType reader in group.DataSetReaders)
                        {
                            if (!reader.TransportSettings.TryGetValue(
                                out BrokerDataSetReaderTransportDataType? settings) ||
                                settings is null)
                            {
                                throw McpPubSubTransportProvider.Refused();
                            }
                            Broker(network, address.Url, settings.QueueName, "configure-subscribe");
                            Broker(network, address.Url, settings.MetaDataQueueName, "configure-subscribe");
                        }
                    }
                    else
                    {
                        Datagram(options, connection.TransportProfileUri, address, "configure-subscribe");
                    }
                }
            }
        }

        /// <summary>
        /// Requires an exact broker configuration grant; this does not open a local TCP connection.
        /// </summary>
        private static void Broker(McpNetworkPolicy network, string? endpoint, string? topic, string operation)
        {
            PubSubInputValidation.Topic(topic);
            _ = network.Authorize(new Uri(PubSubInputValidation.Text(endpoint), UriKind.Absolute),
                operation, topic!, McpCapability.PubSubConfiguration);
        }

        /// <summary>
        /// Requires real datagram or Layer-2 fields instead of substituting a TCP rule.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static void Datagram(
            McpPubSubOptions options, string? profile, NetworkAddressUrlDataType address, string operation)
        {
            bool permitted = profile switch
            {
                Profiles.PubSubUdpUadpTransport => options.DatagramRules.ToList().Any(rule =>
                    rule.Endpoint == address.Url &&
                    rule.NetworkInterface == address.NetworkInterface &&
                    rule.Operations.Contains(operation)),
                EthProfiles.PubSubEthUadpTransport => options.EthernetRules.ToList().Any(rule =>
                    rule.Endpoint == address.Url &&
                    rule.NetworkInterface == address.NetworkInterface &&
                    rule.Operations.Contains(operation)),
                _ => false
            };
            if (!permitted)
            {
                throw McpPubSubTransportProvider.Refused();
            }
        }
    }
}
