// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.PubSub.Encoding;
using UadpDataSetMessageV2 = Opc.Ua.PubSub.Encoding.Uadp.UadpDataSetMessage;

namespace Opc.Ua.PubSub.Transcoding
{
    /// <summary>
    /// Re-encodes every DataSetField to a target field encoding
    /// (Variant / RawData / DataValue). Sets the per-field encoding on
    /// each <see cref="DataSetField"/> and, for UADP DataSetMessages, the
    /// per-message <c>FieldEncoding</c> (DataSetFlags1).
    /// </summary>
    /// <remarks>
    /// Implements the field-encoding selection of
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.5.4">
    /// Part 14 §7.2.4.5.4 DataSetFlags1</see>. RawData requires the
    /// receiver to resolve metadata; the transcoder preserves the
    /// declared metadata so RawData round-trips.
    /// </remarks>
    public sealed class FieldEncodingTransform : IPubSubMessageTransform
    {
        private readonly PubSubFieldEncoding m_encoding;

        /// <summary>
        /// Initializes a new <see cref="FieldEncodingTransform"/>.
        /// </summary>
        /// <param name="encoding">Target field encoding.</param>
        public FieldEncodingTransform(PubSubFieldEncoding encoding)
        {
            m_encoding = encoding;
        }

        /// <inheritdoc/>
        public ValueTask<PubSubNetworkMessage?> TransformAsync(
            PubSubNetworkMessage message,
            TranscodeContext context,
            CancellationToken cancellationToken = default)
        {
            if (message is null)
            {
                throw new ArgumentNullException(nameof(message));
            }
            if (message.DataSetMessages.Count == 0)
            {
                return new ValueTask<PubSubNetworkMessage?>(message);
            }

            var mapped = new List<PubSubDataSetMessage>(message.DataSetMessages.Count);
            for (int i = 0; i < message.DataSetMessages.Count; i++)
            {
                PubSubDataSetMessage dsm = message.DataSetMessages[i];
                PubSubDataSetMessage updated = dsm with
                {
                    Fields = SetFieldEncoding(dsm.Fields, m_encoding)
                };
                if (updated is UadpDataSetMessageV2 uadp)
                {
                    updated = uadp with { FieldEncoding = m_encoding };
                }
                mapped.Add(updated);
            }
            return new ValueTask<PubSubNetworkMessage?>(
                message with { DataSetMessages = mapped });
        }

        private static ArrayOf<DataSetField> SetFieldEncoding(
            ArrayOf<DataSetField> fields,
            PubSubFieldEncoding encoding)
        {
            if (fields.Count == 0)
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
    }
}
