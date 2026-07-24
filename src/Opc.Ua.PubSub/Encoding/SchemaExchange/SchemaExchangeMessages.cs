/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.IO;
using System.Text;
using System.Text.Json;
using Opc.Ua;
using Opc.Ua.PubSub.Encoding.Json;

namespace Opc.Ua.PubSub.Encoding
{
    /// <summary>
    /// Builds schema-exchange announcements for experimental PubSub messages.
    /// </summary>
    /// <remarks>
    /// NOTE (experimental limitation): the announcement body is a deterministic internal schema
    /// <em>descriptor</em> (see <see cref="BuildSchemaDescriptor"/>) that captures the DataSet shape,
    /// not the canonical schema the Part 14 mappings name (the Avro Parsing Canonical Form, §9.3, or
    /// the serialized Arrow Schema IPC bytes, §5.2.1). The SchemaId fingerprint <em>algorithms</em>
    /// are spec-correct, but because the fingerprint <em>input</em> is the descriptor, the resulting
    /// SchemaId and body are consistent within this library only. This is sufficient to demonstrate
    /// the announce-once / cache / verify-on-ingest handshake; emitting the canonical schema so a
    /// spec-conformant peer computes the same SchemaId and can parse the body is a documented
    /// follow-up for cross-implementation interoperability.
    /// </remarks>
    internal static class SchemaExchangeMessages
    {
        /// <summary>
        /// Creates an Avro announcement for a single DataSetMessage's schema. Per the Part 14 Avro
        /// mapping (§8.1) the SchemaId identifies the schema of one DataSet, so each DataSetMessage
        /// carried opaquely in the fixed NetworkMessage envelope is announced and identified by its
        /// own per-DataSet SchemaId.
        /// </summary>
        /// <param name="envelope">The Avro network message envelope.</param>
        /// <param name="dataSetMessage">The DataSetMessage whose schema is announced.</param>
        /// <param name="context">The encoding context (used to resolve DataSetMetaData).</param>
        /// <returns>The per-DataSet schema announcement.</returns>
        internal static AvroSchemaAnnouncement CreateAvroDataSetAnnouncement(
            AvroNetworkMessage envelope,
            PubSubDataSetMessage dataSetMessage,
            PubSubNetworkMessageContext context)
        {
            string schemaJson = BuildDataSetDescriptor(envelope, dataSetMessage, SchemaCache.AvroFormat, context);
            ByteString schemaBytes = ByteString.From(System.Text.Encoding.UTF8.GetBytes(schemaJson));
            ByteString schemaId = SchemaCache.ComputeSchemaId(schemaBytes, SchemaCache.AvroFormat);
            return new AvroSchemaAnnouncement(schemaId, schemaJson, null);
        }

        /// <summary>
        /// Creates a JSON Schema announcement for the first DataSetMessage shape in the current
        /// JSON NetworkMessage.
        /// </summary>
        /// <param name="message">The JSON network message.</param>
        /// <param name="context">The encoding context (used to resolve DataSetMetaData).</param>
        /// <param name="schemaProvider">The provider that creates the JSON Schema document.</param>
        /// <param name="verbose">Whether to generate the verbose OPC UA JSON encoding schema.</param>
        /// <returns>The schema announcement.</returns>
        internal static JsonSchemaAnnouncement CreateJsonAnnouncement(
            Json.JsonNetworkMessage message,
            PubSubNetworkMessageContext context,
            IDataSetJsonSchemaProvider schemaProvider,
            bool verbose = false)
        {
            if (message is null)
            {
                throw new ArgumentNullException(nameof(message));
            }
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (schemaProvider is null)
            {
                throw new ArgumentNullException(nameof(schemaProvider));
            }

            DataSetMetaDataType metaData = ResolveJsonMetaData(message, context);
            string schemaJson = schemaProvider.CreateJsonSchema(metaData, verbose);
            ByteString schemaId = JsonSchemaAnnouncement.ComputeSchemaId(schemaJson);
            return new JsonSchemaAnnouncement(schemaId, schemaJson, null);
        }

#if NET8_0_OR_GREATER
        /// <summary>
        /// Creates an Arrow announcement for the current PubSub schema shape. The SchemaId and Schema
        /// are derived from the internal descriptor (see the type remarks), not yet the canonical
        /// serialized Arrow Schema.
        /// </summary>
        /// <param name="message">The Arrow network message.</param>
        /// <param name="context">The encoding context (used to resolve DataSetMetaData).</param>
        /// <returns>The schema announcement.</returns>
        internal static ArrowSchemaAnnouncement CreateArrowAnnouncement(
            ArrowNetworkMessage message,
            PubSubNetworkMessageContext context)
        {
            string descriptor = BuildSchemaDescriptor(message, SchemaCache.ArrowFormat, context);
            ByteString schema = ByteString.From(System.Text.Encoding.UTF8.GetBytes(descriptor));
            ByteString schemaId = SchemaCache.ComputeSchemaId(schema, SchemaCache.ArrowFormat);
            return new ArrowSchemaAnnouncement(schemaId, schema, null);
        }
#endif

