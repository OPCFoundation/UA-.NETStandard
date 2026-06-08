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

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua
{
    /// <summary>
    /// Implemented by client objects (typically sessions) that attach
    /// to a channel managed by an <see cref="IClientChannelManager"/>
    /// and need to perform reactivation work when the underlying
    /// transport channel is reconnected.
    /// </summary>
    /// <remarks>
    /// The manager invokes <see cref="OnReconnectAsync"/> for every
    /// active participant lease on a channel after the underlying
    /// transport has been (re)opened. Participants may issue
    /// session-service requests (e.g. ActivateSession) on the supplied
    /// channel during this call; the manager bypasses the channel's
    /// service-call gate for the duration of the call.
    /// </remarks>
    public interface IReconnectParticipant
    {
        /// <summary>
        /// Stable identifier of the participant for logging and
        /// diagnostics.
        /// </summary>
        string Id { get; }

        /// <summary>
        /// The endpoint the participant wishes to connect to. Used by
        /// the manager to compute the <see cref="ManagedChannelKey"/> when the
        /// participant acquires a channel via
        /// <see cref="IClientChannelManager.GetAsync(IReconnectParticipant, CancellationToken)"/>.
        /// </summary>
        ConfiguredEndpoint Endpoint { get; }

        /// <summary>
        /// Invoked by the manager when the underlying channel has been
        /// (re)opened and the participant should re-establish its
        /// session-level state (e.g. ActivateSession). May be called
        /// multiple times within one reconnect cycle if the channel
        /// manager has to retry transport-level open.
        /// </summary>
        /// <param name="channel">The reconnected managed channel.</param>
        /// <param name="reconnectAttempt">The attempt counter for the
        /// current cycle (0-based). A value of <c>-1</c> indicates the
        /// manager is shutting down the channel and the participant
        /// should release any state associated with it.</param>
        /// <param name="ct">Cancellation token bound to the manager's
        /// shutdown.</param>
        /// <returns>
        /// A <see cref="ParticipantReconnectResult"/> describing the
        /// outcome from this participant's perspective.
        /// </returns>
        ValueTask<ParticipantReconnectResult> OnReconnectAsync(
            IManagedTransportChannel channel,
            int reconnectAttempt,
            CancellationToken ct);

#if NETSTANDARD2_1 || NET8_0_OR_GREATER
        /// <summary>
        /// Invoked fire-and-forget by the manager after the participant returned
        /// <see cref="ParticipantReconnectResult.RequiresSessionRecreate"/> from
        /// <see cref="OnReconnectAsync"/>.
        /// </summary>
        /// <remarks>
        /// The participant performs its own session recreation here. The manager does not block
        /// its transition to <see cref="ChannelState.Ready"/> on this call.
        /// </remarks>
        /// <param name="ct">Cancellation token bound to the manager's shutdown.</param>
        /// <returns>The asynchronous recreation work.</returns>
        public ValueTask RecreateAsync(CancellationToken ct = default)
        {
            _ = ct;
            return new ValueTask();
        }
#endif
    }

    /// <summary>
    /// Optional interface for reconnect participants that provide a recreate callback on TFMs
    /// without default interface method support.
    /// </summary>
    public interface IRecreateAwareReconnectParticipant : IReconnectParticipant
    {
        /// <summary>
        /// Invoked fire-and-forget by the manager after the participant returned
        /// <see cref="ParticipantReconnectResult.RequiresSessionRecreate"/> from
        /// <see cref="IReconnectParticipant.OnReconnectAsync"/>.
        /// </summary>
        /// <remarks>
        /// The participant performs its own session recreation here. The manager does not block
        /// its transition to <see cref="ChannelState.Ready"/> on this call.
        /// </remarks>
        /// <param name="ct">Cancellation token bound to the manager's shutdown.</param>
        /// <returns>The asynchronous recreation work.</returns>
#if NETSTANDARD2_1 || NET8_0_OR_GREATER
        new ValueTask RecreateAsync(CancellationToken ct = default);
#else
        ValueTask RecreateAsync(CancellationToken ct = default);
#endif
    }
}
