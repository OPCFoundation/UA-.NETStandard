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
    /// Component runtime state is a public lifecycle value rather than a secret, so it is
    /// stored without record protection. When the caller provides a fencing token the write is
    /// additionally guarded against stale-active overwrite; callers that do not yet flow a token
    /// keep the previous unconditional behavior. Use the security-key or session stores for
    /// material that must be encrypted at rest.
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
            m_store = store ?? throw new ArgumentNullException(nameof(store));
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

            (bool found, ByteString value) = await m_store
                .TryGetAsync(PubSubRedundancyStoreKeys.RuntimeStatePrefix + componentId, cancellationToken)
                .ConfigureAwait(false);
            if (!found || !FencedSharedStoreValue.TryExtractPayload(value, out ByteString payload, out _))
            {
                return null;
            }

            ReadOnlySpan<byte> bytes = payload.Span;
            if (bytes.Length < sizeof(int))
            {
                return null;
            }

            return (PubSubState)BinaryPrimitives.ReadInt32LittleEndian(bytes);
        }

        /// <inheritdoc/>
        public async ValueTask SetStateAsync(
            string componentId,
            PubSubState state,
            CancellationToken cancellationToken = default)
        {
            await SetStateCoreAsync(componentId, state, null, cancellationToken).ConfigureAwait(false);
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

        private async ValueTask SetStateCoreAsync(
            string componentId,
            PubSubState state,
            long? fencingToken,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(componentId))
            {
                throw new ArgumentException("componentId must be non-empty.", nameof(componentId));
            }

            byte[] buffer = new byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(buffer, (int)state);
            await FencedSharedStoreValue.StoreAsync(
                m_store,
                PubSubRedundancyStoreKeys.RuntimeStatePrefix + componentId,
                new ByteString(buffer),
                fencingToken,
                cancellationToken).ConfigureAwait(false);
        }

        private readonly ISharedKeyValueStore m_store;
    }
}
