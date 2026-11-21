// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua
{
    /// <summary>
    /// A source of random material.
    /// </summary>
    /// <remarks>
    /// This is a facet of <see cref="ICryptoProvider"/> rather than a member of
    /// it, so that a provider declares it only when it can serve
    /// <see cref="CryptoPurpose.RandomNumberGeneration"/>, and so that adding it
    /// does not break providers written against the shipped interface.
    /// <para>
    /// Resolve once, where something is bound, and hold the result. A caller that
    /// resolves nothing keeps using the platform generator, at no cost.
    /// </para>
    /// </remarks>
    public interface ISecureRandomSource
    {
        /// <summary>
        /// Fills a buffer with cryptographically strong random bytes.
        /// </summary>
        /// <param name="buffer">The buffer to fill completely.</param>
        void GetBytes(Span<byte> buffer);
    }
}
