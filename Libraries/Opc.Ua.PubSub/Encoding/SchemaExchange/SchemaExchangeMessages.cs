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
        /// Creates an Avro announcement for the current PubSub schema shape. The SchemaId and
        /// SchemaJson are derived from the internal descriptor (see the type remarks), not yet the
        /// canonical Avro Parsing Canonical Form.
        /// </summary>
        /// <param name="message">The Avro network message.</param>
        /// <returns>The schema announcement.</returns>
        internal static AvroSchemaAnnouncement CreateAvroAnnouncement(AvroNetworkMessage message)
        {
            string schemaJson = BuildSchemaDescriptor(message, SchemaCache.AvroFormat);
            ByteString schemaBytes = ByteString.From(System.Text.Encoding.UTF8.GetBytes(schemaJson));
            ByteString schemaId = SchemaCache.ComputeSchemaId(schemaBytes, SchemaCache.AvroFormat);
            return new AvroSchemaAnnouncement(schemaId, schemaJson, null);
        }

        /// <summary>
        /// Creates an Arrow announcement for the current PubSub schema shape. The SchemaId and Schema
        /// are derived from the internal descriptor (see the type remarks), not yet the canonical
        /// serialized Arrow Schema.
        /// </summary>
        /// <param name="message">The Arrow network message.</param>
        /// <returns>The schema announcement.</returns>
        internal static ArrowSchemaAnnouncement CreateArrowAnnouncement(ArrowNetworkMessage message)
        {
            string descriptor = BuildSchemaDescriptor(message, SchemaCache.ArrowFormat);
            ByteString schema = ByteString.From(System.Text.Encoding.UTF8.GetBytes(descriptor));
            ByteString schemaId = SchemaCache.ComputeSchemaId(schema, SchemaCache.ArrowFormat);
            return new ArrowSchemaAnnouncement(schemaId, schema, null);
        }

        /// <summary>
        /// Writes a deterministic schema descriptor for announcement change tracking. This is an
        /// internal shape summary, not the canonical Avro/Arrow schema (see the type remarks).
        /// </summary>
        /// <param name="message">The network message.</param>
        /// <param name="format">The schema format.</param>
        /// <returns>The deterministic schema descriptor.</returns>
        internal static string BuildSchemaDescriptor(PubSubNetworkMessage message, string format)
        {
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
                if (message is ArrowNetworkMessage arrow)
                {
                    writer.WriteString("dataSetClassId", arrow.DataSetClassId.ToString());
                }
                writer.WriteStartArray("messages");
                for (int i = 0; i < message.DataSetMessages.Count; i++)
                {
                    WriteDataSetMessage(writer, message.DataSetMessages[i]);
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            return System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }

        private static void WriteDataSetMessage(Utf8JsonWriter writer, PubSubDataSetMessage message)
        {
            writer.WriteStartObject();
            writer.WriteNumber("writerId", message.DataSetWriterId);
            writer.WriteNumber("messageType", (int)message.MessageType);
            writer.WriteNumber("major", message.MetaDataVersion.MajorVersion);
            writer.WriteNumber("minor", message.MetaDataVersion.MinorVersion);
            writer.WriteNumber("fieldContentMask", FieldContentMask(message));
            writer.WriteStartArray("fields");
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
                ArrowDataSetMessage arrow => (uint)arrow.FieldContentMask,
                _ => 0
            };
        }
    }
}
