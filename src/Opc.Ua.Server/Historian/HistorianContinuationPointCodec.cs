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
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server.Historian
{
    internal sealed class HistorianContinuationPointCodec :
        IHistoryContinuationPointCodec
    {
        public HistorianContinuationPointCodec(IServerInternal server)
        {
            m_server = server ?? throw new ArgumentNullException(nameof(server));
        }

        public async ValueTask<HistoryContinuationPointEnvelope?> EncodeAsync(
            NodeId ownerSessionId,
            IHistoryContinuationPoint continuationPoint,
            CancellationToken cancellationToken)
        {
            if (continuationPoint is not HistorianContinuationState state ||
                state.Provider is not IHistorianProviderIdentity identity ||
                state.BufferedProcessedOutputs != null)
            {
                return null;
            }
            HistorianNodeCapabilities capabilities = await state.Provider
                .GetCapabilitiesAsync(state.NodeId, cancellationToken)
                .ConfigureAwait(false);
            if (!capabilities.PortableResumeTokens)
            {
                return null;
            }
            if (ownerSessionId.IsNull ||
                state.Id == Guid.Empty ||
                state.NodeId.IsNull ||
                !HasSerializableRequest(state))
            {
                return null;
            }
            if (string.IsNullOrWhiteSpace(identity.ProviderId) ||
                identity.ProviderId.Length > kMaxProviderIdLength)
            {
                return null;
            }
            if (state.ResumeToken.State.Length > kMaxResumeTokenSize)
            {
                return null;
            }
            string indexRange = FormatIndexRange(state.IndexRange);
            if (indexRange.Length > kMaxIndexRangeLength)
            {
                return null;
            }

            using var encoder = new BinaryEncoder(m_server.MessageContext);
            encoder.WriteInt32(null, (int)kFormatVersion);
            encoder.WriteStringArray(
                null,
                m_server.NamespaceUris.ToArrayOf());
            encoder.WriteStringArray(
                null,
                m_server.ServerUris.ToArrayOf());
            encoder.SetMappingTables(
                m_server.NamespaceUris,
                m_server.ServerUris);
            encoder.WriteString(null, identity.ProviderId);
            encoder.WriteEnumerated(null, state.Kind);
            encoder.WriteNodeId(null, state.NodeId);
            encoder.WriteByteString(null, state.ResumeToken.State);
            encoder.WriteEnumerated(null, state.TimestampsToReturn);
            encoder.WriteString(null, indexRange);
            encoder.WriteQualifiedName(null, state.DataEncoding);
            WriteRequest(encoder, state);
            byte[]? payload = encoder.CloseAndReturnBuffer();
            if (payload == null)
            {
                return null;
            }
            if (payload.Length > kMaxPayloadSize)
            {
                return null;
            }

            return new HistoryContinuationPointEnvelope
            {
                Id = state.Id,
                OwnerSessionId = ownerSessionId,
                CodecId = kCodecId,
                CodecVersion = kFormatVersion,
                Payload = ByteString.From(payload)
            };
        }

        public async ValueTask<IHistoryContinuationPoint?> DecodeAsync(
            HistoryContinuationPointEnvelope envelope,
            CancellationToken cancellationToken)
        {
            if (!string.Equals(envelope.CodecId, kCodecId, StringComparison.Ordinal) ||
                envelope.CodecVersion is < kLegacyFormatVersion or > kFormatVersion ||
                envelope.Id == Guid.Empty ||
                envelope.OwnerSessionId.IsNull ||
                envelope.Payload.IsEmpty ||
                envelope.Payload.Length > kMaxPayloadSize ||
                m_server is not IHistorianRegistryProvider registry)
            {
                return null;
            }

            try
            {
                using var decoder = new BinaryDecoder(
                    envelope.Payload.ToArray(),
                    m_server.MessageContext);
                int formatVersion = decoder.ReadInt32(null);
                if (formatVersion != envelope.CodecVersion)
                {
                    return null;
                }
                if (formatVersion >= kNamespaceMappedFormatVersion)
                {
                    if (!TryCreateNamespaceTable(
                            decoder.ReadStringArray(null),
                            out NamespaceTable? namespaceUris) ||
                        !TryCreateStringTable(
                            decoder.ReadStringArray(null),
                            out StringTable? serverUris))
                    {
                        return null;
                    }
                    decoder.SetMappingTables(namespaceUris, serverUris);
                }
                string? providerId = decoder.ReadString(null);
                HistorianReadKind kind = decoder.ReadEnumerated<HistorianReadKind>(null);
                NodeId nodeId = decoder.ReadNodeId(null);
                ByteString resumeToken = decoder.ReadByteString(null);
                TimestampsToReturn timestamps =
                    decoder.ReadEnumerated<TimestampsToReturn>(null);
                string? indexRangeText = decoder.ReadString(null);
                QualifiedName dataEncoding = decoder.ReadQualifiedName(null);
                bool timestampsValid = kind == HistorianReadKind.Events
                    ? timestamps is >= TimestampsToReturn.Source and
                        <= TimestampsToReturn.Neither
                    : timestamps is TimestampsToReturn.Source or
                        TimestampsToReturn.Server or
                        TimestampsToReturn.Both;
                if (string.IsNullOrWhiteSpace(providerId) ||
                    providerId.Length > kMaxProviderIdLength ||
                    nodeId.IsNull ||
                    resumeToken.Length > kMaxResumeTokenSize ||
                    (indexRangeText?.Length ?? 0) > kMaxIndexRangeLength ||
                    !timestampsValid)
                {
                    return null;
                }

                IHistorianProvider? provider = registry.HistorianRegistry.Resolve(nodeId);
                if (provider is not IHistorianProviderIdentity identity ||
                    !string.Equals(identity.ProviderId, providerId, StringComparison.Ordinal))
                {
                    return null;
                }
                HistorianNodeCapabilities capabilities = await provider
                    .GetCapabilitiesAsync(nodeId, cancellationToken)
                    .ConfigureAwait(false);
                if (!capabilities.PortableResumeTokens)
                {
                    return null;
                }

                HistorianContinuationState state = ReadRequest(
                    decoder,
                    envelope.Id,
                    provider,
                    kind,
                    nodeId,
                    new HistorianResumeToken(resumeToken),
                    timestamps,
                    string.IsNullOrEmpty(indexRangeText)
                        ? default
                        : NumericRange.Parse(indexRangeText),
                    dataEncoding);
                if (decoder.Position != envelope.Payload.Length)
                {
                    state.Dispose();
                    return null;
                }
                return state;
            }
            catch (Exception exception) when (exception is
                ServiceResultException or
                ArgumentException or
                InvalidOperationException or
                FormatException or
                EndOfStreamException or
                IOException or
                OverflowException or
                IndexOutOfRangeException)
            {
                return null;
            }
        }

        private static void WriteRequest(
            BinaryEncoder encoder,
            HistorianContinuationState state)
        {
            switch (state.Kind)
            {
                case HistorianReadKind.Raw:
                    WriteRawRequest(encoder, state.RawRequest!);
                    break;
                case HistorianReadKind.Modified:
                    WriteModifiedRequest(encoder, state.ModifiedRequest!);
                    break;
                case HistorianReadKind.Processed:
                    WriteProcessedRequest(encoder, state.ProcessedRequest!);
                    break;
                case HistorianReadKind.Annotations:
                    WriteAnnotationRequest(encoder, state.AnnotationRequest!);
                    break;
                case HistorianReadKind.Events:
                    WriteEventRequest(encoder, state.EventRequest!);
                    break;
                default:
                    throw new ServiceResultException(StatusCodes.BadEncodingError);
            }
        }

        private static bool HasSerializableRequest(
            HistorianContinuationState state)
        {
            return state.Kind switch
            {
                HistorianReadKind.Raw => state.RawRequest != null,
                HistorianReadKind.Modified => state.ModifiedRequest != null,
                HistorianReadKind.Processed => state.ProcessedRequest != null,
                HistorianReadKind.Annotations => state.AnnotationRequest != null,
                HistorianReadKind.Events => state.EventRequest != null,
                _ => false
            };
        }

        private static string FormatIndexRange(NumericRange range)
        {
            if (range.IsNull)
            {
                return string.Empty;
            }
            NumericRange[]? subRanges = range.SubRanges;
            if (subRanges == null)
            {
                return range.ToString();
            }

            var builder = new StringBuilder();
            for (int i = 0; i < subRanges.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }
                builder.Append(subRanges[i].ToString());
            }
            return builder.ToString();
        }

        private static bool TryCreateNamespaceTable(
            ArrayOf<string?> values,
            out NamespaceTable? table)
        {
            if (values.Count > kMaxMappingTableEntries)
            {
                table = null;
                return false;
            }
            string[] entries = new string[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                string? value = values[i];
                if (value == null ||
                    value.Length > kMaxMappingTableEntryLength)
                {
                    table = null;
                    return false;
                }
                entries[i] = value;
            }
            table = new NamespaceTable(entries);
            return table.Count == entries.Length;
        }

        private static bool TryCreateStringTable(
            ArrayOf<string?> values,
            out StringTable? table)
        {
            if (values.Count > kMaxMappingTableEntries)
            {
                table = null;
                return false;
            }
            string[] entries = new string[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                string? value = values[i];
                if (value == null ||
                    value.Length > kMaxMappingTableEntryLength)
                {
                    table = null;
                    return false;
                }
                entries[i] = value;
            }
            table = new StringTable(entries);
            return table.Count == entries.Length;
        }

        private static HistorianContinuationState ReadRequest(
            BinaryDecoder decoder,
            Guid id,
            IHistorianProvider provider,
            HistorianReadKind kind,
            NodeId nodeId,
            HistorianResumeToken resumeToken,
            TimestampsToReturn timestamps,
            NumericRange indexRange,
            QualifiedName dataEncoding)
        {
            return kind switch
            {
                HistorianReadKind.Raw => new HistorianContinuationState
                {
                    Id = id,
                    Provider = provider,
                    Kind = kind,
                    NodeId = nodeId,
                    ResumeToken = resumeToken,
                    TimestampsToReturn = timestamps,
                    IndexRange = indexRange,
                    DataEncoding = dataEncoding,
                    RawRequest = ReadRawRequest(decoder, nodeId)
                },
                HistorianReadKind.Modified => new HistorianContinuationState
                {
                    Id = id,
                    Provider = provider,
                    Kind = kind,
                    NodeId = nodeId,
                    ResumeToken = resumeToken,
                    TimestampsToReturn = timestamps,
                    IndexRange = indexRange,
                    DataEncoding = dataEncoding,
                    ModifiedRequest = ReadModifiedRequest(decoder, nodeId)
                },
                HistorianReadKind.Processed => new HistorianContinuationState
                {
                    Id = id,
                    Provider = provider,
                    Kind = kind,
                    NodeId = nodeId,
                    ResumeToken = resumeToken,
                    TimestampsToReturn = timestamps,
                    IndexRange = indexRange,
                    DataEncoding = dataEncoding,
                    ProcessedRequest = ReadProcessedRequest(decoder, nodeId)
                },
                HistorianReadKind.Annotations => new HistorianContinuationState
                {
                    Id = id,
                    Provider = provider,
                    Kind = kind,
                    NodeId = nodeId,
                    ResumeToken = resumeToken,
                    TimestampsToReturn = timestamps,
                    IndexRange = indexRange,
                    DataEncoding = dataEncoding,
                    AnnotationRequest = ReadAnnotationRequest(decoder, nodeId)
                },
                HistorianReadKind.Events => new HistorianContinuationState
                {
                    Id = id,
                    Provider = provider,
                    Kind = kind,
                    NodeId = nodeId,
                    ResumeToken = resumeToken,
                    TimestampsToReturn = timestamps,
                    IndexRange = indexRange,
                    DataEncoding = dataEncoding,
                    EventRequest = ReadEventRequest(decoder, nodeId)
                },
                _ => throw new ServiceResultException(StatusCodes.BadDecodingError)
            };
        }

        private static void WriteRawRequest(
            BinaryEncoder encoder,
            HistorianRawReadRequest request)
        {
            encoder.WriteDateTime(null, request.StartTime);
            encoder.WriteDateTime(null, request.EndTime);
            encoder.WriteUInt32(null, request.MaxValues);
            encoder.WriteBoolean(null, request.IsForward);
            encoder.WriteBoolean(null, request.ReturnBounds);
        }

        private static HistorianRawReadRequest ReadRawRequest(
            BinaryDecoder decoder,
            NodeId nodeId)
        {
            return new HistorianRawReadRequest
            {
                NodeId = nodeId,
                StartTime = decoder.ReadDateTime(null),
                EndTime = decoder.ReadDateTime(null),
                MaxValues = decoder.ReadUInt32(null),
                IsForward = decoder.ReadBoolean(null),
                ReturnBounds = decoder.ReadBoolean(null)
            };
        }

        private static void WriteModifiedRequest(
            BinaryEncoder encoder,
            HistorianModifiedReadRequest request)
        {
            encoder.WriteDateTime(null, request.StartTime);
            encoder.WriteDateTime(null, request.EndTime);
            encoder.WriteUInt32(null, request.MaxValues);
            encoder.WriteBoolean(null, request.IsForward);
        }

        private static HistorianModifiedReadRequest ReadModifiedRequest(
            BinaryDecoder decoder,
            NodeId nodeId)
        {
            return new HistorianModifiedReadRequest
            {
                NodeId = nodeId,
                StartTime = decoder.ReadDateTime(null),
                EndTime = decoder.ReadDateTime(null),
                MaxValues = decoder.ReadUInt32(null),
                IsForward = decoder.ReadBoolean(null)
            };
        }

        private static void WriteProcessedRequest(
            BinaryEncoder encoder,
            HistorianProcessedReadRequest request)
        {
            encoder.WriteNodeId(null, request.AggregateId);
            encoder.WriteDateTime(null, request.StartTime);
            encoder.WriteDateTime(null, request.EndTime);
            encoder.WriteDouble(null, request.ProcessingInterval);
            encoder.WriteUInt32(null, request.MaxValues);
            encoder.WriteEncodeable(null, request.Configuration);
        }

        private static HistorianProcessedReadRequest ReadProcessedRequest(
            BinaryDecoder decoder,
            NodeId nodeId)
        {
            NodeId aggregateId = decoder.ReadNodeId(null);
            DateTimeUtc startTime = decoder.ReadDateTime(null);
            DateTimeUtc endTime = decoder.ReadDateTime(null);
            double processingInterval = decoder.ReadDouble(null);
            uint maxValues = decoder.ReadUInt32(null);
            AggregateConfiguration configuration =
                decoder.ReadEncodeable<AggregateConfiguration>(null) ??
                throw new ServiceResultException(StatusCodes.BadDecodingError);
            return new HistorianProcessedReadRequest
            {
                NodeId = nodeId,
                AggregateId = aggregateId,
                StartTime = startTime,
                EndTime = endTime,
                ProcessingInterval = processingInterval,
                MaxValues = maxValues,
                Configuration = configuration
            };
        }

        private static void WriteAnnotationRequest(
            BinaryEncoder encoder,
            HistorianAnnotationReadRequest request)
        {
            encoder.WriteDateTime(null, request.StartTime);
            encoder.WriteDateTime(null, request.EndTime);
            encoder.WriteUInt32(null, request.MaxValues);
            encoder.WriteBoolean(null, request.IsForward);
        }

        private static HistorianAnnotationReadRequest ReadAnnotationRequest(
            BinaryDecoder decoder,
            NodeId nodeId)
        {
            return new HistorianAnnotationReadRequest
            {
                NodeId = nodeId,
                StartTime = decoder.ReadDateTime(null),
                EndTime = decoder.ReadDateTime(null),
                MaxValues = decoder.ReadUInt32(null),
                IsForward = decoder.ReadBoolean(null)
            };
        }

        private static void WriteEventRequest(
            BinaryEncoder encoder,
            HistorianEventReadRequest request)
        {
            encoder.WriteDateTime(null, request.StartTime);
            encoder.WriteDateTime(null, request.EndTime);
            encoder.WriteUInt32(null, request.MaxValues);
            encoder.WriteBoolean(null, request.IsForward);
            encoder.WriteEncodeable(null, request.Filter);
        }

        private static HistorianEventReadRequest ReadEventRequest(
            BinaryDecoder decoder,
            NodeId nodeId)
        {
            DateTimeUtc startTime = decoder.ReadDateTime(null);
            DateTimeUtc endTime = decoder.ReadDateTime(null);
            uint maxValues = decoder.ReadUInt32(null);
            bool isForward = decoder.ReadBoolean(null);
            EventFilter filter = decoder.ReadEncodeable<EventFilter>(null) ??
                throw new ServiceResultException(StatusCodes.BadDecodingError);
            return new HistorianEventReadRequest
            {
                NodeId = nodeId,
                StartTime = startTime,
                EndTime = endTime,
                MaxValues = maxValues,
                IsForward = isForward,
                Filter = filter
            };
        }

        private const string kCodecId = "opcua-historian";
        private const uint kLegacyFormatVersion = 1;
        private const uint kNamespaceMappedFormatVersion = 2;
        private const uint kFormatVersion = kNamespaceMappedFormatVersion;
        private const int kMaxPayloadSize = 1024 * 1024;
        private const int kMaxProviderIdLength = 256;
        private const int kMaxResumeTokenSize = 64 * 1024;
        private const int kMaxIndexRangeLength = 1024;
        private const int kMaxMappingTableEntries = 65_536;
        private const int kMaxMappingTableEntryLength = 4 * 1024;
        private readonly IServerInternal m_server;
    }
}
