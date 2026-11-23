// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua
{
    /// <summary>
    /// Lifecycle state of an <see cref="IManagedTransportChannel"/>.
    /// Normal service calls are only permitted in <see cref="Ready"/>;
    /// other states gate <see cref="ITransportChannel.SendRequestAsync"/>
    /// callers until the channel returns to <see cref="Ready"/>.
    /// </summary>
    public enum ChannelState
    {
        /// <summary>
        /// Initial state before the first connect, or after a graceful
        /// close. Service calls fail with
        /// <see cref="StatusCodes.BadSecureChannelClosed"/>.
        /// </summary>
        Disconnected = 0,

        /// <summary>
        /// The underlying transport is being opened for the first time.
        /// Service calls block on the gate.
        /// </summary>
        TransportConnecting = 1,

        /// <summary>
        /// The underlying transport is being reopened following a
        /// disconnect. Service calls block on the gate.
        /// </summary>
        TransportReconnecting = 2,

        /// <summary>
        /// The underlying transport is connected; attached participants
        /// (e.g. <see cref="IReconnectParticipant"/> sessions) are
        /// running their reactivation work. Service calls from outside
        /// the participant reactivation context block on the gate; the
        /// manager bypasses the gate internally so that participants can
        /// send session-service requests.
        /// </summary>
        TransportConnectedSessionReactivating = 3,

        /// <summary>
        /// The channel is fully connected and all attached participants
        /// have completed reactivation. Service calls flow normally.
        /// </summary>
        Ready = 4,

        /// <summary>
        /// The channel manager exhausted its retry budget. Service calls
        /// fail fast with <see cref="StatusCodes.BadSecureChannelClosed"/>
        /// until the channel is reset or recreated.
        /// </summary>
        Faulted = 5,

        /// <summary>
        /// The channel has been closed and disposed by the manager.
        /// Service calls fail with
        /// <see cref="StatusCodes.BadSecureChannelClosed"/>.
        /// </summary>
        Closed = 6
    }
}
