/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Identity
{
    /// <summary>
    /// Resolves JWT issuer verification keys from a fixed in-memory set.
    /// </summary>
    /// <remarks>
    /// The resolver owns the supplied <see cref="IssuerVerificationKey"/>
    /// instances and disposes them when the resolver is disposed.
    /// Lookup by <c>kid</c> uses case-sensitive ordinal comparison.
    /// </remarks>
    public sealed class StaticIssuerKeyResolver : IIssuerKeyResolver, IDisposable
    {
        private readonly IReadOnlyList<IssuerVerificationKey> m_keys;
        private readonly Dictionary<string, IReadOnlyList<IssuerVerificationKey>> m_keysById;
        private bool m_disposed;

        /// <summary>
        /// Creates a fixed-key resolver for one trusted issuer.
        /// </summary>
        /// <param name="issuerUri">The trusted JWT <c>iss</c> claim.</param>
        /// <param name="keys">Verification keys owned by this resolver.</param>
        /// <exception cref="ArgumentException"><paramref name="issuerUri"/>
        /// is empty, or <paramref name="keys"/> contains a null element.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="keys"/>
        /// is <see langword="null"/>.</exception>
        public StaticIssuerKeyResolver(string issuerUri, IEnumerable<IssuerVerificationKey> keys)
        {
            if (string.IsNullOrEmpty(issuerUri))
            {
                throw new ArgumentException(
                    "Issuer URI must be supplied.",
                    nameof(issuerUri));
            }
            if (keys == null)
            {
                throw new ArgumentNullException(nameof(keys));
            }

            IssuerUri = issuerUri;

            var allKeys = new List<IssuerVerificationKey>();
            var keyed = new Dictionary<string, List<IssuerVerificationKey>>(StringComparer.Ordinal);
            foreach (IssuerVerificationKey key in keys)
            {
                if (key == null)
                {
                    throw new ArgumentException(
                        "Verification key collection must not contain null elements.",
                        nameof(keys));
                }

                allKeys.Add(key);
                if (key.KeyId != null)
                {
                    if (!keyed.TryGetValue(key.KeyId, out List<IssuerVerificationKey>? matchingKeys))
                    {
                        matchingKeys = [];
                        keyed[key.KeyId] = matchingKeys;
                    }
                    matchingKeys.Add(key);
                }
            }

            m_keys = allKeys.AsReadOnly();
            var readOnlyById = new Dictionary<string, IReadOnlyList<IssuerVerificationKey>>(
                StringComparer.Ordinal);
            foreach (KeyValuePair<string, List<IssuerVerificationKey>> item in keyed)
            {
                readOnlyById[item.Key] = item.Value.AsReadOnly();
            }
            m_keysById = readOnlyById;
        }

        /// <inheritdoc/>
        public string IssuerUri { get; }

        /// <inheritdoc/>
        public ValueTask<IReadOnlyList<IIssuerVerificationKey>> GetKeysAsync(
            string? keyId,
            CancellationToken ct = default)
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(StaticIssuerKeyResolver));
            }

            if (keyId == null)
            {
                return new ValueTask<IReadOnlyList<IIssuerVerificationKey>>(m_keys);
            }

            IReadOnlyList<IIssuerVerificationKey> result =
                m_keysById.TryGetValue(keyId, out IReadOnlyList<IssuerVerificationKey>? keys)
                    ? keys
                    : [];
            return new ValueTask<IReadOnlyList<IIssuerVerificationKey>>(result);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (m_disposed)
            {
                return;
            }

            m_disposed = true;
            foreach (IssuerVerificationKey key in m_keys)
            {
                key.Dispose();
            }
        }
    }
}
