// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;

namespace Opc.Ua
{
    /// <summary>
    /// Non secure random
    /// </summary>
    public sealed class UnsecureRandom
    {
        /// <summary>
        /// Get default random provider
        /// </summary>
        public static readonly UnsecureRandom Shared = new();

        /// <summary>
        /// Default random with a non-deterministic seed, so processes do not share one
        /// sequence (e.g. the reconnect jitter that de-synchronises clients).
        /// Use <see cref="UnsecureRandom(int)"/> for a reproducible sequence.
        /// </summary>
        private UnsecureRandom()
        {
#pragma warning disable CA5394 // Do not use insecure randomness
#if NETCOREAPP
            m_random = new Random();
#else
            // the parameterless Random of .NET Framework seeds from the tick count, which
            // processes started in the same tick share.
            m_random = new Random(Guid.NewGuid().GetHashCode());
#endif
#pragma warning restore CA5394 // Do not use insecure randomness
        }

        /// <summary>
        /// Create random with seed
        /// </summary>
        public UnsecureRandom(int seed)
        {
            m_random = new Random(seed);
        }

        /// <summary>
        /// Returns a non-negative random integer.
        /// </summary>
        public int Next()
        {
            lock (m_lock)
            {
#pragma warning disable CA5394 // Do not use insecure randomness
                return m_random.Next();
#pragma warning restore CA5394 // Do not use insecure randomness
            }
        }

        /// <summary>
        /// Returns a random integer that is within a specified range.
        /// </summary>
        public int Next(int minValue, int maxValue)
        {
            lock (m_lock)
            {
#pragma warning disable CA5394 // Do not use insecure randomness
                return m_random.Next(minValue, maxValue);
#pragma warning restore CA5394 // Do not use insecure randomness
            }
        }

        /// <summary>
        /// Returns a non-negative random integer that is less than the specified maximum.
        /// </summary>
        public int Next(int maxValue)
        {
            lock (m_lock)
            {
#pragma warning disable CA5394 // Do not use insecure randomness
                return m_random.Next(maxValue);
#pragma warning restore CA5394 // Do not use insecure randomness
            }
        }

        /// <summary>
        /// Fills the elements of a specified array of bytes with random numbers.
        /// </summary>
        public void NextBytes(byte[] buffer)
        {
            lock (m_lock)
            {
#pragma warning disable CA5394 // Do not use insecure randomness
                m_random.NextBytes(buffer);
#pragma warning restore CA5394 // Do not use insecure randomness
            }
        }

        /// <summary>
        /// Returns a random floating-point number that is greater than or equal to 0.0,
        ///  and less than 1.0.
        /// </summary>
        public double NextDouble()
        {
            lock (m_lock)
            {
#pragma warning disable CA5394 // Do not use insecure randomness
                return m_random.NextDouble();
#pragma warning restore CA5394 // Do not use insecure randomness
            }
        }

        /// <summary>
        /// Get a random byte string of the specified length.
        /// </summary>
        public ByteString GetByteString(int length)
        {
            byte[] buffer = new byte[length];
            lock (m_lock)
            {
#pragma warning disable CA5394 // Do not use insecure randomness
                m_random.NextBytes(buffer);
#pragma warning restore CA5394 // Do not use insecure randomness
            }
            return ByteString.From(buffer);
        }

        /// <summary>
        /// Shuffle a span
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public void Shuffle<T>(Span<T> source)
        {
#if NET8_0_OR_GREATER
            lock (m_lock)
            {
#pragma warning disable CA5394 // Do not use insecure randomness
                m_random.Shuffle(source);
#pragma warning restore CA5394 // Do not use insecure randomness
            }
#else
            int count = source.Length;
            for (int i = 0; i < count; i++)
            {
                int j = Next(i, count);
                (source[j], source[i]) = (source[i], source[j]);
            }
#endif
        }

        private readonly Random m_random;
        private readonly Lock m_lock = new();
    }
}
