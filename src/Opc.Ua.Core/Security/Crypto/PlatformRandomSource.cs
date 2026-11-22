// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
