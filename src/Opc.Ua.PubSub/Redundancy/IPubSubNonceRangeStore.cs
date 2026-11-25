// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Redundancy
{
    /// <summary>
    /// Reserves durable AES-CTR MessageNonce sequence numbers for secured publishers.
    /// </summary>
    public interface IPubSubNonceRangeStore
    {
        /// <summary>
        /// Commits a range for the wire replay identity and security token before returning it.
        /// Ranges must advance across restarts and lease changes, including unused numbers.
        /// </summary>
        /// <param name="publisherId">PublisherId as seen by subscriber replay protection.</param>
        /// <param name="writerGroupId">WriterGroupId as seen by subscriber replay protection.</param>
        /// <param name="tokenId">SecurityTokenId of the publication key.</param>
        /// <param name="componentId">Stable component identity whose lease governs this replay scope.</param>
        /// <param name="fencingToken">Positive fence of the currently held activation lease.</param>
        /// <param name="count">Maximum number of sequence numbers requested.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A committed range, or no range if this token is exhausted.</returns>
        ValueTask<PubSubNonceRange?> ReserveAsync(
            PublisherId publisherId,
            ushort writerGroupId,
            uint tokenId,
            string componentId,
            long fencingToken,
            uint count,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// An inclusive range of nonzero UADP MessageNonce sequence numbers.
    /// </summary>
    /// <param name="First">First reserved sequence number.</param>
    /// <param name="Last">Last reserved sequence number.</param>
    public readonly record struct PubSubNonceRange(uint First, uint Last);
}
