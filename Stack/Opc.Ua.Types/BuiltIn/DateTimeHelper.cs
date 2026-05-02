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
using System.Globalization;

#if NET8_0_OR_GREATER
using System.Runtime.CompilerServices;
#else
using System.Runtime.CompilerServices;
#endif

namespace Opc.Ua
{
    /// <summary>
    /// Date time helpers
    /// </summary>
    public static class DateTimeHelper
    {
        /// <summary>
        /// The length of the DateTime string encoded by "o"
        /// </summary>
        public const int DateTimeRoundTripKindLength = 28;

        /// <summary>
        /// the index of the last digit which can be omitted if 0
        /// </summary>
        internal const int DateTimeRoundTripKindLastDigit = DateTimeRoundTripKindLength - 2;

        /// <summary>
        /// the index of the first digit which can be omitted (7 digits total)
        /// </summary>
        internal const int DateTimeRoundTripKindFirstDigit = DateTimeRoundTripKindLastDigit - 7;

        /// <summary>
        /// Write Utc time in the format "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK".
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
        public static void ConvertUniversalTimeToString(
            DateTime value,
            Span<char> valueString,
            out int charsWritten)
        {
            // Note: "o" is a shortcut for "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK" and implicitly
            // uses invariant culture and gregorian calendar, but executes up to 10 times faster.
            // But in contrary to the explicit format string, trailing zeroes are not omitted!
            if (value.Kind != DateTimeKind.Utc)
            {
                value.ToUniversalTime()
                    .TryFormat(valueString, out charsWritten, "o", CultureInfo.InvariantCulture);
            }
            else
            {
                value.TryFormat(valueString, out charsWritten, "o", CultureInfo.InvariantCulture);
            }

            System.Diagnostics.Debug.Assert(charsWritten == DateTimeRoundTripKindLength);

            // check if trailing zeroes can be omitted
            int i = DateTimeRoundTripKindLastDigit;
            while (i > DateTimeRoundTripKindFirstDigit)
            {
                if (valueString[i] != '0')
                {
                    break;
                }
                i--;
            }

            if (i < DateTimeRoundTripKindLastDigit)
            {
                // check if the decimal point has to be removed too
                if (i == DateTimeRoundTripKindFirstDigit)
                {
                    i--;
                }
                valueString[i + 1] = 'Z';
                charsWritten = i + 2;
            }
        }
#else
        public static string ConvertUniversalTimeToString(DateTime value)
        {
            // Note: "o" is a shortcut for "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK" and implicitly
            // uses invariant culture and gregorian calendar, but executes up to 10 times faster.
            // But in contrary to the explicit format string, trailing zeroes are not omitted!
            string valueString = value.ToUniversalTime().ToString("o");

            // check if trailing zeroes can be omitted
            int i = DateTimeRoundTripKindLastDigit;
            while (i > DateTimeRoundTripKindFirstDigit)
            {
                if (valueString[i] != '0')
                {
                    break;
                }
                i--;
            }

            if (i < DateTimeRoundTripKindLastDigit)
            {
                // check if the decimal point has to be removed too
                if (i == DateTimeRoundTripKindFirstDigit)
                {
                    i--;
                }
                valueString = valueString.Remove(i + 1, DateTimeRoundTripKindLastDigit - i);
            }

            return valueString;
        }
#endif
    }
}
