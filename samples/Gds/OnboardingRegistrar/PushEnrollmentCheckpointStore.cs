/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;

namespace OnboardingRegistrar
{
    /// <summary>
    /// Adapts the production push-enrollment journal to public checkpoint files, not a general secret store.
    /// Only canonical PushEnrollment- SHA256 identifiers with this store type and no store path are accepted.
    /// Its sole caller writes public certificates, signed tickets, CSRs, request IDs and trust lists.
    /// Never route passwords, private keys, new-key receipts or device-security journals through this adapter.
    /// </summary>
    internal sealed class PushEnrollmentCheckpointStore : ISecretStore
    {
        /// <summary>
        /// Borrows the exclusive public-checkpoint writer.
        /// </summary>
        public PushEnrollmentCheckpointStore(PushRegistrarCheckpointFiles files)
        {
            m_files = files ?? throw new ArgumentNullException(nameof(files));
        }

        /// <inheritdoc/>
        public string StoreType => "SamplePublicPushEnrollment";

        /// <inheritdoc/>
        public ISecret? TryGet(SecretIdentifier id)
        {
            RequireIdentifier(id);
            return null;
        }

        /// <inheritdoc/>
        public async ValueTask<ISecret?> GetAsync(SecretIdentifier id, CancellationToken ct = default)
        {
            RequireIdentifier(id);
            byte[]? bytes = await m_files.ReadAsync(id.Name, ct).ConfigureAwait(false);
            return bytes == null ? null : new PublicCheckpoint(bytes);
        }

        /// <inheritdoc/>
        public ValueTask SetAsync(
            SecretIdentifier id,
            ReadOnlyMemory<byte> bytes,
            CancellationToken ct = default)
        {
            RequireIdentifier(id);
            return m_files.WriteAsync(id.Name, bytes, ct);
        }

        /// <inheritdoc/>
        public ValueTask<bool> RemoveAsync(SecretIdentifier id, CancellationToken ct = default)
        {
            RequireIdentifier(id);
            return m_files.RemoveAsync(id.Name, ct);
        }

        /// <summary>
        /// Rejects all identifiers except the production enrollment provider's exact canonical journal keys.
        /// </summary>
        private void RequireIdentifier(SecretIdentifier id)
        {
            ArgumentNullException.ThrowIfNull(id);
            const string prefix = "PushEnrollment-";
            if (id.StoreType != StoreType || id.StorePath != null ||
                id.Name == null || !id.Name.StartsWith(prefix, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Only public push-enrollment journal identifiers are permitted.", nameof(id));
            }
            string suffix = id.Name[prefix.Length..];
            Span<byte> digest = stackalloc byte[32];
            if (!Convert.TryFromBase64String(suffix, digest, out int written) || written != digest.Length ||
                Convert.ToBase64String(digest) != suffix)
            {
                throw new ArgumentException("A canonical SHA256 push-enrollment identifier is required.", nameof(id));
            }
        }

        /// <summary>
        /// Owns the caller's detached public checkpoint buffer.
        /// </summary>
        private sealed class PublicCheckpoint(byte[] bytes) : ISecret
        {
            /// <inheritdoc/>
            public ReadOnlySpan<byte> Bytes => bytes;

            /// <inheritdoc/>
            public void Dispose()
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }

        /// <summary>The exclusive writer shared only by this sample's public providers.</summary>
        private readonly PushRegistrarCheckpointFiles m_files;
    }
}
