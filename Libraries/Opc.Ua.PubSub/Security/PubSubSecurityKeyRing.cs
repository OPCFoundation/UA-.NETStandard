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

namespace Opc.Ua.PubSub.Security
{
    /// <summary>
    /// In-memory ring of SKS-issued <see cref="PubSubSecurityKey"/>
    /// instances tracked for one SecurityGroup. The ring keeps the
    /// active token, a configurable list of past tokens (so late
    /// messages with previous TokenIds can still be decrypted) and
    /// any pre-fetched future tokens awaiting rotation.
    /// </summary>
    /// <remarks>
    /// Implements the SecurityGroup key-ring concept described in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/8.3">
    /// Part 14 §8.3 Security Key Service</see>. The ring is the
    /// stateful object inside <see cref="StaticSecurityKeyProvider"/>
    /// and any SKS-backed provider added in Phase 8.
    /// </remarks>
    public sealed class PubSubSecurityKeyRing : IDisposable
    {
        /// <summary>
        /// Default upper bound on retained past keys.
        /// </summary>
        public const int DefaultPastKeyLimit = 4;

        private readonly Lock m_lock = new();
        private readonly TimeProvider m_timeProvider;
        private readonly int m_pastKeyLimit;
        private readonly LinkedList<PubSubSecurityKey> m_past = new();
        private readonly Queue<PubSubSecurityKey> m_future = new();
        private readonly Dictionary<uint, PubSubSecurityKey> m_byToken = [];
        private PubSubSecurityKey? m_current;

        /// <summary>
        /// Initializes a new <see cref="PubSubSecurityKeyRing"/>.
        /// </summary>
        /// <param name="securityGroupId">Owning SecurityGroup id.</param>
        /// <param name="timeProvider">Time source.</param>
        /// <param name="pastKeyLimit">
        /// Maximum number of expired tokens retained for late-arrival
        /// decryption. Defaults to <see cref="DefaultPastKeyLimit"/>.
        /// </param>
        public PubSubSecurityKeyRing(
            string securityGroupId,
            TimeProvider? timeProvider = null,
            int pastKeyLimit = DefaultPastKeyLimit)
        {
            if (string.IsNullOrEmpty(securityGroupId))
            {
                throw new ArgumentException(
                    "SecurityGroupId must be non-empty.",
                    nameof(securityGroupId));
            }
            if (pastKeyLimit < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(pastKeyLimit),
                    "Past key limit must be non-negative.");
            }
            SecurityGroupId = securityGroupId;
            m_timeProvider = timeProvider ?? TimeProvider.System;
            m_pastKeyLimit = pastKeyLimit;
        }

        /// <summary>
        /// SecurityGroup identifier this ring belongs to.
        /// </summary>
        public string SecurityGroupId { get; }

        /// <summary>
        /// Currently active key, or <see langword="null"/> if none has
        /// been provisioned yet.
        /// </summary>
        public PubSubSecurityKey? Current
        {
            get
            {
                lock (m_lock)
                {
                    ThrowIfDisposed();
                    return m_current;
                }
            }
        }

        /// <summary>
        /// Snapshot of every token id currently known to this ring
        /// (current + past + future).
        /// </summary>
        public IReadOnlyList<uint> KnownTokenIds
        {
            get
            {
                lock (m_lock)
                {
                    ThrowIfDisposed();
                    return [.. m_byToken.Keys];
                }
            }
        }

        /// <summary>
        /// Raised every time the active token rotates.
        /// </summary>
        public event EventHandler<PubSubKeyRotatedEventArgs>? Rotated;

