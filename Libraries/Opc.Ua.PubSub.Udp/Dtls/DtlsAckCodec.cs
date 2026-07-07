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
using System.Collections.Generic;

namespace Opc.Ua.PubSub.Udp.Dtls
{
    /// <summary>
    /// DTLS 1.3 ACK message codec from RFC 9147 §7.
    /// </summary>
    internal static class DtlsAckCodec
    {
        /// <summary>
        /// Encodes a list of record numbers into a DTLS 1.3 ACK message body.
        /// </summary>
        public static byte[] Encode(IReadOnlyList<DtlsRecordNumber> records)
        {
            if (records is null)
            {
                throw new ArgumentNullException(nameof(records));
            }

            byte[] output = new byte[2 + (records.Count * 10)];
            BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(0, 2), (ushort)(records.Count * 10));
            int offset = 2;
            foreach (DtlsRecordNumber record in records)
            {
                BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(offset, 2), record.Epoch);
                BinaryPrimitives.WriteUInt64BigEndian(output.AsSpan(offset + 2, 8), record.SequenceNumber);
                offset += 10;
            }

            return output;
        }

        /// <summary>
        /// Decodes a DTLS 1.3 ACK message body into the acknowledged record numbers.
        /// </summary>
        public static IReadOnlyList<DtlsRecordNumber> Decode(ReadOnlySpan<byte> body)
        {
            if (body.Length < 2)
            {
                throw new DtlsHandshakeException("DTLS ACK body is truncated.");
            }

            int length = BinaryPrimitives.ReadUInt16BigEndian(body[..2]);
            if (length != body.Length - 2 || length % 10 != 0)
            {
                throw new DtlsHandshakeException("DTLS ACK vector length is invalid.");
            }

            var records = new List<DtlsRecordNumber>();
            for (int offset = 2; offset < body.Length; offset += 10)
            {
                records.Add(new DtlsRecordNumber(
                    BinaryPrimitives.ReadUInt16BigEndian(body.Slice(offset, 2)),
                    BinaryPrimitives.ReadUInt64BigEndian(body.Slice(offset + 2, 8))));
            }

            return records;
        }
    }

    /// <summary>
    /// Identifies a DTLS record by its epoch and sequence number for ACK processing.
    /// </summary>
    internal readonly record struct DtlsRecordNumber(ushort Epoch, ulong SequenceNumber);
}
