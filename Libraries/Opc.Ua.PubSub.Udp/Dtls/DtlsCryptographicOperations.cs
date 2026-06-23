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

namespace Opc.Ua.PubSub.Udp.Dtls
{
    /// <summary>
    /// Small compatibility wrapper for constant-time comparison and zeroization.
    /// </summary>
    internal static class DtlsCryptographicOperations
    {
        /// <summary>
        /// Zeros a buffer before it leaves scope.
        /// </summary>
        public static void ZeroMemory(Span<byte> buffer)
        {
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(buffer);
#else
            buffer.Clear();
#endif
        }

        /// <summary>
        /// Compares two buffers in constant time when their lengths match.
        /// </summary>
        public static bool FixedTimeEquals(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
        {
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
            return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(left, right);
#else
            if (left.Length != right.Length)
            {
                return false;
            }

            int different = 0;
            for (int ii = 0; ii < left.Length; ii++)
            {
                different |= left[ii] ^ right[ii];
            }

            return different == 0;
#endif
        }
    }
}
