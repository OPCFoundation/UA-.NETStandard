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
    /// Configures a complete-message PubSub flow independently of its carrier and publisher identifiers.
    /// </summary>
    [global::System.Diagnostics.CodeAnalysis.Experimental(DataChannelFeature.ExperimentalDiagnosticId,
        UrlFormat = DataChannelFeature.ExperimentalUrlFormat)]
    public static class DataChannelPubSubConfiguration
    {
        /// <summary>
        /// Selects the carrier and stores a defensive copy of its flow descriptor in ConnectionProperties.
        /// The address, publisher, writer and reader configuration is preserved.
        /// Queue expiry is unsupported without a payload-aware Publisher dependency and recovery policy.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static PubSubConnectionDataType Configure(
            PubSubConnectionDataType connection,
            DataChannelCarrierKind carrierKind,
            DataChannelParametersDataType parameters)
        {
            if (connection is null)
            {
                throw new ArgumentNullException(nameof(connection));
            }
            ValidateParameters(parameters, carrierKind);
            string profile = GetTransportProfile(carrierKind);
            var properties = new List<KeyValuePair>();
            foreach (KeyValuePair property in connection.ConnectionProperties)
            {
                if (property.Key != new QualifiedName(ParametersProperty))
                {
                    properties.Add(property);
                }
            }
            properties.Add(new KeyValuePair
            {
                Key = new QualifiedName(ParametersProperty),
                Value = new Variant(new ExtensionObject(CopyParameters(parameters)))
            });
            connection.TransportProfileUri = profile;
            connection.ConnectionProperties = new ArrayOf<KeyValuePair>(properties.ToArray());
            return connection;
        }

        /// <summary>
        /// Configures the optional UAFX mapping over an existing shared UASC context.
        /// </summary>
        /// <remarks>
        /// This selects the standard PubSub-over-UASC profile and validates the
        /// binding-specific transport, payload, protection and address constraints.
        /// It does not provision peer authorization or install Part 81 configuration.
        /// </remarks>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="NotSupportedException"></exception>
        public static PubSubConnectionDataType ConfigureUafx(
            PubSubConnectionDataType connection,
            DataChannelParametersDataType parameters)
        {
            if (connection is null)
            {
                throw new ArgumentNullException(nameof(connection));
            }
            ValidateUafxBinding(connection, parameters);
            var properties = new List<KeyValuePair>();
            foreach (KeyValuePair property in connection.ConnectionProperties)
            {
                if (property.Key != new QualifiedName(ParametersProperty))
                {
                    properties.Add(property);
                }
            }
            connection.TransportProfileUri = UafxTransportProfile;
            connection.ConnectionProperties = new ArrayOf<KeyValuePair>(properties.ToArray());
            return connection;
        }

        /// <summary>
        /// Returns the configured descriptor after checking the payload mapping and its finite bounds.
        /// Queue expiry must be disabled because this transport has no payload-aware Publisher policy.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        public static DataChannelParametersDataType GetParameters(PubSubConnectionDataType connection)
        {
            if (connection is null)
            {
                throw new ArgumentNullException(nameof(connection));
            }
            if (!TryGetCarrierKind(connection.TransportProfileUri, out DataChannelCarrierKind carrierKind))
            {
                throw new ArgumentException(
                    "The connection does not select a DataChannel carrier.", nameof(connection));
            }
            DataChannelParametersDataType? result = null;
            foreach (KeyValuePair property in connection.ConnectionProperties)
            {
                if (property.Key != new QualifiedName(ParametersProperty))
                {
                    continue;
                }
                if (result is not null ||
                    !property.Value.TryGetValue(out ExtensionObject extension) ||
                    !extension.TryGetValue(out DataChannelParametersDataType? parameters) ||
                    parameters is null)
                {
                    throw new ArgumentException(
                        "Exactly one typed DataChannelParameters connection property is required.",
                        nameof(connection));
                }
                result = parameters;
            }
            if (result is null)
            {
                throw new ArgumentException(
                    "The DataChannelParameters connection property is missing.", nameof(connection));
            }
            ValidateParameters(result, carrierKind);
            ValidateMessageSettings(connection);
            return CopyParameters(result);
        }

        /// <summary>
        /// Maps an explicitly selected carrier to its exact profile URI.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public static string GetTransportProfile(DataChannelCarrierKind carrierKind)
        {
            return carrierKind switch
            {
                DataChannelCarrierKind.DedicatedUasc => DataChannelProfiles.DedicatedUasc,
                DataChannelCarrierKind.SharedUasc => DataChannelProfiles.SharedUasc,
                DataChannelCarrierKind.Quic => DataChannelProfiles.Quic,
                DataChannelCarrierKind.WebTransport => DataChannelProfiles.WebTransport,
                DataChannelCarrierKind.QuicUasc => DataChannelProfiles.UascQuic,
                DataChannelCarrierKind.WebTransportUasc => DataChannelProfiles.UascWebTransport,
                _ => throw new ArgumentOutOfRangeException(nameof(carrierKind))
            };
        }

        /// <summary>
        /// Recognizes the exact flow and reliable-baseline profiles without inferring a codec from their names.
        /// </summary>
        public static bool TryGetCarrierKind(string? profile, out DataChannelCarrierKind carrierKind)
        {
            switch (profile)
            {
                case DataChannelProfiles.DedicatedUasc:
                    carrierKind = DataChannelCarrierKind.DedicatedUasc;
                    return true;
                case DataChannelProfiles.SharedUasc:
                    carrierKind = DataChannelCarrierKind.SharedUasc;
                    return true;
                case DataChannelProfiles.Quic:
                    carrierKind = DataChannelCarrierKind.Quic;
                    return true;
                case DataChannelProfiles.WebTransport:
                    carrierKind = DataChannelCarrierKind.WebTransport;
                    return true;
                case DataChannelProfiles.UascQuic:
                    carrierKind = DataChannelCarrierKind.QuicUasc;
                    return true;
                case DataChannelProfiles.UascWebTransport:
                    carrierKind = DataChannelCarrierKind.WebTransportUasc;
                    return true;
                default:
                    carrierKind = default;
                    return false;
            }
        }

        /// <summary>
        /// Maps the required UADP payload protocol to the existing codec registry.
        /// </summary>
        /// <exception cref="NotSupportedException"></exception>
        internal static string GetEncodingProfile(string? protocol)
        {
            return protocol switch
            {
                DataChannelProfiles.UadpPayload => Profiles.PubSubUdpUadpTransport,
                _ => throw new NotSupportedException(
                    "PubSub over Data Channels requires a UADP NetworkMessage payload.")
            };
        }

        /// <summary>
        /// Copies the generated descriptor without retaining caller-owned mutable state and canonicalizes zero rate.
        /// </summary>
        internal static DataChannelParametersDataType CopyParameters(DataChannelParametersDataType parameters)
        {
            return new DataChannelParametersDataType
            {
                ResourceUri = parameters.ResourceUri,
                Protocol = parameters.Protocol,
                ContentType = parameters.ContentType,
                Direction = parameters.Direction,
                Delivery = parameters.Delivery,
                Protection = parameters.Protection,
                MaxMessageSize = parameters.MaxMessageSize,
                MaxReceiveBytes = parameters.MaxReceiveBytes,
                MaxRate = parameters.MaxRate == 0 ? 0 : parameters.MaxRate,
                Priority = parameters.Priority,
                AllowQueueExpiry = parameters.AllowQueueExpiry
            };
        }

        /// <summary>
        /// Validates the PubSub portion of the descriptor before any flow or publishing schedule is opened.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="NotSupportedException"></exception>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        internal static void ValidateParameters(
            DataChannelParametersDataType parameters,
            DataChannelCarrierKind carrierKind)
        {
            if (parameters is null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }
            _ = GetTransportProfile(carrierKind);
            _ = GetEncodingProfile(parameters.Protocol);
            if (parameters.AllowQueueExpiry)
            {
                throw new NotSupportedException(
                    "Queue expiry requires a payload-aware Publisher policy that preserves or recovers " +
                    "delta-frame, key-frame and metadata dependencies; this transport has no such policy.");
            }
            if (string.IsNullOrEmpty(parameters.ResourceUri) ||
                System.Text.Encoding.UTF8.GetByteCount(parameters.ResourceUri) > 4096 ||
                !Uri.TryCreate(parameters.ResourceUri, UriKind.Absolute, out _))
            {
                throw new ArgumentException("A bounded absolute flow resource URI is required.", nameof(parameters));
            }
            if (string.IsNullOrEmpty(parameters.ContentType) ||
                System.Text.Encoding.UTF8.GetByteCount(parameters.ContentType) > 255)
            {
                throw new ArgumentException(
                    "A nonempty flow content type of at most 255 UTF-8 bytes is required.", nameof(parameters));
            }
            if (parameters.Direction is not (DataChannelDirection.OpenerToAcceptor or
                DataChannelDirection.AcceptorToOpener or DataChannelDirection.Duplex) ||
                parameters.Delivery is not (DataChannelDeliveryMode.Ordered or DataChannelDeliveryMode.Datagram) ||
                parameters.Protection is not (DataChannelProtectionMode.Confidential or
                    DataChannelProtectionMode.Integrity))
            {
                throw new ArgumentException(
                    "The flow descriptor contains an invalid discriminator.", nameof(parameters));
            }
            if (parameters.Delivery == DataChannelDeliveryMode.Datagram &&
                carrierKind is DataChannelCarrierKind.DedicatedUasc or DataChannelCarrierKind.SharedUasc or
                    DataChannelCarrierKind.QuicUasc or DataChannelCarrierKind.WebTransportUasc)
            {
                throw new NotSupportedException("UASC flow carriers do not support datagrams.");
            }
            uint overhead = carrierKind is DataChannelCarrierKind.DedicatedUasc or DataChannelCarrierKind.SharedUasc or
                DataChannelCarrierKind.QuicUasc or DataChannelCarrierKind.WebTransportUasc
                ? 0u
                : parameters.Delivery == DataChannelDeliveryMode.Datagram ? 17u : 13u;
            if (parameters.MaxMessageSize == 0 ||
                parameters.MaxMessageSize > int.MaxValue ||
                parameters.MaxReceiveBytes < (ulong)parameters.MaxMessageSize + overhead ||
                double.IsNaN(parameters.MaxRate) ||
                double.IsInfinity(parameters.MaxRate) ||
                parameters.MaxRate < 0 ||
                (parameters.Delivery == DataChannelDeliveryMode.Datagram && parameters.MaxRate == 0))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(parameters),
                    "Message and receive limits must match the carrier; datagrams require a finite rate.");
            }
        }

        /// <summary>
        /// Rejects JSON-specific message settings instead of silently ignoring them on the UADP mapping.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        private static void ValidateMessageSettings(PubSubConnectionDataType connection)
        {
            foreach (WriterGroupDataType group in connection.WriterGroups)
            {
                if (group.MessageSettings.TryGetValue(out JsonWriterGroupMessageDataType? _))
                {
                    throw new ArgumentException("WriterGroup message settings disagree with the flow payload.");
                }
                foreach (DataSetWriterDataType writer in group.DataSetWriters)
                {
                    if (writer.MessageSettings.TryGetValue(out JsonDataSetWriterMessageDataType? _))
                    {
                        throw new ArgumentException("DataSetWriter message settings disagree with the flow payload.");
                    }
                }
            }
            foreach (ReaderGroupDataType group in connection.ReaderGroups)
            {
                foreach (DataSetReaderDataType reader in group.DataSetReaders)
                {
                    if (reader.MessageSettings.TryGetValue(out JsonDataSetReaderMessageDataType? _))
                    {
                        throw new ArgumentException("DataSetReader message settings disagree with the flow payload.");
                    }
                }
            }
        }

        private static void ValidateUafxBinding(
            PubSubConnectionDataType connection,
            DataChannelParametersDataType parameters)
        {
            ValidateParameters(parameters, DataChannelCarrierKind.SharedUasc);
            ValidateMessageSettings(connection);
            ValidateUafxMessageSettings(connection);
            if (parameters.Protocol != DataChannelProfiles.UadpPayload ||
                parameters.ContentType != UafxContentType ||
                parameters.Delivery != DataChannelDeliveryMode.Ordered ||
                parameters.Protection != DataChannelProtectionMode.Confidential ||
                parameters.AllowQueueExpiry)
            {
                throw new NotSupportedException(
                    "The UAFX binding requires ordered confidential UADP with application/octet-stream content.");
            }
            ValidateUafxMessageBounds(connection, parameters.MaxMessageSize);
            if (!connection.TransportSettings.IsNull)
            {
                throw new NotSupportedException(
                    "The UAFX PubSub-over-UASC mapping requires null connection TransportSettings.");
            }
            if (!connection.Address.TryGetValue(out NetworkAddressUrlDataType? address) ||
                address is null ||
                !Uri.TryCreate(address.Url, UriKind.Absolute, out Uri? endpoint) ||
                endpoint.Scheme is not ("opc.tcp" or "opc.wss"))
            {
                throw new ArgumentException(
                    "The UAFX binding requires an opc.tcp or opc.wss NetworkAddressUrlDataType.",
                    nameof(connection));
            }
        }

        private static void ValidateUafxMessageBounds(
            PubSubConnectionDataType connection,
            uint maxMessageSize)
        {
            foreach (WriterGroupDataType group in connection.WriterGroups)
            {
                if (group.MaxNetworkMessageSize == 0 || group.MaxNetworkMessageSize > maxMessageSize)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(connection),
                        $"WriterGroup '{group.Name}' must have a finite MaxNetworkMessageSize " +
                        $"no greater than the local UAFX flow limit ({maxMessageSize} bytes).");
                }
            }
            foreach (ReaderGroupDataType group in connection.ReaderGroups)
            {
                if (group.MaxNetworkMessageSize == 0 || group.MaxNetworkMessageSize > maxMessageSize)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(connection),
                        $"ReaderGroup '{group.Name}' must have a finite MaxNetworkMessageSize " +
                        $"no greater than the local UAFX flow limit ({maxMessageSize} bytes).");
                }
            }
        }

        private static void ValidateUafxMessageSettings(PubSubConnectionDataType connection)
        {
            foreach (WriterGroupDataType group in connection.WriterGroups)
            {
                if (!group.MessageSettings.IsNull &&
                    !group.MessageSettings.TryGetValue(out UadpWriterGroupMessageDataType? _))
                {
                    throw new NotSupportedException(
                        $"WriterGroup '{group.Name}' must use UADP WriterGroup message settings.");
                }
                foreach (DataSetWriterDataType writer in group.DataSetWriters)
                {
                    if (!writer.MessageSettings.IsNull &&
                        !writer.MessageSettings.TryGetValue(out UadpDataSetWriterMessageDataType? _))
                    {
                        throw new NotSupportedException(
                            $"DataSetWriter '{writer.Name}' must use UADP DataSetWriter message settings.");
                    }
                }
            }
            foreach (ReaderGroupDataType group in connection.ReaderGroups)
            {
                if (!group.MessageSettings.IsNull &&
                    !group.MessageSettings.TryGetValue(out ReaderGroupMessageDataType? _))
                {
                    throw new NotSupportedException(
                        $"ReaderGroup '{group.Name}' must use standard ReaderGroup message settings.");
                }
                foreach (DataSetReaderDataType reader in group.DataSetReaders)
                {
                    if (!reader.MessageSettings.IsNull &&
                        !reader.MessageSettings.TryGetValue(out UadpDataSetReaderMessageDataType? _))
                    {
                        throw new NotSupportedException(
                            $"DataSetReader '{reader.Name}' must use UADP DataSetReader message settings.");
                    }
                }
            }
        }

        /// <summary>
        /// Resolves and validates the locally provisioned flow policy for a UAFX PubSub connection.
        /// </summary>
        internal static DataChannelParametersDataType GetUafxParameters(
            PubSubConnectionDataType connection,
            Func<PubSubConnectionDataType, DataChannelParametersDataType> parametersResolver)
        {
            if (connection is null)
            {
                throw new ArgumentNullException(nameof(connection));
            }
            if (parametersResolver is null)
            {
                throw new ArgumentNullException(nameof(parametersResolver));
            }
            DataChannelParametersDataType parameters = parametersResolver(connection) ??
                throw new InvalidOperationException("The UAFX local binding returned no flow parameters.");
            ValidateUafxBinding(connection, parameters);
            return CopyParameters(parameters);
        }

        /// <summary>The connection property containing the generated flow descriptor.</summary>
        public const string ParametersProperty = "DataChannelParameters";

        /// <summary>The standard PubSub-over-UASC profile used by the UAFX binding.</summary>
        public const string UafxTransportProfile = DataChannelProfiles.PubSubUasc;

        /// <summary>The required content type for the UAFX UADP binding.</summary>
        public const string UafxContentType = "application/octet-stream";
    }
}
