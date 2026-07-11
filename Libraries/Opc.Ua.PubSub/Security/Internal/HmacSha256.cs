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

namespace Opc.Ua.PubSub.Security.Internal
{
    /// <summary>
    /// HMAC-SHA-256 helpers used by the PubSub-AesXxx-CTR policies.
    /// Centralises the multi-TFM polyfill — net6+ exposes static
    /// <c>HMACSHA256.HashData</c>; older TFMs require an instance.
    /// </summary>
    internal static class HmacSha256
    {
        /// <summary>
        /// Output size, in bytes, of HMAC-SHA-256.
        /// </summary>
        public const int OutputLength = 32;

        /// <summary>
        /// Computes <c>HMAC-SHA-256(key, data)</c> into the destination
        /// span (must be at least <see cref="OutputLength"/> bytes).
        /// </summary>
        /// <param name="key">HMAC key (any non-empty length).</param>
        /// <param name="data">Bytes to authenticate.</param>
        /// <param name="destination">Destination span receiving the MAC.</param>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="destination"/> is shorter than
        /// <see cref="OutputLength"/> bytes.
        /// </exception>
        /// <exception cref="CryptographicException">
        /// Thrown when the platform HMAC-SHA-256 primitive returns an
        /// unexpected output length.
        /// </exception>
        public static void HashData(
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> data,
            Span<byte> destination)
        {
            if (destination.Length < OutputLength)
            {
                throw new ArgumentException(
                    $"Destination must be at least {OutputLength} bytes.",
                    nameof(destination));
            }

#if NET6_0_OR_GREATER
            int written = HMACSHA256.HashData(key, data, destination);
            if (written != OutputLength)
            {
                throw new CryptographicException(
                    "Unexpected HMAC-SHA-256 output length.");
            }
#else
            byte[] hmacKey = key.ToArray();
            try
            {
                using var hmac = new HMACSHA256(hmacKey);
                byte[] computed = hmac.ComputeHash(data.ToArray());
                computed.AsSpan(0, OutputLength).CopyTo(destination);
            }
            finally
            {
                ClearSensitiveBuffer(hmacKey);
            }
#endif
        }

#if !NET6_0_OR_GREATER
        private static void ClearSensitiveBuffer(byte[] buffer)
        {
            Array.Clear(buffer, 0, buffer.Length);
        }
#endif
    }
}
