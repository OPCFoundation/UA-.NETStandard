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
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.MetaData;

namespace Opc.Ua.PubSub.Encoding.Json
{
    /// <summary>
    /// <see cref="INetworkMessageDecoder"/> implementation that parses
    /// JSON NetworkMessage frames (<c>ua-data</c> and
    /// <c>ua-metadata</c>) into <see cref="JsonNetworkMessage"/> /
    /// <see cref="JsonMetaDataMessage"/> records.
    /// </summary>
    /// <remarks>
    /// Implements the decoder side of
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.5">
    /// Part 14 §7.2.5</see> JSON mapping. The decoder is intentionally
    /// tolerant: malformed JSON, missing or unknown <c>MessageType</c>,
    /// and identity conflicts return <see langword="null"/> and update
    /// the supplied <see cref="IPubSubDiagnostics"/> counters instead
    /// of throwing.
    /// </remarks>
    public sealed class JsonDecoder : INetworkMessageDecoder
    {
        /// <inheritdoc/>
        public string TransportProfileUri => Profiles.PubSubMqttJsonTransport;

        /// <inheritdoc/>
        public ValueTask<PubSubNetworkMessage?> TryDecodeAsync(
            ReadOnlyMemory<byte> frame,
            PubSubNetworkMessageContext context,
            CancellationToken cancellationToken = default)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<PubSubNetworkMessage?>(DecodeCore(frame, context));
        }

