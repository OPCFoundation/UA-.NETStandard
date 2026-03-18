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

namespace Opc.Ua
{
    /// <summary>
    /// Represents a generic enumeration value that can be stored within
    /// a Variant and retains symbol information for encoders. It contains
    /// both symbol and value which both are required to write/read from
    /// wire representation..
    /// </summary>
    public readonly struct EnumValue : IEquatable<EnumValue>
    {
        /// <summary>
        /// The symbol for the value
        /// </summary>
        public string Symbol => m_symbol;

        /// <summary>
        /// The enum value
        /// </summary>
        public long Value => m_value;

        /// <summary>
        /// Create enum value
        /// </summary>
        public EnumValue(long value, string symbol)
        {
            m_symbol = symbol;
            m_value = value;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj switch
            {
                string value => Equals(value),
                int value => Equals(value),
                long value => Equals(value),
                EnumValue value => Equals(value),
                _ => false
            };
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(m_value);
        }

        /// <inheritdoc/>
        public bool Equals(EnumValue other)
        {
            return m_value == other.m_value;
        }

        /// <inheritdoc/>
        public bool Equals(string other)
        {
            return string.Equals(
                m_symbol,
                other,
                StringComparison.OrdinalIgnoreCase);
        }

        /// <inheritdoc/>
        public bool Equals(long value)
        {
            return m_value == value;
        }

        /// <inheritdoc/>
        public static bool operator ==(EnumValue left, EnumValue right)
        {
            return left.Equals(right);
        }

        /// <inheritdoc/>
        public static bool operator !=(EnumValue left, EnumValue right)
        {
            return !(left == right);
        }

        /// <inheritdoc/>
        public static bool operator ==(EnumValue left, long right)
        {
            return left.Equals(right);
        }

        /// <inheritdoc/>
        public static bool operator !=(EnumValue left, long right)
        {
            return !(left == right);
        }

        /// <inheritdoc/>
        public static bool operator ==(EnumValue left, string right)
        {
            return left.Equals(right);
        }

        /// <inheritdoc/>
        public static bool operator !=(EnumValue left, string right)
        {
            return !(left == right);
        }

        private readonly long m_value;
        private readonly string m_symbol;
    }
}
