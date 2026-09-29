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
using Opc.Ua.PubSub.Encoding.Uadp;
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
        internal static PubSubNetworkMessage? DecodeCore(
            ReadOnlyMemory<byte> frame,
            PubSubNetworkMessageContext context)
        {
            int maxMessageSize = context.MessageContext.MaxMessageSize;
            if (maxMessageSize > 0 && frame.Length > maxMessageSize)
            {
                // Refuse oversized frames before any parsing allocation.
                context.Diagnostics.Increment(
                    PubSubDiagnosticsCounterKind.ReceivedInvalidNetworkMessages);
                return null;
            }
            JsonDocument? document;
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
                try
                {
                    JsonElement root = document.RootElement;
                    if (root.ValueKind == JsonValueKind.Array)
                    {
                        context.Diagnostics.Increment(
                            PubSubDiagnosticsCounterKind.ReceivedNetworkMessages);
                        return DecodeDataWithoutNetworkHeader(root, context);
                    }
                    if (root.ValueKind != JsonValueKind.Object)
                    {
                        context.Diagnostics.Increment(
                            PubSubDiagnosticsCounterKind.ReceivedInvalidNetworkMessages);
                        return null;
                    }
                    bool hasMessageType = root.TryGetProperty("MessageType", out JsonElement typeElement) &&
                        typeElement.ValueKind == JsonValueKind.String;
                    string messageType = hasMessageType
                        ? typeElement.GetString() ?? string.Empty
                        : string.Empty;
                    // Without a NetworkMessage header the root is a single
                    // DataSetMessage, which may carry its own PublisherId and a
                    // DataSetMessage MessageType (Part 14 §7.2.5.4.1 Table 185).
                    if (!hasMessageType || JsonDataSetMessageType.TryParse(messageType, out _))
                    {
                        if (root.TryGetProperty("MessageId", out _) ||
                            root.TryGetProperty("Messages", out _))
                        {
                            context.Diagnostics.Increment(
                                PubSubDiagnosticsCounterKind.ReceivedInvalidNetworkMessages);
                            return null;
                        }
                        context.Diagnostics.Increment(
                            PubSubDiagnosticsCounterKind.ReceivedNetworkMessages);
                        return DecodeDataWithoutNetworkHeader(root, context);
                    }
                    context.Diagnostics.Increment(
                        PubSubDiagnosticsCounterKind.ReceivedNetworkMessages);
                    return messageType switch
                    {
                        JsonNetworkMessage.MessageTypeData
                            => DecodeData(root, context),
                        JsonNetworkMessage.MessageTypeMetaData
                            => DecodeMetaData(root, context),
                        JsonDiscoveryMessage.MessageTypeApplication
                            => DecodeDiscovery(root, context, UadpDiscoveryType.ApplicationInformation),
                        JsonDiscoveryMessage.MessageTypeEndpoints
                            => DecodeDiscovery(root, context, UadpDiscoveryType.PublisherEndpoints),
                        JsonDiscoveryMessage.MessageTypeStatus
                            => DecodeDiscovery(root, context, UadpDiscoveryType.None),
                        JsonDiscoveryMessage.MessageTypeConnection
                            => DecodeDiscovery(root, context, UadpDiscoveryType.PubSubConnection),
                        JsonActionNetworkMessage.MessageTypeActionRequest
                            => DecodeAction(root, context),
                        JsonActionNetworkMessage.MessageTypeActionResponse
                            => DecodeAction(root, context),
                        JsonActionNetworkMessage.MessageTypeActionMetaData
                            => DecodeActionMetaData(root, context),
                        JsonActionNetworkMessage.MessageTypeActionResponder
                            => DecodeActionResponder(root, context),
                        _ => DecodeUnknown(context, messageType)
                    };
                }
                catch (Exception ex) when (IsMalformedInput(ex))
                {
                    // Malformed or hostile input surfaces as an invalid
                    // NetworkMessage, never as an exception.
                    context.Diagnostics.Increment(
                        PubSubDiagnosticsCounterKind.ReceivedInvalidNetworkMessages);
                    return null;
                }
            }
        }

        private static bool IsMalformedInput(Exception exception)
        {
            return exception is ServiceResultException
                or InvalidOperationException
                or FormatException
                or ArgumentException
                or JsonException;
        }

        private static JsonNetworkMessage? DecodeDataWithoutNetworkHeader(
            JsonElement root,
            PubSubNetworkMessageContext context)
        {
            var dataSetMessages = new List<PubSubDataSetMessage>();
            bool singleMessage = root.ValueKind == JsonValueKind.Object;
            if (singleMessage)
            {
                JsonDataSetMessage? dsm = DecodeOneDataSetMessage(
                    root,
                    PublisherId.Null,
                    Uuid.Empty,
                    context,
                    out bool identityConflict);
                if (identityConflict || dsm is null)
                {
                    return null;
                }
                dataSetMessages.Add(dsm);
            }
            else
            {
                _ = CheckArrayLength(root, context);
                foreach (JsonElement entry in root.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }
                    JsonDataSetMessage? dsm = DecodeOneDataSetMessage(
                        entry,
                        PublisherId.Null,
                        Uuid.Empty,
                        context,
                        out bool identityConflict);
                    if (identityConflict)
                    {
                        return null;
                    }
                    if (dsm is not null)
                    {
                        dataSetMessages.Add(dsm);
                    }
                }
            }
            context.Diagnostics.Increment(
                PubSubDiagnosticsCounterKind.ReceivedDataSetMessages,
                dataSetMessages.Count);
            if (dataSetMessages.Count == 0)
            {
                context.Diagnostics.Increment(
                    PubSubDiagnosticsCounterKind.ReceivedInvalidNetworkMessages);
                return null;
            }
            return new JsonNetworkMessage
            {
                // A header-less single DataSetMessage names its Publisher itself.
                PublisherId = singleMessage
                    ? ((JsonDataSetMessage)dataSetMessages[0]).PublisherId
                    : PublisherId.Null,
                ContentMask = singleMessage
                    ? JsonNetworkMessageContentMask.SingleDataSetMessage
                    : JsonNetworkMessageContentMask.None,
                SingleMessageMode = singleMessage,
                DataSetMessages = dataSetMessages
            };
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
            string messageId = ReadOptionalString(root, "MessageId", context);
            PublisherId envelopePublisherId = ReadPublisherId(root, context);
            Uuid envelopeDataSetClassId = ReadUuid(root, "DataSetClassId");
            string writerGroupName = ReadOptionalString(root, "WriterGroupName", context);
            ArrayOf<string> replyTo = ReadStringArray(root, "ReplyTo", context);
            bool flatLayout = !root.TryGetProperty("Messages", out JsonElement messagesElement) ||
                messagesElement.ValueKind == JsonValueKind.Object;
            var dataSetMessages = new List<PubSubDataSetMessage>();
            if (flatLayout)
            {
                JsonElement singleElement = root.TryGetProperty("Messages", out messagesElement)
                    ? messagesElement
                    : root;
                JsonDataSetMessage? dsm = DecodeOneDataSetMessage(
                    singleElement,
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
                _ = CheckArrayLength(messagesElement, context);
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
                WriterGroupName = writerGroupName,
                ReplyTo = replyTo,
                ContentMask = DeriveNetworkMask(root, flatLayout),
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
            string messageId = ReadOptionalString(root, "MessageId", context);
            PublisherId publisherId = ReadPublisherId(root, context);
            ushort writerId = ReadOptionalUInt16(root, "DataSetWriterId");
            Uuid dataSetClassId = ReadUuid(root, "DataSetClassId");
            if (!root.TryGetProperty("MetaData", out JsonElement metaElement) ||
                metaElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
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
        /// Decodes a <c>ua-discovery</c> envelope into a
        /// <see cref="JsonDiscoveryMessage"/> per
        /// <see href="https://reference.opcfoundation.org/Core/Part14/v105/docs/7.2.5.5">
        /// Part 14 §7.2.5.5</see>.
        /// </summary>
        /// <param name="root">Root element.</param>
        /// <param name="context">Decoder context.</param>
        /// <param name="forcedType">Discovery type implied by the JSON
        /// MessageType, when the spec-specific envelope is used.</param>
        /// <returns>Decoded discovery message or
        /// <see langword="null"/>.</returns>
        private static JsonDiscoveryMessage? DecodeDiscovery(
            JsonElement root,
            PubSubNetworkMessageContext context,
            UadpDiscoveryType? forcedType = null)
        {
            string messageId = ReadOptionalString(root, "MessageId", context);
            PublisherId publisherId = ReadPublisherId(root, context);
            uint typeCode = ReadOptionalUInt32(root, "DiscoveryType");
            ushort writerId = ReadOptionalUInt16(root, "DataSetWriterId");
            uint statusCode = ReadOptionalUInt32(root, "Status");
            UadpDiscoveryType discoveryType = forcedType ?? (UadpDiscoveryType)typeCode;
            if (discoveryType == UadpDiscoveryType.None &&
                root.TryGetProperty("WriterConfiguration", out _))
            {
                discoveryType = UadpDiscoveryType.DataSetWriterConfiguration;
            }
            var msg = new JsonDiscoveryMessage
            {
                MessageId = messageId,
                PublisherId = publisherId,
                DiscoveryType = discoveryType,
                DataSetWriterId = writerId,
                Status = new StatusCode(statusCode)
            };
            switch (discoveryType)
            {
                case UadpDiscoveryType.ApplicationInformation:
                    if (root.TryGetProperty("ApplicationInformation",
                            out JsonElement appElement) &&
                        appElement.ValueKind == JsonValueKind.Object)
                    {
                        msg = msg with
                        {
                            ApplicationInformation = ReadApplicationInformation(appElement, context)
                        };
                    }
                    break;
                case UadpDiscoveryType.PubSubConnection:
                    if (root.TryGetProperty("Connection", out JsonElement connElement) &&
                        connElement.ValueKind == JsonValueKind.Object)
                    {
                        msg = msg with
                        {
                            Connection = DecodeEncodeable<PubSubConnectionDataType>(connElement, context)
                        };
                    }
                    break;
                case UadpDiscoveryType.DataSetMetaData:
                    if (root.TryGetProperty("MetaData", out JsonElement metaElement) &&
                        metaElement.ValueKind == JsonValueKind.Object)
                    {
                        DataSetMetaDataType? meta = DecodeMetaDataPayload(
                            metaElement, context);
                        msg = msg with { MetaData = meta };
                    }
                    break;
                case UadpDiscoveryType.DataSetWriterConfiguration:
                    msg = msg with
                    {
                        DataSetWriterIds = ReadUInt16Array(root, "DataSetWriterIds", context)
                    };
                    if (root.TryGetProperty("WriterConfiguration",
                            out JsonElement cfgElement) &&
                        cfgElement.ValueKind == JsonValueKind.Object)
                    {
                        msg = msg with
                        {
                            WriterConfiguration = DecodeEncodeable<WriterGroupDataType>(cfgElement, context)
                        };
                    }
                    break;
                case UadpDiscoveryType.PublisherEndpoints:
                    if (root.TryGetProperty("PublisherEndpoints",
                            out JsonElement epsElement) &&
                        epsElement.ValueKind == JsonValueKind.Array)
                    {
                        msg = msg with
                        {
                            PublisherEndpoints = ReadEndpointArray(epsElement, context)
                        };
                    }
                    break;
            }
            return msg;
        }

        private static T? DecodeEncodeable<T>(
            JsonElement element,
            PubSubNetworkMessageContext context)
            where T : class, IEncodeable, new()
        {
            try
            {
                return JsonVariantDecoder.DecodeSpliced(
                    element,
                    context.MessageContext,
                    static decoder => decoder.ReadEncodeable<T>(JsonVariantDecoder.SpliceFieldName));
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

        private static EndpointDescription[] ReadEndpointArray(
            JsonElement array,
            PubSubNetworkMessageContext context)
        {
            var list = new List<EndpointDescription>(CheckArrayLength(array, context));
            foreach (JsonElement entry in array.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }
                EndpointDescription? ep =
                    DecodeEncodeable<EndpointDescription>(entry, context);
                if (ep is not null)
                {
                    list.Add(ep);
                }
            }
            return [.. list];
        }

        private static ushort[] ReadUInt16Array(
            JsonElement root,
            string name,
            PubSubNetworkMessageContext context)
        {
            if (!root.TryGetProperty(name, out JsonElement array) ||
                array.ValueKind != JsonValueKind.Array)
            {
                return [];
            }
            var list = new List<ushort>(CheckArrayLength(array, context));
            foreach (JsonElement entry in array.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.Number &&
                    entry.TryGetUInt16(out ushort v))
                {
                    list.Add(v);
                }
            }
            return [.. list];
        }

        private static UadpApplicationInformation ReadApplicationInformation(
            JsonElement element,
            PubSubNetworkMessageContext context)
        {
            string text = ReadOptionalString(element, "ApplicationName", context);
            string locale = ReadOptionalString(element, "ApplicationLocale", context);
            string appUri = ReadOptionalString(element, "ApplicationUri", context);
            string productUri = ReadOptionalString(element, "ProductUri", context);
            uint appType = ReadOptionalUInt32(element, "ApplicationType");
            return new UadpApplicationInformation
            {
                ApplicationName = new LocalizedText(locale, text),
                ApplicationUri = appUri,
                ProductUri = productUri,
                ApplicationType = (ApplicationType)appType,
                Capabilities = ReadStringList(element, "Capabilities", context),
                SupportedTransportProfiles =
                    ReadStringList(element, "SupportedTransportProfiles", context),
                SupportedSecurityPolicies =
                    ReadStringList(element, "SupportedSecurityPolicies", context)
            };
        }

        private static string[] ReadStringList(
            JsonElement root,
            string name,
            PubSubNetworkMessageContext context)
        {
            return ReadStringArray(root, name, context);
        }

        /// <summary>
        /// Decodes a <c>ua-action</c> envelope into a
        /// <see cref="JsonActionNetworkMessage"/> per
        /// <see href="https://reference.opcfoundation.org/Core/Part14/v105/docs/7.2.5.6">
        /// Part 14 §7.2.5.6</see>.
        /// </summary>
        /// <param name="root">Root element.</param>
        /// <param name="context">Decoder context.</param>
        /// <returns>Decoded action message or
        /// <see langword="null"/>.</returns>
        private static JsonActionNetworkMessage? DecodeAction(
            JsonElement root,
            PubSubNetworkMessageContext context)
        {
            Ua.JsonActionNetworkMessage? network =
                DecodeEncodeable<Ua.JsonActionNetworkMessage>(root, context);
            if (network is null || network.Messages.Count == 0)
            {
                context.Diagnostics.Increment(
                    PubSubDiagnosticsCounterKind.ReceivedInvalidNetworkMessages);
                return null;
            }
            ArrayOf<ExtensionObject> messages = DecodeActionMessageBodies(
                root,
                network.Messages,
                context);
            network.Messages = messages;
            return new JsonActionNetworkMessage
            {
                NetworkMessage = network,
                MessageId = network.MessageId ?? string.Empty,
                PublisherId = ReadPublisherId(root, context),
                ResponseAddress = network.ResponseAddress ?? string.Empty,
                CorrelationData = network.CorrelationData,
                RequestorId = network.RequestorId ?? string.Empty,
                TimeoutHint = network.TimeoutHint,
                Messages = messages
            };
        }

        private static ArrayOf<ExtensionObject> DecodeActionMessageBodies(
            JsonElement root,
            ArrayOf<ExtensionObject> fallback,
            PubSubNetworkMessageContext context)
        {
            if (!root.TryGetProperty("Messages", out JsonElement messagesElement) ||
                messagesElement.ValueKind != JsonValueKind.Array)
            {
                return fallback;
            }
            var messages = new List<ExtensionObject>();
            foreach (JsonElement entry in messagesElement.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }
                IEncodeable? body = entry.TryGetProperty("Status", out _)
                    ? DecodeEncodeable<Opc.Ua.JsonActionResponseMessage>(entry, context)
                    : DecodeEncodeable<Opc.Ua.JsonActionRequestMessage>(entry, context);
                if (body is not null)
                {
                    messages.Add(new ExtensionObject(body));
                }
            }
            return messages.Count == 0
                ? fallback
                : new ArrayOf<ExtensionObject>(messages.ToArray());
        }

        private static JsonActionNetworkMessage? DecodeActionMetaData(
            JsonElement root,
            PubSubNetworkMessageContext context)
        {
            JsonActionMetaDataMessage? metaData =
                DecodeEncodeable<JsonActionMetaDataMessage>(root, context);
            if (metaData is null)
            {
                return null;
            }
            return new JsonActionNetworkMessage
            {
                MetaDataMessage = metaData,
                MessageId = metaData.MessageId ?? string.Empty,
                PublisherId = ReadPublisherId(root, context)
            };
        }

        private static JsonActionNetworkMessage? DecodeActionResponder(
            JsonElement root,
            PubSubNetworkMessageContext context)
        {
            JsonActionResponderMessage? responder =
                DecodeEncodeable<JsonActionResponderMessage>(root, context);
            if (responder is null)
            {
                return null;
            }
            return new JsonActionNetworkMessage
            {
                ResponderMessage = responder,
                MessageId = responder.MessageId ?? string.Empty,
                PublisherId = ReadPublisherId(root, context)
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
                PublisherId nested = ParsePublisherId(entryPub, context);
                if (!nested.IsNull &&
                    !envelopePublisherId.IsNull &&
                    !PublisherIdEquals(envelopePublisherId, nested))
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
                if (nestedClass.Guid != Guid.Empty &&
                    envelopeClassId.Guid != Guid.Empty &&
                    envelopeClassId.Guid != nestedClass.Guid)
                {
                    identityConflict = true;
                    context.Diagnostics.Increment(
                        PubSubDiagnosticsCounterKind.ReceivedInvalidNetworkMessages);
                    return null;
                }
            }
            ushort writerId = ReadOptionalUInt16(entry, "DataSetWriterId");
            string writerName = ReadOptionalString(entry, "DataSetWriterName", context);
            PublisherId messagePublisherId = entry.TryGetProperty("PublisherId", out JsonElement pubElement)
                ? ParsePublisherId(pubElement, context)
                : PublisherId.Null;
            string writerGroupName = ReadOptionalString(entry, "WriterGroupName", context);
            uint sequenceNumber = ReadOptionalUInt32(entry, "SequenceNumber");
            ConfigurationVersionDataType metaVersion = ReadMetaVersion(entry, out bool hasMajorVersion);
            uint minorVersion = ReadOptionalUInt32(entry, "MinorVersion");
            if (minorVersion != 0)
            {
                metaVersion.MinorVersion = minorVersion;
            }
            DateTimeUtc timestamp = ReadOptionalTimestamp(entry, "Timestamp");
            StatusCode status = ReadOptionalStatus(entry, "Status");
            PubSubDataSetMessageType messageType = ReadMessageType(
                entry, context, out string messageTypeName);
            JsonDataSetMessageContentMask mask = DeriveMask(entry);
            bool hasPayloadWrapper = entry.TryGetProperty("Payload", out JsonElement payload);
            bool hasDataSetHeader = HasDataSetMessageHeader(entry);
            DataSetMetaDataType? metaData = ResolveMetaData(
                messagePublisherId.IsNull ? envelopePublisherId : messagePublisherId,
                envelopeClassId,
                writerId,
                metaVersion,
                hasMajorVersion,
                context);
            JsonEncodingMode detectedMode = DetectMode(entry);
            // A header-only DataSetMessage (e.g. ua-keepalive) has no
            // fields to type, so it does not need metadata.
            bool hasFields = hasPayloadWrapper || !hasDataSetHeader;
            if (hasFields &&
                !JsonVariantEncoder.WrapsInVariantEnvelope(detectedMode) &&
                metaData is null)
            {
                context.Diagnostics.Increment(
                    PubSubDiagnosticsCounterKind.ResolverErrors);
                return null;
            }
            ArrayOf<DataSetField> fields = [];
            if (hasPayloadWrapper)
            {
                if (!JsonFieldDecoder.TryDecodeFields(
                    payload,
                    metaData,
                    detectedMode,
                    context.MessageContext,
                    out ArrayOf<DataSetField> decodedFields))
                {
                    return null;
                }
                fields = decodedFields;
            }
            else if (!hasDataSetHeader)
            {
                if (!JsonFieldDecoder.TryDecodeFields(
                    entry,
                    metaData,
                    detectedMode,
                    context.MessageContext,
                    out ArrayOf<DataSetField> decodedFields))
                {
                    return null;
                }
                fields = decodedFields;
            }
            return new JsonDataSetMessage
            {
                DataSetWriterId = writerId,
                DataSetWriterName = writerName,
                PublisherId = messagePublisherId,
                WriterGroupName = writerGroupName,
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

        private static bool HasDataSetMessageHeader(JsonElement entry)
        {
            return entry.TryGetProperty("DataSetWriterId", out _) ||
                entry.TryGetProperty("DataSetWriterName", out _) ||
                entry.TryGetProperty("PublisherId", out _) ||
                entry.TryGetProperty("WriterGroupName", out _) ||
                entry.TryGetProperty("MinorVersion", out _) ||
                entry.TryGetProperty("SequenceNumber", out _) ||
                entry.TryGetProperty("MetaDataVersion", out _) ||
                entry.TryGetProperty("Timestamp", out _) ||
                entry.TryGetProperty("Status", out _) ||
                entry.TryGetProperty("MessageType", out _) ||
                entry.TryGetProperty("Payload", out _);
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
            return DecodeEncodeable<DataSetMetaDataType>(element, context);
        }

        /// <summary>
        /// Reads an optional string property.
        /// </summary>
        /// <param name="root">Source object.</param>
        /// <param name="name">Property name.</param>
        /// <param name="context">Decoder context providing the limits.</param>
        /// <returns>Property value or empty string.</returns>
        private static string ReadOptionalString(
            JsonElement root,
            string name,
            PubSubNetworkMessageContext context)
        {
            if (root.TryGetProperty(name, out JsonElement value) &&
                value.ValueKind == JsonValueKind.String)
            {
                return CheckStringLength(value.GetString() ?? string.Empty, context);
            }
            return string.Empty;
        }

        /// <summary>
        /// Enforces <see cref="IServiceMessageContext.MaxStringLength"/>
        /// on an envelope string.
        /// </summary>
        /// <param name="value">Decoded string.</param>
        /// <param name="context">Decoder context providing the limits.</param>
        /// <returns>The unchanged <paramref name="value"/>.</returns>
        /// <exception cref="ServiceResultException">The string exceeds the
        /// limit.</exception>
        private static string CheckStringLength(
            string value,
            PubSubNetworkMessageContext context)
        {
            int maxStringLength = context.MessageContext.MaxStringLength;
            if (maxStringLength > 0 && value.Length > maxStringLength)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "MaxStringLength {0} < {1}.",
                    maxStringLength,
                    value.Length);
            }
            return value;
        }

        /// <summary>
        /// Enforces <see cref="IServiceMessageContext.MaxArrayLength"/>
        /// on an envelope array or object member count.
        /// </summary>
        /// <param name="element">JSON array or object.</param>
        /// <param name="context">Decoder context providing the limits.</param>
        /// <returns>The number of entries or members.</returns>
        /// <exception cref="ServiceResultException">The element has more
        /// entries than the limit.</exception>
        internal static int CheckArrayLength(
            JsonElement element,
            PubSubNetworkMessageContext context)
        {
            return CheckArrayLength(element, context.MessageContext);
        }

        /// <inheritdoc cref="CheckArrayLength(JsonElement, PubSubNetworkMessageContext)"/>
        internal static int CheckArrayLength(
            JsonElement element,
            IServiceMessageContext context)
        {
            int maxArrayLength = context.MaxArrayLength;
            int count;
            if (element.ValueKind == JsonValueKind.Array)
            {
                count = element.GetArrayLength();
            }
            else
            {
                count = 0;
                foreach (JsonProperty _ in element.EnumerateObject())
                {
                    // Stop counting as soon as the limit is exceeded.
                    if (++count > maxArrayLength && maxArrayLength > 0)
                    {
                        break;
                    }
                }
            }
            if (maxArrayLength > 0 && count > maxArrayLength)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "MaxArrayLength {0} < {1}.",
                    maxArrayLength,
                    count);
            }
            return count;
        }

        /// <summary>
        /// Reads an optional uint16 property.
        /// </summary>
        /// <param name="root">Source object.</param>
        /// <param name="name">Property name.</param>
        /// <returns>Property value or zero.</returns>
        private static ushort ReadOptionalUInt16(JsonElement root, string name)
        {
            if (root.TryGetProperty(name, out JsonElement value) &&
                value.ValueKind == JsonValueKind.Number &&
                value.TryGetUInt16(out ushort v))
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
            if (root.TryGetProperty(name, out JsonElement value) &&
                value.ValueKind == JsonValueKind.Number &&
                value.TryGetUInt32(out uint v))
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
            if (root.TryGetProperty(name, out JsonElement value) &&
                value.ValueKind == JsonValueKind.String &&
                DateTime.TryParse(
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
                if (value.ValueKind == JsonValueKind.Number &&
                    value.TryGetUInt32(out uint v))
                {
                    return new StatusCode(v);
                }
                if (value.ValueKind == JsonValueKind.Object &&
                    value.TryGetProperty("Code", out JsonElement codeElement) &&
                    codeElement.ValueKind == JsonValueKind.Number &&
                    codeElement.TryGetUInt32(out uint codeValue))
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
        /// <param name="hasMajorVersion">On return, whether a numeric
        /// <c>MajorVersion</c> was present.</param>
        /// <returns>Configuration version (zeroed when absent).</returns>
        private static ConfigurationVersionDataType ReadMetaVersion(
            JsonElement root,
            out bool hasMajorVersion)
        {
            hasMajorVersion = false;
            if (!root.TryGetProperty("MetaDataVersion", out JsonElement value) ||
                value.ValueKind != JsonValueKind.Object)
            {
                return new ConfigurationVersionDataType();
            }
            uint major = 0;
            uint minor = 0;
            if (value.TryGetProperty("MajorVersion", out JsonElement majorElement) &&
                majorElement.ValueKind == JsonValueKind.Number)
            {
                hasMajorVersion = majorElement.TryGetUInt32(out major);
            }
            if (value.TryGetProperty("MinorVersion", out JsonElement minorElement) &&
                minorElement.ValueKind == JsonValueKind.Number)
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
        /// <param name="context">Decoder context providing the limits.</param>
        /// <param name="wireName">On return, the wire form when one
        /// was supplied; otherwise empty.</param>
        /// <returns>Resolved enum value.</returns>
        private static PubSubDataSetMessageType ReadMessageType(
            JsonElement root,
            PubSubNetworkMessageContext context,
            out string wireName)
        {
            wireName = string.Empty;
            if (root.TryGetProperty("MessageType", out JsonElement value) &&
                value.ValueKind == JsonValueKind.String)
            {
                string raw = CheckStringLength(value.GetString() ?? string.Empty, context);
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
            if (root.TryGetProperty("DataSetWriterName", out _))
            {
                mask |= JsonDataSetMessageContentMask.DataSetWriterName;
            }
            if (root.TryGetProperty("PublisherId", out _))
            {
                mask |= JsonDataSetMessageContentMask.PublisherId;
            }
            if (root.TryGetProperty("WriterGroupName", out _))
            {
                mask |= JsonDataSetMessageContentMask.WriterGroupName;
            }
            if (root.TryGetProperty("MinorVersion", out _))
            {
                mask |= JsonDataSetMessageContentMask.MinorVersion;
            }
            return mask;
        }

        private static JsonNetworkMessageContentMask DeriveNetworkMask(
            JsonElement root,
            bool singleMessage)
        {
            JsonNetworkMessageContentMask mask =
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.DataSetMessageHeader;
            if (singleMessage)
            {
                mask |= JsonNetworkMessageContentMask.SingleDataSetMessage;
            }
            if (root.TryGetProperty("PublisherId", out _))
            {
                mask |= JsonNetworkMessageContentMask.PublisherId;
            }
            if (root.TryGetProperty("DataSetClassId", out _))
            {
                mask |= JsonNetworkMessageContentMask.DataSetClassId;
            }
            if (root.TryGetProperty("ReplyTo", out _))
            {
                mask |= JsonNetworkMessageContentMask.ReplyTo;
            }
            if (root.TryGetProperty("WriterGroupName", out _))
            {
                mask |= JsonNetworkMessageContentMask.WriterGroupName;
            }
            return mask;
        }

        /// <summary>
        /// Detects the encoding mode of the supplied DataSetMessage from
        /// the entries in its <c>Payload</c>.
        /// </summary>
        /// <param name="root">Source DataSetMessage object.</param>
        /// <returns>
        /// <see cref="JsonEncodingMode.Verbose"/> when any payload entry
        /// is a Part 6 §5.4.2.17 <c>{ "UaType", "Value" }</c> Variant
        /// (top-level Variants with a concrete FieldMetaData type are
        /// collapsed to bare values, Part 14 §7.2.5.4.2) or the first
        /// entry is a DataValue object;
        /// <see cref="JsonEncodingMode.RawData"/> when bodies are bare.
        /// </returns>
        private static JsonEncodingMode DetectMode(JsonElement root)
        {
            JsonElement payload = root;
            if (root.TryGetProperty("Payload", out JsonElement wrappedPayload))
            {
                payload = wrappedPayload;
            }
            if (payload.ValueKind != JsonValueKind.Object)
            {
                return JsonEncodingMode.Verbose;
            }
            JsonEncodingMode? firstMode = null;
            foreach (JsonProperty member in payload.EnumerateObject())
            {
                JsonElement value = member.Value;
                if (JsonVariantDecoder.IsVariantEnvelope(value))
                {
                    return JsonEncodingMode.Verbose;
                }
                firstMode ??= value.ValueKind == JsonValueKind.Object &&
                    value.TryGetProperty("Value", out _)
                    ? JsonEncodingMode.Verbose
                    : JsonEncodingMode.RawData;
            }
            return firstMode ?? JsonEncodingMode.Verbose;
        }

        /// <summary>
        /// Resolves metadata for the supplied identity tuple via the
        /// <see cref="PubSubNetworkMessageContext.MetaDataRegistry"/>.
        /// </summary>
        /// <param name="publisherId">PublisherId.</param>
        /// <param name="dataSetClassId">DataSetClassId.</param>
        /// <param name="writerId">DataSetWriterId.</param>
        /// <param name="metaVersion">Configuration version.</param>
        /// <param name="hasMajorVersion">Whether the DataSetMessage carried a
        /// MajorVersion. Without one (only <c>MinorVersion</c>, or no version at
        /// all) the metadata registered for the identity is used.</param>
        /// <param name="context">Decoder context.</param>
        /// <returns>Resolved metadata or
        /// <see langword="null"/>.</returns>
        private static DataSetMetaDataType? ResolveMetaData(
            PublisherId publisherId,
            Uuid dataSetClassId,
            ushort writerId,
            ConfigurationVersionDataType metaVersion,
            bool hasMajorVersion,
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
            if (IsUsableMatch(result, hasMajorVersion))
            {
                return metaData;
            }
            if (TryGetUIntegerPublisherIdString(publisherId, out string? numericText) &&
                numericText is not null)
            {
                foreach (PublisherId numericPublisherId in EnumerateNumericPublisherIds(numericText))
                {
                    key = new DataSetMetaDataKey(
                        numericPublisherId,
                        0,
                        writerId,
                        dataSetClassId,
                        metaVersion?.MajorVersion ?? 0);
                    result = context.MetaDataRegistry.TryGet(in key, out metaData);
                    if (IsUsableMatch(result, hasMajorVersion))
                    {
                        return metaData;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Whether a registry lookup result can be used to decode the
        /// DataSetMessage. MetaDataVersion and MinorVersion are both
        /// optional (Part 14 §7.2.5.4.1 Table 185); without a MajorVersion
        /// any registered description for the identity applies.
        /// </summary>
        private static bool IsUsableMatch(MetaDataMatchResult result, bool hasMajorVersion)
        {
            return result is MetaDataMatchResult.Match or MetaDataMatchResult.MinorVersionMismatch ||
                (!hasMajorVersion && result == MetaDataMatchResult.MajorVersionMismatch);
        }

        /// <summary>
        /// Reads the envelope <c>PublisherId</c> property and converts
        /// it to a <see cref="PublisherId"/>.
        /// </summary>
        /// <param name="root">Source object.</param>
        /// <param name="context">Decoder context providing the limits.</param>
        /// <returns>Decoded publisher id.</returns>
        private static PublisherId ReadPublisherId(
            JsonElement root,
            PubSubNetworkMessageContext context)
        {
            if (!root.TryGetProperty("PublisherId", out JsonElement value))
            {
                return PublisherId.Null;
            }
            return ParsePublisherId(value, context);
        }

        /// <summary>
        /// Parses a single <see cref="JsonElement"/> as a
        /// <see cref="PublisherId"/>.
        /// </summary>
        /// <param name="value">Source element.</param>
        /// <param name="context">Decoder context providing the limits.</param>
        /// <returns>Decoded publisher id.</returns>
        private static PublisherId ParsePublisherId(
            JsonElement value,
            PubSubNetworkMessageContext context)
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
                    string raw = CheckStringLength(value.GetString() ?? string.Empty, context);
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
            if (value.ValueKind == JsonValueKind.String &&
                Guid.TryParse(value.GetString(), out Guid g))
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
        /// <param name="context">Decoder context providing the limits.</param>
        /// <returns>Decoded array (never null).</returns>
        private static string[] ReadStringArray(
            JsonElement root,
            string name,
            PubSubNetworkMessageContext context)
        {
            if (!root.TryGetProperty(name, out JsonElement value) ||
                value.ValueKind != JsonValueKind.Array)
            {
                return [];
            }
            var list = new List<string>(CheckArrayLength(value, context));
            foreach (JsonElement entry in value.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.String)
                {
                    list.Add(CheckStringLength(entry.GetString() ?? string.Empty, context));
                }
            }
            return [.. list];
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
            if (TryGetUIntegerPublisherIdString(left, out string? leftNumber) &&
                TryGetUIntegerPublisherIdString(right, out string? rightNumber))
            {
                return string.Equals(leftNumber, rightNumber, StringComparison.Ordinal);
            }
            return left.Equals(right);
        }

        private static IEnumerable<PublisherId> EnumerateNumericPublisherIds(string value)
        {
            if (byte.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out byte b))
            {
                yield return PublisherId.FromByte(b);
            }
            if (ushort.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out ushort u16))
            {
                yield return PublisherId.FromUInt16(u16);
            }
            if (uint.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out uint u32))
            {
                yield return PublisherId.FromUInt32(u32);
            }
            if (ulong.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out ulong u64))
            {
                yield return PublisherId.FromUInt64(u64);
            }
        }

        private static bool TryGetUIntegerPublisherIdString(
            PublisherId publisherId,
            out string? value)
        {
            if (publisherId.TryGetByte(out byte b))
            {
                value = b.ToString(CultureInfo.InvariantCulture);
                return true;
            }
            if (publisherId.TryGetUInt16(out ushort u16))
            {
                value = u16.ToString(CultureInfo.InvariantCulture);
                return true;
            }
            if (publisherId.TryGetUInt32(out uint u32))
            {
                value = u32.ToString(CultureInfo.InvariantCulture);
                return true;
            }
            if (publisherId.TryGetUInt64(out ulong u64))
            {
                value = u64.ToString(CultureInfo.InvariantCulture);
                return true;
            }
            if (publisherId.TryGetString(out string? text) &&
                IsUIntegerPublisherIdString(text))
            {
                value = text;
                return true;
            }
            value = null;
            return false;
        }

        private static bool IsUIntegerPublisherIdString(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }
            if (value.Length > 1 && value[0] == '0')
            {
                return false;
            }
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] is < '0' or > '9')
                {
                    return false;
                }
            }
            return ulong.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out _);
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