        /// <summary>
        /// Sets <paramref name="key"/> as the active token, moving the
        /// previous active token into the past list.
        /// </summary>
        /// <param name="key">New active key.</param>
        public void SetCurrent(PubSubSecurityKey key)
        {
            if (key is null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            uint? previousTokenId;
            lock (m_lock)
            {
                ThrowIfDisposed();
                previousTokenId = m_current?.TokenId;
                if (m_current != null)
                {
                    DemoteToPastLocked(m_current);
                }
                m_current = key;
                m_byToken[key.TokenId] = key;
            }
            RaiseRotated(key.TokenId, previousTokenId);
        }

        /// <summary>
        /// Adds a future token, queued for use by
        /// <see cref="RotateToNextFuture"/>.
        /// </summary>
        /// <param name="key">Future key.</param>
        public void AddFuture(PubSubSecurityKey key)
        {
            if (key is null)
            {
                throw new ArgumentNullException(nameof(key));
            }
            lock (m_lock)
            {
                ThrowIfDisposed();
                m_future.Enqueue(key);
                m_byToken[key.TokenId] = key;
            }
        }

        /// <summary>
        /// Promotes the next queued future key to be the active key.
        /// </summary>
        /// <returns>
        /// <see langword="true"/> when a future key was promoted;
        /// <see langword="false"/> when the queue was empty.
        /// </returns>
        public bool RotateToNextFuture()
        {
            uint? previousTokenId;
            uint newTokenId;
            lock (m_lock)
            {
                ThrowIfDisposed();
                if (m_future.Count == 0)
                {
                    return false;
                }
                PubSubSecurityKey next = m_future.Dequeue();
                previousTokenId = m_current?.TokenId;
                if (m_current != null)
                {
                    DemoteToPastLocked(m_current);
                }
                m_current = next;
                m_byToken[next.TokenId] = next;
                newTokenId = next.TokenId;
            }
            RaiseRotated(newTokenId, previousTokenId);
            return true;
        }

        /// <summary>
        /// Looks up a previously-observed token by id.
        /// </summary>
        /// <param name="tokenId">Token id.</param>
        /// <returns>The key or <see langword="null"/>.</returns>
        public PubSubSecurityKey? TryGetByTokenId(uint tokenId)
        {
            lock (m_lock)
            {
                ThrowIfDisposed();
                return m_byToken.TryGetValue(tokenId, out PubSubSecurityKey? key) ? key : null;
            }
        }

        /// <summary>
        /// Zeroizes all retained key material and clears the ring.
        /// </summary>
        public void Dispose()
        {
            lock (m_lock)
            {
                if (m_disposed)
                {
                    return;
                }

                foreach (PubSubSecurityKey key in m_byToken.Values)
                {
                    key.Dispose();
                }

                m_current?.Dispose();
                m_current = null;
                m_past.Clear();
                m_future.Clear();
                m_byToken.Clear();
                m_disposed = true;
            }
        }

        private void DemoteToPastLocked(PubSubSecurityKey key)
        {
            m_past.AddLast(key);
            while (m_past.Count > m_pastKeyLimit)
            {
                LinkedListNode<PubSubSecurityKey>? oldest = m_past.First;
                if (oldest is null)
                {
                    break;
                }
                m_past.RemoveFirst();
                m_byToken.Remove(oldest.Value.TokenId);
                DisposeIfUnretainedLocked(oldest.Value);
            }
        }

        private void DisposeIfUnretainedLocked(PubSubSecurityKey key)
        {
            if (ReferenceEquals(m_current, key) || m_past.Contains(key))
            {
                return;
            }

            foreach (PubSubSecurityKey future in m_future)
            {
                if (ReferenceEquals(future, key))
                {
                    return;
                }
            }

            key.Dispose();
        }

        private void RaiseRotated(uint newTokenId, uint? previousTokenId)
        {
            EventHandler<PubSubKeyRotatedEventArgs>? handler = Rotated;
            if (handler is null)
            {
                return;
            }
            DateTimeUtc now = DateTimeUtc.From(m_timeProvider.GetUtcNow().UtcDateTime);
            handler.Invoke(this, new PubSubKeyRotatedEventArgs(newTokenId, previousTokenId, now));
        }

        private void ThrowIfDisposed()
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(PubSubSecurityKeyRing));
            }
        }

        private bool m_disposed;
    }
}
