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
