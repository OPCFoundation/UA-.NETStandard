// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PubSub.Security
{
    /// <summary>
    /// In-process <see cref="IPubSubSecurityKeyProvider"/> backed by a
    /// caller-supplied <see cref="PubSubSecurityKeyRing"/>. Used by
    /// unit tests and by deployments that source keys locally without
    /// an SKS round-trip.
    /// </summary>
    /// <remarks>
    /// Implements the local-key-provider contract referenced from
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/8.3">
    /// Part 14 §8.3 Security Key Service</see>. An SKS-backed
    /// provider wraps the same ring abstraction.
    /// </remarks>
    public sealed class StaticSecurityKeyProvider : IPubSubSecurityKeyProvider
    {
        private readonly PubSubSecurityKeyRing m_ring;

        /// <summary>
        /// Initializes a new <see cref="StaticSecurityKeyProvider"/>.
        /// </summary>
        /// <param name="securityGroupId">SecurityGroup identifier.</param>
        /// <param name="keyRing">Underlying key ring.</param>
        public StaticSecurityKeyProvider(
            string securityGroupId,
            PubSubSecurityKeyRing keyRing)
        {
            if (string.IsNullOrEmpty(securityGroupId))
            {
                throw new ArgumentException(
                    "SecurityGroupId must be non-empty.",
                    nameof(securityGroupId));
            }
            if (keyRing is null)
            {
                throw new ArgumentNullException(nameof(keyRing));
            }
            if (!string.Equals(
                keyRing.SecurityGroupId,
                securityGroupId,
                StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Key ring SecurityGroupId does not match the provider SecurityGroupId.",
                    nameof(keyRing));
            }
            SecurityGroupId = securityGroupId;
            m_ring = keyRing;
            m_ring.Rotated += OnRingRotated;
        }

        /// <inheritdoc/>
        public string SecurityGroupId { get; }

        /// <inheritdoc/>
        public event EventHandler<PubSubKeyRotatedEventArgs>? KeyRotated;

        /// <inheritdoc/>
        public ValueTask<PubSubSecurityKey> GetCurrentKeyAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PubSubSecurityKey? current = m_ring.Current ??
                throw new InvalidOperationException(
                    $"No current key available for SecurityGroupId '{SecurityGroupId}'.");
            return new ValueTask<PubSubSecurityKey>(current);
        }

        /// <inheritdoc/>
        public ValueTask<PubSubSecurityKey?> TryGetKeyAsync(
            uint tokenId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<PubSubSecurityKey?>(m_ring.TryGetByTokenId(tokenId));
        }

        private void OnRingRotated(object? sender, PubSubKeyRotatedEventArgs e)
        {
            KeyRotated?.Invoke(this, e);
        }
    }
}
