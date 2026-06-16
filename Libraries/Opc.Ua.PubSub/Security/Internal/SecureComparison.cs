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
using System.Runtime.CompilerServices;
#if !NETFRAMEWORK
using System.Security.Cryptography;
#endif

namespace Opc.Ua.PubSub.Security.Internal
{
    /// <summary>
    /// Multi-TFM polyfill for constant-time byte-array comparison.
    /// Forwards to <c>System.Security.Cryptography.CryptographicOperations.FixedTimeEquals</c>
    /// on .NET Standard 2.1 and modern .NET; falls back to a manual
    /// XOR-accumulate loop on .NET Framework where the BCL helper is
    /// unavailable.
    /// </summary>
    internal static class SecureComparison
    {
        /// <summary>
        /// Compares two spans for equality without short-circuiting on
        /// the first differing byte.
        /// </summary>
        /// <param name="left">First span.</param>
        /// <param name="right">Second span.</param>
        /// <returns>
        /// <see langword="true"/> when both spans are the same length
        /// and contain the same bytes.
        /// </returns>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static bool FixedTimeEquals(
            ReadOnlySpan<byte> left,
            ReadOnlySpan<byte> right)
        {
#if NETFRAMEWORK
            if (left.Length != right.Length)
            {
                return false;
            }
            int accumulator = 0;
            for (int i = 0; i < left.Length; i++)
            {
                accumulator |= left[i] ^ right[i];
            }
            return accumulator == 0;
#else
            return CryptographicOperations.FixedTimeEquals(left, right);
#endif
        }
    }
}