        /// <summary>
        /// Writes a deterministic schema descriptor for announcement change tracking. This is an
        /// internal shape summary, not the canonical Avro/Arrow schema (see the type remarks).
        /// </summary>
        /// <param name="message">The network message.</param>
        /// <param name="format">The schema format.</param>
        /// <param name="context">The encoding context (used to resolve DataSetMetaData).</param>
        /// <returns>The deterministic schema descriptor.</returns>
        internal static string BuildSchemaDescriptor(
            PubSubNetworkMessage message,
            string format,
            PubSubNetworkMessageContext context)
        {
            Uuid dataSetClassId = message switch
            {
                AvroNetworkMessage avroMessage => avroMessage.DataSetClassId,
#if NET8_0_OR_GREATER
                ArrowNetworkMessage arrowMessage => arrowMessage.DataSetClassId,
#endif
                _ => default
            };
            using MemoryStream stream = new();
            using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = false }))
            {
                writer.WriteStartObject();
                writer.WriteString("format", format);
                writer.WriteString("publisherId", message.PublisherId.ToString());
                writer.WriteNumber("writerGroupId", message.WriterGroupId ?? 0);
                if (message is AvroNetworkMessage avro)
                {
                    writer.WriteString("dataSetClassId", avro.DataSetClassId.ToString());
                }
#if NET8_0_OR_GREATER
                if (message is ArrowNetworkMessage arrow)
                {
                    writer.WriteString("dataSetClassId", arrow.DataSetClassId.ToString());
                }
#endif
                writer.WriteStartArray("messages");
                for (int i = 0; i < message.DataSetMessages.Count; i++)
                {
                    PubSubDataSetMessage dataSetMessage = message.DataSetMessages[i];
                    DataSetMetaDataType? metaData = PubSubMessageEncoding.ResolveMetaData(
                        message,
                        dataSetMessage,
                        context,
                        dataSetClassId);
                    WriteDataSetMessage(writer, dataSetMessage, metaData);
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            return System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }

        /// <summary>
        /// Builds a deterministic per-DataSet schema descriptor for a single DataSetMessage. This is
        /// the per-DataSet analog of <see cref="BuildSchemaDescriptor"/>: it is a function of the
        /// DataSet shape only (writer, ConfigurationVersion, field-content mask and the
        /// metadata-driven field list), independent of the NetworkMessage envelope routing, so two
        /// frames of the same DataSet - including a full key frame and any sparse subset - produce
        /// the identical descriptor and per-DataSet SchemaId.
        /// </summary>
        /// <param name="envelope">The network message envelope.</param>
        /// <param name="dataSetMessage">The DataSetMessage to describe.</param>
        /// <param name="format">The schema format.</param>
        /// <param name="context">The encoding context (used to resolve DataSetMetaData).</param>
        /// <returns>The deterministic per-DataSet schema descriptor.</returns>
        internal static string BuildDataSetDescriptor(
            PubSubNetworkMessage envelope,
            PubSubDataSetMessage dataSetMessage,
            string format,
            PubSubNetworkMessageContext context)
        {
            Uuid dataSetClassId = envelope switch
            {
                AvroNetworkMessage avroMessage => avroMessage.DataSetClassId,
#if NET8_0_OR_GREATER
                ArrowNetworkMessage arrowMessage => arrowMessage.DataSetClassId,
#endif
                _ => default
            };
            DataSetMetaDataType? metaData = PubSubMessageEncoding.ResolveMetaData(
                envelope,
                dataSetMessage,
                context,
                dataSetClassId);
            using MemoryStream stream = new();
            using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = false }))
            {
                writer.WriteStartObject();
                writer.WriteString("format", format);
                writer.WriteString("dataSetClassId", dataSetClassId.ToString());
                writer.WritePropertyName("dataSet");
                WriteDataSetMessage(writer, dataSetMessage, metaData);
                writer.WriteEndObject();
            }
            return System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }

        private static void WriteDataSetMessage(
            Utf8JsonWriter writer,
            PubSubDataSetMessage message,
            DataSetMetaDataType? metaData)
        {
            writer.WriteStartObject();
            writer.WriteNumber("writerId", message.DataSetWriterId);
            writer.WriteNumber("messageType", (int)message.MessageType);
            writer.WriteNumber("major", message.MetaDataVersion.MajorVersion);
            writer.WriteNumber("minor", message.MetaDataVersion.MinorVersion);
            writer.WriteNumber("fieldContentMask", FieldContentMask(message));
            writer.WriteStartArray("fields");
            if (metaData?.Fields is { Count: > 0 } metaFields)
            {
                // Metadata-driven descriptor: emit every declared key so a full
                // key frame and any sparse subset (absent keys encoded as
                // null:null) produce the identical descriptor and SchemaId. The
                // field encoding mode is uniform per writer, so it is taken from
                // the first present field (or 0 when the frame is empty).
                int uniformEncoding = message.Fields.Count > 0
                    ? (int)message.Fields[0].Encoding
                    : 0;
                for (int i = 0; i < metaFields.Count; i++)
                {
                    FieldMetaData fieldMetaData = metaFields[i];
                    writer.WriteStartObject();
                    writer.WriteString("name", fieldMetaData.Name ?? string.Empty);
                    writer.WriteNumber("index", i);
                    writer.WriteNumber("encoding", uniformEncoding);
                    writer.WriteString(
                        "type",
                        new TypeInfo(
                            (BuiltInType)fieldMetaData.BuiltInType,
                            fieldMetaData.ValueRank).ToString());
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
                return;
            }

            for (int i = 0; i < message.Fields.Count; i++)
            {
                DataSetField field = message.Fields[i];
                writer.WriteStartObject();
                writer.WriteString("name", field.Name ?? string.Empty);
                writer.WriteNumber("index", field.FieldIndex >= 0 ? field.FieldIndex : i);
                writer.WriteNumber("encoding", (int)field.Encoding);
                writer.WriteString("type", field.Value.TypeInfo.ToString());
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        private static uint FieldContentMask(PubSubDataSetMessage message)
        {
            return message switch
            {
                AvroDataSetMessage avro => (uint)avro.FieldContentMask,
#if NET8_0_OR_GREATER
                ArrowDataSetMessage arrow => (uint)arrow.FieldContentMask,
#endif
                _ => 0
            };
        }

        private static DataSetMetaDataType ResolveJsonMetaData(
            Json.JsonNetworkMessage message,
            PubSubNetworkMessageContext context)
        {
            if (message.DataSetMessages.Count > 0)
            {
                DataSetMetaDataType? metaData = PubSubMessageEncoding.ResolveMetaData(
                    message,
                    message.DataSetMessages[0],
                    context,
                    message.DataSetClassId);
                if (metaData is not null)
                {
                    return metaData;
                }
            }

            return message.MetaData ?? new DataSetMetaDataType { Name = "DataSet" };
        }
    }
}
