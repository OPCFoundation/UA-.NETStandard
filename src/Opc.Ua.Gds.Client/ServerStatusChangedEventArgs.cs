// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua.Gds.Client
{
    /// <summary>
    /// Carries the state of the connected server, as observed by the session
    /// keep-alive.
    /// </summary>
    public sealed class ServerStatusChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Initializes a new instance of the
        /// <see cref="ServerStatusChangedEventArgs"/> class.
        /// </summary>
        /// <param name="status">Status of the keep-alive that observed the
        /// state.</param>
        /// <param name="currentState">The state of the server.</param>
        /// <param name="currentTime">The time reported by the server.</param>
        public ServerStatusChangedEventArgs(
            ServiceResult? status,
            ServerState currentState,
            DateTime currentTime)
        {
            Status = status;
            CurrentState = currentState;
            CurrentTime = currentTime;
        }

        /// <summary>
        /// Status of the keep-alive that observed the state. A bad status means
        /// the server could not be reached, in which case
        /// <see cref="CurrentState"/> is the last state that was read.
        /// </summary>
        public ServiceResult? Status { get; }

        /// <summary>
        /// The state of the server.
        /// </summary>
        public ServerState CurrentState { get; }

        /// <summary>
        /// The time reported by the server.
        /// </summary>
        public DateTime CurrentTime { get; }
    }
}
