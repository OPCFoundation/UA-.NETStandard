/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace System.Collections.Concurrent
{
    /// <summary>
    /// Polyfills for System.Io methods that are not available in .NET Standard 2.0 or .NET Framework.
    /// </summary>
    public static class Polyfills
    {
#if NETSTANDARD2_0 || NETFRAMEWORK
        /// <summary>
        /// Try add value
        /// </summary>
        /// <typeparam name="TValue"></typeparam>
        public static void Clear<TValue>(
            this ConcurrentQueue<TValue> target)
        {
            while (target.TryDequeue(out _))
            {
            }
        }
#endif
    }
}
