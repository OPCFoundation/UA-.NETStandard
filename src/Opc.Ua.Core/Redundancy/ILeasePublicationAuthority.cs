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

namespace Opc.Ua.Redundancy
{
    /// <summary>
    /// Grants a fencing identity for one lease group.
    /// </summary>
    /// <remarks>
    /// A token is a trusted caller-supplied fencing identity, not an authentication credential.
    /// </remarks>
    public sealed class LeasePublicationToken
    {
        /// <summary>
        /// Creates a fencing identity for an authority's successful acquisition.
        /// </summary>
        /// <param name="groupIdentity">The lease group.</param>
        /// <param name="ownerIdentity">The owner identity.</param>
        /// <param name="epoch">The fencing epoch.</param>
        public LeasePublicationToken(string groupIdentity, string ownerIdentity, long epoch)
        {
            if (string.IsNullOrWhiteSpace(groupIdentity))
            {
                throw new ArgumentException("A lease group is required.", nameof(groupIdentity));
            }
            if (string.IsNullOrWhiteSpace(ownerIdentity))
            {
                throw new ArgumentException("A lease owner is required.", nameof(ownerIdentity));
            }
            if (epoch <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(epoch), "A positive fencing epoch is required.");
            }
            GroupIdentity = groupIdentity;
            OwnerIdentity = ownerIdentity;
            Epoch = epoch;
        }

        /// <summary>
        /// Gets the lease group scoped by this token.
        /// </summary>
        public string GroupIdentity { get; }

        /// <summary>
        /// Gets the caller-supplied owner identity.
        /// </summary>
        public string OwnerIdentity { get; }

