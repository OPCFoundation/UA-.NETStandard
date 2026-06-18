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
using System.Buffers;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.PubSub.MetaData;

namespace Opc.Ua.PubSub.Encoding.Json
{
    /// <summary>
    /// <see cref="INetworkMessageEncoder"/> implementation that
    /// serialises <see cref="JsonNetworkMessage"/> and
    /// <see cref="JsonMetaDataMessage"/> instances to the JSON
    /// NetworkMessage wire format using
    /// <see cref="System.Text.Json.Utf8JsonWriter"/>.
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.5">
    /// Part 14 §7.2.5</see> JSON mapping, including the envelope shape
    /// described in §7.2.5.3, the per-DataSetMessage field set from
    /// §7.2.5.4, the metadata-message shape from §7.2.5.5 and the
    /// single-message layout from Annex A.3.3.
    /// </remarks>
    public sealed class JsonEncoder : INetworkMessageEncoder
    {
        /// <summary>
        /// Creates a new encoder.
        /// </summary>
        /// <param name="mode">
        /// Encoding mode applied to every Variant payload.
        /// </param>
        public JsonEncoder(JsonEncodingMode mode = JsonEncodingMode.Verbose)
        {
            Mode = mode;
        }

        /// <summary>
        /// Encoding mode used for Variant payloads.
        /// </summary>
        public JsonEncodingMode Mode { get; }

        /// <inheritdoc/>
        public string TransportProfileUri => Profiles.PubSubMqttJsonTransport;

        /// <inheritdoc/>
        public int EstimatedHeaderOverhead => 256;

        /// <inheritdoc/>
        public ValueTask<ReadOnlyMemory<byte>> EncodeAsync(
            PubSubNetworkMessage networkMessage,
            PubSubNetworkMessageContext context,
            CancellationToken cancellationToken = default)
        {
            if (networkMessage is null)
            {
                throw new ArgumentNullException(nameof(networkMessage));
            }
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            cancellationToken.ThrowIfCancellationRequested();
            return networkMessage switch
            {
                JsonNetworkMessage data => new ValueTask<ReadOnlyMemory<byte>>(
                    EncodeNetwork(data, context)),
                JsonMetaDataMessage meta => new ValueTask<ReadOnlyMemory<byte>>(
                    EncodeMetaData(meta, context)),
                JsonDiscoveryMessage discovery => new ValueTask<ReadOnlyMemory<byte>>(
                    EncodeDiscovery(discovery, context)),
                JsonActionNetworkMessage action => new ValueTask<ReadOnlyMemory<byte>>(
                    EncodeAction(action, context)),
                _ => throw new ArgumentException(
                    "Network message type is not supported by the JSON encoder.",
                    nameof(networkMessage))
            };
        }

