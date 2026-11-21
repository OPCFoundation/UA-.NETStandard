// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.PubSub.Mqtt
{
    /// <summary>
    /// Event payload raised by an
    /// <c>IMqttClientAdapter</c> whenever the underlying broker
    /// connection state changes.
    /// </summary>
    /// <remarks>
    /// Implements the connection state notification surface used by
    /// the MQTT broker transport defined in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.3.4">
    /// Part 14 §7.3.4 Broker transport (MQTT)</see>.
    /// </remarks>
    public sealed class MqttConnectionStateChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Initializes a new
        /// <see cref="MqttConnectionStateChangedEventArgs"/>.
        /// </summary>
        /// <param name="isConnected">
        /// <see langword="true"/> when the adapter just transitioned
        /// to <c>Connected</c>; <see langword="false"/> when it
        /// transitioned to <c>Disconnected</c>.
        /// </param>
        /// <param name="reason">
        /// Optional human-readable explanation. Must not contain
        /// sensitive data such as cleartext credentials.
        /// </param>
        public MqttConnectionStateChangedEventArgs(bool isConnected, string? reason)
        {
            IsConnected = isConnected;
            Reason = reason;
        }

        /// <summary>
        /// <see langword="true"/> when the adapter just transitioned
        /// to the connected state.
        /// </summary>
        public bool IsConnected { get; }

        /// <summary>
        /// Optional human-readable description of the transition.
        /// </summary>
        public string? Reason { get; }
    }
}
