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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// Central registry of client-side transport channels with
    /// reference counting, sharing across participants, coalesced
    /// reconnect and asynchronous participant notification.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the recommended way for new code to obtain client-side
    /// transport channels. Multiple <see cref="IReconnectParticipant"/>
    /// instances whose <see cref="ManagedChannelKey"/> values match share a
    /// single underlying <see cref="ITransportChannel"/>; the
    /// underlying channel is closed when the last lease is released.
    /// </para>
    /// <para>
    /// The interface extends <see cref="ITransportChannelManager"/> so
    /// existing single-shot channel creation continues to work
    /// unchanged for legacy callers.
    /// </para>
    /// </remarks>
    public interface IClientChannelManager : ITransportChannelManager
    {
        /// <summary>
        /// Acquire a managed channel for the supplied participant. If
        /// an existing channel with a matching <see cref="ManagedChannelKey"/>
        /// is already open, the participant joins that channel and the
        /// underlying transport is shared. Otherwise the manager
        /// opens a new transport channel.
        /// </summary>
        /// <param name="participant">The participant requesting the
        /// channel.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>A managed channel lease. The caller releases its
        /// lease by disposing the returned channel; the underlying
        /// transport is closed only when the last lease is released.
        /// </returns>
        ValueTask<IManagedTransportChannel> GetAsync(
            IReconnectParticipant participant,
            CancellationToken ct = default);

        /// <summary>
        /// Acquire a managed channel for the supplied participant
        /// using a reverse-connect waiting connection. Reverse-connect
        /// channels are never shared with forward connections; two
        /// participants share a reverse-connect channel only when they
        /// supply the same <paramref name="reverseConnection"/>
        /// instance.
        /// </summary>
        /// <param name="participant">The participant requesting the
        /// channel.</param>
        /// <param name="reverseConnection">The waiting reverse
        /// connection.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>A managed channel lease.</returns>
        ValueTask<IManagedTransportChannel> GetAsync(
            IReconnectParticipant participant,
            ITransportWaitingConnection reverseConnection,
            CancellationToken ct = default);

        /// <summary>
        /// Acquire a managed channel for an endpoint and atomically bind a
        /// participant produced by <paramref name="participantFactory"/>.
        /// The factory is invoked exactly once with the freshly-acquired
        /// lease; this closes the window between channel acquisition and
        /// participant binding that the placeholder + RebindParticipant
        /// pattern leaves open.
        /// </summary>
        /// <param name="endpoint">The configured endpoint to connect to.</param>
        /// <param name="participantFactory">Factory invoked with the new managed-channel lease.</param>
        /// <param name="reverseConnection">Optional waiting reverse connection.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>A managed channel lease.</returns>
        ValueTask<IManagedTransportChannel> GetAsync(
            ConfiguredEndpoint endpoint,
            Func<IManagedTransportChannel, IReconnectParticipant> participantFactory,
            ITransportWaitingConnection? reverseConnection,
            CancellationToken ct = default);

        /// <summary>
        /// Trigger a reconnect of the supplied managed channel.
        /// Multiple concurrent reconnect requests for the same channel
        /// are coalesced into a single reconnect cycle, then all
        /// attached participants are notified in parallel via
        /// <see cref="IReconnectParticipant.OnReconnectAsync"/>.
        /// </summary>
        /// <param name="channel">The channel to reconnect. The caller
        /// must hold a lease on <paramref name="channel"/>.</param>
        /// <param name="ct">Cancellation token.</param>
        ValueTask ReconnectAsync(
            IManagedTransportChannel channel,
            CancellationToken ct = default);

#if NETSTANDARD2_1 || NET8_0_OR_GREATER
        /// <summary>
        /// Trigger a reconnect of the supplied managed channel, consulting
        /// the shared <paramref name="budget"/> so channel-level retries
        /// stay within the outer deadline. The existing
        /// <see cref="ReconnectAsync(IManagedTransportChannel, CancellationToken)"/>
        /// overload constructs an unlimited budget internally and remains
        /// for back-compat.
        /// </summary>
        /// <param name="channel">The channel to reconnect. The caller
        /// must hold a lease on <paramref name="channel"/>.</param>
        /// <param name="budget">The shared retry budget.</param>
        /// <param name="ct">Cancellation token.</param>
        ValueTask ReconnectAsync(
            IManagedTransportChannel channel,
            IRetryBudget budget,
            CancellationToken ct = default)
        {
            if (budget == null)
            {
                throw new ArgumentNullException(nameof(budget));
            }

            return ReconnectAsync(channel, ct);
        }
#endif

        /// <summary>
        /// Trigger a reconnect of every managed channel currently
        /// open. Typically used after a client-certificate rotation
        /// invalidates all secure channels.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        ValueTask ReconnectAllAsync(CancellationToken ct = default);

        /// <summary>
        /// Captures a point-in-time snapshot of every managed channel
        /// currently registered for ad-hoc inspection (e.g. health
        /// endpoints, debug UIs). The snapshot is a single, allocation-once
        /// list; no continuous subscription required.
        /// </summary>
        IReadOnlyList<ManagedChannelDiagnostic> GetChannelDiagnostics();

        /// <summary>
        /// Replace the participant currently bound to the supplied
        /// managed-channel lease. Used by session factories that
        /// acquire a channel before the participant (e.g. a Session)
        /// has been constructed. The new participant takes over
        /// reactivation callbacks from the next reconnect cycle
        /// onward.
        /// </summary>
        /// <param name="channel">A lease previously returned by
        /// <see cref="GetAsync(IReconnectParticipant, CancellationToken)"/>.</param>
        /// <param name="participant">The participant that should
        /// receive subsequent reactivation callbacks.</param>
        [Obsolete("Use GetAsync(ConfiguredEndpoint, Func<IManagedTransportChannel, IReconnectParticipant>, " +
            "ITransportWaitingConnection?, CancellationToken) to bind a participant atomically.")]
        void RebindParticipant(
            IManagedTransportChannel channel,
            IReconnectParticipant participant);

        /// <summary>
        /// Update the client certificate used for newly opened (or
        /// reconnected) channels. The new certificate takes effect on
        /// the next call to
        /// <see cref="GetAsync(IReconnectParticipant, CancellationToken)"/>
        /// or <see cref="ReconnectAsync(IManagedTransportChannel, CancellationToken)"/>.
        /// </summary>
        /// <param name="clientCertificate">The client instance
        /// certificate. May be <c>null</c> to clear.</param>
        /// <param name="clientCertificateChain">Optional certificate
        /// chain. May be <c>null</c>.</param>
        void UpdateClientCertificate(
            Certificate? clientCertificate,
            CertificateCollection? clientCertificateChain);
    }
}
