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

namespace Opc.Ua.PubSub.Udp.Dtls
{
    /// <summary>
    /// RFC 5869 HKDF and RFC 8446 HKDF-Expand-Label helpers.
    /// </summary>
    public static class DtlsHkdf
    {
        /// <summary>
        /// RFC 5869 HKDF-Extract.
        /// </summary>
        public static byte[] Extract(HashAlgorithmName hashAlgorithmName, ReadOnlySpan<byte> salt, ReadOnlySpan<byte> inputKeyingMaterial)
        {
            int hashLength = GetHashLength(hashAlgorithmName);
            byte[] actualSalt = salt.IsEmpty ? new byte[hashLength] : salt.ToArray();
            try
            {
                using HMAC hmac = CreateHmac(hashAlgorithmName, actualSalt);
                return hmac.ComputeHash(inputKeyingMaterial.ToArray());
            }
            finally
            {
                DtlsCryptographicOperations.ZeroMemory(actualSalt);
            }
        }

        /// <summary>
        /// RFC 5869 HKDF-Expand.
        /// </summary>
        public static byte[] Expand(
            HashAlgorithmName hashAlgorithmName,
            ReadOnlySpan<byte> pseudoRandomKey,
            ReadOnlySpan<byte> info,
            int length)
        {
            if (length < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }

            int hashLength = GetHashLength(hashAlgorithmName);
            if (length > 255 * hashLength)
            {
                throw new ArgumentOutOfRangeException(nameof(length), "HKDF output is limited to 255 hash blocks.");
            }

            byte[] output = new byte[length];
            byte[] previous = [];
            int offset = 0;
            byte counter = 1;
            try
            {
                using HMAC hmac = CreateHmac(hashAlgorithmName, pseudoRandomKey.ToArray());
                while (offset < length)
                {
                    hmac.Initialize();
                    if (previous.Length > 0)
                    {
                        _ = hmac.TransformBlock(previous, 0, previous.Length, previous, 0);
                    }

                    byte[] infoBytes = info.ToArray();
                    try
                    {
                        if (infoBytes.Length > 0)
                        {
                            _ = hmac.TransformBlock(infoBytes, 0, infoBytes.Length, infoBytes, 0);
                        }

                        byte[] counterBytes = [counter];
                        _ = hmac.TransformFinalBlock(counterBytes, 0, counterBytes.Length);
                    }
                    finally
                    {
                        DtlsCryptographicOperations.ZeroMemory(infoBytes);
                    }

                    previous = hmac.Hash ?? throw new CryptographicException("HKDF HMAC did not produce a hash.");
                    int toCopy = Math.Min(previous.Length, length - offset);
                    Buffer.BlockCopy(previous, 0, output, offset, toCopy);
                    offset += toCopy;
                    counter++;
                }
            }
            finally
            {
                DtlsCryptographicOperations.ZeroMemory(previous);
            }

            return output;
        }

        /// <summary>
        /// RFC 8446 §7.1 HKDF-Expand-Label.
        /// </summary>
        public static byte[] ExpandLabel(
            HashAlgorithmName hashAlgorithmName,
            ReadOnlySpan<byte> secret,
            string label,
            ReadOnlySpan<byte> context,
            int length)
        {
            if (label is null)
            {
                throw new ArgumentNullException(nameof(label));
            }

            byte[] labelBytes = System.Text.Encoding.ASCII.GetBytes("tls13 " + label);
            byte[] info = new byte[2 + 1 + labelBytes.Length + 1 + context.Length];
            info[0] = (byte)(length >> 8);
            info[1] = (byte)length;
            info[2] = (byte)labelBytes.Length;
            Buffer.BlockCopy(labelBytes, 0, info, 3, labelBytes.Length);
            info[3 + labelBytes.Length] = (byte)context.Length;
            context.CopyTo(info.AsSpan(4 + labelBytes.Length));
            try
            {
                return Expand(hashAlgorithmName, secret, info, length);
            }
            finally
            {
                DtlsCryptographicOperations.ZeroMemory(labelBytes);
                DtlsCryptographicOperations.ZeroMemory(info);
            }
        }

        /// <summary>
        /// Hashes data with the selected SHA-2 algorithm.
        /// </summary>
        public static byte[] HashData(HashAlgorithmName hashAlgorithmName, ReadOnlySpan<byte> data)
        {
#if NET8_0_OR_GREATER
            return hashAlgorithmName.Name switch
            {
                "SHA256" => SHA256.HashData(data),
                "SHA384" => SHA384.HashData(data),
                _ => throw new NotSupportedException("Only SHA-256 and SHA-384 are supported for DTLS 1.3.")
            };
#else
            using HashAlgorithm hash = hashAlgorithmName.Name switch
            {
                "SHA256" => SHA256.Create(),
                "SHA384" => SHA384.Create(),
                _ => throw new NotSupportedException("Only SHA-256 and SHA-384 are supported for DTLS 1.3.")
            };
            return hash.ComputeHash(data.ToArray());
#endif
        }

        /// <summary>
        /// Gets the output size for a DTLS SHA-2 hash.
        /// </summary>
        public static int GetHashLength(HashAlgorithmName hashAlgorithmName)
        {
            return hashAlgorithmName.Name switch
            {
                "SHA256" => 32,
                "SHA384" => 48,
                _ => throw new NotSupportedException("Only SHA-256 and SHA-384 are supported for DTLS 1.3.")
            };
        }

        internal static HMAC CreateHmac(HashAlgorithmName hashAlgorithmName, byte[] key)
        {
            return hashAlgorithmName.Name switch
            {
                "SHA256" => new HMACSHA256(key),
                "SHA384" => new HMACSHA384(key),
                _ => throw new NotSupportedException("Only SHA-256 and SHA-384 are supported for DTLS 1.3.")
            };
        }
    }
}
