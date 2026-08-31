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
 *
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

namespace Opc.Ua.WotCon.Server.Materialization
{
    /// <summary>
    /// Orders strings by Unicode code point, which is what
    /// <i>OPC UA — WoT Binding</i> §12.6 requires of the <c>ViewVersion</c>
    /// canonicalization.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="StringComparer.Ordinal"/> orders by UTF-16 code
    /// <em>unit</em>, and the two orders disagree. A supplementary character
    /// (U+10000 and above) is stored as a surrogate pair whose first unit lies
    /// in U+D800–U+DBFF, so ordinal comparison sorts every supplementary
    /// character <em>below</em> U+E000–U+FFFF even though its code point is far
    /// above them. A .NET consumer sorting ordinally and a consumer sorting by
    /// code point would then hash different orders and compute different
    /// <c>ViewVersion</c> values for the same View membership, which is exactly
    /// the interoperability the clause exists to provide.
    /// </para>
    /// <para>
    /// The comparison reweights each unit instead of decoding scalars or
    /// re-encoding to UTF-8, so it allocates nothing and needs no API newer
    /// than the oldest supported framework. Units below U+D800 keep their
    /// value, units from U+E000 up move down by 0x800, and surrogate units move
    /// up by 0x2000. That places every non-surrogate BMP unit below every
    /// surrogate while preserving the order within each group, which is the
    /// same total order as comparing the UTF-8 encodings byte by byte - and
    /// UTF-8 byte order <em>is</em> code point order.
    /// </para>
    /// </remarks>
    internal sealed class WotCodePointComparer : IComparer<string>
    {
        /// <summary>
        /// Gets the shared instance.
        /// </summary>
        public static WotCodePointComparer Instance { get; } = new WotCodePointComparer();

        private WotCodePointComparer()
        {
        }

        /// <inheritdoc/>
        public int Compare(string? x, string? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }
            if (x is null)
            {
                return -1;
            }
            if (y is null)
            {
                return 1;
            }

            int shared = x.Length < y.Length ? x.Length : y.Length;
            for (int ii = 0; ii < shared; ii++)
            {
                if (x[ii] == y[ii])
                {
                    continue;
                }
                return Weight(x[ii]) - Weight(y[ii]);
            }
            return x.Length - y.Length;
        }

        /// <summary>
        /// Reweights one UTF-16 code unit so that ordering the weights orders
        /// the code points.
        /// </summary>
        private static int Weight(char unit)
        {
            if (unit < HighSurrogateFirst)
            {
                return unit;
            }
            if (unit > LowSurrogateLast)
            {
                return unit - SurrogateCount;
            }
            return unit + SurrogateShift;
        }

        private const char HighSurrogateFirst = '\ud800';
        private const char LowSurrogateLast = '\udfff';
        private const int SurrogateCount = 0x800;
        private const int SurrogateShift = 0x2000;
    }
}
