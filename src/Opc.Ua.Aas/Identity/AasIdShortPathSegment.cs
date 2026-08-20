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
using System.Globalization;

namespace Opc.Ua.Aas
{
    /// <summary>
    /// One segment of an <c>idShortPath</c>: either a short name or a
    /// zero-based list index.
    /// </summary>
    public readonly struct AasIdShortPathSegment : IEquatable<AasIdShortPathSegment>
    {
        private AasIdShortPathSegment(string? name, int index)
        {
            Name = name;
            Index = index;
        }

        /// <summary>
        /// Gets the short name, or <c>null</c> where the segment is an index.
        /// </summary>
        public string? Name { get; }

        /// <summary>
        /// Gets the zero-based list position, or <c>-1</c> where the segment
        /// is a short name.
        /// </summary>
        public int Index { get; }

        /// <summary>
        /// Gets a value indicating whether the segment addresses a list member
        /// by position rather than a child by name.
        /// </summary>
        public bool IsIndex => Name is null;

        /// <summary>
        /// Creates a segment naming a child.
        /// </summary>
        /// <param name="name">The short name.</param>
        /// <returns>The segment.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="name"/> is <c>null</c>.</exception>
        public static AasIdShortPathSegment ForName(string name)
        {
            if (name is null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            return new AasIdShortPathSegment(name, -1);
        }

        /// <summary>
        /// Creates a segment addressing a list member by position.
        /// </summary>
        /// <param name="index">The zero-based position.</param>
        /// <returns>The segment.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative.</exception>
        public static AasIdShortPathSegment ForIndex(int index)
        {
            if (index < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return new AasIdShortPathSegment(null, index);
        }

        /// <inheritdoc/>
        public bool Equals(AasIdShortPathSegment other)
        {
            return string.Equals(Name, other.Name, StringComparison.Ordinal) &&
                Index == other.Index;
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return obj is AasIdShortPathSegment other && Equals(other);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(Name, Index);
        }

        /// <summary>
        /// Compares two segments for equality.
        /// </summary>
        /// <param name="left">The left operand.</param>
        /// <param name="right">The right operand.</param>
        /// <returns><c>true</c> when the two segments are the same.</returns>
        public static bool operator ==(AasIdShortPathSegment left, AasIdShortPathSegment right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Compares two segments for inequality.
        /// </summary>
        /// <param name="left">The left operand.</param>
        /// <param name="right">The right operand.</param>
        /// <returns><c>true</c> when the two segments differ.</returns>
        public static bool operator !=(AasIdShortPathSegment left, AasIdShortPathSegment right)
        {
            return !left.Equals(right);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return Name ?? string.Concat(
                "[",
                Index.ToString(CultureInfo.InvariantCulture),
                "]");
        }
    }
}
