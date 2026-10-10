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
using System.Text;
using System.Text.Json;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// Compares JSON numeric constraints using the canonicalizer's exact decimal representation.
    /// Comparison does not restrict significant digits to the precision of Double or Decimal.
    /// Nonzero values require a decimal exponent in the Int32 range.
    /// </summary>
    public static class WotJsonNumberComparer
    {
        /// <summary>
        /// Compares two JSON numbers without rounding or allocating powers of ten.
        /// </summary>
        /// <param name="left">The first JSON number.</param>
        /// <param name="right">The second JSON number.</param>
        /// <param name="comparison">Negative, zero or positive for less than, equal to or greater than.</param>
        /// <param name="error">Why comparison is unsupported for the supplied values or exponents.</param>
        /// <returns>Whether the numbers could be compared exactly.</returns>
        public static bool TryCompare(
            JsonElement left, JsonElement right, out int comparison, out string error)
        {
            comparison = 0;
            if (left.ValueKind != JsonValueKind.Number ||
                right.ValueKind != JsonValueKind.Number ||
                !TryDecompose(
                    left.GetRawText(), out bool leftNegative, out string leftDigits, out long leftExponent) ||
                !TryDecompose(
                    right.GetRawText(), out bool rightNegative, out string rightDigits, out long rightExponent))
            {
                error = "Exact numeric comparison requires JSON numbers with decimal exponents in the Int32 range.";
                return false;
            }
            int leftSign = leftDigits.Length == 0 ? 0 : leftNegative ? -1 : 1;
            int rightSign = rightDigits.Length == 0 ? 0 : rightNegative ? -1 : 1;
            comparison = leftSign.CompareTo(rightSign);
            if (comparison == 0 && leftSign != 0)
            {
                comparison = leftExponent.CompareTo(rightExponent);
                if (comparison == 0)
                {
                    int length = Math.Max(leftDigits.Length, rightDigits.Length);
                    for (int index = 0; index < length; index++)
                    {
                        char a = index < leftDigits.Length ? leftDigits[index] : '0';
                        char b = index < rightDigits.Length ? rightDigits[index] : '0';
                        comparison = a.CompareTo(b);
                        if (comparison != 0)
                        {
                            break;
                        }
                    }
                }
                comparison *= leftSign;
            }
            error = string.Empty;
            return true;
        }

        /// <summary>
        /// Decomposes a decimal literal into its sign, significant digits and decimal position:
        /// the value is <c>0.&lt;digits&gt; * 10^exponent</c>, without leading or trailing zeros.
        /// </summary>
        internal static bool TryDecompose(
            string literal, out bool negative, out string digits, out long exponent)
        {
            negative = false;
            digits = string.Empty;
            exponent = 0;
            int index = 0;
            if (index < literal.Length && (literal[index] == '-' || literal[index] == '+'))
            {
                negative = literal[index] == '-';
                index++;
            }
            var mantissa = new StringBuilder();
            int pointPosition = -1;
            int power = 0;
            bool supportedExponent = true;
            for (; index < literal.Length; index++)
            {
                char unit = literal[index];
                if (unit == '.')
                {
                    pointPosition = mantissa.Length;
                    continue;
                }
                if (unit is 'e' or 'E')
                {
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP2_1_OR_GREATER
                    ReadOnlySpan<char> exponentText = literal.AsSpan(index + 1);
#else
                    string exponentText = literal[(index + 1)..];
#endif
                    supportedExponent = int.TryParse(
                        exponentText, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out power);
                    break;
                }
                mantissa.Append(unit);
            }
            if (pointPosition < 0)
            {
                pointPosition = mantissa.Length;
            }
            string allDigits = mantissa.ToString();
            int leading = 0;
            while (leading < allDigits.Length && allDigits[leading] == '0')
            {
                leading++;
            }
            int trailing = allDigits.Length;
            while (trailing > leading && allDigits[trailing - 1] == '0')
            {
                trailing--;
            }
            if (leading >= trailing)
            {
                negative = false;
                return true;
            }
            if (!supportedExponent)
            {
                return false;
            }
            digits = allDigits[leading..trailing];
            exponent = (long)pointPosition - leading + power;
            return true;
        }
    }
}
