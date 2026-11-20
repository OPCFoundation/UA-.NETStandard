/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PubSub.Kafka.Internal
{
    /// <summary>
    /// Internal abstraction shielding the rest of the library from the
    /// concrete Kafka client surface. The default implementation wraps a
    /// producer / consumer pair; unit tests can inject a
    /// fake to drive the transport without an actual broker.
    /// </summary>
    /// <remarks>
    /// The adapter wraps a single Kafka client session — connect /
    /// produce / subscribe / close. It does not own retry semantics; the
    /// owning <see cref="KafkaBrokerTransport"/> is responsible for
    /// reconnect orchestration. Implementations must be safe to call
    /// <see cref="IAsyncDisposable.DisposeAsync"/> concurrently with an
    /// in-flight <see cref="ProduceAsync"/>.
    /// </remarks>
    internal interface IKafkaClientAdapter : IAsyncDisposable
    {
        /// <summary>
        /// Whether the adapter believes it is connected.
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Connects to the brokers using <paramref name="options"/>.
        /// Idempotent — calling on an already-connected adapter returns
        /// immediately.
        /// </summary>
        /// <param name="options">Connection options.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask ConnectAsync(
            KafkaConnectionOptions options,
            CancellationToken cancellationToken);

        /// <summary>
        /// Disconnects cleanly from the brokers. Idempotent.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask DisconnectAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Subscribes to the supplied topics and starts the background
        /// consume loop.
        /// </summary>
        /// <param name="topics">Topics to consume.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask SubscribeAsync(
            IReadOnlyList<string> topics,
            CancellationToken cancellationToken);

        /// <summary>
        /// Removes the supplied topics from the consumer subscription.
        /// </summary>
        /// <param name="topics">Topics to remove.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask UnsubscribeAsync(
            IReadOnlyList<string> topics,
            CancellationToken cancellationToken);

        /// <summary>
        /// Produces <paramref name="message"/> to its topic.
        /// </summary>
        /// <param name="message">Record envelope.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask ProduceAsync(
            KafkaMessage message,
            CancellationToken cancellationToken);

        /// <summary>
        /// Raised whenever a broker-delivered record arrives.
        /// </summary>
        event EventHandler<KafkaIncomingMessageEventArgs>? IncomingMessage;

        /// <summary>
        /// Raised whenever the broker connection state changes.
        /// </summary>
        event EventHandler<KafkaConnectionStateChangedEventArgs>? ConnectionStateChanged;
    }
}
