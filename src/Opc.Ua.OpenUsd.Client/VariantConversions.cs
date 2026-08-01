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

namespace Opc.Ua.OpenUsd.Client
{
    /// <summary>
    /// Widening conversions from an OPC UA <see cref="Variant"/> to the CLR
    /// primitives the OpenUSD binding authors. Every conversion goes through the
    /// typed <c>Variant.TryGetValue</c> accessors, so a value never has to be
    /// boxed and a wrongly typed source degrades to <c>false</c> instead of
    /// throwing.
    /// </summary>
    internal static class VariantConversions
    {
        /// <summary>
        /// Widens any OPC UA numeric or Boolean <paramref name="value"/> to a
        /// <see cref="double"/>.
        /// </summary>
        public static bool TryGetDouble(in Variant value, out double result)
        {
            if (value.TryGetValue(out double doubleValue))
            {
                result = doubleValue;
                return true;
            }
            if (value.TryGetValue(out float floatValue))
            {
                result = floatValue;
                return true;
            }
            if (TryGetInt64(value, out long integerValue))
            {
                result = integerValue;
                return true;
            }
            if (value.TryGetValue(out ulong unsignedValue))
            {
                result = unsignedValue;
                return true;
            }
            if (value.TryGetDecimal(out decimal decimalValue))
            {
                result = (double)decimalValue;
                return true;
            }
            result = 0.0;
            return false;
        }

        /// <summary>
        /// Widens any OPC UA signed or unsigned integer, or Boolean,
        /// <paramref name="value"/> to an <see cref="long"/>. <c>UInt64</c>
        /// sources are rejected because they do not fit without loss.
        /// </summary>
        public static bool TryGetInt64(in Variant value, out long result)
        {
            if (value.TryGetValue(out long longValue))
            {
                result = longValue;
                return true;
            }
            if (value.TryGetValue(out int intValue))
            {
                result = intValue;
                return true;
            }
            if (value.TryGetValue(out uint uintValue))
            {
                result = uintValue;
                return true;
            }
            if (value.TryGetValue(out short shortValue))
            {
                result = shortValue;
                return true;
            }
            if (value.TryGetValue(out ushort ushortValue))
            {
                result = ushortValue;
                return true;
            }
            if (value.TryGetValue(out sbyte sbyteValue))
            {
                result = sbyteValue;
                return true;
            }
            if (value.TryGetValue(out byte byteValue))
            {
                result = byteValue;
                return true;
            }
            if (value.TryGetValue(out bool boolValue))
            {
                result = boolValue ? 1 : 0;
                return true;
            }
            result = 0;
            return false;
        }

        /// <summary>
        /// Reads a Boolean, treating any non-zero numeric source as <c>true</c>.
        /// </summary>
        public static bool TryGetBoolean(in Variant value, out bool result)
        {
            if (value.TryGetValue(out bool boolValue))
            {
                result = boolValue;
                return true;
            }
            if (TryGetDouble(value, out double numericValue))
            {
                result = numericValue != 0.0;
                return true;
            }
            result = false;
            return false;
        }

        /// <summary>
        /// Reads a ByteString, accepting a <c>Byte</c> array source as well.
        /// </summary>
        public static bool TryGetBytes(in Variant value, out ByteString result)
        {
            if (value.TryGetValue(out ByteString byteString))
            {
                result = byteString;
                return true;
            }
            if (value.TryGetValue(out ArrayOf<byte> bytes))
            {
                result = new ByteString(bytes.ToArray());
                return true;
            }
            result = default;
            return false;
        }
    }
}
