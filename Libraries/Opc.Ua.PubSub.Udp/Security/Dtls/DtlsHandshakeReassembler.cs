/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Collections.Generic;

namespace Opc.Ua.PubSub.Udp.Security.Dtls
{
    /// <summary>
    /// DTLS 1.3 handshake fragmentation and reassembly per RFC 9147 §5.3.
    /// </summary>
    internal sealed class DtlsHandshakeReassembler
    {
        public bool TryAdd(DtlsHandshakeFrame frame, out byte[]? message)
        {
            if (frame.FragmentOffset == 0 && frame.Fragment.Length == frame.MessageLength)
            {
                message = frame.Fragment;
                return true;
            }

            if (!m_messages.TryGetValue(frame.MessageSequence, out PendingMessage? pending))
            {
                pending = new PendingMessage(frame.MessageType, frame.MessageLength);
                m_messages.Add(frame.MessageSequence, pending);
            }

            if (pending.MessageType != frame.MessageType || pending.Buffer.Length != frame.MessageLength)
            {
                throw new DtlsHandshakeException("Conflicting DTLS handshake fragments for the same message_seq.");
            }

            pending.Add(frame.FragmentOffset, frame.Fragment);
            if (pending.IsComplete)
            {
                m_messages.Remove(frame.MessageSequence);
                message = pending.Buffer;
                return true;
            }

            message = null;
            return false;
        }

        public static IReadOnlyList<byte[]> Fragment(
            DtlsHandshakeType messageType,
            ushort messageSequence,
            ReadOnlySpan<byte> body,
            int maxFragmentLength)
        {
            if (maxFragmentLength <= DtlsHandshakeCodec.HandshakeHeaderLength)
            {
                throw new ArgumentOutOfRangeException(nameof(maxFragmentLength));
            }

            int payloadLimit = maxFragmentLength - DtlsHandshakeCodec.HandshakeHeaderLength;
            var fragments = new List<byte[]>();
            int offset = 0;
            do
            {
                int fragmentLength = Math.Min(payloadLimit, body.Length - offset);
                byte[] fragment = new byte[DtlsHandshakeCodec.HandshakeHeaderLength + fragmentLength];
                fragment[0] = (byte)messageType;
                DtlsHandshakeCodec.WriteUInt24(fragment.AsSpan(1, 3), body.Length);
                System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(fragment.AsSpan(4, 2), messageSequence);
                DtlsHandshakeCodec.WriteUInt24(fragment.AsSpan(6, 3), offset);
                DtlsHandshakeCodec.WriteUInt24(fragment.AsSpan(9, 3), fragmentLength);
                body.Slice(offset, fragmentLength).CopyTo(fragment.AsSpan(DtlsHandshakeCodec.HandshakeHeaderLength));
                fragments.Add(fragment);
                offset += fragmentLength;
            }
            while (offset < body.Length || (body.Length == 0 && fragments.Count == 0));

            return fragments;
        }

        private sealed class PendingMessage
        {
            public PendingMessage(DtlsHandshakeType messageType, int length)
            {
                MessageType = messageType;
                Buffer = new byte[length];
                Received = new bool[length];
            }

            public DtlsHandshakeType MessageType { get; }

            public byte[] Buffer { get; }

            public bool IsComplete => m_receivedCount == Buffer.Length;

            public void Add(int offset, byte[] fragment)
            {
                if (offset < 0 || offset + fragment.Length > Buffer.Length)
                {
                    throw new DtlsHandshakeException("DTLS handshake fragment is outside the message bounds.");
                }

                System.Buffer.BlockCopy(fragment, 0, Buffer, offset, fragment.Length);
                for (int ii = 0; ii < fragment.Length; ii++)
                {
                    if (!Received[offset + ii])
                    {
                        Received[offset + ii] = true;
                        m_receivedCount++;
                    }
                }
            }

            private bool[] Received { get; }

            private int m_receivedCount;
        }

        private readonly Dictionary<ushort, PendingMessage> m_messages = [];
    }
}

