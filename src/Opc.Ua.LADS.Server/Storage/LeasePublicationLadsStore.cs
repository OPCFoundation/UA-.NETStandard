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
 * INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR
 * OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE,
 * ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Redundancy;

namespace Opc.Ua.LADS.Server
{
    /// <summary>
    /// Stores complete LADS snapshots through a lease publication authority.
    /// </summary>
    /// <remarks>
    /// This store has no secondary provider or fallback. Each save and delete is fenced atomically by the authority.
    /// </remarks>
    public sealed class LeasePublicationLadsStore : ILadsStore
    {
        /// <summary>
        /// Creates a store that uses a fixed lease token.
        /// </summary>
        /// <param name="authority">The authority that owns leases and snapshots.</param>
        /// <param name="token">The fencing identity for the store's group.</param>
        /// <param name="maxBytes">The maximum encoded snapshot size.</param>
        public LeasePublicationLadsStore(
            ILeasePublicationAuthority authority,
            LeasePublicationToken token,
            int maxBytes = 64 * 1024 * 1024)
            : this(authority, GetGroupIdentity(token), () => token, maxBytes)
        {
        }

        /// <summary>
        /// Creates a store that obtains the current fencing identity for a fixed group before each mutation.
        /// </summary>
        /// <param name="authority">The authority that owns leases and snapshots.</param>
        /// <param name="groupIdentity">The lease group that scopes all LADS snapshots.</param>
        /// <param name="tokenProvider">Returns the caller's current fencing identity.</param>
        /// <param name="maxBytes">The maximum encoded snapshot size.</param>
        public LeasePublicationLadsStore(
            ILeasePublicationAuthority authority,
            string groupIdentity,
            Func<LeasePublicationToken> tokenProvider,
            int maxBytes = 64 * 1024 * 1024)
        {
            m_authority = authority ?? throw new ArgumentNullException(nameof(authority));
            if (string.IsNullOrWhiteSpace(groupIdentity))
            {
                throw new ArgumentException("A lease group is required.", nameof(groupIdentity));
            }
            m_groupIdentity = groupIdentity;
            m_tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
            LadsSnapshotCodec.ValidateLimit(maxBytes);
            m_maxBytes = maxBytes;
        }

        /// <inheritdoc/>
        public async ValueTask<LadsStoredDeviceData?> LoadAsync(
            string deviceIdentity,
            CancellationToken cancellationToken = default)
        {
            LadsSnapshotCodec.ValidateIdentity(deviceIdentity);
            (bool found, ByteString value) = await m_authority.ReadSnapshotAsync(
                m_groupIdentity,
                deviceIdentity,
                cancellationToken).ConfigureAwait(false);
            if (!found)
            {
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();
            return LadsSnapshotCodec.Decode(value.Span.ToArray(), deviceIdentity, m_maxBytes);
        }

        /// <inheritdoc/>
        public async ValueTask SaveAsync(
            string deviceIdentity,
            LadsStoredDeviceData data,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[] content = LadsSnapshotCodec.Encode(data, deviceIdentity, m_maxBytes);
            cancellationToken.ThrowIfCancellationRequested();
            LeasePublicationStatus status = await m_authority.PublishSnapshotAsync(
                GetCurrentToken(),
                deviceIdentity,
                ByteString.From(content.AsSpan()),
                cancellationToken).ConfigureAwait(false);
            ThrowIfRejected(status, "save");
        }

        /// <inheritdoc/>
        public async ValueTask DeleteAsync(string deviceIdentity, CancellationToken cancellationToken = default)
        {
            LadsSnapshotCodec.ValidateIdentity(deviceIdentity);
            cancellationToken.ThrowIfCancellationRequested();
            LeasePublicationStatus status = await m_authority.DeleteSnapshotAsync(
                GetCurrentToken(),
                deviceIdentity,
                cancellationToken).ConfigureAwait(false);
            ThrowIfRejected(status, "delete");
        }

        /// <summary>
        /// Gets the fixed group from a non-null token.
        /// </summary>
        private static string GetGroupIdentity(LeasePublicationToken token)
        {
            if (token == null)
            {
                throw new ArgumentNullException(nameof(token));
            }
            return token.GroupIdentity;
        }

        /// <summary>
        /// Obtains the caller's fencing identity without allowing group changes.
        /// </summary>
        private LeasePublicationToken GetCurrentToken()
        {
            LeasePublicationToken token = m_tokenProvider() ??
                throw new InvalidOperationException("The LADS lease token provider returned no token.");
            if (!string.Equals(token.GroupIdentity, m_groupIdentity, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The LADS lease token belongs to a different group.");
            }
            return token;
        }

        /// <summary>
        /// Surfaces a rejected authoritative mutation.
        /// </summary>
        private static void ThrowIfRejected(LeasePublicationStatus status, string operation)
        {
            if (status is not (LeasePublicationStatus.Published or LeasePublicationStatus.Deleted))
            {
                throw new InvalidOperationException($"Lease authority rejected LADS snapshot {operation}: {status}.");
            }
        }

        /// <summary>The authority owning all publications.</summary>
        private readonly ILeasePublicationAuthority m_authority;
        /// <summary>The fixed lease group for this store.</summary>
        private readonly string m_groupIdentity;
        /// <summary>Supplies the current caller-owned fencing identity.</summary>
        private readonly Func<LeasePublicationToken> m_tokenProvider;
        /// <summary>The complete encoded snapshot size limit.</summary>
        private readonly int m_maxBytes;
    }
}
