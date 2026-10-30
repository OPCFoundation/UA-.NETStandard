/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * SPDX-License-Identifier: MIT
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.MetaData;
using Opc.Ua.SchemaRegistry;
using Opc.Ua.XRegistry;

namespace Opc.Ua.EndpointRegistry.PubSub
{
    internal static class PubSubSurfaceBuilder
    {
        public static RegistryObjectValueDataType Endpoint(
            string id,
            PubSubConnectionDataType connection,
            WriterGroupDataType group,
            DataSetWriterDataType? writer,
            ArrayOf<RegistryMemberDataType> messages,
            string fingerprint)
        {
            string? version = PubSubBindingRules.MqttVersion(connection);
            string? url = PubSubBindingRules.BrokerUrl(connection);
            (string queue, BrokerTransportQualityOfService guarantee) = PubSubBindingRules.Queue(group, writer);
            if (version is null || url is null || !PubSubBindingRules.ValidTopic(queue) ||
                PubSubBindingRules.ContentType(connection) is null || PubSubBindingRules.Qos(guarantee) is not { } qos ||
                (writer is null ? !PubSubBindingRules.GroupQueueUsed(group) :
                    !PubSubBindingRules.OwnQueue(writer) || !PubSubBindingRules.WriterQueueValid(group)))
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument, "The publishing queue cannot be surfaced.");
            }
            return Map(
                Member("endpointid", Text(id)),
                Member("usage", new RegistryArrayValueDataType { Kind = 4, Items = [Text("subscriber"), Text("consumer")] }),
                Member("protocol", Text("MQTT/" + version)),
                Member("protocoloptions", Map(
                    Member("endpoints", new RegistryArrayValueDataType
                    {
                        Kind = 4,
                        Items = [Map(Member("uri", Text(url)))]
                    }),
                    Member("topicfilter", Text(queue)),
                    Member("qos", PubSubBindingRules.Number((uint)qos)))),
                Member("messages", new RegistryObjectValueDataType { Kind = 5, Members = messages }),
                Member("pubsubconfiguration", Text(fingerprint)));
        }

        public static RegistryObjectValueDataType Message(
            string id, PubSubConnectionDataType connection, WriterGroupDataType group,
            DataSetWriterDataType writer, SchemaReferenceDataType? schema)
        {
            (string queue, BrokerTransportQualityOfService guarantee) = PubSubBindingRules.Queue(group, writer);
            int qos = PubSubBindingRules.Qos(guarantee) ??
                throw new ServiceResultException(StatusCodes.BadInvalidArgument, "The effective QoS is unspecified.");
            var members = new List<RegistryMemberDataType>
            {
                Member("messageid", Text(id)),
                Member("protocol", Text("MQTT/" + PubSubBindingRules.MqttVersion(connection))),
                Member("protocoloptions", Map(Member("topic_name", Text(queue)),
                    Member("qos", PubSubBindingRules.Number((uint)qos)))),
                Member("datacontenttype", Text(PubSubBindingRules.ContentType(connection) ?? string.Empty))
            };
            if (schema is not null)
            {
                string? locator = !string.IsNullOrEmpty(schema.SelectedObjectUri) ? schema.SelectedObjectUri : schema.EntityUri;
                if (string.IsNullOrEmpty(locator) || string.IsNullOrEmpty(schema.Format))
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidArgument, "An exact schema locator and format are required.");
                }
                members.Add(Member("dataschemaformat", Text(schema.Format)));
                members.Add(Member("dataschemauri", Text(locator)));
            }
            return new RegistryObjectValueDataType { Kind = 5, Members = members.ToArray() };
        }

        public static DataSetMetaDataKey Key(
            PubSubConnectionDataType connection, WriterGroupDataType group,
            DataSetWriterDataType writer, DataSetMetaDataType metadata)
        {
            return new DataSetMetaDataKey(PublisherId.From(connection.PublisherId), group.WriterGroupId,
                writer.DataSetWriterId, metadata.DataSetClassId, metadata.ConfigurationVersion?.MajorVersion ?? 0);
        }

        public static string Identifier(string prefix, string identity)
        {
#if NET6_0_OR_GREATER
            byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
#else
            using SHA256 algorithm = SHA256.Create();
            byte[] digest = algorithm.ComputeHash(Encoding.UTF8.GetBytes(identity));
#endif
#if NET9_0_OR_GREATER
            return prefix + Convert.ToHexStringLower(digest);
#else
            var text = new StringBuilder(prefix, prefix.Length + digest.Length * 2);
            foreach (byte value in digest)
            {
                text.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            }
            return text.ToString();
#endif
        }

        public static string Fingerprint(IServiceMessageContext context, PubSubConnectionDataType connection,
            ArrayOf<PublishedDataSetDataType> datasets, ArrayOf<DataSetMetaDataType> metadata)
        {
            using var encoder = new BinaryEncoder(context);
            encoder.WriteEncodeable("Connection", connection, Ua.DataTypeIds.PubSubConnectionDataType);
            encoder.WriteEncodeableArray("DataSets", datasets, Ua.DataTypeIds.PublishedDataSetDataType);
            encoder.WriteEncodeableArray("MetaData", metadata, Ua.DataTypeIds.DataSetMetaDataType);
            return Identifier(string.Empty, Convert.ToBase64String(encoder.CloseAndReturnBuffer()!));
        }

        public static RegistryObjectValueDataType Map(params RegistryMemberDataType[] members)
        {
            return new RegistryObjectValueDataType { Kind = 5, Members = members };
        }

        public static RegistryMemberDataType Member(string name, RegistryValueDataType value)
        {
            return new RegistryMemberDataType { Name = name, Value = value };
        }

        public static RegistryStringValueDataType Text(string value)
        {
            return new RegistryStringValueDataType { Kind = 2, Value = value };
        }
    }
}
