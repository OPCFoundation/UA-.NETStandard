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

#nullable enable

using System;
using System.Diagnostics.CodeAnalysis;
#if NET8_0_OR_GREATER
using System.Runtime.CompilerServices;
#endif

namespace Opc.Ua
{
    /// <summary>
    /// Helper methods to work with enum types
    /// </summary>
    public static class EnumHelper // TODO: Make internal
    {
        /// <summary>
        /// Cast to enum
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public static T Int32ToEnum<T>(int value) where T : struct, Enum
        {
#if NET8_0_OR_GREATER
            if (Unsafe.SizeOf<T>() <= sizeof(int))
            {
                int i32 = value;
                return Unsafe.As<int, T>(ref i32);
            }
#else
            switch (typeof(T).GetEnumUnderlyingType())
            {
                case Type t when t == typeof(byte):
                    return (T)(object)unchecked((byte)value);
                case Type t when t == typeof(sbyte):
                    return (T)(object)unchecked((sbyte)value);
                case Type t when t == typeof(short):
                    return (T)(object)unchecked((short)value);
                case Type t when t == typeof(ushort):
                    return (T)(object)unchecked((ushort)value);
                case Type t when t == typeof(int):
                    return (T)(object)unchecked(value);
                case Type t when t == typeof(uint):
                    return (T)(object)unchecked((uint)value);
                case Type t when t == typeof(long):
                    return (T)(object)unchecked((long)value);
                case Type t when t == typeof(ulong):
                    return (T)(object)unchecked((ulong)value);
            }
#endif
            return default;
        }

        /// <summary>
        /// Cast to enum array
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public static ArrayOf<T> Int32ToEnum<T>(this ArrayOf<int> values)
            where T : struct, Enum
        {
            return values.ConvertAll(Int32ToEnum<T>);
        }

        /// <summary>
        /// Cast to enum matrix
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public static MatrixOf<T> Int32ToEnum<T>(this MatrixOf<int> values)
            where T : struct, Enum
        {
            return values.ConvertAll(Int32ToEnum<T>);
        }

        /// <summary>
        /// Convert int to enum T
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public static int EnumToInt32<T>(T value) where T : struct, Enum
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
            return EnumToInt32(value, typeof(T));
#endif
        }

        /// <summary>
        /// Convert int to enum T
        /// </summary>
        public static int EnumToInt32(object value, Type type)
        {
            switch (type.IsEnum ? Enum.GetUnderlyingType(type) : type)
            {
                case Type t when t == typeof(byte):
                    return unchecked((byte)value);
                case Type t when t == typeof(sbyte):
                    return unchecked((sbyte)value);
                case Type t when t == typeof(short):
                    return unchecked((short)value);
                case Type t when t == typeof(ushort):
                    return unchecked((ushort)value);
                case Type t when t == typeof(int):
                    return unchecked((int)value);
                case Type t when t == typeof(uint):
                    return unchecked((int)(uint)value);
                case Type t when t == typeof(long):
                    return unchecked((int)(long)value);
                case Type t when t == typeof(ulong):
                    return unchecked((int)(ulong)value);
            }
            return 0;
        }

        /// <summary>
        /// Cast from enum array
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public static ArrayOf<int> EnumToInt32<T>(this ArrayOf<T> values)
            where T : struct, Enum
        {
            return values.ConvertAll(EnumToInt32);
        }

        /// <summary>
        /// Cast from enum matrix
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public static MatrixOf<int> EnumToInt32<T>(this MatrixOf<T> values)
            where T : struct, Enum
        {
            return values.ConvertAll(EnumToInt32);
        }

        /// <summary>
        /// Convert int to enum T
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public static long EnumToInt64<T>(T value) where T : struct, Enum
        {
#if NET8_0_OR_GREATER
            switch (Unsafe.SizeOf<T>())
            {
                case sizeof(byte):
                    return Unsafe.As<T, byte>(ref value);
                case sizeof(ushort):
                    return Unsafe.As<T, ushort>(ref value);
                case sizeof(uint):
                    return Unsafe.As<T, uint>(ref value);
                default:
                    return Unsafe.As<T, long>(ref value);
            }
#else
            switch (typeof(T).GetEnumUnderlyingType())
            {
                case Type t when t == typeof(byte):
                    return unchecked((byte)(object)value);
                case Type t when t == typeof(sbyte):
                    return unchecked((sbyte)(object)value);
                case Type t when t == typeof(short):
                    return unchecked((short)(object)value);
                case Type t when t == typeof(ushort):
                    return unchecked((ushort)(object)value);
                case Type t when t == typeof(int):
                    return unchecked((int)(object)value);
                case Type t when t == typeof(uint):
                    return unchecked((uint)(object)value);
                case Type t when t == typeof(long):
                    return unchecked((long)(object)value);
                case Type t when t == typeof(ulong):
                    return unchecked((long)(ulong)(object)value);
            }
            return 0;
#endif
        }

