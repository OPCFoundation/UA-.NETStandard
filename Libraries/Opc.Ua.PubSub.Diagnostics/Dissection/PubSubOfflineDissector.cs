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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Encoding.Uadp;
using Opc.Ua.PubSub.MetaData;
using Opc.Ua.PubSub.Security;
using PubSubJsonDecoder = Opc.Ua.PubSub.Encoding.Json.JsonDecoder;

namespace Opc.Ua.PubSub.Pcap
{
    /// <summary>
    /// Offline PubSub dissector that projects captured NetworkMessage bytes
    /// into decoded DataSets when the message is cleartext.
    /// </summary>
    public sealed class PubSubOfflineDissector
    {
        /// <summary>
        /// Initializes a new offline dissector with an empty metadata registry.
        /// </summary>
        public PubSubOfflineDissector()
            : this(CreateDefaultContext())
        {
        }

        /// <summary>
        /// Initializes a new offline dissector with the supplied decode context.
        /// </summary>
        /// <param name="context">PubSub decoder context.</param>
        public PubSubOfflineDissector(PubSubNetworkMessageContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            m_context = context;
        }

        /// <summary>
        /// Dissects a captured PubSub frame. Malformed input is returned as an
        /// undecodable result instead of throwing.
        /// </summary>
        /// <param name="frame">Captured frame.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The dissection result.</returns>
        public async ValueTask<PubSubDissectionResult> DissectAsync(
            PubSubCaptureFrame frame,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PubSubDissectionMessageType mapping = DetectMessageType(in frame);
            if (mapping == PubSubDissectionMessageType.Uadp)
            {
                return DissectUadp(in frame);
            }
            if (mapping == PubSubDissectionMessageType.Json)
            {
                return await DissectJsonAsync(frame, cancellationToken).ConfigureAwait(false);
            }
            return CreateUndecodable(frame, mapping, "PubSub message mapping could not be determined.");
        }

        private static PubSubNetworkMessageContext CreateDefaultContext()
        {
            return new PubSubNetworkMessageContext(
                ServiceMessageContext.CreateEmpty(null!),
                new DataSetMetaDataRegistry(),
                new PubSubDiagnostics(PubSubDiagnosticsLevel.Low),
                TimeProvider.System);
        }

        private PubSubDissectionResult DissectUadp(in PubSubCaptureFrame frame)
        {
            try
            {
                if (TryDetectSecuredUadp(in frame, out PubSubDissectionResult secured))
                {
                    return secured;
                }

                PubSubNetworkMessage? message = UadpDecoder.Decode(frame.Data, m_context);
                if (message is null)
                {
                    return CreateUndecodable(frame, PubSubDissectionMessageType.Uadp, "UADP decoder rejected the frame.");
                }
                PubSubDissectionMessageType messageType = message.DataSetMessages.Count == 0
                    ? PubSubDissectionMessageType.Discovery
                    : PubSubDissectionMessageType.Uadp;
                return Project(frame, message, messageType);
            }
            catch (Exception ex) when (ex is FormatException || ex is ArgumentException || ex is InvalidOperationException)
            {
                return CreateUndecodable(frame, PubSubDissectionMessageType.Uadp, ex.Message);
            }
        }

        private async ValueTask<PubSubDissectionResult> DissectJsonAsync(
            PubSubCaptureFrame frame,
            CancellationToken cancellationToken)
        {
            try
            {
                PubSubNetworkMessage? message = await m_jsonDecoder.TryDecodeAsync(
                    frame.Data,
                    m_context,
                    cancellationToken).ConfigureAwait(false);
                if (message is null)
                {
                    return CreateUndecodable(frame, PubSubDissectionMessageType.Json, "JSON decoder rejected the frame.");
                }
                PubSubDissectionMessageType messageType = message.DataSetMessages.Count == 0
                    ? PubSubDissectionMessageType.Discovery
                    : PubSubDissectionMessageType.Json;
                return Project(frame, message, messageType);
            }
            catch (Exception ex) when (ex is FormatException || ex is ArgumentException || ex is InvalidOperationException)
            {
                return CreateUndecodable(frame, PubSubDissectionMessageType.Json, ex.Message);
            }
        }

