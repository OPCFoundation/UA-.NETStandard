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
    /// An RSA private key that can be used without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <see cref="RSA"/> is a synchronous contract, and it is .NET's rather than
    /// this stack's, so it cannot be replaced. A key backed by the network — a
    /// cloud key service, a remote signing service — therefore occupies a thread
    /// for the whole of every call.
    /// <para>
    /// The way out is not to replace <see cref="RSA"/> but to let an
    /// implementation of it <em>also</em> declare that it has an asynchronous
    /// path. An implementation subclasses <see cref="RSA"/> exactly as it does
    /// today, and additionally implements this interface. The stack asks for it
    /// by type test, so nothing changes for the implementations that do not.
    /// </para>
    /// <para>
    /// A software key does not implement this and is used synchronously, which
    /// means the stack's asynchronous paths complete synchronously and the
    /// ordering of everything around them is unchanged. That is deliberate: the
    /// secure channel handshake is not a place to introduce new suspension points
    /// for deployments that gain nothing from them.
    /// </para>
    /// </remarks>
    public interface IAsyncRsaKey
    {
        /// <summary>
        /// Signs a hash.
        /// </summary>
        /// <param name="hash">The hash to sign.</param>
        /// <param name="hashAlgorithm">The algorithm that produced the hash.</param>
        /// <param name="padding">The signature padding to apply.</param>
        /// <param name="ct">Cancels the operation.</param>
        /// <returns>The signature.</returns>
        ValueTask<byte[]> SignHashAsync(
            ReadOnlyMemory<byte> hash,
            HashAlgorithmName hashAlgorithm,
            RSASignaturePadding padding,
            CancellationToken ct = default);

        /// <summary>
        /// Decrypts data.
        /// </summary>
        /// <param name="data">The data to decrypt.</param>
        /// <param name="padding">The encryption padding that was applied.</param>
        /// <param name="ct">Cancels the operation.</param>
        /// <returns>The plain text.</returns>
        ValueTask<byte[]> DecryptAsync(
            ReadOnlyMemory<byte> data,
            RSAEncryptionPadding padding,
            CancellationToken ct = default);
    }
}
