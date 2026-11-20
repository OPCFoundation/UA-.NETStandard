/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua.PubSub.Kafka
{
    /// <summary>
    /// Event payload raised by an <c>IKafkaClientAdapter</c> whenever the
    /// underlying broker connection state changes.
    /// </summary>
    /// <remarks>
    /// Implements the connection state notification surface used by the
    /// Kafka broker transport defined in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/Annex-B.2">
    /// Part 14 Annex B.2 Apache Kafka transport</see>.
    /// </remarks>
    public sealed class KafkaConnectionStateChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Initializes a new
        /// <see cref="KafkaConnectionStateChangedEventArgs"/>.
        /// </summary>
        /// <param name="isConnected">
        /// <see langword="true"/> when the adapter just transitioned to
        /// <c>Connected</c>; <see langword="false"/> when it transitioned
        /// to <c>Disconnected</c>.
        /// </param>
        /// <param name="reason">
        /// Optional human-readable explanation. Must not contain
        /// sensitive data such as cleartext credentials.
        /// </param>
        public KafkaConnectionStateChangedEventArgs(bool isConnected, string? reason)
        {
            IsConnected = isConnected;
            Reason = reason;
        }

        /// <summary>
        /// <see langword="true"/> when the adapter just transitioned to
        /// the connected state.
        /// </summary>
        public bool IsConnected { get; }

        /// <summary>
        /// Optional human-readable description of the transition.
        /// </summary>
        public string? Reason { get; }
    }
}
