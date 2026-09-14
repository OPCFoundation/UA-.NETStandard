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

using System.Text;
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// The one place that decides what the encoding limits count, shared by
    /// every codec so that a single <see cref="IServiceMessageContext"/> cannot
    /// give a different answer over Binary, XML and JSON for the same value.
    /// </summary>
    internal static class EncodingLimits
    {
        /// <summary>
        /// Returns true when a string does not fit into MaxStringLength.
        /// </summary>
        /// <remarks>
        /// MaxStringLength counts the <b>bytes</b> of the encoded string
        /// (OPC 10000-3 5.6.4 and OPC 10000-5 6.3.2), not UTF-16 code units.
        /// Measuring code units instead lets a peer encode a non ASCII string
        /// that the receiver then refuses to decode, and makes the limit depend
        /// on the alphabet the value happens to be written in. Zero, and any
        /// negative value, mean unlimited as in every other limit check.
        /// </remarks>
        /// <param name="maxStringLength">The configured limit.</param>
        /// <param name="value">The string to measure.</param>
        /// <param name="byteLength">The measured length, only meaningful when
        /// this method returns true.</param>
        public static bool StringExceedsLimit(
            int maxStringLength,
            string? value,
            out int byteLength)
        {
            byteLength = 0;

            if (maxStringLength <= 0 || value == null)
            {
                return false;
            }

            // UTF-8 never needs more than three bytes per UTF-16 code unit - a
            // surrogate pair is two code units and four bytes - so the string
            // only has to be measured when even that worst case does not fit.
            if (value.Length <= maxStringLength / 3)
            {
                return false;
            }

            byteLength = Encoding.UTF8.GetByteCount(value);
            return byteLength > maxStringLength;
        }

        /// <summary>
        /// Throws if a string does not fit into MaxStringLength.
        /// </summary>
        /// <param name="maxStringLength">The configured limit.</param>
        /// <param name="value">The string to check.</param>
        /// <exception cref="ServiceResultException">Thrown with
        /// <see cref="StatusCodes.BadEncodingLimitsExceeded"/> when the string
        /// is over the limit.</exception>
        public static void CheckStringLength(int maxStringLength, string? value)
        {
            if (StringExceedsLimit(maxStringLength, value, out int byteLength))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "MaxStringLength {0} < {1}",
                    maxStringLength,
                    byteLength);
            }
        }
    }
}
