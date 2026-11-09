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
            ArgumentNullException.ThrowIfNull(configuration);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(network);
            foreach (PubSubConnectionDataType connection in configuration.Connections)
            {
                NetworkAddressUrlDataType address = Connection(connection, options, network);
                bool broker = connection.TransportProfileUri is Profiles.PubSubMqttJsonTransport or
                    Profiles.PubSubMqttUadpTransport or KafkaProfiles.PubSubKafkaJsonTransport or
                    KafkaProfiles.PubSubKafkaUadpTransport;
                foreach (WriterGroupDataType group in connection.WriterGroups)
                {
                    if (broker)
                    {
                        if (!group.TransportSettings.TryGetValue(out BrokerWriterGroupTransportDataType? transport) ||
                            transport is null)
                        {
                            throw GeneratedTopicRefused();
                        }
                        Broker(network, address.Url, transport.QueueName, "configure-publish");
                        foreach (DataSetWriterDataType writer in group.DataSetWriters)
                        {
                            if (!writer.TransportSettings.TryGetValue(
                                out BrokerDataSetWriterTransportDataType? settings) ||
                                settings is null)
                            {
                                throw GeneratedTopicRefused();
                            }
                            Broker(network, address.Url,
                                string.IsNullOrEmpty(settings.QueueName) ? transport.QueueName : settings.QueueName,
                                "configure-publish");
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
        /// Requires endpoint activation independently of groups, including disabled and group-free connections.
        /// The empty-resource configure-connect grant explicitly covers remote transport authentication defaults
        /// and implicit discovery/status traffic; it never grants configured data or metadata topics.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static NetworkAddressUrlDataType Connection(
            PubSubConnectionDataType connection, McpPubSubOptions options, McpNetworkPolicy network)
        {
            if (!connection.Address.TryGetValue(out NetworkAddressUrlDataType? address) || address is null)
            {
                throw McpPubSubTransportProvider.Refused();
            }
            bool mqtt = connection.TransportProfileUri is Profiles.PubSubMqttJsonTransport or
                Profiles.PubSubMqttUadpTransport;
            bool kafka = connection.TransportProfileUri is KafkaProfiles.PubSubKafkaJsonTransport or
                KafkaProfiles.PubSubKafkaUadpTransport;
            if (mqtt || kafka)
            {
                if (!Uri.TryCreate(address.Url, UriKind.Absolute, out Uri? endpoint) ||
                    (mqtt && endpoint.Scheme is not ("mqtt" or "mqtts" or "ws" or "wss")) ||
                    (kafka && endpoint.Scheme is not ("kafka" or "kafkas")))
                {
                    throw McpPubSubTransportProvider.Refused();
                }
                try
                {
                    _ = network.Authorize(
                        endpoint, "configure-connect", string.Empty, McpCapability.PubSubConfiguration);
                }
                catch (ServiceResultException exception) when (exception.StatusCode == StatusCodes.BadUserAccessDenied)
                {
                    throw ConnectionRefused();
                }
            }
            else
            {
                Datagram(options, connection.TransportProfileUri, address, "configure-connect");
            }
            return address;
        }

        /// <summary>
        /// Requires an exact broker configuration grant; this does not open a local TCP connection.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static void Broker(McpNetworkPolicy network, string? endpoint, string? topic, string operation)
        {
            if (string.IsNullOrWhiteSpace(topic))
            {
                throw GeneratedTopicRefused();
            }
            PubSubInputValidation.Topic(topic);
            _ = network.Authorize(new Uri(PubSubInputValidation.Text(endpoint), UriKind.Absolute),
                operation, topic, McpCapability.PubSubConfiguration);
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
                throw operation == "configure-connect" ? ConnectionRefused() : McpPubSubTransportProvider.Refused();
            }
        }

        /// <summary>
        /// Explains the independent activation authority instead of silently excluding group-free configurations.
        /// </summary>
        private static ServiceResultException ConnectionRefused()
        {
            return new ServiceResultException(StatusCodes.BadUserAccessDenied,
                "Remote PubSub connections require an explicit configure-connect endpoint grant covering " +
                "transport authentication defaults and implicit discovery/status traffic, " +
                "in addition to topic grants.");
        }

        /// <summary>
        /// Refuses generated topics whose remote prefix/defaults cannot be established by the configuration tool.
        /// </summary>
        private static ServiceResultException GeneratedTopicRefused()
        {
            return new ServiceResultException(StatusCodes.BadUserAccessDenied,
                "Remote broker configuration requires explicit data, writer-group and metadata queues. " +
                "Generated broker destinations cannot be authorized from this configuration.");
        }
    }
}
