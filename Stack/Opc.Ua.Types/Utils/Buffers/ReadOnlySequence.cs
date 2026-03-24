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

/* Licensed to the .NET Foundation under one or more agreements.
 * The .NET Foundation licenses this file to you under the MIT license.
 * See the LICENSE file in the project root for more information. */

#nullable enable

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
