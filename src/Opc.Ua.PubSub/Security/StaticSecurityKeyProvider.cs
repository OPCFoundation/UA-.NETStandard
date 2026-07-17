/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

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
