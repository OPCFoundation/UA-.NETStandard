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
using Opc.Ua.PubSub.Encoding;
using JsonDataSetMessageV2 = Opc.Ua.PubSub.Encoding.Json.JsonDataSetMessage;
using JsonNetworkMessageV2 = Opc.Ua.PubSub.Encoding.Json.JsonNetworkMessage;
using UadpDataSetMessageV2 = Opc.Ua.PubSub.Encoding.Uadp.UadpDataSetMessage;
using UadpNetworkMessageType = Opc.Ua.PubSub.Encoding.Uadp.UadpNetworkMessageType;
using UadpNetworkMessageV2 = Opc.Ua.PubSub.Encoding.Uadp.UadpNetworkMessage;

namespace Opc.Ua.PubSub.Transcoding
{
    /// <summary>
    /// Default <see cref="INetworkMessageProfileProjector"/>. Re-materialises
    /// the transport-neutral message tree into a concrete UADP or JSON
    /// record, preserving the shared identification and payload while
    /// translating the profile-specific header fields. Stateless and
    /// thread-safe.
    /// </summary>
    /// <remarks>
    /// Implements the mapping between the UADP
    /// (<see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4">
    /// Part 14 §7.2.4</see>) and JSON
    /// (<see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.5">
    /// Part 14 §7.2.5</see>) NetworkMessage mappings.
    /// </remarks>
    public sealed class NetworkMessageProfileProjector : INetworkMessageProfileProjector
    {
        private const UadpDataSetMessageContentMask k_uadpDataSetMask
            = UadpDataSetMessageContentMask.SequenceNumber |
                UadpDataSetMessageContentMask.Status |
                UadpDataSetMessageContentMask.Timestamp |
                UadpDataSetMessageContentMask.MajorVersion |
                UadpDataSetMessageContentMask.MinorVersion;

        /// <summary>
        /// Shared stateless instance.
        /// </summary>
        public static NetworkMessageProfileProjector Instance { get; } = new();

        /// <inheritdoc/>
        public PubSubNetworkMessage Project(
            PubSubNetworkMessage source,
            TranscodeEncoding targetEncoding,
            TranscodeTargetOptions options,
            TranscodeContext context)
        {
            if (source is null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            TranscodeEncoding sourceEncoding = source.EncodingOf();
            if (sourceEncoding == targetEncoding)
            {
                return ApplySameEncodingOptions(source, targetEncoding, options);
            }
            return targetEncoding == TranscodeEncoding.Uadp
                ? ToUadp(source, options)
                : ToJson(source, options);
        }

        private static PubSubNetworkMessage ApplySameEncodingOptions(
            PubSubNetworkMessage source,
            TranscodeEncoding encoding,
            TranscodeTargetOptions options)
        {
            if (encoding == TranscodeEncoding.Uadp && source is UadpNetworkMessageV2 uadp)
            {
                if (options.FieldEncoding is null && options.PreserveMetaDataVersion)
                {
                    return uadp;
                }
                return uadp with
                {
                    DataSetMessages = MapDataSetMessages(
                        uadp.DataSetMessages,
                        dsm => RebuildUadpDataSetMessage(dsm, options))
                };
            }
            if (encoding == TranscodeEncoding.Json && source is JsonNetworkMessageV2 json)
            {
                bool single = options.JsonSingleMessageMode && json.DataSetMessages.Count == 1;
                JsonNetworkMessageContentMask mask = single
                    ? json.ContentMask | JsonNetworkMessageContentMask.SingleDataSetMessage
                    : json.ContentMask & ~JsonNetworkMessageContentMask.SingleDataSetMessage;
                if (!options.PreserveMetaDataVersion)
                {
                    return json with
                    {
                        SingleMessageMode = single,
                        ContentMask = mask,
                        DataSetMessages = MapDataSetMessages(
                            json.DataSetMessages,
                            dsm => RebuildJsonDataSetMessage(dsm, options))
                    };
                }
                if (json.SingleMessageMode == single && json.ContentMask == mask)
                {
                    return json;
                }
                return json with { SingleMessageMode = single, ContentMask = mask };
            }
            return source;
        }

        private static UadpNetworkMessageV2 ToUadp(
            PubSubNetworkMessage source,
            TranscodeTargetOptions options)
        {
            Uuid classId = ExtractDataSetClassId(source);
            UadpNetworkMessageContentMask mask = UadpNetworkMessageContentMask.PayloadHeader;
            if (!source.PublisherId.IsNull)
            {
                mask |= UadpNetworkMessageContentMask.PublisherId;
            }
            if (source.WriterGroupId is ushort groupId && groupId != 0)
            {
                mask |= UadpNetworkMessageContentMask.GroupHeader |
                    UadpNetworkMessageContentMask.WriterGroupId;
            }
            if (classId != Uuid.Empty)
            {
                mask |= UadpNetworkMessageContentMask.DataSetClassId;
            }

            return new UadpNetworkMessageV2
            {
                PublisherId = source.PublisherId,
                WriterGroupId = source.WriterGroupId,
                DataSetClassId = classId,
                ContentMask = mask,
                MetaData = source.MetaData,
                MessageType = UadpNetworkMessageType.DataSetMessage,
                DataSetMessages = MapDataSetMessages(
                    source.DataSetMessages,
                    dsm => RebuildUadpDataSetMessage(dsm, options))
            };
        }

        private static JsonNetworkMessageV2 ToJson(
            PubSubNetworkMessage source,
            TranscodeTargetOptions options)
        {
            Uuid classId = ExtractDataSetClassId(source);
            bool single = options.JsonSingleMessageMode && source.DataSetMessages.Count == 1;
            JsonNetworkMessageContentMask mask = JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.DataSetMessageHeader;
            if (!source.PublisherId.IsNull)
            {
                mask |= JsonNetworkMessageContentMask.PublisherId;
            }
            if (classId != Uuid.Empty)
            {
                mask |= JsonNetworkMessageContentMask.DataSetClassId;
            }
            if (single)
            {
                mask |= JsonNetworkMessageContentMask.SingleDataSetMessage;
            }

            return new JsonNetworkMessageV2
            {
                MessageId = Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture),
                MessageType = JsonNetworkMessageV2.MessageTypeData,
                PublisherId = source.PublisherId,
                WriterGroupId = source.WriterGroupId,
                DataSetClassId = classId,
                ContentMask = mask,
                SingleMessageMode = single,
                MetaData = source.MetaData,
                DataSetMessages = MapDataSetMessages(
                    source.DataSetMessages,
                    dsm => RebuildJsonDataSetMessage(dsm, options))
            };
        }

