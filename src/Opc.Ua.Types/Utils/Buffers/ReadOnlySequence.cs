/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

/* Licensed to the .NET Foundation under one or more agreements.
 * The .NET Foundation licenses this file to you under the MIT license.
 * See the LICENSE file in the project root for more information. */

using System;
using System.Buffers;
using System.Diagnostics;

namespace Opc.Ua
{
    /// <summary>
    /// Helpers that can be used outside of the memory handle context
    /// </summary>
    internal static class ReadOnlySequenceExtensions
    {
        /// <summary>
        /// Compare to sequences by value
        /// compare memory, following code is copyright (c) Andrew Arnott.
        /// All rights reserved.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <returns></returns>
        public static bool SequenceEqual(this in ReadOnlySequence<byte> x,
            in ReadOnlySequence<byte> y)
        {
            if (x.IsSingleSegment && y.IsSingleSegment)
            {
#if NET8_0_OR_GREATER
                return x.FirstSpan.SequenceEqual(y.FirstSpan);
#else
                return x.First.Span.SequenceEqual(y.First.Span);
#endif
            }
            ReadOnlySequence<byte>.Enumerator aEnumerator = x.GetEnumerator();
            ReadOnlySequence<byte>.Enumerator bEnumerator = y.GetEnumerator();

            ReadOnlySpan<byte> aCurrent = default;
            ReadOnlySpan<byte> bCurrent = default;
            while (true)
            {
                bool aNext = TryGetNonEmptySpan(ref aEnumerator, ref aCurrent);
                bool bNext = TryGetNonEmptySpan(ref bEnumerator, ref bCurrent);
                if (!aNext || !bNext)
                {
                    // Success if we reached the end of both sequences at the same time.
                    return !aNext && !bNext;
                }
                Debug.Assert(aNext == bNext);
                int l = Math.Min(aCurrent.Length, bCurrent.Length);
                if (!aCurrent[..l].SequenceEqual(bCurrent[..l]))
                {
                    return false;
                }
                aCurrent = aCurrent[l..];
                bCurrent = bCurrent[l..];
            }
        }

        /// <summary>
        /// Compare to sequences by value
        /// compare memory, following code is copyright (c) Andrew Arnott.
        /// All rights reserved.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <returns></returns>
        public static bool SequenceEqual(this in ReadOnlySequence<byte> x,
            ReadOnlySpan<byte> y)
        {
            if (x.IsSingleSegment)
            {
#if NET8_0_OR_GREATER
                return x.FirstSpan.SequenceEqual(y);
#else
                return x.First.Span.SequenceEqual(y);
#endif
            }

            if (y.Length != x.Length)
            {
                return false;
            }

            ReadOnlySequence<byte>.Enumerator aEnumerator = x.GetEnumerator();
            ReadOnlySpan<byte> aCurrent = default;
            while (true)
            {
                bool aNext = TryGetNonEmptySpan(ref aEnumerator, ref aCurrent);
                if (!aNext)
                {
                    // We've reached the end of both sequences at the same time.
                    return true;
                }
                int l = Math.Min(aCurrent.Length, y.Length);
                if (!aCurrent[..l].SequenceEqual(y[..l]))
                {
                    return false;
                }
                aCurrent = aCurrent[l..];
                y = y[l..];
            }
        }

        private static bool TryGetNonEmptySpan(
            ref ReadOnlySequence<byte>.Enumerator enumerator,
            ref ReadOnlySpan<byte> span)
        {
            while (span.Length == 0)
            {
                if (!enumerator.MoveNext())
                {
                    return false;
                }

                span = enumerator.Current.Span;
            }
            return true;
        }
    }
}
