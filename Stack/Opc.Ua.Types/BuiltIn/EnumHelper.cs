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
#if NET8_0_OR_GREATER
using System.Runtime.CompilerServices;
#else
using System.Globalization;
#endif

namespace Opc.Ua
{
    /// <summary>
    /// Helper methods to work with enum types
    /// </summary>
    internal static class EnumHelper
    {
        /// <summary>
        /// Cast to enum
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public static T Int32ToEnum<T>(int value) where T : Enum
        {
#if NET8_0_OR_GREATER
            if (Unsafe.SizeOf<T>() <= sizeof(int))
            {
                int i32 = value;
                return Unsafe.As<int, T>(ref i32);
            }
#endif
            return (T)(object)value;
        }

        /// <summary>
        /// Convert int to enum T
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public static int EnumToInt32<T>(T value) where T : Enum
        {
#if NET8_0_OR_GREATER
            switch (Unsafe.SizeOf<T>())
            {
                case sizeof(byte):
                    return Unsafe.As<T, byte>(ref value);
                case sizeof(ushort):
                    return Unsafe.As<T, ushort>(ref value);
                default:
                    return Unsafe.As<T, int>(ref value);
            }
#else
            // Use reflection to cast to underlying type to int
            Type enumValueType = typeof(T).GetEnumUnderlyingType();
            return (int)typeof(EnumHelper).GetMethod(nameof(Cast),
                System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.NonPublic)
                .MakeGenericMethod([enumValueType])
                .Invoke(null, [value]);
        }

        internal static int Cast<T>(object enumValue)
        {
            // unchecked cast to int from long
            unchecked
            {
                return (int)Convert.ToInt64(
                    (T)enumValue,
                    CultureInfo.InvariantCulture);
            }
#endif
        }
    }
}