        /// <summary>
        /// Encodes a <see cref="JsonNetworkMessage"/> (<c>ua-data</c>
        /// envelope).
        /// </summary>
        /// <param name="message">Source network message.</param>
        /// <param name="context">Encoder context.</param>
        /// <returns>Encoded UTF-8 frame.</returns>
        private ReadOnlyMemory<byte> EncodeNetwork(
            JsonNetworkMessage message,
            PubSubNetworkMessageContext context)
        {
            if (message.SingleMessageMode && message.DataSetMessages.Count != 1)
            {
                throw new ArgumentException(
                    "JsonNetworkMessage with SingleMessageMode requires exactly one " +
                    "DataSetMessage per Part 14 §7.2.5.4.5 / §7.3.4.7.3 / Annex A.3.3.",
                    nameof(message));
            }
            using JsonBufferWriter buffer = new(512);
            using (Utf8JsonWriter writer = new(buffer, new JsonWriterOptions
            {
                SkipValidation = true,
                Indented = false
            }))
            {
                bool flatLayout = message.SingleMessageMode
                    && message.DataSetMessages.Count == 1;
                writer.WriteStartObject();
                WriteEnvelopeHead(writer, message);
                if (flatLayout)
                {
                    if (message.DataSetMessages[0] is not JsonDataSetMessage only)
                    {
                        throw new ArgumentException(
                            "SingleMessageMode requires a JsonDataSetMessage payload.",
                            nameof(message));
                    }
                    WriteDataSetMessageFields(writer, only, message, context, flatLayout: true);
                }
                else
                {
                    writer.WritePropertyName("Messages");
                    writer.WriteStartArray();
                    for (int i = 0; i < message.DataSetMessages.Count; i++)
                    {
                        if (message.DataSetMessages[i] is not JsonDataSetMessage dsm)
                        {
                            throw new ArgumentException(
                                "DataSetMessage entries must be JsonDataSetMessage instances.",
                                nameof(message));
                        }
                        writer.WriteStartObject();
                        WriteDataSetMessageFields(writer, dsm, message, context, flatLayout: false);
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                }
                WriteEnvelopeTail(writer, message);
                writer.WriteEndObject();
            }
            return buffer.GetWritten();
        }

        /// <summary>
        /// Writes the envelope fields that precede the message body in
        /// the wire order from Part 14 §7.2.5.3.
        /// </summary>
        /// <param name="writer">Destination writer.</param>
        /// <param name="message">Source envelope.</param>
        private static void WriteEnvelopeHead(
            Utf8JsonWriter writer,
            JsonNetworkMessage message)
        {
            if (!string.IsNullOrEmpty(message.MessageId))
            {
                writer.WriteString("MessageId", message.MessageId);
            }
            writer.WriteString(
                "MessageType",
                string.IsNullOrEmpty(message.MessageType)
                    ? JsonNetworkMessage.MessageTypeData
                    : message.MessageType);
            WritePublisherId(writer, "PublisherId", message.PublisherId);
            if (message.DataSetClassId.Guid != Guid.Empty)
            {
                writer.WriteString("DataSetClassId", message.DataSetClassId.ToString());
            }
        }

        /// <summary>
        /// Writes the trailing envelope fields (currently
        /// <c>ReplyTo</c>) per Part 14 §7.2.5.3.
        /// </summary>
        /// <param name="writer">Destination writer.</param>
        /// <param name="message">Source envelope.</param>
        private static void WriteEnvelopeTail(
            Utf8JsonWriter writer,
            JsonNetworkMessage message)
        {
            if (message.ReplyTo.Count == 0)
            {
                return;
            }
            writer.WritePropertyName("ReplyTo");
            writer.WriteStartArray();
            for (int i = 0; i < message.ReplyTo.Count; i++)
            {
                writer.WriteStringValue(message.ReplyTo[i]);
            }
            writer.WriteEndArray();
        }

        /// <summary>
        /// Writes the per-DataSetMessage fields in the order required
        /// by Part 14 §7.2.5.4, respecting the
        /// <see cref="JsonDataSetMessage.ContentMask"/>.
        /// </summary>
        /// <param name="writer">Destination writer.</param>
        /// <param name="dsm">DataSetMessage to encode.</param>
        /// <param name="envelope">Owning envelope (provides defaults).</param>
        /// <param name="context">Encoder context.</param>
        /// <param name="flatLayout">
        /// True when the DataSetMessage is being merged into the envelope
        /// in single-message mode; suppresses the per-message
        /// <c>MessageType</c> property so it does not shadow the
        /// envelope's <c>ua-data</c> tag.
        /// </param>
        private void WriteDataSetMessageFields(
            Utf8JsonWriter writer,
            JsonDataSetMessage dsm,
            JsonNetworkMessage envelope,
            PubSubNetworkMessageContext context,
            bool flatLayout)
        {
            JsonDataSetMessageContentMask mask = dsm.ContentMask;
            if ((mask & JsonDataSetMessageContentMask.DataSetWriterId) != 0
                && dsm.DataSetWriterId != 0)
            {
                writer.WriteNumber("DataSetWriterId", dsm.DataSetWriterId);
            }
            if ((mask & JsonDataSetMessageContentMask.SequenceNumber) != 0)
            {
                writer.WriteNumber("SequenceNumber", dsm.SequenceNumber);
            }
            if ((mask & JsonDataSetMessageContentMask.MetaDataVersion) != 0)
            {
                writer.WritePropertyName("MetaDataVersion");
                writer.WriteStartObject();
                writer.WriteNumber("MajorVersion", dsm.MetaDataVersion.MajorVersion);
                writer.WriteNumber("MinorVersion", dsm.MetaDataVersion.MinorVersion);
                writer.WriteEndObject();
            }
            if ((mask & JsonDataSetMessageContentMask.Timestamp) != 0)
            {
                writer.WriteString(
                    "Timestamp",
                    ((DateTime)dsm.Timestamp).ToString("o", CultureInfo.InvariantCulture));
            }
            if ((mask & JsonDataSetMessageContentMask.Status) != 0)
            {
                writer.WriteNumber("Status", dsm.Status.Code);
            }
            if (!flatLayout
                && (mask & JsonDataSetMessageContentMask.MessageType) != 0)
            {
                string wireType = string.IsNullOrEmpty(dsm.MessageTypeName)
                    ? JsonDataSetMessageType.ToWireString(dsm.MessageType)
                    : dsm.MessageTypeName;
                writer.WriteString("MessageType", wireType);
            }
            DataSetMetaDataType? metaData = ResolveMetaData(envelope, dsm, context);
            JsonFieldEncoder.EncodeFields(
                writer,
                dsm.Fields,
                metaData,
                Mode,
                context.MessageContext,
                dsm.FieldContentMask);
        }

        /// <summary>
        /// Encodes a <see cref="JsonMetaDataMessage"/> (<c>ua-metadata</c>
        /// envelope) per Part 14 §7.2.5.5.
        /// </summary>
        /// <param name="message">Source metadata message.</param>
        /// <param name="context">Encoder context.</param>
        /// <returns>Encoded UTF-8 frame.</returns>
        private ReadOnlyMemory<byte> EncodeMetaData(
            JsonMetaDataMessage message,
            PubSubNetworkMessageContext context)
        {
            DataSetMetaDataType meta = message.MetaDataPayload
                ?? message.MetaData
                ?? throw new ArgumentException(
                    "MetaData payload missing from JsonMetaDataMessage.",
                    nameof(message));
            using JsonBufferWriter buffer = new(1024);
            using (Utf8JsonWriter writer = new(buffer, new JsonWriterOptions
            {
                SkipValidation = true,
                Indented = false
            }))
            {
                writer.WriteStartObject();
                if (!string.IsNullOrEmpty(message.MessageId))
                {
                    writer.WriteString("MessageId", message.MessageId);
                }
                writer.WriteString(
                    "MessageType",
                    JsonNetworkMessage.MessageTypeMetaData);
                WritePublisherId(writer, "PublisherId", message.PublisherId);
                if (message.DataSetWriterId != 0)
                {
                    writer.WriteNumber("DataSetWriterId", message.DataSetWriterId);
                }
                if (message.DataSetClassId.Guid != Guid.Empty)
                {
                    writer.WriteString(
                        "DataSetClassId",
                        message.DataSetClassId.ToString());
                }
                JsonMetaDataEncoder.WriteMetaData(
                    writer,
                    "MetaData",
                    meta,
                    Mode,
                    context.MessageContext);
                writer.WriteEndObject();
            }
            return buffer.GetWritten();
        }

        /// <summary>
        /// Encodes a <see cref="JsonDiscoveryMessage"/>
        /// (<c>ua-discovery</c> envelope) per
        /// <see href="https://reference.opcfoundation.org/Core/Part14/v105/docs/7.2.5.5">
        /// Part 14 §7.2.5.5</see>.
        /// </summary>
        /// <param name="message">Source discovery message.</param>
        /// <param name="context">Encoder context.</param>
        /// <returns>Encoded UTF-8 frame.</returns>
        private ReadOnlyMemory<byte> EncodeDiscovery(
            JsonDiscoveryMessage message,
            PubSubNetworkMessageContext context)
        {
            using JsonBufferWriter buffer = new(1024);
            using (Utf8JsonWriter writer = new(buffer, new JsonWriterOptions
            {
                SkipValidation = true,
                Indented = false
            }))
            {
                writer.WriteStartObject();
                if (!string.IsNullOrEmpty(message.MessageId))
                {
                    writer.WriteString("MessageId", message.MessageId);
                }
                writer.WriteString(
                    "MessageType",
                    JsonDiscoveryMessage.MessageTypeDiscovery);
                WritePublisherId(writer, "PublisherId", message.PublisherId);
                writer.WriteNumber("DiscoveryType", (uint)message.DiscoveryType);
                if (message.DataSetWriterId != 0)
                {
                    writer.WriteNumber("DataSetWriterId", message.DataSetWriterId);
                }
                if (message.Status.Code != StatusCodes.Good)
                {
                    writer.WriteNumber("Status", message.Status.Code);
                }
                switch (message.DiscoveryType)
                {
                    case Uadp.UadpDiscoveryType.ApplicationInformation:
                        WriteApplicationInformation(
                            writer,
                            message.ApplicationInformation
                                ?? new Uadp.UadpApplicationInformation());
                        break;
                    case Uadp.UadpDiscoveryType.PubSubConnection:
                        WriteEncodeableProperty(
                            writer,
                            "Connection",
                            message.Connection,
                            context.MessageContext);
                        break;
                    case Uadp.UadpDiscoveryType.DataSetMetaData:
                        if (message.MetaData is not null)
                        {
                            JsonMetaDataEncoder.WriteMetaData(
                                writer,
                                "MetaData",
                                message.MetaData,
                                Mode,
                                context.MessageContext);
                        }
                        break;
                    case Uadp.UadpDiscoveryType.DataSetWriterConfiguration:
                        WriteUInt16Array(
                            writer,
                            "DataSetWriterIds",
                            message.DataSetWriterIds);
                        WriteEncodeableProperty(
                            writer,
                            "WriterConfiguration",
                            message.WriterConfiguration,
                            context.MessageContext);
                        break;
                    case Uadp.UadpDiscoveryType.PublisherEndpoints:
                        WriteEndpointsProperty(
                            writer,
                            "PublisherEndpoints",
                            message.PublisherEndpoints,
                            context.MessageContext);
                        break;
                }
                writer.WriteEndObject();
            }
            return buffer.GetWritten();
        }

        private static void WriteApplicationInformation(
            Utf8JsonWriter writer,
            Uadp.UadpApplicationInformation info)
        {
            writer.WritePropertyName("ApplicationInformation");
            writer.WriteStartObject();
            writer.WriteString("ApplicationName",
                info.ApplicationName.Text ?? string.Empty);
            writer.WriteString("ApplicationLocale",
                info.ApplicationName.Locale ?? string.Empty);
            writer.WriteString("ApplicationUri", info.ApplicationUri);
            writer.WriteString("ProductUri", info.ProductUri);
            writer.WriteNumber("ApplicationType", (uint)info.ApplicationType);
            writer.WritePropertyName("Capabilities");
            WriteStringArray(writer, info.Capabilities);
            writer.WritePropertyName("SupportedTransportProfiles");
            WriteStringArray(writer, info.SupportedTransportProfiles);
            writer.WritePropertyName("SupportedSecurityPolicies");
            WriteStringArray(writer, info.SupportedSecurityPolicies);
            writer.WriteEndObject();
        }

        private static void WriteStringArray(
            Utf8JsonWriter writer,
            ArrayOf<string> values)
        {
            writer.WriteStartArray();
            foreach (string value in values)
            {
                writer.WriteStringValue(value ?? string.Empty);
            }
            writer.WriteEndArray();
        }

        private static void WriteUInt16Array(
            Utf8JsonWriter writer,
            string propertyName,
            ArrayOf<ushort> values)
        {
            writer.WritePropertyName(propertyName);
            writer.WriteStartArray();
            foreach (ushort value in values)
            {
                writer.WriteNumberValue(value);
            }
            writer.WriteEndArray();
        }

        private static void WriteEncodeableProperty(
            Utf8JsonWriter writer,
            string propertyName,
            IEncodeable? encodeable,
            IServiceMessageContext context)
        {
            writer.WritePropertyName(propertyName);
            if (encodeable is null)
            {
                writer.WriteNullValue();
                return;
            }
            using JsonBufferWriter buffer = new(1024);
            using (Opc.Ua.JsonEncoder encoder = new(buffer, context))
            {
                encoder.WriteEncodeable(propertyName, encodeable, ExpandedNodeId.Null);
            }
            using JsonDocument doc = JsonDocument.Parse(buffer.WrittenMemory);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty(propertyName, out JsonElement v))
            {
                writer.WriteRawValue(v.GetRawText(), skipInputValidation: true);
            }
            else
            {
                writer.WriteNullValue();
            }
        }

