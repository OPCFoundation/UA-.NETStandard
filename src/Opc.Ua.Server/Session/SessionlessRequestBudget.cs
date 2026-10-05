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
using System.Net;
using System.Threading;

namespace Opc.Ua.Server
{
    /// <summary>
    /// The limit a Session-less request ran into.
    /// </summary>
    internal enum SessionlessLimit
    {
        /// <summary>
        /// No limit was reached.
        /// </summary>
        None,

        /// <summary>
        /// The limit over all channels, <see cref="SessionlessInvocationOptions.MaxConcurrentRequests"/>.
        /// </summary>
        Server,

        /// <summary>
        /// The limit of one channel,
        /// <see cref="SessionlessInvocationOptions.MaxConcurrentRequestsPerChannel"/>.
        /// </summary>
        Channel
    }

    /// <summary>
    /// Counts the Session-less requests that run at the same time, server wide and per channel.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A request of a Session is accounted for by its Session. A request without one is not,
    /// although it keeps a worker busy and its channel active, so it gets a budget of its own.
    /// The two limits are checked and taken together under one lock: a request that is refused
    /// by the channel limit does not use up server wide capacity, and the counts cannot be
    /// exceeded by requests that arrive at the same time.
    /// </para>
    /// <para>
    /// A lease is released once, by disposing it, whether the request completed, failed or was
    /// cancelled. An entry of a channel is removed when its last request is released, so the
    /// memory follows the active requests and not the channels ever seen.
    /// </para>
    /// </remarks>
    internal sealed class SessionlessRequestBudget
    {
        /// <summary>
        /// Creates a budget that reads its limits from <paramref name="options"/> on every request,
        /// so a change of the options applies to the next request.
        /// </summary>
        /// <param name="options">The options with the limits.</param>
        /// <exception cref="ArgumentNullException"><paramref name="options"/> is <c>null</c>.</exception>
        public SessionlessRequestBudget(SessionlessInvocationOptions options)
        {
            Options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <summary>
        /// The options the budget reads its limits from.
        /// </summary>
        public SessionlessInvocationOptions Options { get; }

        /// <summary>
        /// The number of requests that currently hold a lease.
        /// </summary>
        public int ActiveRequests
        {
            get
            {
                lock (m_lock)
                {
                    return m_active;
                }
            }
        }

        /// <summary>
        /// The number of channels that currently have a request.
        /// </summary>
        public int TrackedChannels
        {
            get
            {
                lock (m_lock)
                {
                    return m_perChannel.Count;
                }
            }
        }

        /// <summary>
        /// Takes capacity for one request, unless a limit is reached.
        /// </summary>
        /// <param name="channel">The channel the request arrived on.</param>
        /// <param name="lease">
        /// The lease to dispose when the request is done; <c>null</c> if a limit is reached.
        /// </param>
        /// <returns>
        /// <see cref="SessionlessLimit.None"/> if the request may run, otherwise the limit that
        /// refuses it.
        /// </returns>
        public SessionlessLimit TryAcquire(SecureChannelContext channel, out IDisposable? lease)
        {
            int maxRequests = Options.MaxConcurrentRequests;
            int maxPerChannel = Options.MaxConcurrentRequestsPerChannel;
            ChannelKey key = GetKey(channel);

            lock (m_lock)
            {
                if (maxRequests > 0 && m_active >= maxRequests)
                {
                    lease = null;
                    return SessionlessLimit.Server;
                }

                m_perChannel.TryGetValue(key, out int channelActive);
                if (maxPerChannel > 0 && channelActive >= maxPerChannel)
                {
                    lease = null;
                    return SessionlessLimit.Channel;
                }

                m_active++;
                m_perChannel[key] = channelActive + 1;
            }

            lease = new Lease(this, key);
            return SessionlessLimit.None;
        }

        private static ChannelKey GetKey(SecureChannelContext channel)
        {
            IPAddress? peer = channel.PeerAddress;
            if (peer != null && peer.IsIPv4MappedToIPv6)
            {
                peer = peer.MapToIPv4();
            }
            return new ChannelKey(channel.SecureChannelId, peer);
        }

        private void Release(ChannelKey key)
        {
            lock (m_lock)
            {
                m_active--;
                if (m_perChannel.TryGetValue(key, out int channelActive) && channelActive > 1)
                {
                    m_perChannel[key] = channelActive - 1;
                }
                else
                {
                    m_perChannel.Remove(key);
                }
            }
        }

        /// <summary>
        /// Identifies a channel: its SecureChannel and the address of the peer, which is all that
        /// tells HTTPS requests apart, because they share one SecureChannel id per listener.
        /// </summary>
        private readonly record struct ChannelKey(string SecureChannelId, IPAddress? Peer);

        /// <summary>
        /// The capacity taken for one request, returned by the first disposal.
        /// </summary>
        private sealed class Lease(SessionlessRequestBudget budget, ChannelKey key) : IDisposable
        {
            public void Dispose()
            {
                if (Interlocked.Exchange(ref m_released, 1) == 0)
                {
                    budget.Release(key);
                }
            }

            private int m_released;
        }

        private readonly Lock m_lock = new();
        private readonly Dictionary<ChannelKey, int> m_perChannel = new();
        private int m_active;
    }
}
