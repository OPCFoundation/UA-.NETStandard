/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.PubSub.Mqtt.Internal;

namespace Opc.Ua.PubSub.Mqtt.Tests
{
    /// <summary>
    /// In-memory implementation of <see cref="IMqttClientAdapter"/>
    /// used by the unit tests to drive
    /// <see cref="MqttBrokerTransport"/> without a real broker.
    /// </summary>
    /// <remarks>
    /// Records all published messages on <see cref="PublishedMessages"/>
    /// in order. Exposes helpers to simulate broker behaviour:
    /// <see cref="RaiseIncomingMessage"/> and
    /// <see cref="RaiseConnectionStateChanged"/>.
    /// </remarks>
    internal sealed class FakeMqttClientAdapter : IMqttClientAdapter
    {
        private readonly ConcurrentQueue<MqttMessage> m_published = new();
        private readonly List<MqttTopicFilter> m_subscriptions = [];
        private readonly List<string> m_unsubscribed = [];
        private bool m_isConnected;
        private bool m_disposed;

        public IReadOnlyCollection<MqttMessage> PublishedMessages => m_published;

        public IReadOnlyList<MqttTopicFilter> Subscriptions
        {
            get
            {
                lock (m_subscriptions)
                {
                    return [.. m_subscriptions];
                }
            }
        }

        public IReadOnlyList<string> Unsubscriptions
        {
            get
            {
                lock (m_unsubscribed)
                {
                    return [.. m_unsubscribed];
                }
            }
        }

        public int ConnectCount { get; private set; }

        public int DisconnectCount { get; private set; }

        public Func<MqttConnectionOptions, CancellationToken, ValueTask>? OnConnect { get; set; }

        public Func<CancellationToken, ValueTask>? OnDisconnect { get; set; }

        public Func<MqttMessage, CancellationToken, ValueTask>? OnPublish { get; set; }

        public bool IsConnected => m_isConnected;

        public event EventHandler<MqttIncomingMessageEventArgs>? IncomingMessage;

        public event EventHandler<MqttConnectionStateChangedEventArgs>? ConnectionStateChanged;

        public async ValueTask ConnectAsync(
            MqttConnectionOptions options,
            CancellationToken cancellationToken)
        {
            ConnectCount++;
            if (OnConnect is not null)
            {
                await OnConnect(options, cancellationToken).ConfigureAwait(false);
            }
            m_isConnected = true;
            ConnectionStateChanged?.Invoke(
                this,
                new MqttConnectionStateChangedEventArgs(true, "Connected"));
        }

        public async ValueTask DisconnectAsync(CancellationToken cancellationToken)
        {
            DisconnectCount++;
            if (OnDisconnect is not null)
            {
                await OnDisconnect(cancellationToken).ConfigureAwait(false);
            }
            bool was = m_isConnected;
            m_isConnected = false;
            if (was)
            {
                ConnectionStateChanged?.Invoke(
                    this,
                    new MqttConnectionStateChangedEventArgs(false, "Disconnected"));
            }
        }

        public ValueTask SubscribeAsync(
            IReadOnlyList<MqttTopicFilter> topics,
            CancellationToken cancellationToken)
        {
            lock (m_subscriptions)
            {
                m_subscriptions.AddRange(topics);
            }
            return default;
        }

        public ValueTask UnsubscribeAsync(
            IReadOnlyList<string> topics,
            CancellationToken cancellationToken)
        {
            lock (m_unsubscribed)
            {
                m_unsubscribed.AddRange(topics);
            }
            return default;
        }

        public async ValueTask PublishAsync(
            MqttMessage message,
            CancellationToken cancellationToken)
        {
            m_published.Enqueue(message);
            if (OnPublish is not null)
            {
                await OnPublish(message, cancellationToken).ConfigureAwait(false);
            }
        }

        public void RaiseIncomingMessage(MqttMessage message, DateTimeUtc receivedAt)
        {
            IncomingMessage?.Invoke(
                this,
                new MqttIncomingMessageEventArgs(message, receivedAt));
        }

        public void RaiseConnectionStateChanged(bool isConnected, string? reason = null)
        {
            m_isConnected = isConnected;
            ConnectionStateChanged?.Invoke(
                this,
                new MqttConnectionStateChangedEventArgs(isConnected, reason));
        }

        public ValueTask DisposeAsync()
        {
            m_disposed = true;
            return default;
        }

        public bool Disposed => m_disposed;
    }

    /// <summary>
    /// Factory that hands out a single, controllable
    /// <see cref="FakeMqttClientAdapter"/> per test. Tests that want
    /// to inspect the adapter after the transport is opened keep a
    /// reference to <see cref="Adapter"/>.
    /// </summary>
    internal sealed class FakeMqttClientFactory : IMqttClientFactory
    {
        public FakeMqttClientFactory()
        {
            Adapter = new FakeMqttClientAdapter();
        }

        public FakeMqttClientFactory(FakeMqttClientAdapter adapter)
        {
            Adapter = adapter;
        }

        public FakeMqttClientAdapter Adapter { get; }

        public int CreateCount { get; private set; }

        IMqttClientAdapter IMqttClientFactory.CreateAdapter(
            MqttConnectionOptions options,
            ITelemetryContext telemetry,
            TimeProvider timeProvider)
        {
            CreateCount++;
            return Adapter;
        }
    }
}