        private static void WriteEndpointsProperty(
            Utf8JsonWriter writer,
            string propertyName,
            System.Collections.Generic.IReadOnlyList<EndpointDescription> endpoints,
            IServiceMessageContext context)
        {
            writer.WritePropertyName(propertyName);
            writer.WriteStartArray();
            foreach (EndpointDescription endpoint in endpoints)
            {
                using JsonBufferWriter buffer = new(512);
                using (Opc.Ua.JsonEncoder encoder = new(buffer, context))
                {
                    encoder.WriteEncodeable("Endpoint", endpoint);
                }
                using JsonDocument doc = JsonDocument.Parse(buffer.WrittenMemory);
                if (doc.RootElement.TryGetProperty("Endpoint", out JsonElement v))
                {
                    writer.WriteRawValue(v.GetRawText(), skipInputValidation: true);
                }
                else
                {
                    writer.WriteNullValue();
                }
            }
            writer.WriteEndArray();
        }

        /// <summary>
        /// Encodes a <see cref="JsonActionNetworkMessage"/>
        /// (<c>ua-action</c> envelope) per
        /// <see href="https://reference.opcfoundation.org/Core/Part14/v105/docs/7.2.5.6">
        /// Part 14 §7.2.5.6</see>.
        /// </summary>
        /// <param name="message">Source action message.</param>
        /// <param name="context">Encoder context.</param>
        /// <returns>Encoded UTF-8 frame.</returns>
        private ReadOnlyMemory<byte> EncodeAction(
            JsonActionNetworkMessage message,
            PubSubNetworkMessageContext context)
        {
            if (string.IsNullOrEmpty(message.Action))
            {
                throw new ArgumentException(
                    "JsonActionNetworkMessage requires a non-empty Action URI " +
                    "per Part 14 §7.2.5.6.",
                    nameof(message));
            }
            using JsonBufferWriter buffer = new(512);
            using (Utf8JsonWriter writer = new(buffer, new JsonWriterOptions
            {
                SkipValidation = true,
                Indented = false
            }))
            {
                writer.WriteStartObject();
                if (!string.IsNullOrEmpty(message.MessageId))
                {
                    writer.WriteString("MessageId", message.MessageId);
                }
                writer.WriteString(
                    "MessageType",
                    JsonActionNetworkMessage.MessageTypeAction);
                WritePublisherId(writer, "PublisherId", message.PublisherId);
                writer.WriteString("Action", message.Action);
                if (!string.IsNullOrEmpty(message.RequestId))
                {
                    writer.WriteString("RequestId", message.RequestId);
                }
                if (!string.IsNullOrEmpty(message.ResponseId))
                {
                    writer.WriteString("ResponseId", message.ResponseId);
                }
                writer.WritePropertyName("Parameters");
                writer.WriteStartObject();
                foreach (System.Collections.Generic.KeyValuePair<string, Variant> kvp
                    in message.Parameters)
                {
                    JsonVariantEncoder.WriteVariantProperty(
                        writer,
                        kvp.Key,
                        kvp.Value,
                        Mode,
                        context.MessageContext);
                }
                writer.WriteEndObject();
                writer.WriteEndObject();
            }
            return buffer.GetWritten();
        }

