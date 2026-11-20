// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Diagnostics.CodeAnalysis;

namespace System.Collections.Generic
{
    /// <summary>
    /// Polyfills for System.Collections.Generic methods that are not available
    /// in .NET Standard 2.0 or .NET Framework.
    /// </summary>
    public static class Polyfills
    {
#if NETSTANDARD2_0 || NETFRAMEWORK
        /// <summary>
        /// Deconstructs a key/value pair on platforms without the instance method.
        /// </summary>
        /// <typeparam name="TKey">The key type.</typeparam>
        /// <typeparam name="TValue">The value type.</typeparam>
        public static void Deconstruct<TKey, TValue>(
            this KeyValuePair<TKey, TValue> pair, out TKey key, out TValue value)
        {
            key = pair.Key;
            value = pair.Value;
        }

        /// <summary>
        /// Try add value
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        public static bool TryAdd<TKey, TValue>(
            this IDictionary<TKey, TValue> target,
            TKey key,
            TValue value)
        {
            if (!target.ContainsKey(key))
            {
                target.Add(key, value);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Remove from dictionary
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <returns></returns>
        public static bool Remove<TKey, TValue>(
            this IDictionary<TKey, TValue> target,
            TKey key,
            [MaybeNullWhen(false)] out TValue value)
        {
            if (target.TryGetValue(key, out value))
            {
                target.Remove(key);
                return true;
            }
            return false;
        }
#endif
    }
}
