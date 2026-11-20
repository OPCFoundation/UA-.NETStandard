// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.PubSub.Security;

namespace Opc.Ua.PubSub.Pcap
{
    /// <summary>
    /// PubSub key resolver backed by an SKS or static <see cref="IPubSubSecurityKeyProvider"/>.
    /// </summary>
    public sealed class SksKeyResolver : IPubSubKeyResolver
    {
        /// <summary>
        /// Initializes a resolver over an existing PubSub security key provider.
        /// </summary>
        /// <param name="keyProvider">SKS-backed or static security key provider.</param>
        public SksKeyResolver(IPubSubSecurityKeyProvider keyProvider)
        {
            ArgumentNullException.ThrowIfNull(keyProvider);
            m_keyProvider = keyProvider;
        }

        /// <inheritdoc/>
        public async ValueTask<PubSubKeyMaterial?> TryResolveAsync(
            string? securityGroupId,
            uint tokenId,
            string securityPolicyUri,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(securityPolicyUri);
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.IsNullOrEmpty(securityGroupId) &&
                !string.Equals(securityGroupId, m_keyProvider.SecurityGroupId, StringComparison.Ordinal))
            {
                return null;
            }

            PubSubSecurityKey? key = await m_keyProvider
                .TryGetKeyAsync(tokenId, cancellationToken)
                .ConfigureAwait(false);
            if (key is null)
            {
                return null;
            }

            return new PubSubKeyMaterial(
                m_keyProvider.SecurityGroupId,
                key.TokenId,
                securityPolicyUri,
                key.SigningKey.Span.ToArray(),
                key.EncryptingKey.Span.ToArray(),
                key.KeyNonce.Span.ToArray());
        }

        private readonly IPubSubSecurityKeyProvider m_keyProvider;
    }
}
