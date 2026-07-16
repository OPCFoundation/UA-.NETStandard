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
    /// Monotonic sliding reception window enforcing replay and
    /// nonce-reuse rejection over the
    /// <c>(TokenId, SequenceNumber, Nonce)</c> triple.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implements the receiver-side replay protection requirement
    /// from
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.2.3">
    /// Part 14 §7.2.2.3 NetworkMessage processing</see> and the
    /// nonce-uniqueness obligation of
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.4.3.1">
    /// Part 14 §7.2.4.4.3.1 PubSub security policies</see>.
    /// </para>
    /// <para>
    /// State per registered <c>TokenId</c>: the highest accepted
    /// sequence number and a sliding bitmap of the most recent
    /// <see cref="HistorySize"/> sequence numbers (IPsec-style
    /// anti-replay). A sequence number that falls below the lower
    /// edge of the window — i.e. more than <see cref="HistorySize"/>
    /// behind the highest accepted value — is permanently rejected as
    /// "too old", so a captured message can never be replayed once the
    /// window has advanced past it (no eviction-replay). Duplicates
    /// inside the window are rejected via the bitmap.
    /// </para>
    /// <para>
    /// In addition the window retains the <b>full</b> bytes of the
    /// most recently seen nonces (bounded by <see cref="HistorySize"/>)
    /// and rejects any exact nonce reuse. Because every legitimate
    /// message carries a strictly increasing sequence number folded
    /// into its nonce, an evicted nonce always maps to a sequence
    /// below the window's lower edge and is therefore still rejected
    /// by the monotonic check.
    /// </para>
    /// </remarks>
    public sealed class SecurityTokenWindow : ISecurityTokenWindow
    {
        private readonly Lock m_lock = new();
        private readonly Dictionary<uint, TokenState> m_states = [];

        /// <summary>
        /// Initializes a new <see cref="SecurityTokenWindow"/>.
        /// </summary>
        /// <param name="historySize">
        /// Maximum number of accepted sequence numbers retained per
        /// token before eviction. Must be positive.
        /// </param>
        /// <param name="timeProvider">
        /// Time source. Currently unused — accepted for symmetry with
        /// other PubSub services and to allow future TTL eviction.
        /// </param>
        public SecurityTokenWindow(
            int historySize = 1024,
            TimeProvider? timeProvider = null)
        {
            if (historySize <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(historySize),
                    "History size must be positive.");
            }
            HistorySize = historySize;
            TimeProvider = timeProvider ?? TimeProvider.System;
        }

        /// <summary>
        /// Configured per-token history size.
        /// </summary>
        public int HistorySize { get; }

        /// <summary>
        /// Time source supplied to the window. Reserved for future use.
        /// </summary>
        public TimeProvider TimeProvider { get; }

        /// <summary>
        /// Snapshot of the currently registered tokens.
        /// </summary>
        public IReadOnlyCollection<uint> RegisteredTokens
        {
            get
            {
                lock (m_lock)
                {
                    return [.. m_states.Keys];
                }
            }
        }

        /// <summary>
        /// Registers a token id. Inbound messages with an unknown
        /// token id are rejected by <see cref="TryAccept"/>.
        /// </summary>
        /// <param name="tokenId">Token id to register.</param>
        public void RegisterToken(uint tokenId)
        {
            lock (m_lock)
            {
                if (!m_states.ContainsKey(tokenId))
                {
                    m_states.Add(tokenId, new TokenState(HistorySize));
                }
            }
        }

        /// <summary>
        /// Removes <paramref name="tokenId"/> from the window. Pending
        /// messages with the retired token are rejected immediately.
        /// </summary>
        /// <param name="tokenId">Token id to retire.</param>
        public void RetireToken(uint tokenId)
        {
            lock (m_lock)
            {
                m_states.Remove(tokenId);
            }
        }

        /// <inheritdoc/>
        public bool TryAccept(
            uint tokenId,
            ulong sequenceNumber,
            ReadOnlySpan<byte> nonce)
        {
            // Copy the nonce before taking the lock so the reuse set
            // can retain the full bytes (no truncation) for an exact
            // comparison on later frames.
            byte[]? nonceKey = nonce.Length == 0 ? null : nonce.ToArray();

            lock (m_lock)
            {
                if (!m_states.TryGetValue(tokenId, out TokenState? state))
                {
                    return false;
                }

                // Reject exact nonce reuse before mutating any state.
                if (nonceKey != null && state.SeenNonces.Contains(nonceKey))
                {
                    return false;
                }

                // Reject too-old / duplicate sequence numbers without
                // mutating the window when the nonce check passed.
                if (!state.WouldAcceptSequence(sequenceNumber, HistorySize))
                {
                    return false;
                }

                state.CommitSequence(sequenceNumber, HistorySize);

                if (nonceKey != null)
                {
                    if (state.SeenNonces.Count >= HistorySize)
                    {
                        byte[] evicted = state.NonceOrder.Dequeue();
                        state.SeenNonces.Remove(evicted);
                    }
                    state.SeenNonces.Add(nonceKey);
                    state.NonceOrder.Enqueue(nonceKey);
                }

                return true;
            }
        }

        /// <inheritdoc/>
        public void Reset()
        {
            lock (m_lock)
            {
                m_states.Clear();
            }
        }

        private sealed class TokenState
        {
            private readonly ulong[] m_window;
            private bool m_hasHighest;
            private ulong m_highest;

            public TokenState(int historyBits)
            {
                m_window = new ulong[(historyBits + 63) / 64];
            }

            public HashSet<byte[]> SeenNonces { get; } = new(NonceComparer.Instance);

            public Queue<byte[]> NonceOrder { get; } = new();

            /// <summary>
            /// Returns whether <paramref name="sequenceNumber"/> would
            /// be accepted without mutating any state.
            /// </summary>
            public bool WouldAcceptSequence(ulong sequenceNumber, int historyBits)
            {
                if (!m_hasHighest || sequenceNumber > m_highest)
                {
                    return true;
                }
                ulong offset = m_highest - sequenceNumber;
                if (offset >= (ulong)historyBits)
                {
                    return false;
                }
                return !GetBit((int)offset);
            }

            /// <summary>
            /// Records an accepted <paramref name="sequenceNumber"/>,
            /// advancing the window when it is the new highest value.
            /// </summary>
            public void CommitSequence(ulong sequenceNumber, int historyBits)
            {
                if (!m_hasHighest)
                {
                    m_hasHighest = true;
                    m_highest = sequenceNumber;
                    Array.Clear(m_window, 0, m_window.Length);
                    SetBit(0);
                    return;
                }
                if (sequenceNumber > m_highest)
                {
                    ShiftUp(sequenceNumber - m_highest, historyBits);
                    m_highest = sequenceNumber;
                    SetBit(0);
                    return;
                }
                SetBit((int)(m_highest - sequenceNumber));
            }

            private bool GetBit(int index)
            {
                return (m_window[index >> 6] & (1UL << (index & 63))) != 0;
            }

            private void SetBit(int index)
            {
                m_window[index >> 6] |= 1UL << (index & 63);
            }

            private void ShiftUp(ulong delta, int historyBits)
            {
                if (delta >= (ulong)historyBits)
                {
                    Array.Clear(m_window, 0, m_window.Length);
                    return;
                }
                int d = (int)delta;
                int wordShift = d >> 6;
                int bitShift = d & 63;
                for (int i = m_window.Length - 1; i >= 0; i--)
                {
                    ulong value = 0;
                    int src = i - wordShift;
                    if (src >= 0)
                    {
                        value = m_window[src] << bitShift;
                        if (bitShift != 0 && src >= 1)
                        {
                            value |= m_window[src - 1] >> (64 - bitShift);
                        }
                    }
                    m_window[i] = value;
                }
                int topBits = historyBits & 63;
                if (topBits != 0)
                {
                    m_window[^1] &= (1UL << topBits) - 1;
                }
            }
        }

        private sealed class NonceComparer : IEqualityComparer<byte[]>
        {
            public static readonly NonceComparer Instance = new();

            public bool Equals(byte[]? x, byte[]? y)
            {
                if (ReferenceEquals(x, y))
                {
                    return true;
                }
                if (x is null || y is null)
                {
                    return false;
                }
                return x.AsSpan().SequenceEqual(y);
            }

            public int GetHashCode(byte[] obj)
            {
                unchecked
                {
                    const ulong offsetBasis = 14695981039346656037UL;
                    const ulong prime = 1099511628211UL;
                    ulong hash = offsetBasis;
                    for (int i = 0; i < obj.Length; i++)
                    {
                        hash = (hash ^ obj[i]) * prime;
                    }
                    return (int)(hash ^ (hash >> 32));
                }
            }
        }
    }
}
