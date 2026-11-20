/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

#if NET8_0_OR_GREATER
using System;
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

    internal static class ReadOnlyMemoryHelper
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

        public static bool IsNull<T>(scoped in ReadOnlyMemory<T> m)
        {
            ref ReadOnlyMemory r = ref Unsafe.As<ReadOnlyMemory<T>, ReadOnlyMemory>(
                ref Unsafe.AsRef(in m));
            return r.Object is null;
        }
#else
        public static bool IsNull<T>(in System.ReadOnlyMemory<T> m)
        {
            return m.Equals(default);
        }
#endif
    }
}
