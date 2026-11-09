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

namespace Opc.Ua.AutoID.Server
{
    /// <summary>
    /// Helpers for APIs that .NET Framework lacks.
    /// </summary>
    internal static class AutoIdCompat
    {
        /// <summary>
        /// Whether <paramref name="value"/> is a named member of its enumeration.
        /// </summary>
        /// <typeparam name="TEnum">The enumeration type.</typeparam>
        public static bool IsDefined<TEnum>(TEnum value)
            where TEnum : struct, Enum
        {
#if NET5_0_OR_GREATER
            return Enum.IsDefined(value);
#else
            return Enum.IsDefined(typeof(TEnum), value);
#endif
        }

        /// <summary>
        /// Parses a string of hexadecimal digit pairs.
        /// </summary>
        /// <exception cref="FormatException">The text is not an even number of hexadecimal digits.</exception>
        public static byte[] FromHexString(string text)
        {
#if NET5_0_OR_GREATER
            return Convert.FromHexString(text);
#else
            if (text.Length % 2 != 0)
            {
                throw new FormatException("The hexadecimal string has an odd number of digits.");
            }
            var bytes = new byte[text.Length / 2];
            for (int index = 0; index < bytes.Length; index++)
            {
                if (!byte.TryParse(
                    text.Substring(index * 2, 2),
                    NumberStyles.AllowHexSpecifier,
                    CultureInfo.InvariantCulture,
                    out bytes[index]))
                {
                    throw new FormatException("The hexadecimal string contains an invalid digit.");
                }
            }
            return bytes;
#endif
        }
    }
}
