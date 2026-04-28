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

using System;
using System.Collections;
using System.Collections.Generic;

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
        /// Default random with fixed seed
        /// </summary>
        private UnsecureRandom()
        {
            m_random = new Random(0x62541);
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
#pragma warning disable CA5394 // Do not use insecure randomness
            return m_random.Next();
#pragma warning restore CA5394 // Do not use insecure randomness
        }

        /// <summary>
        /// Returns a random integer that is within a specified range.
        /// </summary>
        public int Next(int minValue, int maxValue)
        {
#pragma warning disable CA5394 // Do not use insecure randomness
            return m_random.Next(minValue, maxValue);
#pragma warning restore CA5394 // Do not use insecure randomness
        }

        /// <summary>
        /// Returns a non-negative random integer that is less than the specified maximum.
        /// </summary>
        public int Next(int maxValue)
        {
#pragma warning disable CA5394 // Do not use insecure randomness
            return m_random.Next(maxValue);
#pragma warning restore CA5394 // Do not use insecure randomness
        }

        /// <summary>
        /// Fills the elements of a specified array of bytes with random numbers.
        /// </summary>
        public void NextBytes(byte[] buffer)
        {
#pragma warning disable CA5394 // Do not use insecure randomness
            m_random.NextBytes(buffer);
#pragma warning restore CA5394 // Do not use insecure randomness
        }

        /// <summary>
        /// Returns a random floating-point number that is greater than or equal to 0.0,
        ///  and less than 1.0.
        /// </summary>
        public double NextDouble()
        {
#pragma warning disable CA5394 // Do not use insecure randomness
            return m_random.NextDouble();
#pragma warning restore CA5394 // Do not use insecure randomness
        }

        /// <summary>
        /// Get a random byte string of the specified length.
        /// </summary>
        public ByteString GetByteString(int length)
        {
            byte[] buffer = new byte[length];
#pragma warning disable CA5394 // Do not use insecure randomness
            m_random.NextBytes(buffer);
#pragma warning restore CA5394 // Do not use insecure randomness
            return ByteString.From(buffer);
        }

        /// <summary>
        /// Shuffle a span
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public void Shuffle<T>(Span<T> source)
        {
#if NET8_0_OR_GREATER
#pragma warning disable CA5394 // Do not use insecure randomness
            m_random.Shuffle(source);
#pragma warning restore CA5394 // Do not use insecure randomness
#else
            int count = source.Length;
            for (int i = 0; i < count; i++)
            {
                int j = Next(i, count);
                T temp = source[i];
                source[i] = source[j];
                source[j] = temp;
            }
#endif
        }

        private readonly Random m_random;
    }
}
