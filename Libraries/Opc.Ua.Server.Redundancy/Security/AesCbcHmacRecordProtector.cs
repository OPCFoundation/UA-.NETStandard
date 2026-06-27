/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Security.Cryptography;
using System.Text;

namespace Opc.Ua.Server.Redundancy
{
    /// <summary>
    /// Authenticated-encryption <see cref="IRecordProtector"/> using
    /// AES-256-CBC with HMAC-SHA256 in Encrypt-then-MAC construction. The MAC
    /// is verified before any decryption (no padding-oracle exposure), so a
    /// tampered or forged record is rejected fail-closed. The envelope is
    /// <c>[version:1][keyId:4 LE][IV:16][ciphertext][HMAC:32]</c>; the MAC
    /// covers the header + ciphertext. Distinct AES and MAC subkeys are
    /// derived from the supplied master key. Cross-target-framework safe
    /// (no AES-GCM dependency).
    /// </summary>
    public sealed class AesCbcHmacRecordProtector : IRecordProtector, IDisposable
    {
        /// <summary>
        /// Creates a protector from a master key (≥ 32 bytes) and a key
        /// identifier used for staged key rotation.
        /// </summary>
        /// <param name="masterKey">The master key (at least 32 bytes).</param>
        /// <param name="keyId">
        /// Identifies the key version; only records carrying the same id are
        /// accepted by <see cref="TryUnprotect"/>.
        /// </param>
        public AesCbcHmacRecordProtector(ReadOnlySpan<byte> masterKey, uint keyId = 1)
        {
            if (masterKey.Length < MinMasterKeyLength)
            {
                throw new ArgumentException(
                    $"Master key must be at least {MinMasterKeyLength} bytes.", nameof(masterKey));
            }
            m_keyId = keyId;
            byte[] master = masterKey.ToArray();
            try
            {
                m_aesKey = DeriveKey(master, "OpcUaDistributed-AES256-CBC");
                m_macKey = DeriveKey(master, "OpcUaDistributed-HMAC-SHA256");
            }
            finally
            {
                CryptoUtils.ZeroMemory(master);
            }
        }

        /// <inheritdoc/>
        public ByteString Protect(ByteString plaintext)
        {
            byte[] data = plaintext.IsNull ? Array.Empty<byte>() : plaintext.ToArray();

            byte[] cipher;
            byte[] iv;
            using (var aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = m_aesKey;
                // Let the algorithm generate a fresh random IV per record; do
                // not assign one explicitly (CA5401). The IV is authenticated
                // by the MAC below, so it is safe to ship in the clear.
                aes.GenerateIV();
                iv = aes.IV;
                using ICryptoTransform encryptor = aes.CreateEncryptor();
                cipher = encryptor.TransformFinalBlock(data, 0, data.Length);
            }

            int headerLength = HeaderLength;
            byte[] envelope = new byte[headerLength + cipher.Length + TagLength];
            envelope[0] = Version;
            BinaryPrimitives.WriteUInt32LittleEndian(envelope.AsSpan(1, 4), m_keyId);
            Buffer.BlockCopy(iv, 0, envelope, 5, IvLength);
            Buffer.BlockCopy(cipher, 0, envelope, headerLength, cipher.Length);

            byte[] tag = ComputeTag(envelope, headerLength + cipher.Length);
            Buffer.BlockCopy(tag, 0, envelope, headerLength + cipher.Length, TagLength);
            return new ByteString(envelope);
        }

        /// <inheritdoc/>
        public bool TryUnprotect(ByteString protectedRecord, out ByteString plaintext)
        {
            plaintext = default;
            if (protectedRecord.IsNull)
            {
                return false;
            }

            byte[] envelope = protectedRecord.ToArray();
            int headerLength = HeaderLength;
            if (envelope.Length < headerLength + TagLength || envelope[0] != Version)
            {
                return false;
            }
            if (BinaryPrimitives.ReadUInt32LittleEndian(envelope.AsSpan(1, 4)) != m_keyId)
            {
                return false;
            }

            int cipherLength = envelope.Length - headerLength - TagLength;

            // Verify the MAC before decrypting (Encrypt-then-MAC).
            byte[] expectedTag = ComputeTag(envelope, headerLength + cipherLength);
            var actualTag = new ReadOnlySpan<byte>(envelope, headerLength + cipherLength, TagLength);
            if (!CryptoUtils.FixedTimeEquals(expectedTag, actualTag))
            {
                return false;
            }

            byte[] iv = new byte[IvLength];
            Buffer.BlockCopy(envelope, 5, iv, 0, IvLength);

            byte[] data;
            using (var aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = m_aesKey;
                aes.IV = iv;
                using ICryptoTransform decryptor = aes.CreateDecryptor();
                data = decryptor.TransformFinalBlock(envelope, headerLength, cipherLength);
            }

            plaintext = new ByteString(data);
            return true;
        }

        /// <summary>
        /// Zeroizes the derived key material.
        /// </summary>
        public void Dispose()
        {
            CryptoUtils.ZeroMemory(m_aesKey);
            CryptoUtils.ZeroMemory(m_macKey);
        }

        private byte[] ComputeTag(byte[] buffer, int length)
        {
            using var hmac = new HMACSHA256(m_macKey);
            return hmac.ComputeHash(buffer, 0, length);
        }

        private static byte[] DeriveKey(byte[] masterKey, string label)
        {
            using var hmac = new HMACSHA256(masterKey);
            return hmac.ComputeHash(Encoding.ASCII.GetBytes(label));
        }

        private const byte Version = 1;
        private const int IvLength = 16;
        private const int TagLength = 32;
        private const int HeaderLength = 1 + 4 + IvLength;
        private const int MinMasterKeyLength = 32;

        private readonly uint m_keyId;
        private readonly byte[] m_aesKey;
        private readonly byte[] m_macKey;
    }
}
