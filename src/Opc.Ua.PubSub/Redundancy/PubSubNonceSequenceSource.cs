// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Redundancy
{
    /// <summary>
    /// Consumes previously committed sequence ranges on the async secured publication path.
    /// </summary>
    internal sealed class PubSubNonceSequenceSource : IDisposable
    {
        /// <summary>
        /// Creates a connection-local range cache; the store and coordinator remain caller-owned.
        /// </summary>
        public PubSubNonceSequenceSource(
            IPubSubNonceRangeStore? store,
            IPubSubActivationCoordinator coordinator,
            IPubSubActivationFenceProvider? fences)
        {
            m_store = store;
            m_coordinator = coordinator;
            m_fences = fences ?? coordinator as IPubSubActivationFenceProvider;
        }

        /// <summary>
        /// Captures the lease for one send. Standby instances do not request nonce ranges.
        /// </summary>
        public async ValueTask<long> GetActiveFenceAsync(string componentId, CancellationToken cancellationToken)
        {
            if (await m_coordinator.GetRoleAsync(componentId, cancellationToken).ConfigureAwait(false)
                != PubSubComponentRole.Active)
            {
                return 0;
            }
            if (m_store is null || m_fences is null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed,
                    "Redundant secured publishing requires a durable nonce range store and fenced activation.");
            }
            return m_fences.TryGetActiveFence(componentId, out long fence) && fence > 0 ? fence : 0;
        }

        /// <summary>
        /// Takes a nonce sequence, asynchronously committing another range if necessary.
        /// </summary>
        public async ValueTask<uint> NextAsync(
            PublisherId publisherId,
            ushort writerGroupId,
            uint tokenId,
            string componentId,
            long fence,
            CancellationToken cancellationToken)
        {
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!m_ranges.TryGetValue(componentId, out RangeCursor cursor) ||
                    cursor.PublisherId != publisherId ||
                    cursor.WriterGroupId != writerGroupId ||
                    cursor.TokenId != tokenId ||
                    cursor.Fence != fence ||
                    cursor.Next > cursor.Last)
                {
                    if (await GetActiveFenceAsync(componentId, cancellationToken).ConfigureAwait(false) != fence)
                    {
                        throw LostFence();
                    }
                    PubSubNonceRange? range = await m_store!.ReserveAsync(
                        publisherId, writerGroupId, tokenId, componentId, fence, kRangeSize, cancellationToken)
                        .ConfigureAwait(false);
                    if (range is not { First: > 0 } committed || committed.Last < committed.First)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadSecurityChecksFailed,
                            "No PubSub nonce sequences are available. Rotate the key and SecurityTokenId.");
                    }
                    cursor = new RangeCursor(
                        publisherId, writerGroupId, tokenId, fence, committed.First, committed.Last);
                }

                if (await GetActiveFenceAsync(componentId, cancellationToken).ConfigureAwait(false) != fence)
                {
                    throw LostFence();
                }
                cancellationToken.ThrowIfCancellationRequested();
                m_ranges[componentId] = cursor with { Next = cursor.Next + 1 };
                return (uint)cursor.Next;
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_gate.Dispose();
        }

        private static ServiceResultException LostFence()
        {
            return new ServiceResultException(
                StatusCodes.BadSecurityChecksFailed, "The PubSub activation lease changed before publication.");
        }

        private readonly record struct RangeCursor(
            PublisherId PublisherId, ushort WriterGroupId, uint TokenId, long Fence, ulong Next, uint Last);

        private const uint kRangeSize = 1024;
        private readonly IPubSubNonceRangeStore? m_store;
        private readonly IPubSubActivationCoordinator m_coordinator;
        private readonly IPubSubActivationFenceProvider? m_fences;
        private readonly SemaphoreSlim m_gate = new(1, 1);
        private readonly Dictionary<string, RangeCursor> m_ranges = new(StringComparer.Ordinal);
    }
}