        private static UadpDataSetMessageV2 RebuildUadpDataSetMessage(
            PubSubDataSetMessage source,
            TranscodeTargetOptions options)
        {
            var existing = source as UadpDataSetMessageV2;
            PubSubFieldEncoding fieldEncoding = options.FieldEncoding
                ?? existing?.FieldEncoding
                ?? PubSubFieldEncoding.Variant;

            // Preserve the source UADP DataSetMessage content mask on
            // UADP -> UADP rebuilds so the wire representation is not
            // silently changed; fall back to the default mask only when
            // the source is a different mapping (e.g. JSON -> UADP).
            UadpDataSetMessageContentMask contentMask = existing?.ContentMask ?? k_uadpDataSetMask;
            if (!options.PreserveMetaDataVersion)
            {
                contentMask &= ~(UadpDataSetMessageContentMask.MajorVersion |
                    UadpDataSetMessageContentMask.MinorVersion);
            }

            return new UadpDataSetMessageV2
            {
                DataSetWriterId = source.DataSetWriterId,
                SequenceNumber = source.SequenceNumber,
                Timestamp = source.Timestamp,
                Status = source.Status,
                MessageType = source.MessageType,
                MetaDataVersion = options.PreserveMetaDataVersion
                    ? source.MetaDataVersion
                    : new ConfigurationVersionDataType(),
                Fields = RetargetFieldEncoding(source.Fields, options.FieldEncoding),
                FieldEncoding = fieldEncoding,
                ContentMask = contentMask
            };
        }

        private static JsonDataSetMessageV2 RebuildJsonDataSetMessage(
            PubSubDataSetMessage source,
            TranscodeTargetOptions options)
        {
            return new JsonDataSetMessageV2
            {
                DataSetWriterId = source.DataSetWriterId,
                SequenceNumber = source.SequenceNumber,
                Timestamp = source.Timestamp,
                Status = source.Status,
                MessageType = source.MessageType,
                MetaDataVersion = options.PreserveMetaDataVersion
                    ? source.MetaDataVersion
                    : new ConfigurationVersionDataType(),
                Fields = RetargetFieldEncoding(source.Fields, options.FieldEncoding)
            };
        }

        private static ArrayOf<DataSetField> RetargetFieldEncoding(
            ArrayOf<DataSetField> fields,
            PubSubFieldEncoding? fieldEncoding)
        {
            if (fieldEncoding is not { } encoding || fields.Count == 0)
            {
                return fields;
            }
            var mapped = new List<DataSetField>(fields.Count);
            for (int i = 0; i < fields.Count; i++)
            {
                mapped.Add(fields[i] with { Encoding = encoding });
            }
            return mapped;
        }

        private static ArrayOf<PubSubDataSetMessage> MapDataSetMessages(
            ArrayOf<PubSubDataSetMessage> source,
            Func<PubSubDataSetMessage, PubSubDataSetMessage> selector)
        {
            var mapped = new List<PubSubDataSetMessage>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                mapped.Add(selector(source[i]));
            }
            return mapped;
        }

        private static Uuid ExtractDataSetClassId(PubSubNetworkMessage source)
        {
            return source switch
            {
                UadpNetworkMessageV2 uadp => uadp.DataSetClassId,
                JsonNetworkMessageV2 json => json.DataSetClassId,
                _ => Uuid.Empty
            };
        }
    }
}
