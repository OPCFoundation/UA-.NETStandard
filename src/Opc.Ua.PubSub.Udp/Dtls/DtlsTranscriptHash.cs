/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace Opc.Ua.PubSub.Udp.Dtls
{
    /// <summary>
    /// Incremental TLS 1.3 transcript hash for RFC 8446 §4.4.1.
    /// </summary>
    public sealed class DtlsTranscriptHash
    {
        /// <summary>
        /// Initializes a new <see cref="DtlsTranscriptHash"/>.
        /// </summary>
        public DtlsTranscriptHash(HashAlgorithmName hashAlgorithmName)
        {
            HashAlgorithmName = hashAlgorithmName;
        }

        /// <summary>
        /// SHA-2 hash used for the transcript.
        /// </summary>
        public HashAlgorithmName HashAlgorithmName { get; }

        /// <summary>
        /// Appends a complete handshake message as it appears on the wire.
        /// </summary>
        public void Append(ReadOnlySpan<byte> handshakeMessage)
        {
            m_messages.Add(handshakeMessage.ToArray());
        }

        /// <summary>
        /// Computes the transcript hash over all appended handshake messages.
        /// </summary>
        public byte[] GetHash()
        {
            int length = 0;
            foreach (byte[] message in m_messages)
            {
                length += message.Length;
            }

            byte[] transcript = new byte[length];
            int offset = 0;
            foreach (byte[] message in m_messages)
            {
                Buffer.BlockCopy(message, 0, transcript, offset, message.Length);
                offset += message.Length;
            }

            try
            {
                return DtlsHkdf.HashData(HashAlgorithmName, transcript);
            }
            finally
            {
                CryptoUtils.ZeroMemory(transcript);
            }
        }

        private readonly List<byte[]> m_messages = [];
    }
}
