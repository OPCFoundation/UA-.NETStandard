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

using System.Runtime.CompilerServices;

namespace Opc.Ua
{
    /// <summary>
    /// Compatibility helpers for the experimental Avro and Protobuf encoders.
    /// They bridge BCL APIs that are missing on the legacy target frameworks
    /// (net472/net48/netstandard2.0/netstandard2.1) while keeping the modern
    /// .NET code paths allocation-free and intrinsic-friendly. The bit-cast
    /// helpers use <see cref="Unsafe"/> reinterpretation, which lowers to the
    /// same reinterpret operation the JIT emits for the corresponding
    /// <see cref="System.BitConverter"/> intrinsics on the modern frameworks, so no
    /// runtime performance is lost on .NET 8 and later.
    /// </summary>
    internal static class EncoderCompat
    {
        /// <summary>
        /// Reinterprets a single-precision float as its 32-bit signed integer bit pattern.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int SingleToInt32Bits(float value)
        {
            return Unsafe.As<float, int>(ref value);
        }

        /// <summary>
        /// Reinterprets a 32-bit signed integer bit pattern as a single-precision float.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Int32BitsToSingle(int value)
        {
            return Unsafe.As<int, float>(ref value);
        }

        /// <summary>
        /// Reinterprets a single-precision float as its 32-bit unsigned integer bit pattern.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint SingleToUInt32Bits(float value)
        {
            return Unsafe.As<float, uint>(ref value);
        }

        /// <summary>
        /// Reinterprets a 32-bit unsigned integer bit pattern as a single-precision float.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float UInt32BitsToSingle(uint value)
        {
            return Unsafe.As<uint, float>(ref value);
        }

        /// <summary>
        /// Reinterprets a double-precision float as its 64-bit signed integer bit pattern.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long DoubleToInt64Bits(double value)
        {
            return Unsafe.As<double, long>(ref value);
        }

        /// <summary>
        /// Reinterprets a 64-bit signed integer bit pattern as a double-precision float.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Int64BitsToDouble(long value)
        {
            return Unsafe.As<long, double>(ref value);
        }

        /// <summary>
        /// Reinterprets a double-precision float as its 64-bit unsigned integer bit pattern.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong DoubleToUInt64Bits(double value)
        {
            return Unsafe.As<double, ulong>(ref value);
        }

        /// <summary>
        /// Reinterprets a 64-bit unsigned integer bit pattern as a double-precision float.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double UInt64BitsToDouble(ulong value)
        {
            return Unsafe.As<ulong, double>(ref value);
        }
    }
}
