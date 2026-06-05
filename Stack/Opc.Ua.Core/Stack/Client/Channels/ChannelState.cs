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
        /// (e.g. <see cref="Opc.Ua.IReconnectParticipant"/> sessions) are
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
