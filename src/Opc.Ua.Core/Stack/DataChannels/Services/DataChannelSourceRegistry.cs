/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// A Node that can be one end of a data channel.
    /// </summary>
    /// <remarks>
    /// A server registers one of these per Node that implements
    /// IDataChannelSourceType. The registry is what OpenDataChannel
    /// resolves sourceNodeId against, and is deliberately independent of
    /// the AddressSpace so that a channel can be opened before the model
    /// is browsable. Registration only states that the source exists; when
    /// the source is not owned by an AddressSpace NodeManager the server
    /// must provide an explicit <see cref="IDataChannelAuthorizer"/> policy
    /// before granting access.
    /// </remarks>
    public interface IDataChannelSource
    {
        /// <summary>
        /// The Node the channel is opened on.
        /// </summary>
        NodeId NodeId { get; }

        /// <summary>
        /// What this endpoint will accept.
        /// </summary>
        DataChannelSourceCapabilities Capabilities { get; }

        /// <summary>
        /// The number of channels currently open on this endpoint,
        /// across all Sessions.
        /// </summary>
        int ActiveChannelCount { get; }

        /// <summary>
        /// Called once the channel has reached Open, so the source can
        /// start producing or consuming payload.
        /// </summary>
        /// <param name="channel">The channel.</param>
        void OnChannelOpened(DataChannel channel);

        /// <summary>
        /// Called once the channel has reached Closed or Faulted.
        /// </summary>
        /// <param name="channel">The channel.</param>
        /// <param name="reason">Why.</param>
        void OnChannelClosed(DataChannel channel, StatusCode reason);
    }

    /// <summary>
    /// The data channel sources a server hosts, resolved by NodeId.
    /// </summary>
    public sealed class DataChannelSourceRegistry
    {
        /// <summary>
        /// Adds or replaces a source.
        /// </summary>
        /// <param name="source">The source.</param>
        public void Register(IDataChannelSource source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            m_sources[source.NodeId] = source;
        }

        /// <summary>
        /// Removes a source.
        /// </summary>
        /// <param name="nodeId">The Node.</param>
        public bool Unregister(NodeId nodeId)
        {
            return m_sources.TryRemove(nodeId, out _);
        }

        /// <summary>
        /// Resolves a source.
        /// </summary>
        /// <param name="nodeId">The Node named by the request.</param>
        /// <param name="source">The source.</param>
        public bool TryGet(NodeId nodeId, out IDataChannelSource? source)
        {
            return m_sources.TryGetValue(nodeId, out source);
        }

        /// <summary>
        /// The registered sources.
        /// </summary>
        public IReadOnlyCollection<IDataChannelSource> Sources => [.. m_sources.Values];

        private readonly ConcurrentDictionary<NodeId, IDataChannelSource> m_sources = new();
    }

    /// <summary>
    /// The server initiated offers outstanding on one SecureChannel.
    /// </summary>
    /// <remarks>
    /// OPC UA Services are request and response and a server cannot call
    /// a client, so a server that wants a stream to start offers instead:
    /// it raises a DataChannelOfferedEventType Event and the client
    /// accepts by quoting the OfferId in OpenDataChannel. A client that is
    /// not subscribed never learns of the offer, which is the correct
    /// outcome - a server must not be able to push bytes at a client that
    /// has not asked for them.
    /// </remarks>
    public sealed class DataChannelOfferRegistry
    {
        /// <summary>
        /// Creates a registry.
        /// </summary>
        /// <param name="timeProvider">The clock used to expire
        /// offers.</param>
        public DataChannelOfferRegistry(TimeProvider? timeProvider = null)
        {
            m_timeProvider = timeProvider ?? TimeProvider.System;
        }

        /// <summary>
        /// Creates an offer. The identifier is single use and scoped to
        /// the SecureChannel it was delivered on.
        /// </summary>
        /// <param name="sourceNodeId">The endpoint being offered.</param>
        /// <param name="parameters">The parameters proposed.</param>
        /// <param name="lifetime">How long the offer stands.</param>
        public DataChannelOfferDataType Create(
            NodeId sourceNodeId,
            DataChannelParametersDataType parameters,
            TimeSpan lifetime)
        {
            uint offerId = (uint)Interlocked.Increment(ref m_nextOfferId);

            var offer = new DataChannelOfferDataType
            {
                OfferId = offerId,
                SourceNodeId = sourceNodeId,
                Parameters = parameters,
                ExpirationTime = m_timeProvider.GetUtcNow().UtcDateTime.Add(lifetime)
            };

            m_offers[offerId] = offer;
            return offer;
        }

        /// <summary>
        /// Redeems an offer. A server holds no resources for an
        /// unaccepted offer beyond its expiration, or an unsubscribed
        /// client would leak them.
        /// </summary>
        /// <param name="offerId">The identifier quoted by the client.</param>
        /// <param name="sourceNodeId">The Node the client named, which
        /// shall match the offer.</param>
        /// <param name="offer">The offer.</param>
        /// <returns>False when the offer is unknown, already accepted,
        /// expired, or does not match the source.</returns>
        public bool TryRedeem(
            uint offerId,
            NodeId sourceNodeId,
            out DataChannelOfferDataType? offer)
        {
            offer = null;

            if (!m_offers.TryRemove(offerId, out DataChannelOfferDataType? candidate))
            {
                return false;
            }

            if (candidate.ExpirationTime <= m_timeProvider.GetUtcNow().UtcDateTime)
            {
                return false;
            }

            if (candidate.SourceNodeId != sourceNodeId)
            {
                return false;
            }

            offer = candidate;
            return true;
        }

        /// <summary>
        /// Drops offers whose expiration has passed.
        /// </summary>
        public int PurgeExpired()
        {
            DateTime now = m_timeProvider.GetUtcNow().UtcDateTime;
            int removed = 0;

            foreach (uint offerId in m_offers
                .Where(entry => entry.Value.ExpirationTime <= now)
                .Select(entry => entry.Key)
                .ToArray())
            {
                if (m_offers.TryRemove(offerId, out _))
                {
                    removed++;
                }
            }

            return removed;
        }

        /// <summary>
        /// The offers currently outstanding.
        /// </summary>
        public int Count => m_offers.Count;

        private readonly ConcurrentDictionary<uint, DataChannelOfferDataType> m_offers = new();
        private readonly TimeProvider m_timeProvider;
        private int m_nextOfferId;
    }
}
