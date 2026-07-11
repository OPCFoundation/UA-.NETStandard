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
using System.Buffers.Binary;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua.Bindings;
using Opc.Ua.Pcap.Capture;
using Opc.Ua.Pcap.Frame;
using Opc.Ua.Pcap.KeyLog;

namespace Opc.Ua.Pcap.Dissection
{
    /// <summary>
    /// Reassembles decrypted OPC UA service calls from captured UA-SC
    /// chunks.
    /// </summary>
    public sealed class ServiceCallReassembler
    {
        /// <summary>
        /// Constructs a service-call reassembler with no-op logging.
        /// </summary>
        public ServiceCallReassembler()
            : this(NullLoggerFactory.Instance)
        {
        }

        /// <summary>
        /// Constructs a service-call reassembler with explicit logging.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="loggerFactory"/> is <c>null</c>.
        /// </exception>
        public ServiceCallReassembler(ILoggerFactory loggerFactory)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);

            m_loggerFactory = loggerFactory;
            m_logger = loggerFactory.CreateLogger<ServiceCallReassembler>();
            m_messageContext = new ServiceMessageContext(
                NoopTelemetryContext.Instance,
                EncodeableFactory.Create());
        }

        /// <summary>
        /// Add (or update) the key material for a token observed in the
        /// capture. Tokens are matched against incoming chunks by their
        /// (channelId, tokenId). Must be called at least once for each
        /// channel before <see cref="Push(CaptureFrame)"/> chunks for that
        /// channel can be decoded.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="material"/> is <c>null</c>.
        /// </exception>
        public void LoadKeyMaterial(ChannelKeyMaterial material)
        {
            ArgumentNullException.ThrowIfNull(material);

            if (m_channels.TryGetValue(material.ChannelId, out OfflineSecureChannel? channel))
            {
                channel.LoadKeyMaterial(material);
                return;
            }

            m_channels[material.ChannelId] = new OfflineSecureChannel(material, m_loggerFactory);
        }

        /// <summary>
        /// Feeds one captured OPC UA chunk through the reassembler. If a
        /// request/response pair completes as a result of this chunk, the
        /// completed <see cref="DecodedServiceCall"/> is appended to the
        /// internal result queue and returned by the next call to
        /// <see cref="DrainCompleted"/>.
        /// </summary>
        /// <param name="frame">
        /// The chunk to feed. The first 4 bytes of <see cref="CaptureFrame.Data"/>
        /// must be a valid OPC UA message-type marker; otherwise the chunk is
        /// silently skipped.
        /// </param>
        public void Push(CaptureFrame frame)
        {
            try
            {
                if (frame.Data.Length < sizeof(uint))
                {
                    return;
                }

                ReadOnlySpan<byte> data = frame.Data.Span;
                uint messageType = BinaryPrimitives.ReadUInt32LittleEndian(data);
                if (!TcpMessageType.IsValid(messageType))
                {
                    return;
                }

                if (frame.Direction == CaptureFrameDirection.Unknown)
                {
                    m_logger.LogWarning(
                        "Skipping OPC UA chunk with unknown direction at {Timestamp}.",
                        frame.Timestamp);
                    return;
                }

                bool fromClient = frame.Direction == CaptureFrameDirection.ClientToServer;
                uint baseType = messageType & TcpMessageType.MessageTypeMask;
                switch (baseType)
                {
                    case TcpMessageType.Hello:
                        AddControlMessage(frame, "Hello");
                        return;
                    case TcpMessageType.Acknowledge:
                        AddControlMessage(frame, "Acknowledge");
                        return;
                    case TcpMessageType.Error:
                        AddControlMessage(frame, "Error");
                        return;
                    case TcpMessageType.ReverseHello:
                        AddControlMessage(frame, "ReverseHello");
                        return;
                    case TcpMessageType.Open:
                        AddOpenSecureChannelMessage(frame, fromClient);
                        return;
                    case TcpMessageType.Message:
                    case TcpMessageType.Close:
                        PushSecureChannelChunk(frame, fromClient, baseType);
                        return;
                }
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(ex, "Skipping OPC UA chunk because service-call reassembly failed.");
            }
        }

        /// <summary>
        /// Returns and clears the set of service calls that completed since
        /// the last call. The reassembler keeps internal state for in-flight
        /// requests until they complete or the channel closes.
        /// </summary>
        public IReadOnlyList<DecodedServiceCall> DrainCompleted()
        {
            DecodedServiceCall[] completed = [.. m_completed];
            m_completed.Clear();
            return completed;
        }

        /// <summary>
        /// Convenience helper: feeds every frame from an enumerable, then
        /// returns all completed service calls.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="frames"/> is <c>null</c>.
        /// </exception>
        public IReadOnlyList<DecodedServiceCall> ProcessAll(IEnumerable<CaptureFrame> frames)
        {
            ArgumentNullException.ThrowIfNull(frames);

            foreach (CaptureFrame frame in frames)
            {
                Push(frame);
            }

            return DrainCompleted();
        }

        /// <summary>
        /// Same as <see cref="ProcessAll(IEnumerable{CaptureFrame})"/> but
        /// for async streams (e.g. directly from
        /// <see cref="ICaptureSource.ReadCapturedFramesAsync"/>).
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="frames"/> is <c>null</c>.
        /// </exception>
        public async ValueTask<IReadOnlyList<DecodedServiceCall>> ProcessAllAsync(
            IAsyncEnumerable<CaptureFrame> frames,
            CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(frames);

            await foreach (CaptureFrame frame in frames.WithCancellation(ct).ConfigureAwait(false))
            {
                Push(frame);
            }

            return DrainCompleted();
        }

        private void AddControlMessage(CaptureFrame frame, string name)
        {
            m_completed.Add(new DecodedServiceCall
            {
                RequestTimestamp = frame.Timestamp,
                RequestName = name,
                RequestBodySize = frame.Data.Length
            });
        }

        private void AddOpenSecureChannelMessage(CaptureFrame frame, bool fromClient)
        {
            uint channelId = 0;
            uint tokenId = 0;
            if (frame.Data.Length >= 16)
            {
                ReadOnlySpan<byte> data = frame.Data.Span;
                channelId = BinaryPrimitives.ReadUInt32LittleEndian(data[8..]);
                tokenId = BinaryPrimitives.ReadUInt32LittleEndian(data[12..]);
            }

            m_completed.Add(new DecodedServiceCall
            {
                ChannelId = channelId,
                TokenId = tokenId,
                RequestTimestamp = frame.Timestamp,
                RequestName = fromClient ? "OpenSecureChannelRequest" : "OpenSecureChannelResponse",
                RequestBodySize = frame.Data.Length,
                RequestSummary = "(asymmetric OpenSecureChannel chunk)"
            });
        }

        private void PushSecureChannelChunk(CaptureFrame frame, bool fromClient, uint baseType)
        {
            try
            {
                if (frame.Data.Length < TcpMessageLimits.BaseHeaderSize)
                {
                    return;
                }

                uint channelId = BinaryPrimitives.ReadUInt32LittleEndian(frame.Data.Span[8..]);
                if (!m_channels.TryGetValue(channelId, out OfflineSecureChannel? channel))
                {
                    m_logger.LogWarning(
                        "Skipping OPC UA chunk for channel 0x{ChannelId:X8}; no key material is loaded.",
                        channelId);
                    return;
                }

                OfflineDecodedChunk decoded = channel.ReadChunk(frame.Data.Span, fromClient);
                PendingServiceCall pending = GetPendingCall(decoded, frame.Timestamp, fromClient);
                if (fromClient)
                {
                    pending.RequestChunks.Add(decoded.Body.ToArray());
                    pending.TokenId = decoded.TokenId;
                    if (decoded.IsFinal)
                    {
                        pending.RequestIsComplete = true;
                        pending.Request = DecodeMessage(pending.RequestChunks);
                    }
                }
                else
                {
                    pending.ResponseChunks.Add(decoded.Body.ToArray());
                    pending.TokenId = decoded.TokenId;
                    if (decoded.IsFinal)
                    {
                        pending.ResponseTimestamp = frame.Timestamp;
                        pending.ResponseIsComplete = true;
                        pending.Response = DecodeMessage(pending.ResponseChunks);
                    }
                }

                if (pending.RequestIsComplete && pending.ResponseIsComplete)
                {
                    m_completed.Add(CreateDecodedServiceCall(decoded, pending));
                    m_pending.Remove((decoded.ChannelId, decoded.RequestId));
                }

                if (baseType == TcpMessageType.Close && decoded.IsFinal)
                {
                    RemoveChannelState(decoded.ChannelId);
                }
            }
            catch (PcapDiagnosticsException ex)
            {
                m_logger.LogWarning(ex, "Skipping OPC UA secure-channel chunk because offline decoding failed.");
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(ex, "Skipping OPC UA secure-channel chunk because reassembly failed.");
            }
        }

        private PendingServiceCall GetPendingCall(
            OfflineDecodedChunk decoded,
            DateTimeOffset timestamp,
            bool fromClient)
        {
            (uint ChannelId, uint RequestId) key = (decoded.ChannelId, decoded.RequestId);
            if (m_pending.TryGetValue(key, out PendingServiceCall? pending))
            {
                if (!fromClient || !pending.RequestIsComplete)
                {
                    return pending;
                }
            }

            pending = new PendingServiceCall(timestamp);
            m_pending[key] = pending;
            return pending;
        }

        private DecodedServiceCall CreateDecodedServiceCall(
            OfflineDecodedChunk decoded,
            PendingServiceCall pending)
        {
            Dictionary<string, string?> annotations = [];
            AddRequestAnnotations(annotations, pending.Request?.Message);

            return new DecodedServiceCall
            {
                ChannelId = decoded.ChannelId,
                TokenId = pending.TokenId,
                RequestId = decoded.RequestId,
                RequestTimestamp = pending.RequestTimestamp,
                ResponseTimestamp = pending.ResponseTimestamp,
                RequestName = pending.Request?.Name,
                ResponseName = pending.Response?.Name,
                ResponseStatus = pending.Response?.ResponseStatus,
                RequestBodySize = GetTotalLength(pending.RequestChunks),
                ResponseBodySize = GetTotalLength(pending.ResponseChunks),
                RequestSummary = pending.Request?.Summary,
                ResponseSummary = pending.Response?.Summary,
                Annotations = annotations
            };
        }

        private DecodedMessage DecodeMessage(List<byte[]> chunks)
        {
            byte[] body = Concatenate(chunks);
            string undecodedName = $"{body.Length}B";
            try
            {
                using var decoder = new BinaryDecoder(body, m_messageContext);
                ExpandedNodeId typeId = decoder.ReadExpandedNodeId(null);
                undecodedName = typeId.ToString();
                if (!m_messageContext.Factory.TryGetEncodeableType(
                    typeId,
                    out IEncodeableType? encodeableType))
                {
                    return DecodedMessage.Undecoded(undecodedName, body.Length);
                }

                IEncodeable message = encodeableType.CreateInstance();
                message.Decode(decoder);

                return CreateDecodedMessage(message, body.Length);
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(ex, "Unable to decode OPC UA service message body.");
                return DecodedMessage.Undecoded(undecodedName, body.Length);
            }
        }

        private DecodedMessage CreateDecodedMessage(IEncodeable message, int byteCount)
        {
            string name = message.GetType().Name;
            if (message is IServiceRequest request)
            {
                return new DecodedMessage(name, CreateRequestSummary(request, name, byteCount), null, request);
            }

            if (message is IServiceResponse response)
            {
                StatusCode status = response.ResponseHeader.ServiceResult;
                string summary = $"status={status} ts={response.ResponseHeader.Timestamp:O}";
                return new DecodedMessage(name, summary, status, response);
            }

            return new DecodedMessage(name, $"{name} body={byteCount}B", null, message);
        }

        private static string CreateRequestSummary(IServiceRequest request, string typeName, int byteCount)
        {
            string detail = CreateKnownRequestDetail(request);
            string header = request.RequestHeader == null
                ? string.Empty
                : $"handle={request.RequestHeader.RequestHandle} audit={request.RequestHeader.AuditEntryId ?? string.Empty}";

            if (detail.Length == 0)
            {
                detail = $"{typeName} body={byteCount}B";
            }

            return header.Length == 0 ? detail : $"{header} {detail}";
        }

        private static string CreateKnownRequestDetail(IServiceRequest request)
        {
            string typeName = request.GetType().Name;
            return typeName switch
            {
                "ReadRequest" => FormatCountAndValue(
                    request,
                    "NodesToRead",
                    "nodes",
                    "MaxAge",
                    "maxAge"),
                "BrowseRequest" => FormatCountAndValue(
                    request,
                    "NodesToBrowse",
                    "nodes",
                    "View",
                    "view"),
                "WriteRequest" => FormatCount(request, "NodesToWrite", "nodes"),
                "CallRequest" => FormatCount(request, "MethodsToCall", "methods"),
                "HistoryReadRequest" => FormatCount(request, "NodesToRead", "nodes"),
                "CreateMonitoredItemsRequest" => FormatCount(request, "ItemsToCreate", "items"),
                "DeleteMonitoredItemsRequest" => FormatCount(request, "MonitoredItemIds", "items"),
                "PublishRequest" => FormatCount(request, "SubscriptionAcknowledgements", "acks"),
                _ => string.Empty
            };
        }

        private static string FormatCount(
            IEncodeable message,
            string propertyName,
            string itemName)
        {
            int? count = GetCollectionCount(message, propertyName);
            return count.HasValue ? $"{count.Value} {itemName}" : string.Empty;
        }

        private static string FormatCountAndValue(
            IEncodeable message,
            string countPropertyName,
            string itemName,
            string valuePropertyName,
            string valueName)
        {
            int? count = GetCollectionCount(message, countPropertyName);
            string? value = GetPropertyString(message, valuePropertyName);
            if (!count.HasValue)
            {
                return string.Empty;
            }

            return value == null
                ? $"{count.Value} {itemName}, {valueName}=null"
                : $"{count.Value} {itemName}, {valueName}={value}";
        }

        private static int? GetCollectionCount(IEncodeable message, string propertyName)
        {
            object? value = message.GetType().GetProperty(propertyName)?.GetValue(message);
            if (value is ICollection collection)
            {
                return collection.Count;
            }

            if (value is IConvertableToArray)
            {
                var countProperty = value.GetType().GetProperty("Count");
                if (countProperty?.CanRead == true &&
                    countProperty.PropertyType == typeof(int) &&
                    countProperty.GetIndexParameters().Length == 0 &&
                    countProperty.GetValue(value) is int count)
                {
                    return count;
                }
            }

            return null;
        }

        private static string? GetPropertyString(IEncodeable message, string propertyName)
        {
            object? value = message.GetType().GetProperty(propertyName)?.GetValue(message);
            if (value is ViewDescription view)
            {
                return ViewDescription.IsDefault(view) ? "default" : view.ViewId.ToString();
            }

            return value?.ToString();
        }

        private static void AddRequestAnnotations(
            Dictionary<string, string?> annotations,
            IEncodeable? message)
        {
            if (message is not IServiceRequest request || request.RequestHeader == null)
            {
                return;
            }

            annotations["RequestHandle"] = request.RequestHeader.RequestHandle.ToString(CultureInfo.InvariantCulture);
            annotations["AuditEntryId"] = request.RequestHeader.AuditEntryId;
        }

        private static byte[] Concatenate(List<byte[]> chunks)
        {
            byte[] result = new byte[GetTotalLength(chunks)];
            int offset = 0;
            foreach (byte[] chunk in chunks)
            {
                chunk.AsSpan().CopyTo(result.AsSpan(offset));
                offset += chunk.Length;
            }

            return result;
        }

        private static int GetTotalLength(List<byte[]> chunks)
        {
            int length = 0;
            foreach (byte[] chunk in chunks)
            {
                length += chunk.Length;
            }

            return length;
        }

        private void RemoveChannelState(uint channelId)
        {
            List<(uint channelId, uint requestId)> removeKeys = [];
            foreach ((uint pendingChannelId, uint pendingRequestId) in m_pending.Keys)
            {
                if (pendingChannelId == channelId)
                {
                    removeKeys.Add((pendingChannelId, pendingRequestId));
                }
            }

            foreach ((uint pendingChannelId, uint pendingRequestId) in removeKeys)
            {
                m_pending.Remove((pendingChannelId, pendingRequestId));
            }

            if (m_channels.Remove(channelId, out OfflineSecureChannel? channel))
            {
                channel.Dispose();
            }
        }

        private sealed class PendingServiceCall
        {
            public PendingServiceCall(DateTimeOffset requestTimestamp)
            {
                RequestTimestamp = requestTimestamp;
            }

            public DateTimeOffset RequestTimestamp { get; }

            public DateTimeOffset? ResponseTimestamp { get; set; }

            public List<byte[]> RequestChunks { get; } = [];

            public List<byte[]> ResponseChunks { get; } = [];

            public bool RequestIsComplete { get; set; }

            public bool ResponseIsComplete { get; set; }

            public uint TokenId { get; set; }

            public DecodedMessage? Request { get; set; }

            public DecodedMessage? Response { get; set; }
        }

        private readonly struct DecodedMessage
        {
            public DecodedMessage(
                string? name,
                string? summary,
                StatusCode? responseStatus,
                IEncodeable? message)
            {
                Name = name;
                Summary = summary;
                ResponseStatus = responseStatus;
                Message = message;
            }

            public string? Name { get; }

            public string? Summary { get; }

            public StatusCode? ResponseStatus { get; }

            public IEncodeable? Message { get; }

            public static DecodedMessage Undecoded(string name, int byteCount)
            {
                return new DecodedMessage(name, $"(undecoded: {byteCount} bytes)", null, null);
            }
        }

        /// <summary>
        /// Minimal <see cref="ITelemetryContext"/> implementation that
        /// hands out <see cref="NullLogger.Instance"/> for everything.
        /// </summary>
        private sealed class NoopTelemetryContext : ITelemetryContext
        {
            public static NoopTelemetryContext Instance { get; } = new();

            public ILoggerFactory LoggerFactory => NullLoggerFactory.Instance;

            public ActivitySource ActivitySource => s_activitySource;

            public Meter CreateMeter()
            {
                return new Meter("Opc.Ua.Core.Diagnostics.ServiceCallReassembler");
            }

            private NoopTelemetryContext()
            {
            }

            private static readonly ActivitySource s_activitySource
                = new("Opc.Ua.Core.Diagnostics.ServiceCallReassembler");
        }

        private readonly Dictionary<uint, OfflineSecureChannel> m_channels = [];
        private readonly Dictionary<(uint channelId, uint requestId), PendingServiceCall> m_pending = [];
        private readonly List<DecodedServiceCall> m_completed = [];
        private readonly ServiceMessageContext m_messageContext;
        private readonly ILoggerFactory m_loggerFactory;
        private readonly ILogger m_logger;
    }
}
