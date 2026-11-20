/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua.Client
{
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

        /// <summary>
        /// Underlying managed-channel state transition that caused this
        /// outer state transition, or <c>null</c> when no channel-manager
        /// state was involved.
        /// </summary>
        public ChannelStateChange? UnderlyingChannelState { get; init; }
    }
}
