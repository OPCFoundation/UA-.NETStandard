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
using System.Security.Cryptography;

namespace Opc.Ua.PubSub.Udp.Security.Dtls
{
    /// <summary>
    /// TLS 1.3 resumption-less key schedule from RFC 8446 §7.1.
    /// </summary>
    public sealed class DtlsKeySchedule
    {
        /// <summary>
        /// Initializes a new <see cref="DtlsKeySchedule"/>.
        /// </summary>
        public DtlsKeySchedule(DtlsCipherSuite cipherSuite)
        {
            CipherSuite = cipherSuite;
            HashAlgorithmName = cipherSuite is DtlsCipherSuite.TlsAes256GcmSha384
                or DtlsCipherSuite.TlsSha384Sha384
                ? HashAlgorithmName.SHA384
                : HashAlgorithmName.SHA256;
            HashLength = DtlsHkdf.GetHashLength(HashAlgorithmName);
        }

        /// <summary>
        /// TLS 1.3 cipher suite whose hash controls the schedule.
        /// </summary>
        public DtlsCipherSuite CipherSuite { get; }

        /// <summary>
        /// SHA-2 hash selected by <see cref="CipherSuite"/>.
        /// </summary>
        public HashAlgorithmName HashAlgorithmName { get; }

        /// <summary>
        /// Hash output size in bytes.
        /// </summary>
        public int HashLength { get; }

        /// <summary>
        /// Derives TLS 1.3 handshake and application traffic secrets.
        /// </summary>
        public DtlsTrafficSecrets DeriveTrafficSecrets(
            ReadOnlySpan<byte> sharedSecret,
            ReadOnlySpan<byte> handshakeTranscriptHash,
            ReadOnlySpan<byte> applicationTranscriptHash)
        {
            byte[] zero = new byte[HashLength];
            byte[] emptyHash = DtlsHkdf.HashData(HashAlgorithmName, ReadOnlySpan<byte>.Empty);
            byte[] earlySecret = [];
            byte[] derivedEarlySecret = [];
            byte[] handshakeSecret = [];
            byte[] derivedHandshakeSecret = [];
            byte[] masterSecret = [];
            try
            {
                earlySecret = DtlsHkdf.Extract(HashAlgorithmName, zero, zero);
                derivedEarlySecret = DeriveSecret(earlySecret, "derived", emptyHash);
                handshakeSecret = DtlsHkdf.Extract(HashAlgorithmName, derivedEarlySecret, sharedSecret);
                byte[] clientHandshakeTrafficSecret = DeriveSecret(
                    handshakeSecret,
                    "c hs traffic",
                    handshakeTranscriptHash);
                byte[] serverHandshakeTrafficSecret = DeriveSecret(
                    handshakeSecret,
                    "s hs traffic",
                    handshakeTranscriptHash);
                derivedHandshakeSecret = DeriveSecret(handshakeSecret, "derived", emptyHash);
                masterSecret = DtlsHkdf.Extract(HashAlgorithmName, derivedHandshakeSecret, ReadOnlySpan<byte>.Empty);
                byte[] clientApplicationTrafficSecret = DeriveSecret(
                    masterSecret,
                    "c ap traffic",
                    applicationTranscriptHash);
                byte[] serverApplicationTrafficSecret = DeriveSecret(
                    masterSecret,
                    "s ap traffic",
                    applicationTranscriptHash);
                return new DtlsTrafficSecrets(
                    clientHandshakeTrafficSecret,
                    serverHandshakeTrafficSecret,
                    clientApplicationTrafficSecret,
                    serverApplicationTrafficSecret,
                    FinishedKey(clientHandshakeTrafficSecret),
                    FinishedKey(serverHandshakeTrafficSecret));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(zero);
                CryptographicOperations.ZeroMemory(emptyHash);
                CryptographicOperations.ZeroMemory(earlySecret);
                CryptographicOperations.ZeroMemory(derivedEarlySecret);
                CryptographicOperations.ZeroMemory(handshakeSecret);
                CryptographicOperations.ZeroMemory(derivedHandshakeSecret);
                CryptographicOperations.ZeroMemory(masterSecret);
            }
        }

        /// <summary>
        /// RFC 8446 §7.1 Derive-Secret.
        /// </summary>
        public byte[] DeriveSecret(ReadOnlySpan<byte> secret, string label, ReadOnlySpan<byte> transcriptHash)
        {
            if (label is null)
            {
                throw new ArgumentNullException(nameof(label));
            }

            return DtlsHkdf.ExpandLabel(HashAlgorithmName, secret, label, transcriptHash, HashLength);
        }

        /// <summary>
        /// RFC 8446 §4.4.4 Finished key derivation.
        /// </summary>
        public byte[] FinishedKey(ReadOnlySpan<byte> baseKey)
        {
            return DtlsHkdf.ExpandLabel(HashAlgorithmName, baseKey, "finished", ReadOnlySpan<byte>.Empty, HashLength);
        }

        /// <summary>
        /// Computes the Finished MAC over the transcript hash.
        /// </summary>
        public byte[] ComputeFinished(ReadOnlySpan<byte> finishedKey, ReadOnlySpan<byte> transcriptHash)
        {
            byte[] key = finishedKey.ToArray();
            try
            {
                using HMAC hmac = DtlsHkdf.CreateHmac(HashAlgorithmName, key);
                return hmac.ComputeHash(transcriptHash.ToArray());
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }
    }

    /// <summary>
    /// TLS 1.3 traffic secrets derived by <see cref="DtlsKeySchedule"/>.
    /// </summary>
    public sealed record DtlsTrafficSecrets(
        byte[] ClientHandshakeTrafficSecret,
        byte[] ServerHandshakeTrafficSecret,
        byte[] ClientApplicationTrafficSecret,
        byte[] ServerApplicationTrafficSecret,
        byte[] ClientFinishedKey,
        byte[] ServerFinishedKey);
}
