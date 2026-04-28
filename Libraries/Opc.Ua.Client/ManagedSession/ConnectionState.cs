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

namespace Opc.Ua.Client
{
    using System;

    /// <summary>
    /// Connection states for a ManagedSession, following OPC UA
    /// client connectivity guidance.
    /// </summary>
    public enum ConnectionState
    {
        /// <summary>Session is not connected and not attempting to connect.</summary>
        Disconnected,

        /// <summary>Session is attempting initial connection.</summary>
        Connecting,

        /// <summary>Session is connected and operational.</summary>
        Connected,

        /// <summary>Connection lost, attempting to reconnect to the same server.</summary>
        Reconnecting,

        /// <summary>Reconnect failed, attempting failover to a redundant server.</summary>
        Failover,

        /// <summary>Session is closing.</summary>
        Closing,

        /// <summary>Session is closed and disposed.</summary>
        Closed
    }

    /// <summary>
    /// Event args for connection state changes.
    /// </summary>
    public sealed class ConnectionStateChangedEventArgs : EventArgs
    {
        /// <summary>The previous state.</summary>
        public ConnectionState PreviousState { get; init; }

        /// <summary>The new state.</summary>
        public ConnectionState NewState { get; init; }

        /// <summary>Service result if the transition was due to an error.</summary>
        public ServiceResult? Error { get; init; }

        /// <summary>The reconnect attempt number (0 when not reconnecting).</summary>
        public int ReconnectAttempt { get; init; }
    }
}
