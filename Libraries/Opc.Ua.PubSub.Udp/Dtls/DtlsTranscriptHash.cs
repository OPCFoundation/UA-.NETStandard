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
                CryptographicOperations.ZeroMemory(transcript);
            }
        }

        private readonly List<byte[]> m_messages = [];
    }
}
