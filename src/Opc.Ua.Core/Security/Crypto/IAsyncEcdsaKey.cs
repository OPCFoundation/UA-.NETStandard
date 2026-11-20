// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua
{
    /// <summary>
    /// An ECDSA private key that can be used without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// The reasoning is the same as for <see cref="IAsyncRsaKey"/>: an
    /// implementation subclasses <see cref="ECDsa"/> as it does today and
    /// additionally declares this, and the stack finds it by type test.
    /// </remarks>
    public interface IAsyncEcdsaKey
    {
        /// <summary>
        /// Signs a hash, producing a signature in the IEEE P1363 fixed field
        /// format the OPC UA security policies use.
        /// </summary>
        /// <param name="hash">The hash to sign.</param>
        /// <param name="ct">Cancels the operation.</param>
        /// <returns>The signature.</returns>
        ValueTask<byte[]> SignHashAsync(
            ReadOnlyMemory<byte> hash,
            CancellationToken ct = default);
    }
}
