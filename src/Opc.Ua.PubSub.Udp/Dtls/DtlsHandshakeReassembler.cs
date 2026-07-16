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

namespace Opc.Ua.PubSub.Udp.Dtls
{
    /// <summary>
    /// DTLS 1.3 handshake fragmentation and reassembly per RFC 9147 §5.3.
    /// </summary>
    internal sealed class DtlsHandshakeReassembler
    {
        /// <summary>
        /// Adds a received handshake fragment and returns the reassembled message when complete.
        /// </summary>
        /// <exception cref="DtlsHandshakeException"></exception>
        public bool TryAdd(DtlsHandshakeFrame frame, out byte[]? message)
        {
            // Defense-in-depth (SA-DTLS-HS-07): the MessageLength is an
            // attacker-controlled 24-bit field. Bound the per-message buffer and
            // the number of concurrent in-flight messages so a hostile peer cannot
            // drive unbounded allocation if this reassembler is wired into the
            // live datagram path.
            if (frame.MessageLength > MaxHandshakeMessageLength)
            {
                throw new DtlsHandshakeException(
                    "DTLS handshake message exceeds the maximum reassembly size.");
            }

            if (frame.FragmentOffset == 0 && frame.Fragment.Length == frame.MessageLength)
            {
                message = frame.Fragment;
                return true;
            }

            if (!m_messages.TryGetValue(frame.MessageSequence, out PendingMessage? pending))
            {
                if (m_messages.Count >= MaxConcurrentMessages)
                {
                    throw new DtlsHandshakeException(
                        "Too many concurrent in-flight DTLS handshake messages.");
                }

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

        /// <summary>
        /// Splits a handshake message body into wire fragments no larger than the limit.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
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

        /// <summary>
        /// Tracks the received fragments of a single in-flight handshake message.
        /// </summary>
        private sealed class PendingMessage
        {
            /// <summary>
            /// Initializes a new <see cref="PendingMessage"/> for the given type and length.
            /// </summary>
            public PendingMessage(DtlsHandshakeType messageType, int length)
            {
                MessageType = messageType;
                Buffer = new byte[length];
                Received = new bool[length];
            }

            /// <summary>
            /// Handshake message type being reassembled.
            /// </summary>
            public DtlsHandshakeType MessageType { get; }

            /// <summary>
            /// Backing buffer that accumulates fragment payloads.
            /// </summary>
            public byte[] Buffer { get; }

            /// <summary>
            /// Indicates whether every byte of the message has been received.
            /// </summary>
            public bool IsComplete => m_receivedCount == Buffer.Length;

            /// <summary>
            /// Copies a fragment into the buffer and tracks the bytes received.
            /// </summary>
            /// <exception cref="DtlsHandshakeException"></exception>
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

        private const int MaxHandshakeMessageLength = 64 * 1024;
        private const int MaxConcurrentMessages = 16;
    }
}