        /// <summary>
        /// Writes a <see cref="PublisherId"/> as the matching JSON
        /// scalar. Numeric publisher ids round-trip as numbers; string,
        /// Guid and Byte ids serialise as strings or numbers per
        /// Part 14 §7.2.5.3.
        /// </summary>
        /// <param name="writer">Destination writer.</param>
        /// <param name="propertyName">Property name.</param>
        /// <param name="publisherId">Publisher identifier.</param>
        private static void WritePublisherId(
            Utf8JsonWriter writer,
            string propertyName,
            PublisherId publisherId)
        {
            if (publisherId.IsNull)
            {
                return;
            }
            if (publisherId.TryGetByte(out byte b))
            {
                writer.WriteNumber(propertyName, b);
                return;
            }
            if (publisherId.TryGetUInt16(out ushort u16))
            {
                writer.WriteNumber(propertyName, u16);
                return;
            }
            if (publisherId.TryGetUInt32(out uint u32))
            {
                writer.WriteNumber(propertyName, u32);
                return;
            }
            if (publisherId.TryGetUInt64(out ulong u64))
            {
                writer.WriteNumber(propertyName, u64);
                return;
            }
            if (publisherId.TryGetGuid(out Guid g))
            {
                writer.WriteString(propertyName, g.ToString("D", CultureInfo.InvariantCulture));
                return;
            }
            if (publisherId.TryGetString(out string? s) && s is not null)
            {
                writer.WriteString(propertyName, s);
                return;
            }
            writer.WriteString(propertyName, publisherId.ToString());
        }

