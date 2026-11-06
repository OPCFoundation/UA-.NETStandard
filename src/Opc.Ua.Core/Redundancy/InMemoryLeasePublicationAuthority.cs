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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Redundancy
{
    /// <summary>
    /// Provides process-local lease fencing and atomic snapshot publication.
    /// </summary>
    /// <remarks>
    /// All leases and snapshots live in this instance. Multiple participants must share the same instance;
    /// this implementation does not coordinate processes or provide durable high availability.
    /// </remarks>
    public sealed class InMemoryLeasePublicationAuthority : ILeasePublicationAuthority
    {
        /// <summary>
        /// Creates an in-memory authority that uses monotonic timestamps from <paramref name="timeProvider"/>.
        /// </summary>
        /// <param name="timeProvider">The timestamp provider, or null to use the system provider.</param>
        public InMemoryLeasePublicationAuthority(TimeProvider? timeProvider = null)
        {
            m_timeProvider = timeProvider ?? TimeProvider.System;
        }

        /// <inheritdoc/>
        public ValueTask<LeaseAcquisitionResult> TryAcquireLeaseAsync(
            string groupIdentity,
            string ownerIdentity,
            TimeSpan duration,
            CancellationToken cancellationToken = default)
        {
            ValidateIdentity(groupIdentity, nameof(groupIdentity));
            ValidateIdentity(ownerIdentity, nameof(ownerIdentity));
            ValidateDuration(duration);
            cancellationToken.ThrowIfCancellationRequested();

            lock (m_lock)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long now = m_timeProvider.GetTimestamp();
                if (m_leases.TryGetValue(groupIdentity, out LeaseRecord current) && !IsExpired(current, now))
                {
                    return new ValueTask<LeaseAcquisitionResult>(
                        new LeaseAcquisitionResult(LeaseAcquisitionStatus.LeaseHeld, null));
                }

                m_epochs.TryGetValue(groupIdentity, out long previousEpoch);
                long epoch;
                try
                {
                    epoch = checked(previousEpoch + 1);
                }
                catch (OverflowException exception)
                {
                    throw new InvalidOperationException("The lease fencing epoch is exhausted.", exception);
                }

                var token = new LeasePublicationToken(groupIdentity, ownerIdentity, epoch);
                m_epochs[groupIdentity] = epoch;
                m_leases[groupIdentity] = new LeaseRecord(ownerIdentity, epoch, now, duration);
                return new ValueTask<LeaseAcquisitionResult>(
                    new LeaseAcquisitionResult(LeaseAcquisitionStatus.Acquired, token));
            }
        }

        /// <inheritdoc/>
        public ValueTask<LeaseOperationStatus> RenewLeaseAsync(
            LeasePublicationToken token,
            TimeSpan duration,
            CancellationToken cancellationToken = default)
        {
            ValidateToken(token);
            ValidateDuration(duration);
            cancellationToken.ThrowIfCancellationRequested();

            lock (m_lock)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long now = m_timeProvider.GetTimestamp();
                LeaseOperationStatus status = ValidateLeaseLocked(token, now, out LeaseRecord current);
                if (status != LeaseOperationStatus.Succeeded)
                {
                    return new ValueTask<LeaseOperationStatus>(status);
                }

                m_leases[token.GroupIdentity] = current with { StartTimestamp = now, Duration = duration };
                return new ValueTask<LeaseOperationStatus>(LeaseOperationStatus.Succeeded);
            }
        }

        /// <inheritdoc/>
        public ValueTask<LeaseOperationStatus> ReleaseLeaseAsync(
            LeasePublicationToken token,
            CancellationToken cancellationToken = default)
        {
            ValidateToken(token);
            cancellationToken.ThrowIfCancellationRequested();

            lock (m_lock)
            {
                cancellationToken.ThrowIfCancellationRequested();
                LeaseOperationStatus status = ValidateLeaseLocked(
                    token,
                    m_timeProvider.GetTimestamp(),
                    out _);
                if (status != LeaseOperationStatus.Succeeded)
                {
                    return new ValueTask<LeaseOperationStatus>(status);
                }

                m_leases.Remove(token.GroupIdentity);
                return new ValueTask<LeaseOperationStatus>(LeaseOperationStatus.Succeeded);
            }
        }

        /// <inheritdoc/>
        public ValueTask<(bool Found, ByteString Value)> ReadSnapshotAsync(
            string groupIdentity,
            string snapshotKey,
            CancellationToken cancellationToken = default)
        {
            ValidateIdentity(groupIdentity, nameof(groupIdentity));
            ValidateIdentity(snapshotKey, nameof(snapshotKey));
            cancellationToken.ThrowIfCancellationRequested();

            lock (m_lock)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (m_snapshots.TryGetValue((groupIdentity, snapshotKey), out byte[]? content))
                {
                    return new ValueTask<(bool, ByteString)>(
                        (true, ByteString.From(content.AsSpan())));
                }
            }

            return new ValueTask<(bool, ByteString)>((false, default));
        }

        /// <inheritdoc/>
        public ValueTask<LeasePublicationStatus> PublishSnapshotAsync(
            LeasePublicationToken token,
            string snapshotKey,
            ByteString value,
            CancellationToken cancellationToken = default)
        {
            ValidateToken(token);
            ValidateIdentity(snapshotKey, nameof(snapshotKey));
            ValidatePayload(value, nameof(value));
            byte[] content = value.Span.ToArray();
            cancellationToken.ThrowIfCancellationRequested();

            lock (m_lock)
            {
                cancellationToken.ThrowIfCancellationRequested();
                LeasePublicationStatus status = ValidatePublicationLeaseLocked(token);
                if (status != LeasePublicationStatus.Published)
                {
                    return new ValueTask<LeasePublicationStatus>(status);
                }

                m_snapshots[(token.GroupIdentity, snapshotKey)] = content;
                return new ValueTask<LeasePublicationStatus>(LeasePublicationStatus.Published);
            }
        }

        /// <inheritdoc/>
        public ValueTask<LeasePublicationStatus> TryPublishSnapshotAsync(
            LeasePublicationToken token,
            string snapshotKey,
            ByteString expected,
            ByteString value,
            CancellationToken cancellationToken = default)
        {
            ValidateToken(token);
            ValidateIdentity(snapshotKey, nameof(snapshotKey));
            ValidatePayload(value, nameof(value));
            byte[]? expectedContent = expected.IsNull ? null : expected.Span.ToArray();
            byte[] content = value.Span.ToArray();
            cancellationToken.ThrowIfCancellationRequested();

            lock (m_lock)
            {
                cancellationToken.ThrowIfCancellationRequested();
                LeasePublicationStatus status = ValidatePublicationLeaseLocked(token);
                if (status != LeasePublicationStatus.Published)
                {
                    return new ValueTask<LeasePublicationStatus>(status);
                }

                (string Group, string Key) key = (token.GroupIdentity, snapshotKey);
                bool found = m_snapshots.TryGetValue(key, out byte[]? current);
                if (expected.IsNull ? found : !found || !current!.AsSpan().SequenceEqual(expectedContent))
                {
                    return new ValueTask<LeasePublicationStatus>(LeasePublicationStatus.SnapshotConflict);
                }

                m_snapshots[key] = content;
                return new ValueTask<LeasePublicationStatus>(LeasePublicationStatus.Published);
            }
        }

        /// <inheritdoc/>
        public ValueTask<LeasePublicationStatus> DeleteSnapshotAsync(
            LeasePublicationToken token,
            string snapshotKey,
            CancellationToken cancellationToken = default)
        {
            ValidateToken(token);
            ValidateIdentity(snapshotKey, nameof(snapshotKey));
            cancellationToken.ThrowIfCancellationRequested();

            lock (m_lock)
            {
                cancellationToken.ThrowIfCancellationRequested();
                LeasePublicationStatus status = ValidatePublicationLeaseLocked(token);
                if (status != LeasePublicationStatus.Published)
                {
                    return new ValueTask<LeasePublicationStatus>(status);
                }

                m_snapshots.Remove((token.GroupIdentity, snapshotKey));
                return new ValueTask<LeasePublicationStatus>(LeasePublicationStatus.Deleted);
            }
        }

        /// <inheritdoc/>
        public ValueTask<LeasePublicationStatus> TryDeleteSnapshotAsync(
            LeasePublicationToken token,
            string snapshotKey,
            ByteString expected,
            CancellationToken cancellationToken = default)
        {
            ValidateToken(token);
            ValidateIdentity(snapshotKey, nameof(snapshotKey));
            byte[]? expectedContent = expected.IsNull ? null : expected.Span.ToArray();
            cancellationToken.ThrowIfCancellationRequested();

            lock (m_lock)
            {
                cancellationToken.ThrowIfCancellationRequested();
                LeasePublicationStatus status = ValidatePublicationLeaseLocked(token);
                if (status != LeasePublicationStatus.Published)
                {
                    return new ValueTask<LeasePublicationStatus>(status);
                }

                (string Group, string Key) key = (token.GroupIdentity, snapshotKey);
                bool found = m_snapshots.TryGetValue(key, out byte[]? current);
                if (expected.IsNull ? found : !found || !current!.AsSpan().SequenceEqual(expectedContent))
                {
                    return new ValueTask<LeasePublicationStatus>(LeasePublicationStatus.SnapshotConflict);
                }

                m_snapshots.Remove(key);
                return new ValueTask<LeasePublicationStatus>(LeasePublicationStatus.Deleted);
            }
        }

        /// <summary>
        /// Validates a group, owner or snapshot identity.
        /// </summary>
        private static void ValidateIdentity(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("A non-empty identity is required.", parameterName);
            }
        }

        /// <summary>
        /// Rejects non-positive lease durations.
        /// </summary>
        private static void ValidateDuration(TimeSpan duration)
        {
            if (duration <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(duration), "A positive lease duration is required.");
            }
        }

        /// <summary>
        /// Validates the supplied fencing identity.
        /// </summary>
        private static void ValidateToken(LeasePublicationToken token)
        {
            if (token == null)
            {
                throw new ArgumentNullException(nameof(token));
            }
            ValidateIdentity(token.GroupIdentity, nameof(token));
            ValidateIdentity(token.OwnerIdentity, nameof(token));
            if (token.Epoch <= 0)
            {
                throw new ArgumentException("A positive fencing epoch is required.", nameof(token));
            }
        }

        /// <summary>
        /// Rejects null snapshot payloads.
        /// </summary>
        private static void ValidatePayload(ByteString value, string parameterName)
        {
            if (value.IsNull)
            {
                throw new ArgumentException("A non-null snapshot payload is required.", parameterName);
            }
        }

        /// <summary>
        /// Checks ownership, epoch and expiry while the authority state is protected.
        /// </summary>
        private LeaseOperationStatus ValidateLeaseLocked(
            LeasePublicationToken token,
            long now,
            out LeaseRecord current)
        {
            if (!m_leases.TryGetValue(token.GroupIdentity, out current) ||
                current.Epoch != token.Epoch ||
                !string.Equals(current.OwnerIdentity, token.OwnerIdentity, StringComparison.Ordinal))
            {
                return LeaseOperationStatus.LeaseMismatch;
            }

            return IsExpired(current, now)
                ? LeaseOperationStatus.LeaseExpired
                : LeaseOperationStatus.Succeeded;
        }

        /// <summary>
        /// Maps authoritative lease validation to a publication outcome.
        /// </summary>
        private LeasePublicationStatus ValidatePublicationLeaseLocked(LeasePublicationToken token)
        {
            LeaseOperationStatus status = ValidateLeaseLocked(token, m_timeProvider.GetTimestamp(), out _);
            return status switch
            {
                LeaseOperationStatus.Succeeded => LeasePublicationStatus.Published,
                LeaseOperationStatus.LeaseExpired => LeasePublicationStatus.LeaseExpired,
                _ => LeasePublicationStatus.LeaseMismatch
            };
        }

        /// <summary>
        /// Checks the inclusive expiry boundary using monotonic elapsed time.
        /// </summary>
        private bool IsExpired(LeaseRecord lease, long now)
        {
            return m_timeProvider.GetElapsedTime(lease.StartTimestamp, now) >= lease.Duration;
        }

        /// <summary>The authority's monotonic clock.</summary>
        private readonly TimeProvider m_timeProvider;
        /// <summary>Protects leases, epochs and snapshot publication together.</summary>
        private readonly Lock m_lock = new();
        /// <summary>The current lease for each group.</summary>
        private readonly Dictionary<string, LeaseRecord> m_leases = new(StringComparer.Ordinal);
        /// <summary>Retains fencing epochs after lease release.</summary>
        private readonly Dictionary<string, long> m_epochs = new(StringComparer.Ordinal);
        /// <summary>Detached complete snapshots scoped to lease groups.</summary>
        private readonly Dictionary<(string Group, string Key), byte[]> m_snapshots = new();

        /// <summary>
        /// Stores one authoritative ownership interval.
        /// </summary>
        private readonly record struct LeaseRecord(
            string OwnerIdentity,
            long Epoch,
            long StartTimestamp,
            TimeSpan Duration);
    }
}