        /// <summary>
        /// Gets the monotonically increasing fencing epoch for the group.
        /// </summary>
        public long Epoch { get; }
    }

    /// <summary>
    /// Reports whether a lease acquisition created a new fencing identity or found a current lease.
    /// </summary>
    public enum LeaseAcquisitionStatus
    {
        /// <summary>
        /// A new lease was acquired.
        /// </summary>
        Acquired,

        /// <summary>
        /// An unexpired lease already occupies the group.
        /// </summary>
        LeaseHeld
    }

    /// <summary>
    /// Returns the outcome of an acquisition attempt.
    /// </summary>
    public sealed class LeaseAcquisitionResult
    {
        /// <summary>
        /// Creates an acquisition result.
        /// </summary>
        /// <param name="status">The acquisition outcome.</param>
        /// <param name="token">The fencing identity, present only after acquisition.</param>
        public LeaseAcquisitionResult(LeaseAcquisitionStatus status, LeasePublicationToken? token)
        {
            if (status is not (LeaseAcquisitionStatus.Acquired or LeaseAcquisitionStatus.LeaseHeld))
            {
                throw new ArgumentOutOfRangeException(nameof(status));
            }
            if ((status == LeaseAcquisitionStatus.Acquired) != (token != null))
            {
                throw new ArgumentException("Only an acquired lease must include a token.", nameof(token));
            }
            Status = status;
            Token = token;
        }

        /// <summary>
        /// Gets the acquisition outcome.
        /// </summary>
        public LeaseAcquisitionStatus Status { get; }

        /// <summary>
        /// Gets the fencing identity when the lease was acquired.
        /// </summary>
        public LeasePublicationToken? Token { get; }
    }

    /// <summary>
    /// Reports the outcome of lease renewal or release.
    /// </summary>
    public enum LeaseOperationStatus
    {
        /// <summary>
        /// The operation succeeded.
        /// </summary>
        Succeeded,

        /// <summary>
        /// The token does not identify the current lease.
        /// </summary>
        LeaseMismatch,

        /// <summary>
        /// The identified lease reached its expiry boundary.
        /// </summary>
        LeaseExpired
    }

    /// <summary>
    /// Reports the outcome of a snapshot publication or deletion.
    /// </summary>
    public enum LeasePublicationStatus
    {
        /// <summary>
        /// The snapshot was published.
        /// </summary>
        Published,

        /// <summary>
        /// The snapshot was deleted.
        /// </summary>
        Deleted,

        /// <summary>
        /// The token does not identify the current lease.
        /// </summary>
        LeaseMismatch,

        /// <summary>
        /// The identified lease reached its expiry boundary.
        /// </summary>
        LeaseExpired,

        /// <summary>
        /// The snapshot did not match the conditional operation's expected value.
        /// </summary>
        SnapshotConflict
    }

    /// <summary>
    /// Coordinates lease ownership and snapshot publication through one authoritative boundary.
    /// </summary>
    /// <remarks>
    /// Implementations must check the current owner, fencing epoch and authoritative expiry in the same
    /// atomic operation that publishes or deletes a snapshot. Snapshot keys are scoped to their lease group.
    /// At or after expiry, mutations must be rejected even when no successor has acquired the group.
    /// Acquisition must advance the group epoch after release or expiry, including for untouched snapshot keys.
    /// A preliminary lease read or an epoch-only snapshot comparison does not satisfy this contract.
    /// </remarks>
    public interface ILeasePublicationAuthority
    {
        /// <summary>
        /// Attempts to acquire a lease for a group.
        /// </summary>
        /// <param name="groupIdentity">A non-empty, stable lease-group identity.</param>
        /// <param name="ownerIdentity">A non-empty caller-supplied owner identity.</param>
        /// <param name="duration">A positive lease duration.</param>
        /// <param name="cancellationToken">Cancels the operation before it takes effect.</param>
        /// <returns>A new token or an explicit indication that the group is already leased.</returns>
        ValueTask<LeaseAcquisitionResult> TryAcquireLeaseAsync(
            string groupIdentity,
            string ownerIdentity,
            TimeSpan duration,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Renews a current lease without changing its fencing epoch.
        /// </summary>
        /// <param name="token">The current fencing identity.</param>
        /// <param name="duration">A positive lease duration.</param>
        /// <param name="cancellationToken">Cancels the operation before it takes effect.</param>
        /// <returns>The renewal outcome.</returns>
        ValueTask<LeaseOperationStatus> RenewLeaseAsync(
            LeasePublicationToken token,
            TimeSpan duration,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Releases a current lease while retaining its fencing epoch for the next acquisition.
        /// </summary>
        /// <param name="token">The current fencing identity.</param>
        /// <param name="cancellationToken">Cancels the operation before it takes effect.</param>
        /// <returns>The release outcome.</returns>
        ValueTask<LeaseOperationStatus> ReleaseLeaseAsync(
            LeasePublicationToken token,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Reads a snapshot from the specified group and key.
        /// </summary>
        /// <param name="groupIdentity">The lease group that scopes the key.</param>
        /// <param name="snapshotKey">The key within the group.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        /// <returns>A detached payload when present, or a null byte string when absent.</returns>
        ValueTask<(bool Found, ByteString Value)> ReadSnapshotAsync(
            string groupIdentity,
            string snapshotKey,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Publishes a complete snapshot after atomically checking the current lease.
        /// </summary>
        /// <param name="token">The current fencing identity.</param>
        /// <param name="snapshotKey">The key within the token's group.</param>
        /// <param name="value">The non-null snapshot payload.</param>
        /// <param name="cancellationToken">Cancels the operation before it takes effect.</param>
        /// <returns>The publication outcome.</returns>
        ValueTask<LeasePublicationStatus> PublishSnapshotAsync(
            LeasePublicationToken token,
            string snapshotKey,
            ByteString value,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Publishes a complete snapshot only when the current payload matches the expected payload.
        /// A null expected byte string requires the key to be absent.
        /// </summary>
        /// <param name="token">The current fencing identity.</param>
        /// <param name="snapshotKey">The key within the token's group.</param>
        /// <param name="expected">The expected payload, or null to require absence.</param>
        /// <param name="value">The non-null replacement payload.</param>
        /// <param name="cancellationToken">Cancels the operation before it takes effect.</param>
        /// <returns>The publication outcome.</returns>
        ValueTask<LeasePublicationStatus> TryPublishSnapshotAsync(
            LeasePublicationToken token,
            string snapshotKey,
            ByteString expected,
            ByteString value,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes a snapshot after atomically checking the current lease.
        /// </summary>
        /// <param name="token">The current fencing identity.</param>
        /// <param name="snapshotKey">The key within the token's group.</param>
        /// <param name="cancellationToken">Cancels the operation before it takes effect.</param>
        /// <returns>The deletion outcome.</returns>
        ValueTask<LeasePublicationStatus> DeleteSnapshotAsync(
            LeasePublicationToken token,
            string snapshotKey,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes a snapshot only when its current payload matches the expected payload.
        /// A null expected byte string requires the key to be absent.
        /// </summary>
        /// <param name="token">The current fencing identity.</param>
        /// <param name="snapshotKey">The key within the token's group.</param>
        /// <param name="expected">The expected payload, or null to require absence.</param>
        /// <param name="cancellationToken">Cancels the operation before it takes effect.</param>
        /// <returns>The deletion outcome.</returns>
        ValueTask<LeasePublicationStatus> TryDeleteSnapshotAsync(
            LeasePublicationToken token,
            string snapshotKey,
            ByteString expected,
            CancellationToken cancellationToken = default);
    }
}