        /// <summary>
        /// Cast from enum array
        /// </summary>
        public static ArrayOf<int> EnumArrayToInt32Array(Array values)
        {
            if (values == null)
            {
                return default;
            }

            if (values.GetType().GetElementType() == typeof(int))
            {
                return ArrayOf.From<int>(values);
            }

            int[] array = new int[values.Length];
            // Convert array of enum values to array of int values
            for (int i = 0; i < values.Length; i++)
            {
                array[i] = (int)values.GetValue(i)!;
            }
            return array;
        }

        /// <summary>
        /// Cast from enum matrix
        /// </summary>
        public static MatrixOf<int> EnumArrayToInt32Matrix(Array values)
        {
            if (values == null)
            {
                return default;
            }

            if (values.GetType().GetElementType() == typeof(int))
            {
                return MatrixOf.From<int>(values);
            }

            // Get dimensions from the multi-dimensional array
            int[] dimensions = new int[values.Rank];
            for (int i = 0; i < values.Rank; i++)
            {
                dimensions[i] = values.GetLength(i);
            }

            // Convert all enum values to int, iterating in row-major order
            int[] array = new int[values.Length];
            int index = 0;
            foreach (object value in values)
            {
                array[index++] = (int)value;
            }
            return array.ToMatrixOf(dimensions);
        }

        /// <summary>
        /// Cast to enum
        /// </summary>
        public static object? Int32ToEnum(int value, Type type)
        {
            if (type == typeof(int))
            {
                return value;
            }
            if (!type.IsEnum)
            {
                return null;
            }
            Type underlyingType = type.GetEnumUnderlyingType();
            if (underlyingType == typeof(int))
            {
                return Enum.ToObject(type, value);
            }
            else if (underlyingType == typeof(uint))
            {
                return Enum.ToObject(type, (uint)value);
            }
            else if (underlyingType == typeof(byte))
            {
                return Enum.ToObject(type, (byte)value);
            }
            else if (underlyingType == typeof(sbyte))
            {
                return Enum.ToObject(type, (sbyte)value);
            }
            else if (underlyingType == typeof(short))
            {
                return Enum.ToObject(type, (short)value);
            }
            else if (underlyingType == typeof(ushort))
            {
                return Enum.ToObject(type, (ushort)value);
            }
            else if (underlyingType == typeof(long))
            {
                return Enum.ToObject(type, (long)value);
            }
            else if (underlyingType == typeof(ulong))
            {
                return Enum.ToObject(type, (ulong)value);
            }
            else
            {
                return Enum.ToObject(underlyingType, value);
            }
        }

        /// <summary>
        /// Cast to enum array
        /// </summary>
        [RequiresUnreferencedCode(
            "Array.CreateInstance is used with potentially unknown enum types.")]
        [RequiresDynamicCode(
            "Array.CreateInstance is used with potentially unknown enum types.")]
        public static Array? Int32ArrayToEnumArray(ArrayOf<int> values, Type type)
        {
            if (values.IsNull)
            {
                return null;
            }
            if (type == typeof(int))
            {
                return values.ToArray();
            }
            Array array = Array.CreateInstance(type, values.Count);
            // Convert array of int values to array of enum values
            for (int i = 0; i < values.Count; i++)
            {
                array.SetValue(Enum.ToObject(type, values.Span[i]), i);
            }
            return array;
        }

        /// <summary>
        /// Cast to enum matrix
        /// </summary>
        [RequiresUnreferencedCode(
            "Array.CreateInstance is used with potentially unknown enum types.")]
        [RequiresDynamicCode(
            "Array.CreateInstance is used with potentially unknown enum types.")]
        public static Array? Int32MatrixToEnumArray(MatrixOf<int> values, Type type)
        {
            if (values.IsNull)
            {
                return null;
            }
            if (type == typeof(int))
            {
                return values.CreateArrayInstance();
            }
            int[] dim = values.Dimensions;
            Array array = Array.CreateInstance(type, dim);
            // Convert the matrix with dimensions into an multi dimensional Array of enum values
            int[] indexes = new int[dim.Length];
            foreach (int element in values.Span)
            {
                array.SetValue(Enum.ToObject(type, element), indexes);
                for (int dimension = indexes.Length - 1; dimension >= 0; dimension--)
                {
                    indexes[dimension]++;
                    if (indexes[dimension] < dim[dimension])
                    {
                        break;
                    }
                    indexes[dimension] = 0;
                }
            }
            return array;
        }
    }
}
