// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PubSub.Pcap
{
    /// <summary>
    /// In-memory PubSub key resolver populated from captured key material or key-log records.
    /// </summary>
    public sealed class CapturedKeyLogKeyResolver : IPubSubKeyResolver, IDisposable
    {
        /// <summary>
        /// Initializes an empty resolver.
        /// </summary>
        public CapturedKeyLogKeyResolver()
        {
        }

        /// <summary>
        /// Initializes a resolver with a defensive copy of the supplied key material.
        /// </summary>
        /// <param name="keyMaterial">Key-material snapshots to import.</param>
        public CapturedKeyLogKeyResolver(IEnumerable<PubSubKeyMaterial> keyMaterial)
        {
            ArgumentNullException.ThrowIfNull(keyMaterial);
            foreach (PubSubKeyMaterial material in keyMaterial)
            {
                AddKeyMaterial(material);
            }
        }

        /// <summary>
        /// Adds a defensive copy of one captured key-material snapshot.
        /// </summary>
        /// <param name="keyMaterial">Key material to import.</param>
        public void AddKeyMaterial(PubSubKeyMaterial keyMaterial)
        {
            ArgumentNullException.ThrowIfNull(keyMaterial);
            ThrowIfDisposed();
            PubSubKeyMaterial copy = Copy(keyMaterial);
            var key = new Key(keyMaterial.SecurityGroupId, keyMaterial.TokenId, keyMaterial.SecurityPolicyUri);
            lock (m_lock)
            {
                if (m_keys.TryGetValue(key, out PubSubKeyMaterial? existing))
                {
                    existing.Dispose();
                }
                m_keys[key] = copy;
            }
        }

        /// <summary>
        /// Imports key material from an asynchronous source.
        /// </summary>
        /// <param name="keyMaterial">Key-material stream to import.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async ValueTask AddKeyMaterialAsync(
            IAsyncEnumerable<PubSubKeyMaterial> keyMaterial,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(keyMaterial);
            await foreach (PubSubKeyMaterial material in keyMaterial.ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddKeyMaterial(material);
            }
        }

        /// <inheritdoc/>
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Reliability",
            "CA2000:Dispose objects before losing scope",
            Justification = "TODO: TryResolveAsync returns caller-owned key snapshots; callers dispose them.")]
        public ValueTask<PubSubKeyMaterial?> TryResolveAsync(
            string? securityGroupId,
            uint tokenId,
            string securityPolicyUri,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(securityPolicyUri);
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            lock (m_lock)
            {
                if (!string.IsNullOrEmpty(securityGroupId) &&
                    TryGetLocked(securityGroupId, tokenId, securityPolicyUri, out PubSubKeyMaterial? exact))
                {
                    PubSubKeyMaterial copy = Copy(exact!);
                    return new ValueTask<PubSubKeyMaterial?>(copy);
                }

                foreach (KeyValuePair<Key, PubSubKeyMaterial> entry in m_keys)
                {
                    if (entry.Key.TokenId == tokenId &&
                        string.Equals(entry.Key.SecurityPolicyUri, securityPolicyUri, StringComparison.Ordinal) &&
                        (string.IsNullOrEmpty(securityGroupId) ||
                            string.Equals(entry.Key.SecurityGroupId, securityGroupId, StringComparison.Ordinal)))
                    {
                        PubSubKeyMaterial copy = Copy(entry.Value);
                        return new ValueTask<PubSubKeyMaterial?>(copy);
                    }
                }
            }
            return new ValueTask<PubSubKeyMaterial?>((PubSubKeyMaterial?)null);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            lock (m_lock)
            {
                if (m_disposed)
                {
                    return;
                }
                foreach (PubSubKeyMaterial material in m_keys.Values)
                {
                    material.Dispose();
                }
                m_keys.Clear();
                m_disposed = true;
            }
        }

        private static PubSubKeyMaterial Copy(PubSubKeyMaterial material)
        {
            return new PubSubKeyMaterial(
                material.SecurityGroupId,
                material.TokenId,
                material.SecurityPolicyUri,
                material.SigningKey.ToArray(),
                material.EncryptingKey.ToArray(),
                material.KeyNonce.ToArray());
        }

        private bool TryGetLocked(
            string securityGroupId,
            uint tokenId,
            string securityPolicyUri,
            out PubSubKeyMaterial? material)
        {
            return m_keys.TryGetValue(new Key(securityGroupId, tokenId, securityPolicyUri), out material);
        }

        private void ThrowIfDisposed()
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(CapturedKeyLogKeyResolver));
            }
        }

        private readonly record struct Key(string SecurityGroupId, uint TokenId, string SecurityPolicyUri);

        private readonly Dictionary<Key, PubSubKeyMaterial> m_keys = [];
        private readonly Lock m_lock = new();
        private bool m_disposed;
    }
}
