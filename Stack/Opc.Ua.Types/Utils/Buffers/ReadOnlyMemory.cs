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

#if NET8_0_OR_GREATER
using System.Runtime.CompilerServices;
#endif

namespace Opc.Ua
{
    /// <summary>
    /// Exposes the fields of a read only memory or memory structure
    /// so they can be set/reused in the context of our structs.
    /// </summary>
    internal readonly struct ReadOnlyMemory
    {
#if NET8_0_OR_GREATER
        public static ref T ReinterpretAs<T>(scoped ref readonly ReadOnlyMemory m)
        {
            return ref Unsafe.As<ReadOnlyMemory, T>(ref Unsafe.AsRef(in m));
        }

        public static ref ReadOnlyMemory From<T>(scoped ref readonly T r)
        {
            return ref Unsafe.As<T, ReadOnlyMemory>(ref Unsafe.AsRef(in r));
        }
#endif

        /// <summary>
        /// Create read only memory
        /// </summary>
        /// <param name="obj"></param>
        /// <param name="length"></param>
        /// <param name="index"></param>
        public ReadOnlyMemory(object? obj = null, int length = 0, int index = 0)
        {
            Object = obj;
            Index = index;
            Length = length;
        }

        public readonly object? Object;
        public readonly int Index;
        public readonly int Length;
    }
}
