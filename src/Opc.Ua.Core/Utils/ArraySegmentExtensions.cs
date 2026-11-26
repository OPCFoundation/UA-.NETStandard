// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Opc.Ua
{
    /// <summary>
    /// Internal helpers for <see cref="ArraySegment{T}"/>.
    /// </summary>
    internal static class ArraySegmentExtensions
    {
        /// <summary>
        /// Returns the underlying array of an <see cref="ArraySegment{T}"/>.
        /// </summary>
        /// <typeparam name="T">The element type of the segment.</typeparam>
        /// <remarks>
        /// <see cref="ArraySegment{T}.Array"/> is declared nullable in the BCL
        /// because the default <see cref="ArraySegment{T}"/> value exposes a
        /// null-array segment. All call sites in this codebase invariably
        /// create segments with a non-null array, so this helper centralizes
        /// the assertion.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T[] GetArray<T>(this ArraySegment<T> segment)
        {
            Debug.Assert(segment.Array != null,
                "ArraySegment.Array must be non-null at this site.");
            return segment.Array!;
        }
    }
}
