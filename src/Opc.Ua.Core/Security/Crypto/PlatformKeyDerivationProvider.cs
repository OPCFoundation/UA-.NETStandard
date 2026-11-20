/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua
{
    /// <summary>
    /// The key derivation the .NET platform supplies.
    /// </summary>
    /// <remarks>
    /// This performs exactly the derivation the channel would otherwise perform
    /// inline, so registering it changes nothing. It exists so the seam ships
    /// with an implementation, and so a deployment running under
    /// <see cref="CryptoCompliancePolicy.FipsOnly"/> resolves
    /// <see cref="CryptoPurpose.KeyDerivation"/> to a provider that states its
    /// provenance like every other.
    /// </remarks>
    public sealed class PlatformKeyDerivationProvider : IKeyDerivationProvider
    {
        /// <summary>
        /// The shared instance.
        /// </summary>
        public static PlatformKeyDerivationProvider Instance { get; } = new();

        /// <inheritdoc/>
        public bool Supports(KeyDerivationAlgorithm algorithm)
        {
            return algorithm is KeyDerivationAlgorithm.PSha1
                or KeyDerivationAlgorithm.PSha256
                or KeyDerivationAlgorithm.HKDFSha256
                or KeyDerivationAlgorithm.HKDFSha384;
        }

        /// <inheritdoc/>
        /// <exception cref="NotSupportedException">
        /// <paramref name="algorithm"/> is not a derivation algorithm.
        /// </exception>
        public void DeriveKey(
            KeyDerivationAlgorithm algorithm,
            ReadOnlySpan<byte> secret,
            ReadOnlySpan<byte> seed,
            Span<byte> output)
        {
            if (!Supports(algorithm))
            {
                throw new NotSupportedException(
                    $"{algorithm} is not a key derivation algorithm handled by this provider.");
            }

            byte[] derived = algorithm switch
            {
                KeyDerivationAlgorithm.PSha1
                    => Utils.PSHA1(secret.ToArray(), null, seed.ToArray(), 0, output.Length),
                KeyDerivationAlgorithm.PSha256
                    => Utils.PSHA256(secret.ToArray(), null, seed.ToArray(), 0, output.Length),
                _ => Nonce.DeriveHkdfKeyData(
                    secret.ToArray(), seed.ToArray(), algorithm, output.Length)
            };

            try
            {
                derived.AsSpan(0, output.Length).CopyTo(output);
            }
            finally
            {
                Array.Clear(derived, 0, derived.Length);
            }
        }

        private PlatformKeyDerivationProvider()
        {
        }
    }
}
