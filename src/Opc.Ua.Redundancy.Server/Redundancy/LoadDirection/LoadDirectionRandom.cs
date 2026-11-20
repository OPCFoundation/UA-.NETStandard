/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Thread-safe random index selector used to break load ties among equally-eligible peers without a shared
    /// counter (each replica chooses independently, spreading load across the tied set).
    /// </summary>
    internal static class LoadDirectionRandom
    {
        /// <summary>
        /// Returns a random index in <c>[0, exclusiveMax)</c>, or <c>0</c> when there is at most one choice.
        /// </summary>
        public static int NextIndex(int exclusiveMax)
        {
            // CA5394: the tie-break only spreads load fairly across equally-eligible peers; it is not a security
            // decision, so a fast non-cryptographic PRNG is the correct choice.
#pragma warning disable CA5394
            return exclusiveMax <= 1 ? 0 : s_random.Value!.Next(exclusiveMax);
#pragma warning restore CA5394
        }

        private static readonly ThreadLocal<Random> s_random = new(
            () => new Random(unchecked((Environment.TickCount * 397) ^ Environment.CurrentManagedThreadId)));
    }
}
