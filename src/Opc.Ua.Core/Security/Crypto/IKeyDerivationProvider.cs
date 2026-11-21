// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua
{
    /// <summary>
    /// Derives the key material a secure channel and a session need from a shared
    /// secret.
    /// </summary>
    /// <remarks>
    /// This is a facet of <see cref="ICryptoProvider"/> rather than a member of
    /// it, so that a provider declares it only when it can serve
    /// <see cref="CryptoPurpose.KeyDerivation"/>, and so that adding it does not
    /// break providers written against the shipped interface.
    /// <para>
    /// Both families the stack uses reduce to the same shape: a secret and a seed
    /// in, a caller-sized block of key material out. P_SHA keys its HMAC with the
    /// secret and iterates over the seed; HKDF extracts with the seed as salt and
    /// expands. Expressing them alike keeps the channel free of algorithm
    /// specific branching.
    /// </para>
    /// </remarks>
    public interface IKeyDerivationProvider
    {
        /// <summary>
        /// Whether this provider can derive keys with an algorithm.
        /// </summary>
        /// <param name="algorithm">The algorithm being requested.</param>
        /// <returns>
        /// <c>true</c> when the algorithm is supported. A provider that returns
        /// <c>false</c> is bypassed in favour of the platform, so a partial
        /// implementation is legitimate.
        /// </returns>
        bool Supports(KeyDerivationAlgorithm algorithm);

        /// <summary>
        /// Derives key material.
        /// </summary>
        /// <param name="algorithm">The derivation algorithm to apply.</param>
        /// <param name="secret">The shared secret.</param>
        /// <param name="seed">
        /// The seed, which is the salt for the HKDF algorithms.
        /// </param>
        /// <param name="output">
        /// The buffer to fill completely with derived material. Its length is the
        /// number of bytes to derive.
        /// </param>
        void DeriveKey(
            KeyDerivationAlgorithm algorithm,
            ReadOnlySpan<byte> secret,
            ReadOnlySpan<byte> seed,
            Span<byte> output);
    }
}
