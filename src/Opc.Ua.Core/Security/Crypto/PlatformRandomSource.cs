/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Security.Cryptography;

namespace Opc.Ua
{
    /// <summary>
    /// The random number generator the .NET platform supplies.
    /// </summary>
    /// <remarks>
    /// This is the generator the stack uses when nothing is registered, so
    /// registering it changes nothing. It exists so the seam ships with an
    /// implementation, and so a deployment running under
    /// <see cref="CryptoCompliancePolicy.FipsOnly"/> resolves
    /// <see cref="CryptoPurpose.RandomNumberGeneration"/> to a provider that
    /// states its provenance like every other.
    /// </remarks>
    public sealed class PlatformRandomSource : ISecureRandomSource
    {
        /// <summary>
        /// The shared instance.
        /// </summary>
        public static PlatformRandomSource Instance { get; } = new();

        /// <inheritdoc/>
        public void GetBytes(Span<byte> buffer)
        {
#if NET6_0_OR_GREATER
            RandomNumberGenerator.Fill(buffer);
#else
            byte[] bytes = new byte[buffer.Length];

            try
            {
                s_rng.GetBytes(bytes);
                bytes.AsSpan().CopyTo(buffer);
            }
            finally
            {
                Array.Clear(bytes, 0, bytes.Length);
            }
#endif
        }

        private PlatformRandomSource()
        {
        }

#if !NET6_0_OR_GREATER
        private static readonly RandomNumberGenerator s_rng = RandomNumberGenerator.Create();
#endif
    }
}
