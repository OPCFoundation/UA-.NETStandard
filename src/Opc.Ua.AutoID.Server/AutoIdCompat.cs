// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
