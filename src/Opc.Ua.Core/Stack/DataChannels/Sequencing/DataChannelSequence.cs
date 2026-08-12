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

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Serial number arithmetic over the FrameSequenceNumber value space
    /// (Part 6 errata 5.2.1).
    /// </summary>
    /// <remarks>
    /// Comparison is performed over the 2^32-1 values 1 to 4294967295.
    /// The modulus is 2^32-1 rather than 2^32 because zero is excluded
    /// from the value space, which is what makes the wrap from 4294967295
    /// to 1 a distance of one and therefore in sequence with no special
    /// case. Using 2^32 here computes that wrap as a distance of two and
    /// mandates a spurious gap.
    /// </remarks>
    public static class DataChannelSequence
    {
        /// <summary>
        /// The largest distance that still counts as forward. A distance
        /// above it is read as the peer being behind rather than very far
        /// ahead.
        /// </summary>
        public const uint MaxForwardDistance = int.MaxValue;

        /// <summary>
        /// The forward distance from one number to another, in the
        /// arithmetic of this clause. Zero when they are equal.
        /// </summary>
        /// <param name="from">The number to measure from.</param>
        /// <param name="to">The number to measure to.</param>
        public static uint Distance(uint from, uint to)
        {
            return to >= from
                ? to - from
                : DataChannelConstants.FrameSequenceModulus - (from - to);
        }

        /// <summary>
        /// True when <paramref name="value"/> is after
        /// <paramref name="other"/>.
        /// </summary>
        /// <param name="value">The number under test.</param>
        /// <param name="other">The number to compare against.</param>
        public static bool IsAfter(uint value, uint other)
        {
            uint distance = Distance(other, value);
            return distance >= 1 && distance <= MaxForwardDistance;
        }

        /// <summary>
        /// The number that follows another, wrapping to one after
        /// 4294967295 because zero is excluded.
        /// </summary>
        /// <param name="value">The current number.</param>
        public static uint Next(uint value)
        {
            return value == DataChannelConstants.FrameSequenceModulus
                ? DataChannelConstants.FirstFrameSequenceNumber
                : value + 1;
        }

        /// <summary>
        /// The number that precedes another, wrapping to 4294967295
        /// below one.
        /// </summary>
        /// <param name="value">The current number.</param>
        public static uint Previous(uint value)
        {
            return value <= DataChannelConstants.FirstFrameSequenceNumber
                ? DataChannelConstants.FrameSequenceModulus
                : value - 1;
        }

        /// <summary>
        /// Advances a number by a count, wrapping over the excluded zero.
        /// </summary>
        /// <param name="value">The current number.</param>
        /// <param name="count">How far to advance.</param>
        public static uint Advance(uint value, uint count)
        {
            uint modulus = DataChannelConstants.FrameSequenceModulus;
            ulong sum = (ulong)(value - 1) + (count % modulus);
            return (uint)(sum % modulus) + 1;
        }
    }
}
