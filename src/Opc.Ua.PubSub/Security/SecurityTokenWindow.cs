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
using Opc.Ua.PubSub.Encoding;

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
    /// The stack receive path keeps independent state per authenticated
    /// <c>(PublisherId, WriterGroupId, TokenId)</c> scope. The legacy
    /// <see cref="ISecurityTokenWindow.TryAccept"/> contract retains a
    /// token-only scope for compatibility. Each scope tracks the highest
    /// accepted sequence number and a sliding bitmap of the most recent
    /// <see cref="HistorySize"/> sequence numbers (IPsec-style anti-replay).
    /// A sequence number that falls below the lower edge of the window is
    /// permanently rejected as "too old", so a captured message can never
    /// be replayed once the window has advanced past it (no eviction-replay).
    /// </para>
    /// <para>
    /// In addition each token owns a fixed-size 1 MiB Bloom filter that rejects
    /// nonce reuse globally across all publisher and writer-group scopes
    /// without retaining an unbounded set of nonce values. Filter bits are
    /// never cleared while the token is active, so an exact nonce reuse
    /// cannot become acceptable. As with any Bloom filter, saturation can
    /// cause a fresh nonce to be rejected as a false positive; retiring the
    /// token or resetting the window clears the filter.
    /// </para>
    /// </remarks>
    public sealed class SecurityTokenWindow : ISecurityTokenWindow, IScopedSecurityTokenWindow
    {
        private readonly Lock m_lock = new();
        private readonly Dictionary<uint, TokenState> m_states = [];
        private readonly Dictionary<ScopedTokenKey, TokenState> m_scopedStates = [];
        private readonly Dictionary<uint, NonceReplayFilter> m_nonceFilters = [];

        /// <summary>
        /// Initializes a new <see cref="SecurityTokenWindow"/>.
        /// </summary>
        /// <param name="historySize">
        /// Maximum number of accepted sequence numbers retained per
        /// publisher/writer-group/token scope. Must be positive.
        /// </param>
        /// <param name="timeProvider">
        /// Time source. Currently unused — accepted for symmetry with
        /// other PubSub services and future policy enforcement.
        /// </param>
        public SecurityTokenWindow(
            int historySize = 1024,
            TimeProvider? timeProvider = null)
            : this(historySize, timeProvider, DefaultNonceFilterSizeInBytes)
        {
        }

        internal SecurityTokenWindow(
            int historySize,
            TimeProvider? timeProvider,
            int nonceFilterSizeInBytes)
        {
            if (historySize <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(historySize),
                    "History size must be positive.");
            }
            if (nonceFilterSizeInBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(nonceFilterSizeInBytes),
                    "Nonce filter size must be positive.");
            }
            HistorySize = historySize;
            TimeProvider = timeProvider ?? TimeProvider.System;
            NonceFilterSizeInBytes = nonceFilterSizeInBytes;
        }

        /// <summary>
        /// Configured per-scope sequence history size.
        /// </summary>
        public int HistorySize { get; }

        /// <summary>
        /// Time source supplied to the window. Reserved for future use.
        /// </summary>
        public TimeProvider TimeProvider { get; }

        internal int NonceFilterSizeInBytes { get; }

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
                    m_nonceFilters.Add(tokenId, new NonceReplayFilter(NonceFilterSizeInBytes));
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
                m_nonceFilters.Remove(tokenId);
                List<ScopedTokenKey>? retiredScopes = null;
                foreach (ScopedTokenKey key in m_scopedStates.Keys)
                {
                    if (key.TokenId == tokenId)
                    {
                        retiredScopes ??= [];
                        retiredScopes.Add(key);
                    }
                }
                if (retiredScopes is not null)
                {
                    foreach (ScopedTokenKey key in retiredScopes)
                    {
                        m_scopedStates.Remove(key);
                    }
                }
            }
        }

        /// <inheritdoc/>
        public bool TryAccept(
            uint tokenId,
            ulong sequenceNumber,
            ReadOnlySpan<byte> nonce)
        {
            lock (m_lock)
            {
                if (!m_states.TryGetValue(tokenId, out TokenState? state) ||
                    !m_nonceFilters.TryGetValue(tokenId, out NonceReplayFilter? nonceFilter))
                {
                    return false;
                }

                return TryAcceptState(
                    state,
                    nonceFilter,
                    sequenceNumber,
                    nonce);
            }
        }

        bool IScopedSecurityTokenWindow.TryAccept(
            PublisherId publisherId,
            ushort writerGroupId,
            uint tokenId,
            ulong sequenceNumber,
            ReadOnlySpan<byte> nonce)
        {
            lock (m_lock)
            {
                if (!m_states.ContainsKey(tokenId) ||
                    !m_nonceFilters.TryGetValue(tokenId, out NonceReplayFilter? nonceFilter))
                {
                    return false;
                }

                var key = new ScopedTokenKey(publisherId, writerGroupId, tokenId);
                if (!m_scopedStates.TryGetValue(key, out TokenState? state))
                {
                    state = new TokenState(HistorySize);
                    m_scopedStates.Add(key, state);
                }
                return TryAcceptState(
                    state,
                    nonceFilter,
                    sequenceNumber,
                    nonce);
            }
        }

        /// <inheritdoc/>
        public void Reset()
        {
            lock (m_lock)
            {
                m_states.Clear();
                m_scopedStates.Clear();
                m_nonceFilters.Clear();
            }
        }

        private bool TryAcceptState(
            TokenState state,
            NonceReplayFilter nonceFilter,
            ulong sequenceNumber,
            ReadOnlySpan<byte> nonce)
        {
            if (!nonce.IsEmpty && nonceFilter.MightContain(nonce))
            {
                return false;
            }

            if (!state.WouldAcceptSequence(sequenceNumber, HistorySize))
            {
                return false;
            }

            state.CommitSequence(sequenceNumber, HistorySize);

            if (!nonce.IsEmpty)
            {
                nonceFilter.Add(nonce);
            }

            return true;
        }

        private readonly record struct ScopedTokenKey(
            PublisherId PublisherId,
            ushort WriterGroupId,
            uint TokenId);

        private sealed class NonceReplayFilter
        {
            public NonceReplayFilter(int sizeInBytes)
            {
                m_bits = new byte[sizeInBytes];
                m_bitCount = checked((ulong)sizeInBytes * 8);
            }

            public bool MightContain(ReadOnlySpan<byte> nonce)
            {
                (ulong firstHash, ulong secondHash) = ComputeHashes(nonce);
                for (int ii = 0; ii < NonceHashCount; ii++)
                {
                    ulong bitIndex = unchecked(firstHash + ((ulong)ii * secondHash)) % m_bitCount;
                    int bitMask = 1 << (int)(bitIndex & 7);
                    if ((m_bits[(int)(bitIndex >> 3)] & bitMask) == 0)
                    {
                        return false;
                    }
                }

                return true;
            }

            public void Add(ReadOnlySpan<byte> nonce)
            {
                (ulong firstHash, ulong secondHash) = ComputeHashes(nonce);
                for (int ii = 0; ii < NonceHashCount; ii++)
                {
                    ulong bitIndex = unchecked(firstHash + ((ulong)ii * secondHash)) % m_bitCount;
                    m_bits[(int)(bitIndex >> 3)] |= (byte)(1 << (int)(bitIndex & 7));
                }
            }

            private static (ulong FirstHash, ulong SecondHash) ComputeHashes(
                ReadOnlySpan<byte> nonce)
            {
                ulong firstHash = ComputeHash(nonce, 14695981039346656037UL);
                ulong secondHash = ComputeHash(nonce, 7809847782465536322UL) | 1UL;
                return (firstHash, secondHash);
            }

            private static ulong ComputeHash(ReadOnlySpan<byte> nonce, ulong seed)
            {
                unchecked
                {
                    const ulong prime = 1099511628211UL;
                    ulong hash = seed ^ (ulong)nonce.Length;
                    for (int ii = 0; ii < nonce.Length; ii++)
                    {
                        hash = (hash ^ nonce[ii]) * prime;
                    }

                    hash ^= hash >> 33;
                    hash *= 0xff51afd7ed558ccdUL;
                    hash ^= hash >> 33;
                    hash *= 0xc4ceb9fe1a85ec53UL;
                    return hash ^ (hash >> 33);
                }
            }

            private const int NonceHashCount = 7;

            private readonly byte[] m_bits;
            private readonly ulong m_bitCount;
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

        private const int DefaultNonceFilterSizeInBytes = 1024 * 1024;

    }
}
