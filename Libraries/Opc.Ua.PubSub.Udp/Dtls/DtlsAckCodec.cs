/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
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
        public static byte[] Encode(IReadOnlyList<DtlsRecordNumber> records)
        {
            if (records is null)
            {
                throw new ArgumentNullException(nameof(records));
            }

            byte[] output = new byte[2 + records.Count * 10];
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

        public static IReadOnlyList<DtlsRecordNumber> Decode(ReadOnlySpan<byte> body)
        {
            if (body.Length < 2)
            {
                throw new DtlsHandshakeException("DTLS ACK body is truncated.");
            }

            int length = BinaryPrimitives.ReadUInt16BigEndian(body.Slice(0, 2));
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

    internal readonly record struct DtlsRecordNumber(ushort Epoch, ulong SequenceNumber);
}
