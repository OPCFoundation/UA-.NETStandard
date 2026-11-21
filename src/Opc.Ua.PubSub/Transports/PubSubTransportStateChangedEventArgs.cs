// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.PubSub.Transports
{
    /// <summary>
    /// Event payload raised by an <see cref="IPubSubTransport"/>
    /// whenever its connection state changes. Carries enough detail
    /// for the owning PubSubConnection state machine to decide
    /// between fault and recovery transitions.
    /// </summary>
    /// <remarks>
    /// Implements the transport-state notification surface required
    /// by
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/9.1.5">
    /// Part 14 §9.1.5 PubSubConnection address space model</see> so
    /// the connection's <c>PubSubStatusType</c> can mirror the
    /// underlying transport state.
    /// </remarks>
    public sealed class PubSubTransportStateChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Initializes a new <see cref="PubSubTransportStateChangedEventArgs"/>.
        /// </summary>
        /// <param name="isConnected">
        /// <see langword="true"/> after a successful connect /
        /// reconnect; <see langword="false"/> after a disconnect.
        /// </param>
        /// <param name="status">
        /// Status code summarising the cause of the change.
        /// </param>
        /// <param name="reason">
        /// Optional human-readable explanation. Must not contain
        /// sensitive data.
        /// </param>
        public PubSubTransportStateChangedEventArgs(bool isConnected, StatusCode status, string? reason)
        {
            IsConnected = isConnected;
            Status = status;
            Reason = reason;
        }

        /// <summary>
        /// Whether the transport is currently connected.
        /// </summary>
        public bool IsConnected { get; }

        /// <summary>
        /// Status code summarising the cause of the state change.
        /// </summary>
        public StatusCode Status { get; }

        /// <summary>
        /// Optional human-readable description.
        /// </summary>
        public string? Reason { get; }
    }
}
