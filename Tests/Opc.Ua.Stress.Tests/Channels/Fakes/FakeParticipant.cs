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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Stress.Tests.Channels.Fakes
{
    /// <summary>
    /// Configurable reconnect participant for channel-manager stress tests.
    /// </summary>
    public sealed class FakeParticipant : IReconnectParticipant
    {
        /// <summary>
        /// Initializes a new reconnect participant for the supplied endpoint.
        /// </summary>
        /// <param name="endpoint">Endpoint used to acquire managed channels.</param>
        public FakeParticipant(ConfiguredEndpoint endpoint)
            : this(Guid.NewGuid().ToString("N"), endpoint)
        {
        }

        /// <summary>
        /// Initializes a new reconnect participant for the supplied endpoint and id.
        /// </summary>
        /// <param name="id">Stable participant id.</param>
        /// <param name="endpoint">Endpoint used to acquire managed channels.</param>
        public FakeParticipant(string id, ConfiguredEndpoint endpoint)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("Participant id cannot be empty.", nameof(id));
            }

            Id = id;
            Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        }

        /// <inheritdoc/>
        public string Id { get; }

        /// <inheritdoc/>
        public ConfiguredEndpoint Endpoint { get; }

        /// <summary>
        /// Gets or sets an optional delay applied before reconnect behavior runs.
        /// </summary>
        public TimeSpan? HangFor
        {
            get
            {
                lock (m_lock)
                {
                    return m_hangFor;
                }
            }
            set
            {
                if (value < TimeSpan.Zero && value != Timeout.InfiniteTimeSpan)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }

                lock (m_lock)
                {
                    m_hangFor = value;
                }
            }
        }

        /// <summary>
        /// Gets the number of reconnect notifications received.
        /// </summary>
        public int NotificationCount => Volatile.Read(ref m_notificationCount);

        /// <summary>
        /// Gets the last reconnect attempt number observed.
        /// </summary>
        public int LastAttempt => Volatile.Read(ref m_lastAttempt);

        /// <summary>
        /// Gets the last managed channel supplied to the participant.
        /// </summary>
        public IManagedTransportChannel? LastChannel => Volatile.Read(ref m_lastChannel);

        /// <summary>
        /// Configures the behavior returned from <see cref="OnReconnectAsync"/>.
        /// </summary>
        /// <param name="onReconnect">Delegate called for each reconnect attempt.</param>
        public void ConfigureOnReconnect(
            Func<int, CancellationToken, ValueTask<ParticipantReconnectResult>> onReconnect)
        {
            if (onReconnect == null)
            {
                throw new ArgumentNullException(nameof(onReconnect));
            }

            ConfigureOnReconnect((_, attempt, ct) => onReconnect(attempt, ct));
        }

        /// <summary>
        /// Configures the behavior returned from <see cref="OnReconnectAsync"/>.
        /// </summary>
        /// <param name="onReconnect">Delegate called for each reconnect attempt.</param>
        public void ConfigureOnReconnect(
            Func<IManagedTransportChannel, int, CancellationToken, ValueTask<ParticipantReconnectResult>> onReconnect)
        {
            if (onReconnect == null)
            {
                throw new ArgumentNullException(nameof(onReconnect));
            }

            lock (m_lock)
            {
                m_onReconnect = onReconnect;
            }
        }

        /// <inheritdoc/>
        public async ValueTask<ParticipantReconnectResult> OnReconnectAsync(
            IManagedTransportChannel channel,
            int reconnectAttempt,
            CancellationToken ct)
        {
            if (channel == null)
            {
                throw new ArgumentNullException(nameof(channel));
            }

            Interlocked.Increment(ref m_notificationCount);
            Interlocked.Exchange(ref m_lastAttempt, reconnectAttempt);
            Interlocked.Exchange(ref m_lastChannel, channel);

            Func<IManagedTransportChannel, int, CancellationToken, ValueTask<ParticipantReconnectResult>> onReconnect;
            TimeSpan? hangFor;
            lock (m_lock)
            {
                onReconnect = m_onReconnect;
                hangFor = m_hangFor;
            }

            if (hangFor.HasValue)
            {
                await Task.Delay(hangFor.Value, ct).ConfigureAwait(false);
            }

            return await onReconnect(channel, reconnectAttempt, ct).ConfigureAwait(false);
        }

        private static ValueTask<ParticipantReconnectResult> ReactivatedAsync(
            IManagedTransportChannel channel,
            int attempt,
            CancellationToken ct)
        {
            _ = channel;
            _ = attempt;
            _ = ct;
            return new ValueTask<ParticipantReconnectResult>(ParticipantReconnectResult.Reactivated);
        }

        private readonly Lock m_lock = new();
        private Func<IManagedTransportChannel, int, CancellationToken, ValueTask<ParticipantReconnectResult>>
            m_onReconnect = ReactivatedAsync;
        private TimeSpan? m_hangFor;
        private int m_notificationCount;
        private int m_lastAttempt = -1;
        private IManagedTransportChannel? m_lastChannel;
    }
}
