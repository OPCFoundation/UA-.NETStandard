/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PubSub.Mqtt.Internal
{
    /// <summary>
    /// Internal abstraction shielding the rest of the library from
    /// the MQTTnet v4 / v5 API drift. The library compiles against
    /// MQTTnet 5 on net8/9/10 and the pinned v4.3.7.1207 on
    /// net48; both arms produce a
    /// behaviourally identical implementation of this interface so
    /// callers never see version-specific types.
    /// </summary>
    /// <remarks>
    /// The adapter wraps a single MQTT client session — open / publish
    /// / subscribe / close. It does not own retry semantics; the
    /// owning <see cref="MqttBrokerTransport"/> is responsible for
    /// reconnect orchestration. Implementations must be safe to call
    /// <see cref="IAsyncDisposable.DisposeAsync"/> concurrently with
    /// an in-flight <see cref="PublishAsync"/>.
    /// </remarks>
    internal interface IMqttClientAdapter : IAsyncDisposable
    {
        /// <summary>
        /// Whether the underlying client believes it is connected.
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Connects to the broker using
        /// <paramref name="options"/>. Idempotent — calling on an
        /// already-connected adapter returns immediately.
        /// </summary>
        /// <param name="options">Connection options.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask ConnectAsync(
            MqttConnectionOptions options,
            CancellationToken cancellationToken);

        /// <summary>
        /// Disconnects cleanly from the broker. Idempotent.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask DisconnectAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Subscribes to the supplied topic filters in a single MQTT
        /// SUBSCRIBE round-trip.
        /// </summary>
        /// <param name="topics">Filters to install.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask SubscribeAsync(
            IReadOnlyList<MqttTopicFilter> topics,
            CancellationToken cancellationToken);

        /// <summary>
        /// Removes the supplied topic filters in a single MQTT
        /// UNSUBSCRIBE round-trip.
        /// </summary>
        /// <param name="topics">Filters to remove.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask UnsubscribeAsync(
            IReadOnlyList<string> topics,
            CancellationToken cancellationToken);

        /// <summary>
        /// Publishes <paramref name="message"/>.
        /// </summary>
        /// <param name="message">Message envelope.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask PublishAsync(
            MqttMessage message,
            CancellationToken cancellationToken);

        /// <summary>
        /// Raised whenever a broker-delivered application message
        /// arrives.
        /// </summary>
        event EventHandler<MqttIncomingMessageEventArgs>? IncomingMessage;

        /// <summary>
        /// Raised whenever the broker connection state changes.
        /// </summary>
        event EventHandler<MqttConnectionStateChangedEventArgs>? ConnectionStateChanged;
    }
}