        private static bool TryDetectSecuredUadp(
            in PubSubCaptureFrame frame,
            out PubSubDissectionResult result)
        {
            result = default!;
            if (!UadpDecoder.TryReadOuterPrefix(
                frame.Data,
                out int prefixLength,
                out bool securityEnabled,
                out PublisherId publisherId,
                out ushort writerGroupId) || !securityEnabled)
            {
                return false;
            }

            ReadOnlySpan<byte> data = frame.Data.Span;
            if (prefixLength > data.Length ||
                !UadpSecurityHeader.TryRead(
                    data[prefixLength..],
                    out UadpSecurityHeader header,
                    out _))
            {
                result = CreateUndecodable(
                    frame,
                    PubSubDissectionMessageType.Uadp,
                    "UADP SecurityHeader is malformed or truncated.");
                return true;
            }

            var flags = (UadpSecurityFlagsEncodingMask)header.SecurityFlags;
            bool encrypted = (flags & UadpSecurityFlagsEncodingMask.NetworkMessageEncrypted) != 0;
            bool signed = (flags & UadpSecurityFlagsEncodingMask.NetworkMessageSigned) != 0;
            result = new PubSubDissectionResult
            {
                Timestamp = frame.Timestamp,
                Direction = frame.Direction,
                TransportProfileUri = frame.TransportProfileUri,
                Endpoint = frame.Endpoint,
                Topic = frame.Topic,
                PayloadLength = frame.Data.Length,
                MessageType = PubSubDissectionMessageType.Uadp,
                SecurityState = encrypted
                    ? PubSubDissectionSecurityState.Encrypted
                    : PubSubDissectionSecurityState.Signed,
                PublisherId = publisherId,
                WriterGroupId = writerGroupId == 0 ? null : writerGroupId,
                SecurityTokenId = header.SecurityTokenId,
                IsDecoded = false,
                IsUndecodable = false,
                DiagnosticMessage = encrypted || signed
                    ? "encrypted (key required)"
                    : "SecurityHeader present with no signing or encryption flags."
            };
            return true;
        }

        private static PubSubDissectionResult Project(
            in PubSubCaptureFrame frame,
            PubSubNetworkMessage message,
            PubSubDissectionMessageType messageType)
        {
            List<ushort> writerIds = [];
            List<PubSubDissectedDataSet> dataSets = [];
            foreach (PubSubDataSetMessage dataSetMessage in message.DataSetMessages)
            {
                writerIds.Add(dataSetMessage.DataSetWriterId);
                dataSets.Add(ProjectDataSet(dataSetMessage));
            }

            return new PubSubDissectionResult
            {
                Timestamp = frame.Timestamp,
                Direction = frame.Direction,
                TransportProfileUri = frame.TransportProfileUri,
                Endpoint = frame.Endpoint,
                Topic = frame.Topic,
                PayloadLength = frame.Data.Length,
                MessageType = messageType,
                SecurityState = PubSubDissectionSecurityState.None,
                PublisherId = message.PublisherId,
                WriterGroupId = message.WriterGroupId,
                DataSetWriterIds = [.. writerIds],
                DataSets = [.. dataSets],
                IsDecoded = true,
                IsUndecodable = false
            };
        }

        private static PubSubDissectedDataSet ProjectDataSet(PubSubDataSetMessage dataSetMessage)
        {
            List<PubSubDissectedField> fields = [];
            foreach (DataSetField field in dataSetMessage.Fields)
            {
                fields.Add(new PubSubDissectedField
                {
                    Name = field.Name,
                    Value = field.Value,
                    StatusCode = field.StatusCode,
                    Encoding = field.Encoding
                });
            }

            return new PubSubDissectedDataSet
            {
                DataSetWriterId = dataSetMessage.DataSetWriterId,
                SequenceNumber = dataSetMessage.SequenceNumber,
                MessageType = dataSetMessage.MessageType,
                Status = dataSetMessage.Status,
                Fields = [.. fields]
            };
        }

        private static PubSubDissectionResult CreateUndecodable(
            in PubSubCaptureFrame frame,
            PubSubDissectionMessageType mapping,
            string diagnosticMessage)
        {
            return new PubSubDissectionResult
            {
                Timestamp = frame.Timestamp,
                Direction = frame.Direction,
                TransportProfileUri = frame.TransportProfileUri,
                Endpoint = frame.Endpoint,
                Topic = frame.Topic,
                PayloadLength = frame.Data.Length,
                MessageType = mapping,
                SecurityState = PubSubDissectionSecurityState.None,
                PublisherId = PublisherId.Null,
                IsDecoded = false,
                IsUndecodable = true,
                DiagnosticMessage = diagnosticMessage
            };
        }

        private static PubSubDissectionMessageType DetectMessageType(in PubSubCaptureFrame frame)
        {
            string profile = frame.TransportProfileUri;
            if (profile.Contains("json", StringComparison.OrdinalIgnoreCase))
            {
                return PubSubDissectionMessageType.Json;
            }
            if (profile.Contains("uadp", StringComparison.OrdinalIgnoreCase) ||
                profile.Contains("udp", StringComparison.OrdinalIgnoreCase))
            {
                return PubSubDissectionMessageType.Uadp;
            }
            ReadOnlySpan<byte> data = frame.Data.Span;
            for (int i = 0; i < data.Length; i++)
            {
                byte value = data[i];
                if (value == (byte)'{' || value == (byte)'[')
                {
                    return PubSubDissectionMessageType.Json;
                }
                if (!char.IsWhiteSpace((char)value))
                {
                    return PubSubDissectionMessageType.Uadp;
                }
            }
            return PubSubDissectionMessageType.Unknown;
        }

        private readonly PubSubNetworkMessageContext m_context;
        private readonly PubSubJsonDecoder m_jsonDecoder = new();
    }
}
