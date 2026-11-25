// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Buffers.Binary;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.PubSub.Redundancy;
using Opc.Ua.Redundancy;

namespace Opc.Ua.PubSub.Configuration
{
    /// <summary>
    /// Distributed <see cref="IPubSubRuntimeStateStore"/> backed by an
    /// <see cref="ISharedKeyValueStore"/>, so a redundant PubSub instance can rebuild
    /// component state after a failover (Warm/Hot standby, OPC UA Part 14 §9.1.6).
    /// </summary>
    /// <remarks>
    /// External state requires an enrolled <see cref="FreshnessProtectedKeyValueStore"/> and a
    /// current activation fence. A state already written with a fence cannot be overwritten by
    /// an older or unfenced writer, even with an explicit process-local backend.
    /// </remarks>
    public sealed class SharedStorePubSubRuntimeStateStore : IPubSubRuntimeStateStore
    {
        /// <summary>
        /// Initializes a new <see cref="SharedStorePubSubRuntimeStateStore"/>.
        /// </summary>
        /// <param name="store">Shared key/value backend used for component state.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="store"/> is <see langword="null"/>.
        /// </exception>
        public SharedStorePubSubRuntimeStateStore(ISharedKeyValueStore store)
        {
            m_store = FencedSharedStoreValue.RequireStore(store);
        }

        /// <inheritdoc/>
        public async ValueTask<PubSubState?> GetStateAsync(
            string componentId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(componentId))
            {
                throw new ArgumentException("componentId must be non-empty.", nameof(componentId));
            }

            (bool found, ByteString value) = await FencedSharedStoreValue.ReadRetainedAsync(
                    m_store, PubSubRedundancyStoreKeys.RuntimeStatePrefix + componentId, cancellationToken)
                .ConfigureAwait(false);
            bool allowUnfenced = m_store is InMemorySharedKeyValueStore;
            if (!found ||
                !FencedSharedStoreValue.TryExtractFixedPayload(value, sizeof(int), allowUnfenced,
                    out ByteString payload, out _))
            {
                return null;
            }

            var state = (PubSubState)BinaryPrimitives.ReadInt32LittleEndian(payload.Span);
            return IsValidState(state)
                ? state
                : throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "The shared PubSub runtime state is invalid.");
        }

        /// <inheritdoc/>
        public ValueTask SetStateAsync(
            string componentId,
            PubSubState state,
            CancellationToken cancellationToken = default)
        {
            return SetStateCoreAsync(componentId, state, null, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask SetStateAsync(
            string componentId,
            PubSubState state,
            long fencingToken,
            CancellationToken cancellationToken = default)
        {
            return SetStateCoreAsync(componentId, state, fencingToken, cancellationToken);
        }

        private ValueTask SetStateCoreAsync(
            string componentId,
            PubSubState state,
            long? fencingToken,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(componentId))
            {
                throw new ArgumentException("componentId must be non-empty.", nameof(componentId));
            }
            if (!IsValidState(state))
            {
                throw new ArgumentOutOfRangeException(nameof(state));
            }

            byte[] buffer = new byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(buffer, (int)state);
            return FencedSharedStoreValue.StoreFixedAsync(
                m_store,
                PubSubRedundancyStoreKeys.RuntimeStatePrefix + componentId,
                new ByteString(buffer),
                fencingToken,
                cancellationToken);
        }

        private static bool IsValidState(PubSubState state)
        {
            return state is PubSubState.Disabled or PubSubState.Paused or PubSubState.Operational or
                PubSubState.Error or PubSubState.PreOperational;
        }

        private readonly ISharedKeyValueStore m_store;
    }
}