        /// <summary>
        /// Core synchronous decode path.
        /// </summary>
        /// <param name="frame">Raw frame.</param>
        /// <param name="context">Decoder context.</param>
        /// <returns>Decoded message or <see langword="null"/>.</returns>
        private static PubSubNetworkMessage? DecodeCore(
            ReadOnlyMemory<byte> frame,
            PubSubNetworkMessageContext context)
        {
            JsonDocument? document = null;
            try
            {
                document = JsonDocument.Parse(frame);
            }
            catch (JsonException)
            {
                context.Diagnostics.Increment(
                    PubSubDiagnosticsCounterKind.ReceivedInvalidNetworkMessages);
                return null;
            }
            using (document)
            {
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    context.Diagnostics.Increment(
                        PubSubDiagnosticsCounterKind.ReceivedInvalidNetworkMessages);
                    return null;
                }
                if (!root.TryGetProperty("MessageType", out JsonElement typeElement)
                    || typeElement.ValueKind != JsonValueKind.String)
                {
                    context.Diagnostics.Increment(
                        PubSubDiagnosticsCounterKind.ReceivedInvalidNetworkMessages);
                    return null;
                }
                string messageType = typeElement.GetString() ?? string.Empty;
                context.Diagnostics.Increment(
                    PubSubDiagnosticsCounterKind.ReceivedNetworkMessages);
                return messageType switch
                {
                    JsonNetworkMessage.MessageTypeData
                        => DecodeData(root, context),
                    JsonNetworkMessage.MessageTypeMetaData
                        => DecodeMetaData(root, context),
                    _ => DecodeUnknown(context, messageType)
                };
            }
        }

        /// <summary>
        /// Decodes a <c>ua-data</c> envelope into a
        /// <see cref="JsonNetworkMessage"/>.
        /// </summary>
        /// <param name="root">Root element.</param>
        /// <param name="context">Decoder context.</param>
        /// <returns>Decoded network message or
        /// <see langword="null"/>.</returns>
        private static JsonNetworkMessage? DecodeData(
            JsonElement root,
            PubSubNetworkMessageContext context)
        {
            string messageId = ReadOptionalString(root, "MessageId");
            PublisherId envelopePublisherId = ReadPublisherId(root);
            Uuid envelopeDataSetClassId = ReadUuid(root, "DataSetClassId");
            IReadOnlyList<string> replyTo = ReadStringArray(root, "ReplyTo");
            bool flatLayout = !root.TryGetProperty("Messages", out JsonElement messagesElement);
            var dataSetMessages = new List<PubSubDataSetMessage>();
            if (flatLayout)
            {
                JsonDataSetMessage? dsm = DecodeOneDataSetMessage(
                    root,
                    envelopePublisherId,
                    envelopeDataSetClassId,
                    context,
                    out bool identityConflict);
                if (identityConflict)
                {
                    return null;
                }
                if (dsm is not null)
                {
                    dataSetMessages.Add(dsm);
                    context.Diagnostics.Increment(
                        PubSubDiagnosticsCounterKind.ReceivedDataSetMessages);
                }
            }
            else
            {
                if (messagesElement.ValueKind != JsonValueKind.Array)
                {
                    context.Diagnostics.Increment(
                        PubSubDiagnosticsCounterKind.ReceivedInvalidNetworkMessages);
                    return null;
                }
                foreach (JsonElement entry in messagesElement.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.Object)
                    {
                        context.Diagnostics.Increment(
                            PubSubDiagnosticsCounterKind.FailedDataSetMessages);
                        continue;
                    }
                    JsonDataSetMessage? dsm = DecodeOneDataSetMessage(
                        entry,
                        envelopePublisherId,
                        envelopeDataSetClassId,
                        context,
                        out bool identityConflict);
                    if (identityConflict)
                    {
                        return null;
                    }
                    if (dsm is null)
                    {
                        context.Diagnostics.Increment(
                            PubSubDiagnosticsCounterKind.FailedDataSetMessages);
                        continue;
                    }
                    dataSetMessages.Add(dsm);
                    context.Diagnostics.Increment(
                        PubSubDiagnosticsCounterKind.ReceivedDataSetMessages);
                }
            }
            return new JsonNetworkMessage
            {
                MessageId = messageId,
                MessageType = JsonNetworkMessage.MessageTypeData,
                PublisherId = envelopePublisherId,
                DataSetClassId = envelopeDataSetClassId,
                ReplyTo = replyTo,
                SingleMessageMode = flatLayout,
                DataSetMessages = dataSetMessages
            };
        }

        /// <summary>
        /// Decodes a <c>ua-metadata</c> envelope into a
        /// <see cref="JsonMetaDataMessage"/>.
        /// </summary>
        /// <param name="root">Root element.</param>
        /// <param name="context">Decoder context.</param>
        /// <returns>Decoded metadata message or
        /// <see langword="null"/>.</returns>
        private static JsonMetaDataMessage? DecodeMetaData(
            JsonElement root,
            PubSubNetworkMessageContext context)
        {
            string messageId = ReadOptionalString(root, "MessageId");
            PublisherId publisherId = ReadPublisherId(root);
            ushort writerId = ReadOptionalUInt16(root, "DataSetWriterId");
            Uuid dataSetClassId = ReadUuid(root, "DataSetClassId");
            if (!root.TryGetProperty("MetaData", out JsonElement metaElement)
                || metaElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                context.Diagnostics.Increment(
                    PubSubDiagnosticsCounterKind.ReceivedInvalidNetworkMessages);
                return null;
            }
            DataSetMetaDataType? metaData = DecodeMetaDataPayload(metaElement, context);
            if (metaData is null)
            {
                return null;
            }
            return new JsonMetaDataMessage
            {
                MessageId = messageId,
                PublisherId = publisherId,
                DataSetWriterId = writerId,
                DataSetClassId = dataSetClassId,
                MetaDataPayload = metaData,
                MetaData = metaData
            };
        }

        /// <summary>
        /// Decodes one DataSetMessage object into a
        /// <see cref="JsonDataSetMessage"/>.
        /// </summary>
        /// <param name="entry">DataSetMessage object.</param>
        /// <param name="envelopePublisherId">PublisherId from the
        /// envelope.</param>
        /// <param name="envelopeClassId">DataSetClassId from the
        /// envelope.</param>
        /// <param name="context">Decoder context.</param>
        /// <param name="identityConflict">
        /// On return <see langword="true"/> when a DataSetMessage
        /// declares a PublisherId / DataSetClassId that contradicts the
        /// envelope (per research §3 supplement).
        /// </param>
        /// <returns>Decoded message or <see langword="null"/>.</returns>
        private static JsonDataSetMessage? DecodeOneDataSetMessage(
            JsonElement entry,
            PublisherId envelopePublisherId,
            Uuid envelopeClassId,
            PubSubNetworkMessageContext context,
            out bool identityConflict)
        {
            identityConflict = false;
            if (entry.TryGetProperty("PublisherId", out JsonElement entryPub))
            {
                PublisherId nested = ParsePublisherId(entryPub);
                if (!nested.IsNull
                    && !envelopePublisherId.IsNull
                    && !PublisherIdEquals(envelopePublisherId, nested))
                {
                    identityConflict = true;
                    context.Diagnostics.Increment(
                        PubSubDiagnosticsCounterKind.ReceivedInvalidNetworkMessages);
                    return null;
                }
            }
            if (entry.TryGetProperty("DataSetClassId", out JsonElement entryClass))
            {
                Uuid nestedClass = ParseUuid(entryClass);
                if (nestedClass.Guid != Guid.Empty
                    && envelopeClassId.Guid != Guid.Empty
                    && envelopeClassId.Guid != nestedClass.Guid)
                {
                    identityConflict = true;
                    context.Diagnostics.Increment(
                        PubSubDiagnosticsCounterKind.ReceivedInvalidNetworkMessages);
                    return null;
                }
            }
            ushort writerId = ReadOptionalUInt16(entry, "DataSetWriterId");
            uint sequenceNumber = ReadOptionalUInt32(entry, "SequenceNumber");
            ConfigurationVersionDataType metaVersion = ReadMetaVersion(entry);
            DateTimeUtc timestamp = ReadOptionalTimestamp(entry, "Timestamp");
            StatusCode status = ReadOptionalStatus(entry, "Status");
            PubSubDataSetMessageType messageType = ReadMessageType(
                entry, out string messageTypeName);
            JsonDataSetMessageContentMask mask = DeriveMask(entry);
            DataSetMetaDataType? metaData = ResolveMetaData(
                envelopePublisherId,
                envelopeClassId,
                writerId,
                metaVersion,
                context);
            JsonEncodingMode detectedMode = DetectMode(entry);
            if (!JsonVariantEncoder.IsReversible(detectedMode) && metaData is null)
            {
                context.Diagnostics.Increment(
                    PubSubDiagnosticsCounterKind.ResolverErrors);
                return null;
            }
            IReadOnlyList<DataSetField> fields = [];
            if (entry.TryGetProperty("Payload", out JsonElement payload))
            {
                fields = JsonFieldDecoder.DecodeFields(
                    payload,
                    metaData,
                    detectedMode,
                    context.MessageContext);
            }
            return new JsonDataSetMessage
            {
                DataSetWriterId = writerId,
                SequenceNumber = sequenceNumber,
                MetaDataVersion = metaVersion,
                Timestamp = timestamp,
                Status = status,
                MessageType = messageType,
                MessageTypeName = messageTypeName,
                ContentMask = mask,
                Fields = fields
            };
        }

        /// <summary>
        /// Decodes a <see cref="DataSetMetaDataType"/> from a
        /// <see cref="JsonElement"/> using the Stack JSON decoder.
        /// </summary>
        /// <param name="element">Source element.</param>
        /// <param name="context">Decoder context.</param>
        /// <returns>Decoded metadata or <see langword="null"/>.</returns>
        private static DataSetMetaDataType? DecodeMetaDataPayload(
            JsonElement element,
            PubSubNetworkMessageContext context)
        {
            try
            {
                string wrapped = string.Concat(
                    "{\"MetaData\":",
                    element.GetRawText(),
                    "}");
                using Opc.Ua.JsonDecoder decoder = new(wrapped, context.MessageContext);
                return decoder.ReadEncodeable<DataSetMetaDataType>("MetaData");
            }
            catch (ServiceResultException)
            {
                context.Diagnostics.Increment(
                    PubSubDiagnosticsCounterKind.ReceivedInvalidNetworkMessages);
                return null;
            }
            catch (JsonException)
            {
                context.Diagnostics.Increment(
                    PubSubDiagnosticsCounterKind.ReceivedInvalidNetworkMessages);
                return null;
            }
        }

        /// <summary>
        /// Reads an optional string property.
        /// </summary>
        /// <param name="root">Source object.</param>
        /// <param name="name">Property name.</param>
        /// <returns>Property value or empty string.</returns>
        private static string ReadOptionalString(JsonElement root, string name)
        {
            if (root.TryGetProperty(name, out JsonElement value)
                && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? string.Empty;
            }
            return string.Empty;
        }

        /// <summary>
        /// Reads an optional uint16 property.
        /// </summary>
        /// <param name="root">Source object.</param>
        /// <param name="name">Property name.</param>
        /// <returns>Property value or zero.</returns>
        private static ushort ReadOptionalUInt16(JsonElement root, string name)
        {
            if (root.TryGetProperty(name, out JsonElement value)
                && value.ValueKind == JsonValueKind.Number
                && value.TryGetUInt16(out ushort v))
            {
                return v;
            }
            return 0;
        }

        /// <summary>
        /// Reads an optional uint32 property.
        /// </summary>
        /// <param name="root">Source object.</param>
        /// <param name="name">Property name.</param>
        /// <returns>Property value or zero.</returns>
        private static uint ReadOptionalUInt32(JsonElement root, string name)
        {
            if (root.TryGetProperty(name, out JsonElement value)
                && value.ValueKind == JsonValueKind.Number
                && value.TryGetUInt32(out uint v))
            {
                return v;
            }
            return 0;
        }

        /// <summary>
        /// Reads an optional timestamp property in ISO 8601 format.
        /// </summary>
        /// <param name="root">Source object.</param>
        /// <param name="name">Property name.</param>
        /// <returns>Decoded timestamp or
        /// <see cref="DateTimeUtc.MinValue"/>.</returns>
        private static DateTimeUtc ReadOptionalTimestamp(JsonElement root, string name)
        {
            if (root.TryGetProperty(name, out JsonElement value)
                && value.ValueKind == JsonValueKind.String
                && DateTime.TryParse(
                    value.GetString(),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out DateTime parsed))
            {
                return (DateTimeUtc)parsed.ToUniversalTime();
            }
            return DateTimeUtc.MinValue;
        }

        /// <summary>
        /// Reads an optional <see cref="StatusCode"/> property.
        /// </summary>
        /// <param name="root">Source object.</param>
        /// <param name="name">Property name.</param>
        /// <returns>Status code or zero.</returns>
        private static StatusCode ReadOptionalStatus(JsonElement root, string name)
        {
            if (root.TryGetProperty(name, out JsonElement value))
            {
                if (value.ValueKind == JsonValueKind.Number
                    && value.TryGetUInt32(out uint v))
                {
                    return new StatusCode(v);
                }
                if (value.ValueKind == JsonValueKind.Object
                    && value.TryGetProperty("Code", out JsonElement codeElement)
                    && codeElement.TryGetUInt32(out uint codeValue))
                {
                    return new StatusCode(codeValue);
                }
            }
            return StatusCodes.Good;
        }

        /// <summary>
        /// Reads the <c>MetaDataVersion</c> property.
        /// </summary>
        /// <param name="root">Source object.</param>
        /// <returns>Configuration version (zeroed when absent).</returns>
        private static ConfigurationVersionDataType ReadMetaVersion(JsonElement root)
        {
            if (!root.TryGetProperty("MetaDataVersion", out JsonElement value)
                || value.ValueKind != JsonValueKind.Object)
            {
                return new ConfigurationVersionDataType();
            }
            uint major = 0;
            uint minor = 0;
            if (value.TryGetProperty("MajorVersion", out JsonElement majorElement))
            {
                majorElement.TryGetUInt32(out major);
            }
            if (value.TryGetProperty("MinorVersion", out JsonElement minorElement))
            {
                minorElement.TryGetUInt32(out minor);
            }
            return new ConfigurationVersionDataType
            {
                MajorVersion = major,
                MinorVersion = minor
            };
        }

        /// <summary>
        /// Reads the <c>MessageType</c> property and converts it to a
        /// <see cref="PubSubDataSetMessageType"/>.
        /// </summary>
        /// <param name="root">Source object.</param>
        /// <param name="wireName">On return, the wire form when one
        /// was supplied; otherwise empty.</param>
        /// <returns>Resolved enum value.</returns>
        private static PubSubDataSetMessageType ReadMessageType(
            JsonElement root,
            out string wireName)
        {
            wireName = string.Empty;
            if (root.TryGetProperty("MessageType", out JsonElement value)
                && value.ValueKind == JsonValueKind.String)
            {
                string raw = value.GetString() ?? string.Empty;
                wireName = raw;
                if (JsonDataSetMessageType.TryParse(raw, out PubSubDataSetMessageType parsed))
                {
                    return parsed;
                }
            }
            return PubSubDataSetMessageType.KeyFrame;
        }

        /// <summary>
        /// Derives the
        /// <see cref="JsonDataSetMessageContentMask"/> from the set of
        /// JSON properties actually present on the DataSetMessage.
        /// </summary>
        /// <param name="root">Source DataSetMessage object.</param>
        /// <returns>Reconstructed content mask.</returns>
        private static JsonDataSetMessageContentMask DeriveMask(JsonElement root)
        {
            JsonDataSetMessageContentMask mask = 0;
            if (root.TryGetProperty("DataSetWriterId", out _))
            {
                mask |= JsonDataSetMessageContentMask.DataSetWriterId;
            }
            if (root.TryGetProperty("SequenceNumber", out _))
            {
                mask |= JsonDataSetMessageContentMask.SequenceNumber;
            }
            if (root.TryGetProperty("MetaDataVersion", out _))
            {
                mask |= JsonDataSetMessageContentMask.MetaDataVersion;
            }
            if (root.TryGetProperty("Timestamp", out _))
            {
                mask |= JsonDataSetMessageContentMask.Timestamp;
            }
            if (root.TryGetProperty("Status", out _))
            {
                mask |= JsonDataSetMessageContentMask.Status;
            }
            if (root.TryGetProperty("MessageType", out _))
            {
                mask |= JsonDataSetMessageContentMask.MessageType;
            }
            return mask;
        }

        /// <summary>
        /// Detects the encoding mode of the supplied DataSetMessage by
        /// inspecting the first non-trivial entry in its <c>Payload</c>.
        /// </summary>
        /// <param name="root">Source DataSetMessage object.</param>
        /// <returns>Detected mode (Reversible by default).</returns>
        private static JsonEncodingMode DetectMode(JsonElement root)
        {
            if (!root.TryGetProperty("Payload", out JsonElement payload)
                || payload.ValueKind != JsonValueKind.Object)
            {
                return JsonEncodingMode.Reversible;
            }
            foreach (JsonProperty member in payload.EnumerateObject())
            {
                JsonElement value = member.Value;
                if (value.ValueKind != JsonValueKind.Object)
                {
                    return JsonEncodingMode.NonReversible;
                }
                if (value.TryGetProperty("Type", out _)
                    && value.TryGetProperty("Body", out _))
                {
                    return JsonEncodingMode.Reversible;
                }
                if (value.TryGetProperty("Value", out _))
                {
                    return JsonEncodingMode.Reversible;
                }
                return JsonEncodingMode.NonReversible;
            }
            return JsonEncodingMode.Reversible;
        }

        /// <summary>
        /// Resolves metadata for the supplied identity tuple via the
        /// <see cref="PubSubNetworkMessageContext.MetaDataRegistry"/>.
        /// </summary>
        /// <param name="publisherId">PublisherId.</param>
        /// <param name="dataSetClassId">DataSetClassId.</param>
        /// <param name="writerId">DataSetWriterId.</param>
        /// <param name="metaVersion">Configuration version.</param>
        /// <param name="context">Decoder context.</param>
        /// <returns>Resolved metadata or
        /// <see langword="null"/>.</returns>
        private static DataSetMetaDataType? ResolveMetaData(
            PublisherId publisherId,
            Uuid dataSetClassId,
            ushort writerId,
            ConfigurationVersionDataType metaVersion,
            PubSubNetworkMessageContext context)
        {
            DataSetMetaDataKey key = new(
                publisherId,
                0,
                writerId,
                dataSetClassId,
                metaVersion?.MajorVersion ?? 0);
            MetaDataMatchResult result = context.MetaDataRegistry.TryGet(
                in key,
                out DataSetMetaDataType? metaData);
            if (result is MetaDataMatchResult.Match
                or MetaDataMatchResult.MinorVersionMismatch)
            {
                return metaData;
            }
            return null;
        }

        /// <summary>
        /// Reads the envelope <c>PublisherId</c> property and converts
        /// it to a <see cref="PublisherId"/>.
        /// </summary>
        /// <param name="root">Source object.</param>
        /// <returns>Decoded publisher id.</returns>
        private static PublisherId ReadPublisherId(JsonElement root)
        {
            if (!root.TryGetProperty("PublisherId", out JsonElement value))
            {
                return PublisherId.Null;
            }
            return ParsePublisherId(value);
        }

        /// <summary>
        /// Parses a single <see cref="JsonElement"/> as a
        /// <see cref="PublisherId"/>.
        /// </summary>
        /// <param name="value">Source element.</param>
        /// <returns>Decoded publisher id.</returns>
        private static PublisherId ParsePublisherId(JsonElement value)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.Number:
                    if (value.TryGetByte(out byte b))
                    {
                        return PublisherId.From(new Variant(b));
                    }
                    if (value.TryGetUInt16(out ushort u16))
                    {
                        return PublisherId.From(new Variant(u16));
                    }
                    if (value.TryGetUInt32(out uint u32))
                    {
                        return PublisherId.From(new Variant(u32));
                    }
                    if (value.TryGetUInt64(out ulong u64))
                    {
                        return PublisherId.From(new Variant(u64));
                    }
                    return PublisherId.Null;
                case JsonValueKind.String:
                    string raw = value.GetString() ?? string.Empty;
                    if (Guid.TryParseExact(raw, "D", out Guid g))
                    {
                        return PublisherId.From(new Variant(new Uuid(g)));
                    }
                    return PublisherId.From(new Variant(raw));
                default:
                    return PublisherId.Null;
            }
        }

        /// <summary>
        /// Reads an optional Uuid (string with Guid format).
        /// </summary>
        /// <param name="root">Source object.</param>
        /// <param name="name">Property name.</param>
        /// <returns>Parsed value or default Uuid.</returns>
        private static Uuid ReadUuid(JsonElement root, string name)
        {
            if (root.TryGetProperty(name, out JsonElement value))
            {
                return ParseUuid(value);
            }
            return new Uuid();
        }

        /// <summary>
        /// Parses a single <see cref="JsonElement"/> as a
        /// <see cref="Uuid"/>.
        /// </summary>
        /// <param name="value">Source element.</param>
        /// <returns>Parsed value or default Uuid.</returns>
        private static Uuid ParseUuid(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.String
                && Guid.TryParse(value.GetString(), out Guid g))
            {
                return new Uuid(g);
            }
            return new Uuid();
        }

        /// <summary>
        /// Reads an optional string array.
        /// </summary>
        /// <param name="root">Source object.</param>
        /// <param name="name">Property name.</param>
        /// <returns>Decoded array (never null).</returns>
        private static string[] ReadStringArray(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out JsonElement value)
                || value.ValueKind != JsonValueKind.Array)
            {
                return [];
            }
            var list = new List<string>(value.GetArrayLength());
            foreach (JsonElement entry in value.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.String)
                {
                    list.Add(entry.GetString() ?? string.Empty);
                }
            }
            return list.ToArray();
        }

        /// <summary>
        /// Compares two <see cref="PublisherId"/> values using their
        /// underlying variant payloads.
        /// </summary>
        /// <param name="left">Left side.</param>
        /// <param name="right">Right side.</param>
        /// <returns><see langword="true"/> when both sides represent
        /// the same publisher id.</returns>
        private static bool PublisherIdEquals(PublisherId left, PublisherId right)
        {
            if (left.IsNull && right.IsNull)
            {
                return true;
            }
            if (left.IsNull || right.IsNull)
            {
                return false;
            }
            return left.Equals(right);
        }

        /// <summary>
        /// Handles an unsupported <c>MessageType</c> value by
        /// incrementing diagnostics and returning
        /// <see langword="null"/>.
        /// </summary>
        /// <param name="context">Decoder context.</param>
        /// <param name="messageType">Observed message type.</param>
        /// <returns>Always <see langword="null"/>.</returns>
        private static PubSubNetworkMessage? DecodeUnknown(
            PubSubNetworkMessageContext context,
            string messageType)
        {
            _ = messageType;
            context.Diagnostics.Increment(
                PubSubDiagnosticsCounterKind.ReceivedInvalidNetworkMessages);
            return null;
        }
    }
}
