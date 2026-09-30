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
                            => DecodeApplicationDiscovery(root, context),
                        JsonDiscoveryMessage.MessageTypeEndpoints
                            => DecodeEndpointsDiscovery(root, context),
                        JsonDiscoveryMessage.MessageTypeStatus
                            => DecodeStatusDiscovery(root, context),
                        JsonDiscoveryMessage.MessageTypeConnection
                            => DecodeConnectionDiscovery(root, context),
                        JsonActionNetworkMessage.MessageTypeActionRequest
                            => DecodeAction(root, context, isResponse: false),
                        JsonActionNetworkMessage.MessageTypeActionResponse
                            => DecodeAction(root, context, isResponse: true),
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
        /// Decodes a <c>ua-metadata</c> envelope (Part 14 §7.2.5.5.2
        /// Table 188) into a <see cref="JsonMetaDataMessage"/>.
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
            string writerGroupName = ReadOptionalString(root, "WriterGroupName", context);
            string writerName = ReadOptionalString(root, "DataSetWriterName", context);
            DateTimeUtc timestamp = ReadOptionalTimestamp(root, "Timestamp");
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
                WriterGroupName = writerGroupName,
                DataSetWriterName = writerName,
                Timestamp = timestamp,
                MetaDataPayload = metaData,
                MetaData = metaData
            };
        }

        /// <summary>
        /// Decodes a <c>ua-application</c> message (Part 14 §7.2.5.5.3
        /// Table 189).
        /// </summary>
        /// <param name="root">Root element.</param>
        /// <param name="context">Decoder context.</param>
        /// <returns>Decoded discovery message or
        /// <see langword="null"/>.</returns>
        private static JsonDiscoveryMessage? DecodeApplicationDiscovery(
            JsonElement root,
            PubSubNetworkMessageContext context)
        {
            ApplicationDescription? description = null;
            if (root.TryGetProperty("Description", out JsonElement descriptionElement) &&
                descriptionElement.ValueKind == JsonValueKind.Object)
            {
                description = DecodeEncodeable<ApplicationDescription>(descriptionElement, context);
                if (description is null)
                {
                    return null;
                }
            }
            description ??= new ApplicationDescription();
            return new JsonDiscoveryMessage
            {
                MessageId = ReadOptionalString(root, "MessageId", context),
                PublisherId = ReadPublisherId(root, context),
                Timestamp = ReadOptionalTimestamp(root, "Timestamp"),
                DiscoveryType = UadpDiscoveryType.ApplicationInformation,
                Description = description,
                ApplicationInformation = new UadpApplicationInformation
                {
                    ApplicationName = description.ApplicationName,
                    ApplicationUri = description.ApplicationUri ?? string.Empty,
                    ProductUri = description.ProductUri ?? string.Empty,
                    ApplicationType = description.ApplicationType,
                    Capabilities = ReadStringArray(root, "ServerCapabilities", context)
                }
            };
        }

        /// <summary>
        /// Decodes a <c>ua-endpoints</c> message (Part 14 §7.2.5.5.4
        /// Table 190).
        /// </summary>
        /// <param name="root">Root element.</param>
        /// <param name="context">Decoder context.</param>
        /// <returns>Decoded discovery message.</returns>
        private static JsonDiscoveryMessage DecodeEndpointsDiscovery(
            JsonElement root,
            PubSubNetworkMessageContext context)
        {
            EndpointDescription[] endpoints = [];
            if (root.TryGetProperty("Endpoints", out JsonElement endpointsElement) &&
                endpointsElement.ValueKind == JsonValueKind.Array)
            {
                endpoints = ReadEndpointArray(endpointsElement, context);
            }
            return new JsonDiscoveryMessage
            {
                MessageId = ReadOptionalString(root, "MessageId", context),
                PublisherId = ReadPublisherId(root, context),
                Timestamp = ReadOptionalTimestamp(root, "Timestamp"),
                DiscoveryType = UadpDiscoveryType.PublisherEndpoints,
                PublisherEndpoints = endpoints
            };
        }

        /// <summary>
        /// Decodes a <c>ua-status</c> message (Part 14 §7.2.5.5.5
        /// Table 191) into an application status discovery message.
        /// </summary>
        /// <param name="root">Root element.</param>
        /// <param name="context">Decoder context.</param>
        /// <returns>Decoded discovery message.</returns>
        private static JsonDiscoveryMessage DecodeStatusDiscovery(
            JsonElement root,
            PubSubNetworkMessageContext context)
        {
            bool isCyclic = root.TryGetProperty("IsCyclic", out JsonElement cyclicElement) &&
                cyclicElement.ValueKind == JsonValueKind.True;
            DateTimeUtc timestamp = ReadOptionalTimestamp(root, "Timestamp");
            return new JsonDiscoveryMessage
            {
                MessageId = ReadOptionalString(root, "MessageId", context),
                PublisherId = ReadPublisherId(root, context),
                Timestamp = timestamp,
                DiscoveryType = UadpDiscoveryType.ApplicationInformation,
                ApplicationStatus = new UadpApplicationStatus
                {
                    IsCyclic = isCyclic,
                    Status = (PubSubState)ReadOptionalUInt32(root, "Status"),
                    Timestamp = timestamp,
                    NextReportTime = ReadOptionalTimestamp(root, "NextReportTime")
                }
            };
        }

        /// <summary>
        /// Decodes a <c>ua-connection</c> message (Part 14 §7.2.5.5.6
        /// Table 192).
        /// </summary>
        /// <param name="root">Root element.</param>
        /// <param name="context">Decoder context.</param>
        /// <returns>Decoded discovery message or
        /// <see langword="null"/>.</returns>
        private static JsonDiscoveryMessage? DecodeConnectionDiscovery(
            JsonElement root,
            PubSubNetworkMessageContext context)
        {
            PubSubConnectionDataType? connection = null;
            if (root.TryGetProperty("Connection", out JsonElement connElement) &&
                connElement.ValueKind == JsonValueKind.Object)
            {
                connection = DecodeEncodeable<PubSubConnectionDataType>(connElement, context);
                if (connection is null)
                {
                    return null;
                }
            }
            return new JsonDiscoveryMessage
            {
                MessageId = ReadOptionalString(root, "MessageId", context),
                PublisherId = ReadPublisherId(root, context),
                Timestamp = ReadOptionalTimestamp(root, "Timestamp"),
                DiscoveryType = UadpDiscoveryType.PubSubConnection,
                Connection = connection
            };
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

        /// <summary>
        /// Decodes a <c>ua-action</c> envelope into a
        /// <see cref="JsonActionNetworkMessage"/> per
        /// <see href="https://reference.opcfoundation.org/Core/Part14/v105/docs/7.2.5.6">
        /// Part 14 §7.2.5.6</see>.
        /// </summary>
        /// <param name="root">Root element.</param>
        /// <param name="context">Decoder context.</param>
        /// <param name="isResponse">Whether the envelope MessageType is
        /// <c>ua-action-response</c>; it selects the body type of every
        /// entry in <c>Messages</c>.</param>
        /// <returns>Decoded action message or
        /// <see langword="null"/>.</returns>
        private static JsonActionNetworkMessage? DecodeAction(
            JsonElement root,
            PubSubNetworkMessageContext context,
            bool isResponse)
        {
            // The envelope is decoded without Messages, which are decoded
            // once below as the body type the MessageType selects.
            Ua.JsonActionNetworkMessage? network;
            try
            {
                network = JsonVariantDecoder.DecodeSpliced(
                    root,
                    context.MessageContext,
                    static decoder => decoder.ReadEncodeable<Ua.JsonActionNetworkMessage>(
                        JsonVariantDecoder.SpliceFieldName),
                    excludedProperty: "Messages");
            }
            catch (ServiceResultException)
            {
                network = null;
            }
            ArrayOf<ExtensionObject> messages = network is null
                ? []
                : DecodeActionMessageBodies(root, isResponse, context);
            if (network is null || messages.Count == 0)
            {
                context.Diagnostics.Increment(
                    PubSubDiagnosticsCounterKind.ReceivedInvalidNetworkMessages);
                return null;
            }
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

        /// <summary>
        /// Decodes the <c>Messages</c> of an action NetworkMessage. A
        /// NetworkMessage carries either ActionRequest or ActionResponse
        /// messages (Part 14 §7.2.5.6): every entry is decoded as the type
        /// selected by the envelope MessageType, and a request envelope
        /// with a response entry (one carrying <c>Status</c>) is rejected.
        /// </summary>
        /// <returns>The decoded bodies; empty when the envelope is
        /// invalid.</returns>
        private static ArrayOf<ExtensionObject> DecodeActionMessageBodies(
            JsonElement root,
            bool isResponse,
            PubSubNetworkMessageContext context)
        {
            if (!root.TryGetProperty("Messages", out JsonElement messagesElement) ||
                messagesElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }
            var messages = new List<ExtensionObject>(CheckArrayLength(messagesElement, context));
            foreach (JsonElement entry in messagesElement.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }
                if (!isResponse && entry.TryGetProperty("Status", out _))
                {
                    return [];
                }
                IEncodeable? body = isResponse
                    ? DecodeEncodeable<Opc.Ua.JsonActionResponseMessage>(entry, context)
                    : DecodeEncodeable<Opc.Ua.JsonActionRequestMessage>(entry, context);
                if (body is not null)
                {
                    messages.Add(new ExtensionObject(body));
                }
            }
            return new ArrayOf<ExtensionObject>(messages.ToArray());
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
            if (!TryReadOptionalStatus(entry, "Status", out StatusCode status))
            {
                return null;
            }
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
            // MaxStringLength counts the UTF-8 bytes of a string, as in every
            // other codec; a UTF-16 code unit takes at most three of them.
            int maxStringLength = context.MessageContext.MaxStringLength;
            if (maxStringLength > 0 && value.Length > maxStringLength / 3)
            {
                int byteLength = System.Text.Encoding.UTF8.GetByteCount(value);
                if (byteLength > maxStringLength)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadEncodingLimitsExceeded,
                        "MaxStringLength {0} < {1}.",
                        maxStringLength,
                        byteLength);
                }
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
            // JsonElement.TryGetDateTime accepts only the ISO 8601-1 extended
            // profile required by Part 6 §5.4.2.6. Values are UTC on the wire,
            // so a value without an offset is UTC rather than host local time.
            if (root.TryGetProperty(name, out JsonElement value) &&
                value.ValueKind == JsonValueKind.String &&
                value.TryGetDateTime(out DateTime parsed))
            {
                return (DateTimeUtc)(parsed.Kind == DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
                    : parsed.ToUniversalTime());
            }
            return DateTimeUtc.MinValue;
        }

        /// <summary>
        /// Reads an optional <see cref="StatusCode"/> property encoded as
        /// the Part 6 §5.4.2.12 <c>{ "Code", "Symbol" }</c> object (an
        /// absent <c>Code</c> means Good) or as the legacy bare number.
        /// </summary>
        /// <param name="root">Source object.</param>
        /// <param name="name">Property name.</param>
        /// <param name="status">Status code, Good when absent.</param>
        /// <returns><see langword="false"/> when the property is present
        /// but is not a valid StatusCode object.</returns>
        private static bool TryReadOptionalStatus(
            JsonElement root,
            string name,
            out StatusCode status)
        {
            status = StatusCodes.Good;
            if (!root.TryGetProperty(name, out JsonElement value))
            {
                return true;
            }
            if (value.ValueKind == JsonValueKind.Number)
            {
                // 1.04 and deprecated ReversibleFieldEncoding Publishers
                // (Part 14 §6.3.2.3.1 Table 112) write the bare code.
                if (!value.TryGetUInt32(out uint legacyCode))
                {
                    return false;
                }
                status = new StatusCode(legacyCode);
                return true;
            }
            if (value.ValueKind != JsonValueKind.Object)
            {
                return false;
            }
            if (!value.TryGetProperty("Code", out JsonElement codeElement))
            {
                return true;
            }
            if (codeElement.ValueKind != JsonValueKind.Number ||
                !codeElement.TryGetUInt32(out uint codeValue))
            {
                return false;
            }
            status = new StatusCode(codeValue);
            return true;
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
        /// all) the metadata registered for the identity is used unless its
        /// MajorVersion is newer than the message MinorVersion.</param>
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
            if (IsUsableMatch(result, metaData, metaVersion, hasMajorVersion))
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
                    if (IsUsableMatch(result, metaData, metaVersion, hasMajorVersion))
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
        /// the registered description for the identity applies unless the
        /// message MinorVersion proves it predates that description's
        /// MajorVersion.
        /// </summary>
        /// <param name="result">Registry lookup result.</param>
        /// <param name="metaData">Registered metadata for the identity.</param>
        /// <param name="metaVersion">Version carried by the message.</param>
        /// <param name="hasMajorVersion">Whether the message carried a
        /// MajorVersion.</param>
        private static bool IsUsableMatch(
            MetaDataMatchResult result,
            DataSetMetaDataType? metaData,
            ConfigurationVersionDataType? metaVersion,
            bool hasMajorVersion)
        {
            if (result is MetaDataMatchResult.Match or MetaDataMatchResult.MinorVersionMismatch)
            {
                return true;
            }
            if (hasMajorVersion || result != MetaDataMatchResult.MajorVersionMismatch)
            {
                return false;
            }
            // Part 14 §6.2.3.2.6 Table 11: a MajorVersion change sets the
            // MinorVersion to the same value, and versions only increase, so
            // a message MinorVersion below the registered MajorVersion was
            // produced with an older, incompatible layout.
            uint minorVersion = metaVersion?.MinorVersion ?? 0;
            uint registeredMajor = metaData?.ConfigurationVersion?.MajorVersion ?? 0;
            return minorVersion == 0 || registeredMajor <= minorVersion;
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
                    // JSON PublisherId is a String (Part 14 §7.2.5.4.1 Table 185);
                    // valid PublisherId DataTypes are UInteger and String
                    // (§7.2.4.4.2), so a GUID-shaped value stays a String.
                    string raw = CheckStringLength(value.GetString() ?? string.Empty, context);
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
