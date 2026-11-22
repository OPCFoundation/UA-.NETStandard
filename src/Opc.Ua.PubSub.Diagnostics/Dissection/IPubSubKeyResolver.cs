// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PubSub.Pcap
{
    /// <summary>
    /// Resolves PubSub security key material for offline UADP decryption.
    /// </summary>
    public interface IPubSubKeyResolver
    {
        /// <summary>
        /// Attempts to resolve a key snapshot for the requested SecurityGroup, token and policy.
        /// </summary>
        /// <param name="securityGroupId">SecurityGroupId, or <see langword="null"/> when not known from capture.</param>
        /// <param name="tokenId">SecurityTokenId from the UADP SecurityHeader.</param>
        /// <param name="securityPolicyUri">PubSub security policy URI.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>
        /// A caller-owned key-material snapshot, or <see langword="null"/> when no key is available.
        /// </returns>
        ValueTask<PubSubKeyMaterial?> TryResolveAsync(
            string? securityGroupId,
            uint tokenId,
            string securityPolicyUri,
            CancellationToken cancellationToken = default);
    }
}
