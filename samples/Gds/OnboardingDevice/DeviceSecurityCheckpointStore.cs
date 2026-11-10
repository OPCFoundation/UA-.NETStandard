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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OnboardingRegistrar;
using Opc.Ua;

namespace OnboardingDevice
{
    /// <summary>
    /// Persists only the production device security journal and original trust defaults for one exact scope.
    /// Those encodings contain stages, public certificates, peer fingerprints, trust lists and CRLs, not
    /// passwords or private keys. Private keys remain exclusively in the existing certificate stores.
    /// This adapter is never registered as the application's general ISecretStore.
    /// </summary>
    internal sealed class DeviceSecurityCheckpointStore : ISecretStore
    {
        /// <summary>
        /// Borrows the linked sample writer's bounded atomic publication and exclusive directory lease.
        /// </summary>
        public DeviceSecurityCheckpointStore(PushRegistrarCheckpointFiles files, string scope)
        {
            m_files = files ?? throw new ArgumentNullException(nameof(files));
            ArgumentException.ThrowIfNullOrWhiteSpace(scope);
            string suffix = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(scope)));
            m_securityKey = "DeviceSecurity-" + suffix;
            m_defaultsKey = "ProvisionableDeviceDefaults-" + suffix;
        }

        /// <inheritdoc/>
        public string StoreType => "SamplePublicDeviceSecurity";

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
        /// Refuses to recapture operational trust as original defaults after a durable transition has begun.
        /// </summary>
        public async ValueTask RequireDefaultsAsync(CancellationToken ct)
        {
            using ISecret? defaults = await GetAsync(
                new SecretIdentifier(m_defaultsKey, StoreType), ct).ConfigureAwait(false);
            if (defaults == null)
            {
                throw new InvalidOperationException("The original device trust defaults checkpoint is missing.");
            }
        }

        /// <summary>
        /// Accepts only the two exact canonical identifiers generated for this device, not arbitrary prefixed names.
        /// </summary>
        private void RequireIdentifier(SecretIdentifier id)
        {
            ArgumentNullException.ThrowIfNull(id);
            if (id.StoreType != StoreType || id.StorePath != null ||
                (!string.Equals(id.Name, m_securityKey, StringComparison.Ordinal) &&
                    !string.Equals(id.Name, m_defaultsKey, StringComparison.Ordinal)))
            {
                throw new ArgumentException(
                    "Only this device's public security checkpoints are permitted.", nameof(id));
            }
        }

        /// <summary>
        /// Owns the detached public buffer returned by the existing writer.
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

        /// <summary>The borrowed bounded, atomic sample writer.</summary>
        private readonly PushRegistrarCheckpointFiles m_files;

        /// <summary>The exact production journal key for this scope.</summary>
        private readonly string m_securityKey;

        /// <summary>The exact production original-trust key for this scope.</summary>
        private readonly string m_defaultsKey;
    }
}