        /// <summary>
        /// Looks up metadata for the DataSetMessage, preferring the
        /// envelope's <see cref="PubSubNetworkMessage.MetaData"/>
        /// property and falling back to the
        /// <see cref="IDataSetMetaDataRegistry"/>.
        /// </summary>
        /// <param name="envelope">Owning envelope.</param>
        /// <param name="dsm">DataSetMessage.</param>
        /// <param name="context">Encoder context.</param>
        /// <returns>Metadata or <see langword="null"/> when unknown.</returns>
        private static DataSetMetaDataType? ResolveMetaData(
            JsonNetworkMessage envelope,
            JsonDataSetMessage dsm,
            PubSubNetworkMessageContext context)
        {
            if (envelope.MetaData is not null)
            {
                return envelope.MetaData;
            }
            IDataSetMetaDataRegistry registry = context.MetaDataRegistry;
            DataSetMetaDataKey key = new(
                envelope.PublisherId,
                envelope.WriterGroupId ?? 0,
                dsm.DataSetWriterId,
                envelope.DataSetClassId,
                dsm.MetaDataVersion.MajorVersion);
            MetaDataMatchResult match = registry.TryGet(in key, out DataSetMetaDataType? meta);
            if (match is MetaDataMatchResult.Match
                or MetaDataMatchResult.MinorVersionMismatch)
            {
                return meta;
            }
            return null;
        }
    }
}
