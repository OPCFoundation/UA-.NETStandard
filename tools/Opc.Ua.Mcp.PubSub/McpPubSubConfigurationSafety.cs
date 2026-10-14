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

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Removes extensible credential-bearing bags from an owned configuration before any artifact is written.
    /// Imports reject those bags rather than silently changing the caller's configuration.
    /// </summary>
    internal static class McpPubSubConfigurationSafety
    {
        /// <summary>
        /// Bounds component/field counts and strips sensitive or opaque extensions.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        internal static int Sanitize(PubSubConfigurationDataType configuration, McpPubSubOptions options)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            int removed = 0;
            int components = configuration.Connections.Count + configuration.PublishedDataSets.Count;
            ArgumentOutOfRangeException.ThrowIfGreaterThan(components, options.MaxConfigurationComponents);
            foreach (PublishedDataSetDataType dataSet in configuration.PublishedDataSets)
            {
                ArgumentNullException.ThrowIfNull(dataSet);
                removed += dataSet.ExtensionFields.Count;
                dataSet.ExtensionFields = [];
                ArgumentOutOfRangeException.ThrowIfGreaterThan(dataSet.DataSetMetaData.Fields.Count, options.MaxFields);
                foreach (FieldMetaData field in dataSet.DataSetMetaData.Fields)
                {
                    removed += field.Properties.Count;
                    field.Properties = [];
                }
                if (dataSet.DataSetSource.TryGetValue(out PublishedDataItemsDataType? items) && items is not null)
                {
                    ArgumentOutOfRangeException.ThrowIfGreaterThan(items.PublishedData.Count, options.MaxFields);
                    foreach (PublishedVariableDataType variable in items.PublishedData)
                    {
                        if (!variable.SubstituteValue.IsNull)
                        {
                            variable.SubstituteValue = Variant.Null;
                            removed++;
                        }
                        removed += variable.MetaDataProperties.Count;
                        variable.MetaDataProperties = [];
                    }
                }
                else if (!dataSet.DataSetSource.IsNull)
                {
                    dataSet.DataSetSource = ExtensionObject.Null;
                    removed++;
                }
            }
            foreach (PubSubConnectionDataType connection in configuration.Connections)
            {
                ArgumentNullException.ThrowIfNull(connection);
                if (!connection.Address.TryGetValue(out NetworkAddressUrlDataType? address) || address is null)
                {
                    throw new ArgumentException("Only standard network-address configuration artifacts are supported.");
                }
                removed += SanitizeAddress(address);
                removed += connection.ConnectionProperties.Count;
                connection.ConnectionProperties = [];
                if (!connection.TransportSettings.IsNull)
                {
                    connection.TransportSettings = ExtensionObject.Null;
                    removed++;
                }
                components += connection.WriterGroups.Count + connection.ReaderGroups.Count;
                foreach (WriterGroupDataType group in connection.WriterGroups)
                {
                    removed += group.GroupProperties.Count + group.SecurityKeyServices.Count;
                    group.GroupProperties = [];
                    group.SecurityKeyServices = [];
                    group.TransportSettings = Transport(group.TransportSettings, ref removed);
                    RequireMessageSettings(group.MessageSettings);
                    components += group.DataSetWriters.Count;
                    foreach (DataSetWriterDataType writer in group.DataSetWriters)
                    {
                        removed += writer.DataSetWriterProperties.Count;
                        writer.DataSetWriterProperties = [];
                        writer.TransportSettings = Transport(writer.TransportSettings, ref removed);
                        RequireMessageSettings(writer.MessageSettings);
                    }
                }
                foreach (ReaderGroupDataType group in connection.ReaderGroups)
                {
                    removed += group.GroupProperties.Count + group.SecurityKeyServices.Count;
                    group.GroupProperties = [];
                    group.SecurityKeyServices = [];
                    group.TransportSettings = Transport(group.TransportSettings, ref removed);
                    RequireMessageSettings(group.MessageSettings);
                    components += group.DataSetReaders.Count;
                    foreach (DataSetReaderDataType reader in group.DataSetReaders)
                    {
                        removed += reader.DataSetReaderProperties.Count + reader.SecurityKeyServices.Count;
                        reader.DataSetReaderProperties = [];
                        reader.SecurityKeyServices = [];
                        reader.TransportSettings = Transport(reader.TransportSettings, ref removed);
                        RequireMessageSettings(reader.MessageSettings);
                        ArgumentOutOfRangeException.ThrowIfGreaterThan(reader.DataSetMetaData.Fields.Count,
                            options.MaxFields);
                        foreach (FieldMetaData field in reader.DataSetMetaData.Fields)
                        {
                            removed += field.Properties.Count;
                            field.Properties = [];
                        }
                        if (!reader.SubscribedDataSet.IsNull &&
                            !reader.SubscribedDataSet.TryGetValue(out SubscribedDataSetMirrorDataType? _))
                        {
                            reader.SubscribedDataSet = ExtensionObject.Null;
                            removed++;
                        }
                    }
                }
                ArgumentOutOfRangeException.ThrowIfGreaterThan(components, options.MaxConfigurationComponents);
            }
            return removed;
        }

        /// <summary>
        /// Removes embedded URL credentials from export rather than writing them to a regular file.
        /// </summary>
        private static int SanitizeAddress(NetworkAddressUrlDataType address)
        {
            string url = PubSubInputValidation.Text(address.Url);
            if (url.StartsWith("opc.eth://", StringComparison.Ordinal))
            {
                _ = PubSub.Eth.EthEndpointParser.Parse(url);
                return 0;
            }
            var uri = new Uri(url, UriKind.Absolute);
            if (uri.UserInfo.Length == 0)
            {
                return 0;
            }
            address.Url = new UriBuilder(uri) { UserName = string.Empty, Password = string.Empty }.Uri.AbsoluteUri;
            return 1;
        }

        /// <summary>
        /// Reconstructs known broker settings without authentication fields; refuses opaque extensions.
        /// </summary>
        private static ExtensionObject Transport(ExtensionObject value, ref int removed)
        {
            if (value.IsNull)
            {
                return value;
            }
            if (value.TryGetValue(out BrokerWriterGroupTransportDataType? group) && group is not null)
            {
                if (!string.IsNullOrEmpty(group.AuthenticationProfileUri) || !string.IsNullOrEmpty(group.ResourceUri))
                {
                    removed++;
                }
                return new ExtensionObject(new BrokerWriterGroupTransportDataType
                {
                    QueueName = group.QueueName,
                    RequestedDeliveryGuarantee = group.RequestedDeliveryGuarantee
                });
            }
            if (value.TryGetValue(out BrokerDataSetWriterTransportDataType? writer) && writer is not null)
            {
                if (!string.IsNullOrEmpty(writer.AuthenticationProfileUri) || !string.IsNullOrEmpty(writer.ResourceUri))
                {
                    removed++;
                }
                return new ExtensionObject(new BrokerDataSetWriterTransportDataType
                {
                    QueueName = writer.QueueName,
                    MetaDataQueueName = writer.MetaDataQueueName,
                    MetaDataUpdateTime = writer.MetaDataUpdateTime,
                    RequestedDeliveryGuarantee = writer.RequestedDeliveryGuarantee
                });
            }
            if (value.TryGetValue(out BrokerDataSetReaderTransportDataType? reader) && reader is not null)
            {
                if (!string.IsNullOrEmpty(reader.AuthenticationProfileUri) || !string.IsNullOrEmpty(reader.ResourceUri))
                {
                    removed++;
                }
                return new ExtensionObject(new BrokerDataSetReaderTransportDataType
                {
                    QueueName = reader.QueueName,
                    MetaDataQueueName = reader.MetaDataQueueName,
                    RequestedDeliveryGuarantee = reader.RequestedDeliveryGuarantee
                });
            }
            if (value.TryGetValue(out DatagramWriterGroupTransportDataType? datagram) &&
                datagram is not null &&
                datagram.GetType() == typeof(DatagramWriterGroupTransportDataType))
            {
                return value;
            }
            removed++;
            return ExtensionObject.Null;
        }

        /// <summary>
        /// Allows only fixed-shape standard numeric message masks, never an arbitrary encoded extension body.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        private static void RequireMessageSettings(ExtensionObject value)
        {
            if (value.IsNull)
            {
                return;
            }
            Type[] allowed =
            [
                typeof(UadpWriterGroupMessageDataType), typeof(UadpDataSetWriterMessageDataType),
                typeof(UadpDataSetReaderMessageDataType), typeof(JsonWriterGroupMessageDataType),
                typeof(JsonDataSetWriterMessageDataType), typeof(JsonDataSetReaderMessageDataType)
            ];
            if (!value.TryGetValue(out IEncodeable? body) || body is null || !allowed.Contains(body.GetType()))
            {
                throw new ArgumentException(
                    "Opaque message settings cannot be exported or imported as a safe artifact.");
            }
        }
    }
}
