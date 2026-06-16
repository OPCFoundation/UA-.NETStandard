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
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Threading;

namespace Opc.Ua.PubSub.Security
{
    /// <summary>
    /// Sliding reception window enforcing replay and nonce-reuse
    /// rejection over the
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
    /// State per registered <c>TokenId</c>: the set of recently
    /// accepted sequence numbers (capped at
    /// <see cref="HistorySize"/>) and a fingerprint of recently seen
    /// nonces. Eviction is FIFO once the per-token cap is reached so
    /// the data structures stay bounded for long-running subscribers.
    /// </para>
    /// </remarks>
    public sealed class SecurityTokenWindow : ISecurityTokenWindow
    {
        private readonly Lock m_lock = new();
        private readonly TimeProvider m_timeProvider;
        private readonly int m_historySize;
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
            m_historySize = historySize;
            m_timeProvider = timeProvider ?? TimeProvider.System;
        }

        /// <summary>
        /// Configured per-token history size.
        /// </summary>
        public int HistorySize => m_historySize;

        /// <summary>
        /// Time source supplied to the window. Reserved for future use.
        /// </summary>
        public TimeProvider TimeProvider => m_timeProvider;

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
                    m_states.Add(tokenId, new TokenState());
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
            ulong fingerprint = ComputeNonceFingerprint(nonce);

            lock (m_lock)
            {
                if (!m_states.TryGetValue(tokenId, out TokenState? state))
                {
                    return false;
                }

                if (state.SeenSequences.Contains(sequenceNumber))
                {
                    return false;
                }

                if (fingerprint != 0 && state.SeenNonces.Contains(fingerprint))
                {
                    return false;
                }

                if (state.SeenSequences.Count >= m_historySize)
                {
                    ulong evictedSeq = state.SequenceOrder.Dequeue();
                    state.SeenSequences.Remove(evictedSeq);
                }
                state.SeenSequences.Add(sequenceNumber);
                state.SequenceOrder.Enqueue(sequenceNumber);

                if (fingerprint != 0)
                {
                    if (state.SeenNonces.Count >= m_historySize)
                    {
                        ulong evictedNonce = state.NonceOrder.Dequeue();
                        state.SeenNonces.Remove(evictedNonce);
                    }
                    state.SeenNonces.Add(fingerprint);
                    state.NonceOrder.Enqueue(fingerprint);
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

        private static ulong ComputeNonceFingerprint(ReadOnlySpan<byte> nonce)
        {
            // The nonce is normally 12 bytes for the AES-CTR policies;
            // we hash the first 8 bytes which already include the
            // MessageRandom prefix (4 bytes) plus part of the publisher
            // projection. Empty nonce (None policy) returns 0 — which
            // we treat as "no fingerprint" and skip nonce-reuse checks
            // for, matching the contract that None policy carries no
            // confidentiality guarantee.
            if (nonce.Length == 0)
            {
                return 0;
            }
            if (nonce.Length >= 8)
            {
                return BinaryPrimitives.ReadUInt64LittleEndian(nonce.Slice(0, 8));
            }
            Span<byte> padded = stackalloc byte[8];
            nonce.CopyTo(padded);
            return BinaryPrimitives.ReadUInt64LittleEndian(padded);
        }

        private sealed class TokenState
        {
            public HashSet<ulong> SeenSequences { get; } = [];
            public Queue<ulong> SequenceOrder { get; } = new();
            public HashSet<ulong> SeenNonces { get; } = [];
            public Queue<ulong> NonceOrder { get; } = new();
        }
    }
}
